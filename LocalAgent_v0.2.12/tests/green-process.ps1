$ErrorActionPreference = 'Stop'
$taskStage = Join-Path ([IO.Path]::GetTempPath()) ('VisionQC-pipe-build-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskStage | Out-Null
$taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$taskCompiler = & $taskVsWhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
$taskStudio = 'C:\Program Files\Cognex\VisionPro Deep Learning\4.0\Cognex Deep Learning Studio'
$taskReferences = @('System.Core.dll','System.Drawing.dll','System.Web.Extensions.dll') | ForEach-Object { '/r:' + $_ }
$taskReferences += '/r:' + (Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades\netstandard.dll')
foreach ($taskDll in @('ViDi.NET','ViDi.NET.Base','ViDi.NET.Common','ViDi.NET.Interfaces','ViDi.NET.Local')) {
  $taskReferences += '/r:' + (Join-Path $taskStudio ($taskDll + '.dll'))
  Copy-Item -LiteralPath (Join-Path $taskStudio ($taskDll + '.dll')) -Destination $taskStage
}
$taskTarget = Join-Path $taskStage 'GreenProcessTests.exe'
$taskSources = @('../Services/AgentDiagnostics.cs','../Services/GreenProcessHost.cs','../Services/GreenProcessProtocol.cs','../Engine/Models.cs','../Domain/NamingProfile.cs','fixtures/GreenProcessTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $taskCompiler /nologo /target:exe /platform:x64 /langversion:7.3 ("/out:$taskTarget") $taskReferences $taskSources
if ($LASTEXITCODE -ne 0) { throw 'Green process test compilation failed' }
& $taskTarget
if ($LASTEXITCODE -ne 0) { throw 'Green process tests failed' }
