$ErrorActionPreference='Stop'
$stage=Join-Path ([IO.Path]::GetTempPath()) ('VisionQC-history-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$built=Join-Path $PSScriptRoot '../CoreWorker/bin/x64/Release'
Copy-Item -LiteralPath (Join-Path $built 'System.Data.SQLite.dll') -Destination $stage
Copy-Item -LiteralPath (Join-Path $built 'x64') -Destination $stage -Recurse
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$compiler=& $vswhere -latest -products * -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
$sources=@('AgentDtos.cs','CoreWorker/CoreModels.cs','Domain/NamingProfile.cs','Services/NamingProfileParser.cs','Services/ResultCsv.cs','Services/CsvHistoryFileImporter.cs','Persistence/SqliteRunStore.cs') | ForEach-Object {Join-Path $PSScriptRoot "../$_"}
$target=Join-Path $stage 'HistoryIntegrityTests.exe'
& $compiler /nologo /langversion:7.3 /platform:x64 /target:exe /r:System.Data.dll /r:System.Web.Extensions.dll ("/r:"+(Join-Path $stage 'System.Data.SQLite.dll')) ("/out:$target") $sources (Join-Path $PSScriptRoot 'fixtures/HistoryIntegrityTests.cs')
if($LASTEXITCODE -ne 0){throw 'History integrity build failed'}
& $target
if($LASTEXITCODE -ne 0){throw 'History integrity failed'}
