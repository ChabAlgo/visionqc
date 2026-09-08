param([Parameter(Mandatory=$true)][string]$LauncherPath)
$ErrorActionPreference = 'Stop'
$stage = Join-Path ([IO.Path]::GetTempPath()) ('VisionQC-launcher-test-' + [guid]::NewGuid().ToString('N'))
$core = Join-Path $stage 'Workers\Core'
$empty = Join-Path $stage 'NoVpdl'
New-Item -ItemType Directory -Path $core,$empty -Force | Out-Null
Copy-Item -LiteralPath $LauncherPath -Destination (Join-Path $stage 'VisionQC.LocalAgent.exe')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 ("/out:$core\VisionQC.CoreWorker.exe") (Join-Path $PSScriptRoot 'fixtures\LifecycleWorker.cs')
if ($LASTEXITCODE -ne 0) { throw 'Lifecycle fixture compilation failed' }
$previous = $env:COGNEX_VPDL_ROOT
try {
    $env:COGNEX_VPDL_ROOT = $empty
    $process = Start-Process -FilePath (Join-Path $stage 'VisionQC.LocalAgent.exe') -ArgumentList '--offline' -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(20000)) { $process.Kill(); throw 'Lifecycle test timed out' }
    $starts = @(Get-Content -LiteralPath (Join-Path $stage 'worker-starts.txt'))
    if ($starts.Count -ne 3) { throw "Expected 3 starts, got $($starts.Count)" }
    if ($starts[0] -notmatch '--offline') { throw 'Initial offline launch did not request browser' }
    if ($starts[1] -match '--offline' -or $starts[2] -match '--offline') { throw 'Recovery reopened the browser' }
    $logs = Get-ChildItem -LiteralPath (Join-Path $stage 'logs') -Filter 'agent-launcher-*.log'
    $text = ($logs | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join ''
    if ($text -notmatch 'SDK_LIFECYCLE_PROBE_2' -or $text -notmatch '0x0000002A') { throw 'SDK output or native exit code was not recorded' }
    [pscustomobject]@{Passed=$true;WorkerStarts=$starts.Count;OfflineLaunches=@($starts | Where-Object {$_ -match '--offline'}).Count;SdkOutputLogged=$true;ReportDirectory=$stage} | ConvertTo-Json -Compress
} finally {
    if ($null -eq $previous) { Remove-Item Env:COGNEX_VPDL_ROOT -ErrorAction SilentlyContinue } else { $env:COGNEX_VPDL_ROOT = $previous }
}
