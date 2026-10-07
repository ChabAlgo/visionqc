$ErrorActionPreference='Stop'
$taskRoot=Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../Temp'))) ('exclusion-test-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskRoot)|Out-Null
foreach($name in @('Delet','dElEt\nested','Discard\nested','normal','DeletBackup')){ $dir=Join-Path $taskRoot $name;[IO.Directory]::CreateDirectory($dir)|Out-Null;[IO.File]::WriteAllText((Join-Path $dir 'image.jpg'),'fixture') }
$taskAssembly=[Reflection.Assembly]::LoadFrom((Resolve-Path 'LocalAgent_v0.2.12/Launcher/bin/x64/Release/Workers/8.0/VisionQC.VpdlWorker.exe').Path)
$taskType=$taskAssembly.GetType('VpdlGreenHeatmapOverlay.InspectionFiles')
$taskMethod=$taskType.GetMethod('Enumerate',[Reflection.BindingFlags]'Static,NonPublic')
$taskNames=[string[]]@('Delet','Discard')
$taskFiles=@($taskMethod.Invoke($null,@([string]$taskRoot,$taskNames)))
if($taskFiles.Count-ne2){throw "Expected 2 retained images; found $($taskFiles.Count)"}
$taskExcluded=@($taskMethod.Invoke($null,@([string](Join-Path $taskRoot 'Delet'),$taskNames)))
if($taskExcluded.Count-ne0){throw 'Excluded root was enumerated'}
'PASS: default/custom nested folders excluded, case insensitive, similarly named folders retained, explicitly selected excluded root skipped.'
'Fixture: '+$taskRoot

$taskOnlyCustom=@($taskMethod.Invoke($null,@([string]$taskRoot,[string[]]@('Discard'))))
if($taskOnlyCustom.Count-ne4){throw 'Deleted Delet exclusion still filtered'}
$taskNone=@($taskMethod.Invoke($null,@([string]$taskRoot,[string[]]@())))
if($taskNone.Count-ne5){throw 'Explicit empty exclusions not respected'}
'PASS: removed names and empty exclusion list are honored.'
