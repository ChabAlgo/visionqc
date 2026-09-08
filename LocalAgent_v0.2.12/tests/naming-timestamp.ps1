$ErrorActionPreference = 'Stop'
$stage = Join-Path ([IO.Path]::GetTempPath()) ('VisionQC-naming-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$compiler = & $vswhere -latest -products * -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (-not $compiler) { throw 'C# compiler missing' }
$target = Join-Path $stage 'NamingTimestampTests.exe'
& $compiler /nologo /langversion:7.3 /target:exe ("/out:$target") (Join-Path $PSScriptRoot '..\Domain\NamingProfile.cs') (Join-Path $PSScriptRoot '..\Services\NamingProfileParser.cs') (Join-Path $PSScriptRoot 'fixtures\NamingTimestampTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Naming test build failed' }
& $target
if ($LASTEXITCODE -ne 0) { throw 'Naming tests failed' }
