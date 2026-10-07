$ErrorActionPreference='Stop'
$taskRoot=Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../Temp'))) ('copy-cancel-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskRoot)|Out-Null
$taskSource=Join-Path $taskRoot 'source.jpg';[IO.File]::WriteAllText($taskSource,'original bytes')
$taskAssembly=[Reflection.Assembly]::LoadFrom((Resolve-Path 'LocalAgent_v0.2.12/Launcher/bin/x64/Release/Workers/8.0/VisionQC.VpdlWorker.exe').Path)
$taskType=$taskAssembly.GetType('VisionQC.LocalAgent.Services.ImageCopyPlan');$flags=[Reflection.BindingFlags]'Instance,NonPublic,Public'
$taskPlan=$taskType.GetConstructors($flags)[0].Invoke(@([string]$taskRoot,'CancelledCopy'))
try {
 $taskType.GetMethod('Add',$flags).Invoke($taskPlan,@([string]$taskSource,'AN(TOP)'))|Out-Null
 $taskType.GetMethod('Add',$flags).Invoke($taskPlan,@([string]$taskSource,'AN(TOP)'))|Out-Null
 $taskCancel=[Threading.CancellationTokenSource]::new();$taskCancel.Cancel()
 $taskResult=$taskType.GetMethod('Copy',$flags).Invoke($taskPlan,@($taskCancel.Token))
 if(-not $taskResult.cancelled -or $taskResult.total-ne1 -or $taskResult.copied-ne0){throw 'Cancel/dedup result mismatch'}
 $taskDirectory=$taskType.GetField('DirectoryPath',$flags).GetValue($taskPlan)
 if([IO.File]::ReadAllText($taskSource)-ne'original bytes'){throw 'Original modified'}
}finally{$taskPlan.Dispose()}
if(Test-Path (Join-Path $taskDirectory '.copy-queue.sqlite')){throw 'Queue not removed'}
'PASS cancelled copy leaves original unchanged, copies no pending files, deduplicates queue and removes scratch DB.'
