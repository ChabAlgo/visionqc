param([Parameter(Mandatory=$true)][string]$ScratchRoot)
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath($ScratchRoot)
$taskAllowed=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../Temp')).TrimEnd('\')+'\'
if(!$taskRoot.StartsWith($taskAllowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Test path must stay within project Temp'}
New-Item -ItemType Directory -Path $taskRoot -Force | Out-Null
$target=Join-Path $taskRoot 'EventWriterTests.exe'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler /nologo /target:exe ("/out:$target") (Join-Path $PSScriptRoot '../Services/BoundedEventWriter.cs') (Join-Path $PSScriptRoot 'fixtures/EventWriterTests.cs')
if($LASTEXITCODE -ne 0){throw 'Event fixture build failed'}
& $target
if($LASTEXITCODE -ne 0){throw 'Event writer checks failed'}
