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
    foreach ($root in Get-ChildItem -LiteralPath $VpdlRoot -Directory) {
        $studio = Join-Path $root.FullName 'Cognex Deep Learning Studio'
        $managed = Join-Path $studio 'ViDi.NET.Local.dll'
        if (-not (Test-Path -LiteralPath $managed)) { continue }
        try {
            $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($managed).Version
            $apiVersion = "$($assemblyVersion.Major).$($assemblyVersion.Minor)"
            $native = Join-Path $root.FullName ("bin\vidi_" + $apiVersion.Replace('.', '') + '.dll')
            if (-not (Test-Path -LiteralPath $native)) { continue }
            [pscustomobject]@{ ProductVersion = $root.Name; ApiVersion = $apiVersion; Studio = $studio }
        } catch { }
    }
}

$installations = @(Get-HealthyVpdlInstallations | Sort-Object @{ Expression = { [version]$_.ProductVersion }; Descending = $true })
if ($installations.Count -eq 0) { throw "정상 설치된 VPDL Runtime을 찾지 못했습니다: $VpdlRoot" }

$releaseRoot = Join-Path $agentRoot 'Launcher\bin\x64\Release'
$workerRoot = Join-Path $releaseRoot 'Workers'
New-Item -ItemType Directory -Path $workerRoot -Force | Out-Null

& $msbuild (Join-Path $agentRoot 'Launcher\VisionQC.AgentLauncher.csproj') /m /t:Rebuild /p:Configuration=Release /p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw 'VPDL Launcher 빌드 실패' }

$manifest = @()
foreach ($installation in $installations) {
    $output = Join-Path $workerRoot ($installation.ApiVersion + '\')
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    & $msbuild (Join-Path $agentRoot 'VisionQC.LocalAgent.csproj') /m /t:Rebuild /p:Configuration=Release /p:Platform=x64 ("/p:CognexDir=$($installation.Studio)") /p:AssemblyName=VisionQC.VpdlWorker ("/p:OutDir=$output")
    if ($LASTEXITCODE -ne 0) { throw "VPDL $($installation.ProductVersion) Worker 빌드 실패" }
    $manifest += [pscustomobject]@{
        productVersion = $installation.ProductVersion
        apiVersion = $installation.ApiVersion
        worker = "Workers/$($installation.ApiVersion)/VisionQC.VpdlWorker.exe"
    }
}

$manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $releaseRoot 'vpdl-workers.json') -Encoding UTF8
$workerArchive = Join-Path $releaseRoot 'vpdl-workers.zip'
if (Test-Path -LiteralPath $workerArchive) { Remove-Item -LiteralPath $workerArchive -Force }
Compress-Archive -Path (Join-Path $workerRoot '*') -DestinationPath $workerArchive -CompressionLevel Optimal
if (-not (Test-Path -LiteralPath $workerArchive)) { throw 'VPDL Worker bundle creation failed' }
Write-Host "[OK] Launcher: $releaseRoot\VisionQC.LocalAgent.exe"
Write-Host "[OK] Workers: $($manifest.Count)"
Write-Host "[OK] Worker bundle: $workerArchive"
