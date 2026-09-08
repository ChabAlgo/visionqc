$ErrorActionPreference = 'Stop'
$stage = Join-Path ([IO.Path]::GetTempPath()) ('VisionQC-policy-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$target = Join-Path $stage 'PolicyTests.exe'
& $compiler /nologo /target:exe ("/out:$target") (Join-Path $PSScriptRoot '..\Services\GreenRuntimePolicy.cs') (Join-Path $PSScriptRoot 'fixtures\GreenRuntimePolicyTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Policy test compilation failed' }
& $target
if ($LASTEXITCODE -ne 0) { throw 'Policy tests failed' }
