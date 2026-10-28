param([Parameter(Mandatory=$true)][string]$ScratchRoot,[int]$Rows=100000,[switch]$Baseline)
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath($ScratchRoot)
$taskAllowed=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../Temp')).TrimEnd('\')+'\'
if (!$taskRoot.StartsWith($taskAllowed,[StringComparison]::OrdinalIgnoreCase)) {throw 'Test path must stay within project Temp'}
New-Item -ItemType Directory -Path $taskRoot -Force | Out-Null
$built=Join-Path $PSScriptRoot '../CoreWorker/bin/x64/Release'
Copy-Item -LiteralPath (Join-Path $built 'System.Data.SQLite.dll') -Destination $taskRoot
Copy-Item -LiteralPath (Join-Path $built 'x64') -Destination $taskRoot -Recurse -Force
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$compiler=& $vswhere -latest -products * -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
$sources=@('AgentDtos.cs','CoreWorker/CoreModels.cs','Domain/NamingProfile.cs','Services/NamingProfileParser.cs','Services/ResultCsv.cs','Services/PartitionedCsvWriter.cs','Services/CsvHistoryFileImporter.cs','Services/XlsxResultReader.cs','Services/PositionAliases.cs','Services/ExportSupport.cs','Services/AnalysisService.cs','Persistence/SqliteRunStore.cs','Persistence/AnalysisProjection.cs') | ForEach-Object {Join-Path $PSScriptRoot "../$_"}
if($Baseline){$old=Join-Path $taskRoot 'SqliteRunStore.baseline.cs';$content=& git show '475fed7:LocalAgent_v0.2.12/Persistence/SqliteRunStore.cs';if($LASTEXITCODE -ne 0){throw 'Baseline source unavailable'};[IO.File]::WriteAllLines($old,$content);$sources=$sources | ForEach-Object {if($_.Replace('\','/').EndsWith('Persistence/SqliteRunStore.cs')){$old}else{$_}}}
$target=Join-Path $taskRoot 'StoragePerformanceTests.exe'
& $compiler /nologo /langversion:7.3 /platform:x64 /target:exe /main:StoragePerformanceTests /r:System.Data.dll /r:System.Xml.Linq.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Web.Extensions.dll ("/r:"+(Join-Path $taskRoot 'System.Data.SQLite.dll')) ("/out:$target") $sources (Join-Path $PSScriptRoot 'fixtures/HistoryIntegrityTests.cs') (Join-Path $PSScriptRoot 'fixtures/StoragePerformanceTests.cs')
if($LASTEXITCODE -ne 0){throw 'Storage performance fixture build failed'}
& $target (Join-Path $taskRoot 'data') $Rows
if($LASTEXITCODE -ne 0){throw 'Storage performance checks failed'}
