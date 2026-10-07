param([string]$taskWorkerPath='LocalAgent_v0.2.12/Launcher/bin/x64/Release/Workers/8.0/VisionQC.VpdlWorker.exe')
$ErrorActionPreference='Stop'
$taskRoot=Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../Temp'))) ('copy-cancel-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskRoot)|Out-Null
$taskSource=Join-Path $taskRoot 'source.jpg';[IO.File]::WriteAllText($taskSource,'original bytes')
$taskAssembly=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $taskWorkerPath).Path)
$taskType=$taskAssembly.GetType('VisionQC.LocalAgent.Services.ImageCopyPlan');$flags=[Reflection.BindingFlags]'Instance,NonPublic,Public'
$taskPlan=$taskType.GetConstructors($flags)[0].Invoke(@([string]$taskRoot,'CancelledCopy','미검Cell'))
try {
 $taskType.GetMethod('Add',$flags).Invoke($taskPlan,@([string]$taskSource,'AN(TOP)'))|Out-Null
 $taskType.GetMethod('Add',$flags).Invoke($taskPlan,@([string]$taskSource,'AN(TOP)'))|Out-Null
 $taskCancel=[Threading.CancellationTokenSource]::new();$taskCancel.Cancel()
 $taskResult=$taskType.GetMethod('Copy',$flags).Invoke($taskPlan,@($taskCancel.Token))
 if(-not $taskResult.cancelled -or $taskResult.total-ne1 -or $taskResult.copied-ne0){throw 'Cancel/dedup result mismatch'}
 $taskDirectory=$taskType.GetField('DirectoryPath',$flags).GetValue($taskPlan)
 if([IO.File]::ReadAllText($taskSource)-ne'original bytes'){throw 'Original modified'}
}finally{$taskPlan.Dispose()}
if(Get-ChildItem -LiteralPath $taskDirectory -Filter '.copy-queue*'){throw 'Queue not removed'}
'PASS cancelled copy leaves original unchanged, copies no pending files, deduplicates queue and removes scratch DB.'

$taskLayout=Join-Path $taskRoot 'layout';[IO.Directory]::CreateDirectory($taskLayout)|Out-Null
$taskOther=Join-Path (Join-Path $taskRoot 'other') 'source.jpg';[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($taskOther))|Out-Null;[IO.File]::WriteAllText($taskOther,'different source bytes')
$taskPlan=$taskType.GetConstructors($flags)[0].Invoke(@([string]$taskLayout,'Tool copy','FoilDamage'))
try {
 foreach($entry in @(@($taskSource,'AN(TOP)'),@($taskSource,'AN(TOP)'),@($taskOther,'AN(TOP)'),@($taskSource,'CA(TOP)'))){$taskType.GetMethod('Add',$flags).Invoke($taskPlan,@([string]$entry[0],[string]$entry[1]))|Out-Null}
 $taskResult=$taskType.GetMethod('Copy',$flags).Invoke($taskPlan,@([Threading.CancellationToken]::None))
 if($taskResult.copied-ne3 -or $taskResult.failed-ne0){throw 'Position/path dedup or filename collision failed'}
 $taskAn=Join-Path $taskLayout 'AN(TOP)/FoilDamage';$taskCa=Join-Path $taskLayout 'CA(TOP)/FoilDamage'
 $taskImages=@(Get-ChildItem -LiteralPath $taskAn -File);if($taskImages.Count-ne2){throw 'Same filename originals not retained'}
 $taskContents=@($taskImages|ForEach-Object{[IO.File]::ReadAllText($_.FullName)})
 if($taskContents -notcontains 'original bytes' -or $taskContents -notcontains 'different source bytes'){throw 'Collision contents corrupted'}
 if([IO.File]::ReadAllText((Join-Path $taskCa 'source.jpg'))-ne'original bytes'){throw 'Shared source missing from second Position'}
 if(@(Get-ChildItem -LiteralPath $taskLayout -Directory).Count-ne2){throw 'Unexpected extra export folder'}
}finally{$taskPlan.Dispose()}
if(Get-ChildItem -LiteralPath $taskLayout -Filter '.copy-queue*'){throw 'Layout copy queue not removed'}
'PASS Position/Tool layout, same-name different originals, Position-scoped dedup, bytes and queue cleanup.'

$taskSingleType=$taskAssembly.GetType('VisionQC.LocalAgent.Services.SingleImageCopy')
$taskSingleMethod=$taskSingleType.GetMethod('Copy',[Reflection.BindingFlags]'Static,NonPublic')
$taskSingleDir=Join-Path $taskRoot 'single';[IO.Directory]::CreateDirectory($taskSingleDir)|Out-Null
$taskSingleTarget=Join-Path $taskSingleDir 'source.jpg'
$taskSingleMethod.Invoke($null,@([string]$taskSource,[string]$taskSingleTarget,$false))|Out-Null
if([IO.File]::ReadAllText($taskSingleTarget)-ne'original bytes' -or @(Get-ChildItem -LiteralPath $taskSingleDir).Count-ne1){throw 'Single original image created unexpected files/folders'}
'PASS single-file service creates one original-named file only.'
