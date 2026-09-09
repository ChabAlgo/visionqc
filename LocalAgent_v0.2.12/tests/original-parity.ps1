param(
    [Parameter(Mandatory=$true)][string]$OriginalZip,
    [Parameter(Mandatory=$true)][string]$AgentReportRoot
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$taskStage = Join-Path ([IO.Path]::GetTempPath()) ('VisionQC-original-parity-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskStage | Out-Null
$taskStudio = 'C:\Program Files\Cognex\VisionPro Deep Learning\4.0\Cognex Deep Learning Studio'
$taskArchive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $OriginalZip))
$taskHashes = @{}
try {
    foreach ($taskName in @('GreenOverlayProcessor.cs','Models.cs')) {
        $taskEntries = @($taskArchive.Entries | Where-Object { $_.Name -eq $taskName })
        if ($taskEntries.Count -ne 1) { throw "Ambiguous original source: $taskName" }
        $taskDest = Join-Path $taskStage $taskName
        [IO.Compression.ZipFileExtensions]::ExtractToFile($taskEntries[0], $taskDest)
        $taskHashes[$taskName] = (Get-FileHash -LiteralPath $taskDest -Algorithm SHA256).Hash
    }
} finally { $taskArchive.Dispose() }
$taskCompiler = & (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe') -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
$taskRefs = @('System.Core.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xml.Linq.dll','Microsoft.CSharp.dll') | ForEach-Object { '/r:' + $_ }
$taskRefs += '/r:' + (Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades\netstandard.dll')
foreach ($taskDll in @('ViDi.NET','ViDi.NET.Base','ViDi.NET.Common','ViDi.NET.Interfaces','ViDi.NET.Local')) {
    $taskRefs += '/r:' + (Join-Path $taskStudio ($taskDll + '.dll'))
}
$taskDrawing = Join-Path $taskRepo 'LocalAgent_v0.2.12\lib\System.Drawing.Common.dll'
$taskRefs += '/r:' + $taskDrawing
Copy-Item -LiteralPath $taskDrawing -Destination $taskStage
$taskExe = Join-Path $taskStage 'OriginalParity.exe'
& $taskCompiler /nologo /target:winexe /platform:x64 /optimize+ /langversion:7.3 ("/out:$taskExe") $taskRefs (Join-Path $taskStage 'Models.cs') (Join-Path $taskStage 'GreenOverlayProcessor.cs') (Join-Path $PSScriptRoot 'fixtures\OriginalEngineParity.cs')
if ($LASTEXITCODE -ne 0) { throw 'Unmodified original build failed' }
Copy-Item -LiteralPath (Join-Path $taskRepo 'LocalAgent_v0.2.12\GreenRunner\App.config') -Destination ($taskExe + '.config')
$taskInput = Get-ChildItem -LiteralPath (Join-Path $AgentReportRoot 'logs\green-runs') -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$taskConfig = Get-Content -LiteralPath (Join-Path $taskInput.FullName 'request.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$taskRuns = @{}
foreach ($taskMode in @('original','standalone')) {
    $taskDir = Join-Path $taskStage $taskMode
    New-Item -ItemType Directory -Path $taskDir | Out-Null
    $taskConfig.OutputRoot = Join-Path $taskDir 'output'
    $taskConfig | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath (Join-Path $taskDir 'request.json') -Encoding UTF8
    $taskInfo = New-Object Diagnostics.ProcessStartInfo
    $taskInfo.FileName = if ($taskMode -eq 'original') {$taskExe} else {Join-Path $taskRepo 'LocalAgent_v0.2.12\Launcher\bin\x64\Release\Workers\8.0\VisionQC.GreenRunner.exe'}
    $taskRequest = Join-Path $taskDir 'request.json'
    $taskResult = Join-Path $taskDir 'result.json'
    $taskInfo.Arguments = if ($taskMode -eq 'original') {'"'+$taskRequest+'" "'+$taskResult+'"'} else {'--request "'+$taskRequest+'"'}
    $taskInfo.WorkingDirectory = $taskDir
    $taskInfo.UseShellExecute = $false
    $taskInfo.CreateNoWindow = $true
    $taskInfo.WindowStyle = 'Hidden'
    $taskInfo.EnvironmentVariables['COGNEX_VPDL_DLL_DIR'] = $taskStudio
    $taskInfo.EnvironmentVariables['VISIONQC_VPDL_API_VERSION'] = '8.0'
    $taskInfo.EnvironmentVariables['VISIONQC_AGENT_HOME'] = $taskDir
    $taskProcess = [Diagnostics.Process]::Start($taskInfo)
    if (-not $taskProcess.WaitForExit(180000)) { $taskProcess.Kill(); throw "$taskMode timed out" }
    if ($taskProcess.ExitCode -ne 0) { throw "$taskMode failed: $($taskProcess.ExitCode); $taskDir" }
    $taskRuns[$taskMode] = Get-Content -LiteralPath $taskResult -Raw -Encoding UTF8 | ConvertFrom-Json
}
$taskRuns['agent'] = Get-Content -LiteralPath (Join-Path $taskInput.FullName 'result.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$taskCases = 0; $taskMaxDelta = 0.0
foreach ($taskPosition in $taskRuns.original.SlotCsvPaths.PSObject.Properties.Name) {
    $taskOriginalRows = @(Import-Csv -LiteralPath $taskRuns.original.SlotCsvPaths.$taskPosition)
    foreach ($taskMode in @('standalone','agent')) {
        $taskRows = @(Import-Csv -LiteralPath $taskRuns[$taskMode].SlotCsvPaths.$taskPosition)
        if ($taskOriginalRows.Count -ne $taskRows.Count) { throw "Row count differs: $taskMode $taskPosition" }
        foreach ($taskRow in $taskOriginalRows) {
            $taskMatched = @($taskRows | Where-Object { $_.FullPath -eq $taskRow.FullPath -and $_.Position -eq $taskRow.Position })
            if ($taskMatched.Count -ne 1) { throw 'Image identity mismatch' }
            foreach ($taskColumn in $taskRow.PSObject.Properties.Name) {
                if ($taskColumn -match '_score$') {
                    $taskDelta = [Math]::Abs([double]$taskMatched[0].$taskColumn - [double]$taskRow.$taskColumn)
                    $taskMaxDelta = [Math]::Max($taskMaxDelta,$taskDelta)
                    if ($taskDelta -gt 0.0001) { throw "Score changed: $taskColumn $taskDelta" }
                    $taskCases++
                } elseif ($taskColumn -match '_result$|^Judgement$|^Cell ID$') {
                    if ($taskMatched[0].$taskColumn -ne $taskRow.$taskColumn) { throw "Decision changed: $taskColumn" }
                }
            }
        }
    }
}
$taskReport = @{success=$true; comparisons=$taskCases; maxScoreDelta=$taskMaxDelta; sourceSHA256=$taskHashes; originalZipSHA256=(Get-FileHash -LiteralPath $OriginalZip).Hash; runs=$taskRuns}
$taskReport | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $taskStage 'parity-report.json') -Encoding UTF8
Write-Host "PARITY PASS $taskCases score comparisons, max delta $taskMaxDelta; $taskStage\parity-report.json"
