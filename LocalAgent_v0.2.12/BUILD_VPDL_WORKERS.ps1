param(
    [string]$VpdlRoot = $env:COGNEX_VPDL_ROOT
)

$ErrorActionPreference = 'Stop'
$agentRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($VpdlRoot)) { $VpdlRoot = 'C:\Program Files\Cognex\VisionPro Deep Learning' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio Build Tools의 vswhere.exe를 찾지 못했습니다.' }
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($msbuild)) { throw 'MSBuild.exe를 찾지 못했습니다.' }

function Get-HealthyVpdlInstallations {
    if (-not (Test-Path -LiteralPath $VpdlRoot)) { return @() }
    foreach ($managedFile in Get-ChildItem -LiteralPath $VpdlRoot -Recurse -File -Filter 'ViDi.NET.Local.dll' -ErrorAction SilentlyContinue) {
        try {
            $studio = $managedFile.Directory.FullName
            $managed = $managedFile.FullName
            $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($managed).Version
            $apiVersion = "$($assemblyVersion.Major).$($assemblyVersion.Minor)"
            $cursor = $managedFile.Directory
            $versionRoot = $null
            $depth = 0
            while ($cursor -and $depth -lt 6) {
                $native = Join-Path $cursor.FullName ("bin\vidi_" + $apiVersion.Replace('.', '') + '.dll')
                if (Test-Path -LiteralPath $native) { $versionRoot = $cursor; break }
                $cursor = $cursor.Parent
                $depth++
            }
            if (-not $versionRoot) { continue }
            [pscustomobject]@{ ProductVersion = $versionRoot.Name; ApiVersion = $apiVersion; Studio = $studio; Root = $versionRoot.FullName }
        } catch { }
    }
}

$installations = @(Get-HealthyVpdlInstallations |
    Sort-Object @{ Expression = { if ($_.Studio -like '*Cognex Deep Learning Studio') { 0 } else { 1 } } }, @{ Expression = { [version]$_.ProductVersion }; Descending = $true } |
    Group-Object ApiVersion |
    ForEach-Object { $_.Group | Select-Object -First 1 } |
    Sort-Object @{ Expression = { [version]$_.ProductVersion }; Descending = $true })
$releaseRoot = Join-Path $agentRoot 'Launcher\bin\x64\Release'
$workerRoot = Join-Path $releaseRoot 'Workers'
if ([IO.Path]::GetFullPath($workerRoot) -ne ([IO.Path]::GetFullPath($releaseRoot).TrimEnd('\') + '\Workers')) { throw 'Worker build target escaped release directory' }
if ((Test-Path -LiteralPath $workerRoot) -and ((Get-Item -LiteralPath $workerRoot).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Worker build target must not be a directory link' }
if (Test-Path -LiteralPath $workerRoot) { Remove-Item -LiteralPath $workerRoot -Recurse -Force }
New-Item -ItemType Directory -Path $workerRoot -Force | Out-Null

& $msbuild (Join-Path $agentRoot 'Launcher\VisionQC.AgentLauncher.csproj') /m /t:Rebuild /p:Configuration=Release /p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw 'VPDL Launcher 빌드 실패' }

$coreOutput = Join-Path $workerRoot 'Core'
New-Item -ItemType Directory -Path $coreOutput -Force | Out-Null
& $msbuild (Join-Path $agentRoot 'CoreWorker\VisionQC.CoreWorker.csproj') /restore /m /t:Rebuild /p:Configuration=Release /p:Platform=x64 ("/p:OutDir=$coreOutput\")
if ($LASTEXITCODE -ne 0) { throw 'VPDL 미설치 Core Worker 빌드 실패' }
$coreExe = Join-Path $coreOutput 'VisionQC.CoreWorker.exe'
if (-not (Test-Path -LiteralPath $coreExe)) { throw 'Core Worker 생성 실패' }

$manifest = @()
$universalSource = $null
foreach ($installation in $installations) {
    $output = Join-Path $workerRoot ($installation.ApiVersion + '\')
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    & $msbuild (Join-Path $agentRoot 'VisionQC.LocalAgent.csproj') /m /t:Rebuild /p:Configuration=Release /p:Platform=x64 ("/p:CognexDir=$($installation.Studio)") /p:AssemblyName=VisionQC.VpdlWorker ("/p:OutDir=$output")
    if ($LASTEXITCODE -ne 0) { throw "VPDL $($installation.ProductVersion) Worker 빌드 실패" }
    & $msbuild (Join-Path $agentRoot 'GreenRunner\VisionQC.GreenRunner.csproj') /m /t:Rebuild /p:Configuration=Release /p:Platform=x64 ("/p:CognexDir=$($installation.Studio)") ("/p:OutDir=$output")
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $output 'VisionQC.GreenRunner.exe'))) { throw "VPDL $($installation.ProductVersion) Green Runner 빌드 실패" }
    if (-not $universalSource) { $universalSource = $output }
    $manifest += [pscustomobject]@{
        productVersion = $installation.ProductVersion
        apiVersion = $installation.ApiVersion
        worker = "Workers/$($installation.ApiVersion)/VisionQC.VpdlWorker.exe"
    }
}

$universalExe = $null
if ($universalSource) {
    $universalOutput = Join-Path $workerRoot 'Universal'
    New-Item -ItemType Directory -Path $universalOutput -Force | Out-Null
    Copy-Item -Path (Join-Path $universalSource '*') -Destination $universalOutput -Recurse -Force
    $universalExe = Join-Path $universalOutput 'VisionQC.VpdlWorker.exe'
    if (-not (Test-Path -LiteralPath $universalExe)) { throw 'Universal VPDL Worker 생성 실패' }
}

$manifestDocument = [ordered]@{
    schemaVersion = 3
    strategy = 'core-or-exact-or-universal'
    coreWorker = 'Workers/Core/VisionQC.CoreWorker.exe'
    universalWorker = if ($universalExe) { 'Workers/Universal/VisionQC.VpdlWorker.exe' } else { $null }
    universalBuildApiVersion = if ($installations.Count -gt 0) { $installations[0].ApiVersion } else { $null }
    workers = @($manifest)
}
$manifestDocument | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseRoot 'vpdl-workers.json') -Encoding UTF8
$workerArchive = Join-Path $releaseRoot 'vpdl-workers.zip'
if (Test-Path -LiteralPath $workerArchive) { Remove-Item -LiteralPath $workerArchive -Force }
Compress-Archive -Path (Join-Path $workerRoot '*') -DestinationPath $workerArchive -CompressionLevel Optimal
if (-not (Test-Path -LiteralPath $workerArchive)) { throw 'VPDL Worker bundle creation failed' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($workerArchive)
try {
    $coreEntry = $archive.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq 'Core/VisionQC.CoreWorker.exe' } | Select-Object -First 1
    if (-not $coreEntry) { throw 'Worker bundle에 Core/VisionQC.CoreWorker.exe가 없습니다.' }
    $universalEntry = $archive.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq 'Universal/VisionQC.VpdlWorker.exe' } | Select-Object -First 1
    if ($installations.Count -gt 0 -and -not $universalEntry) { throw 'Worker bundle에 Universal/VisionQC.VpdlWorker.exe가 없습니다.' }
} finally {
    $archive.Dispose()
}
Write-Host "[OK] Launcher: $releaseRoot\VisionQC.LocalAgent.exe"
Write-Host "[OK] Core Worker: $coreExe"
Write-Host "[OK] Workers: $($manifest.Count)"
if ($universalExe) { Write-Host "[OK] Universal Worker: $universalExe" }
Write-Host "[OK] Worker bundle: $workerArchive"
