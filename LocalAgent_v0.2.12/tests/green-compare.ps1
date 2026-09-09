$ErrorActionPreference = 'Stop'
$taskStage = Join-Path ([IO.Path]::GetTempPath()) ('VisionQC-compare-build-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskStage | Out-Null
$taskCompiler = & (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe') -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
$taskTarget = Join-Path $taskStage 'GreenCompareTests.exe'
& $taskCompiler /nologo /target:exe /main:GreenCompareTests /platform:x64 /langversion:7.3 ("/out:$taskTarget") /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll (Join-Path $PSScriptRoot '..\GreenCompare\Program.cs') (Join-Path $PSScriptRoot 'fixtures\GreenCompareTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compare tests compilation failed' }
& $taskTarget
if ($LASTEXITCODE -ne 0) { throw 'Compare tests failed' }
