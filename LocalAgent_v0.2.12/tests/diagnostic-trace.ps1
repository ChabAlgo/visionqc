$ErrorActionPreference = 'Stop'
$stage = Join-Path ([IO.Path]::GetTempPath()) ('VisionQC-trace-build-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$target = Join-Path $stage 'TraceTests.exe'
& $compiler /nologo /target:exe ("/out:$target") (Join-Path $PSScriptRoot '..\Services\AgentDiagnostics.cs') (Join-Path $PSScriptRoot '..\Services\DiagnosticTrace.cs') (Join-Path $PSScriptRoot 'fixtures\DiagnosticTraceTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Trace test compilation failed' }
& $target
if ($LASTEXITCODE -ne 0) { throw 'Trace tests failed' }
