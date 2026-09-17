$ErrorActionPreference = 'Stop'
$stage = Join-Path ([IO.Path]::GetTempPath()) ('VisionQC-integrity-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$compiler = & $vswhere -latest -products * -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (-not $compiler) { throw 'C# compiler missing' }
$target = Join-Path $stage 'DataIntegrityTests.exe'
& $compiler /nologo /langversion:7.3 /target:exe /r:System.Drawing.dll ("/out:$target") (Join-Path $PSScriptRoot '..\Services\ResultCsv.cs') (Join-Path $PSScriptRoot '..\Services\WorkspaceIdentity.cs') (Join-Path $PSScriptRoot '..\Services\OverlayGeometry.cs') (Join-Path $PSScriptRoot 'fixtures\DataIntegrityTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Integrity test build failed' }
& $target
if ($LASTEXITCODE -ne 0) { throw 'Integrity tests failed' }
