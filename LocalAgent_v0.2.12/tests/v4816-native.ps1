param([string]$ScratchRoot)
$ErrorActionPreference='Stop'
if (!$ScratchRoot) { throw 'Explicit synthetic test directory required' }
$taskRoot=[IO.Path]::GetFullPath($ScratchRoot)
$taskAllowed=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../Temp')).TrimEnd('\')+'\'
if (!$taskRoot.StartsWith($taskAllowed,[StringComparison]::OrdinalIgnoreCase)) {throw 'Test path must stay within project Temp'}
New-Item -ItemType Directory -Path $taskRoot -Force | Out-Null
Add-Type -Path (Join-Path $PSScriptRoot '../OfflineInstaller/DesktopShortcut.cs')
$shortcutType=([AppDomain]::CurrentDomain.GetAssemblies() | ForEach-Object {$_.GetType('VisionQC.AgentInstaller.DesktopShortcut')} | Where-Object {$_} | Select-Object -First 1)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$target=Join-Path $taskRoot 'VisionQC.LocalAgent.exe'
[IO.File]::WriteAllText($target,'synthetic non-executable placeholder')
$shortcut=Join-Path $taskRoot 'VisionQC 오프라인 실행.lnk'
$shortcutType.GetMethod('Create',$flags).Invoke($null,[object[]]@([string]$shortcut,[string]$target,[string]$taskRoot)) | Out-Null
$shell=New-Object -ComObject WScript.Shell
$link=$shell.CreateShortcut($shortcut)
if ($link.TargetPath -ne $target -or $link.Arguments -ne '--offline' -or $link.WorkingDirectory -ne $taskRoot) {throw 'Shortcut properties mismatch'}
Write-Output 'PASS native ShellLink target, arguments, working directory'
Add-Type -Path (Join-Path $PSScriptRoot '../Services/AgentDiagnostics.cs')
$diagnosticsType=([AppDomain]::CurrentDomain.GetAssemblies() | ForEach-Object {$_.GetType('VisionQC.LocalAgent.Services.AgentDiagnostics')} | Where-Object {$_} | Select-Object -First 1)
$taskAgentHome=Join-Path $taskRoot 'agent';$taskWorkerHome=Join-Path $taskRoot 'worker';$logs=Join-Path $taskWorkerHome 'logs'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $logs 'agent-worker.log'),'SDK_FAIL Workspace.Load: Failed to read message')
[IO.File]::WriteAllText((Join-Path $logs 'last-sdk-failure.txt'),'original stack')
[IO.File]::WriteAllText((Join-Path $taskWorkerHome 'image.png'),'must not be archived')
$diagnosticsType.GetMethod('Initialize',$flags).Invoke($null,[object[]]@([string]$taskAgentHome,'test','1.4.16')) | Out-Null
for($i=0;$i -lt 14;$i++) {$diagnosticsType.GetMethod('PreserveWorkerFailure',$flags).Invoke($null,[object[]]@([string]$taskWorkerHome,'TAN | GPU=0',[Exception]::new('synthetic failure'))) | Out-Null}
$archives=@(Get-ChildItem -LiteralPath (Join-Path $taskAgentHome 'logs/runtime-failures') -Directory)
if($archives.Count -ne 12){throw 'Failure archive retention mismatch'}
foreach($archive in $archives){
 if(!(Test-Path -LiteralPath (Join-Path $archive.FullName 'worker-last-sdk-failure.txt'))){throw 'Missing SDK detail'}
 if(!(Test-Path -LiteralPath (Join-Path $archive.FullName 'worker-agent-worker.log'))){throw 'Missing worker log'}
 if(Test-Path -LiteralPath (Join-Path $archive.FullName 'image.png')){throw 'Unexpected image archive'}
}
if(!(Test-Path -LiteralPath (Join-Path $taskWorkerHome 'image.png'))){throw 'Source data was modified'}
Write-Output 'PASS native failure archive content, original preservation and 12-archive retention'
$fixtureInstall=Join-Path $taskRoot 'synthetic-install'
if (![IO.Path]::GetFullPath($fixtureInstall).StartsWith($taskRoot.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) {throw 'Fixture escaped scratch root'}
foreach($name in @('data','logs','output','Workers','Web')){
 New-Item -ItemType Directory -Path (Join-Path $fixtureInstall $name) -Force | Out-Null
 [IO.File]::WriteAllText((Join-Path $fixtureInstall ($name+'/fixture.txt')),'sentinel')
}
[IO.File]::WriteAllText((Join-Path $fixtureInstall 'old-agent.exe'),'old placeholder')
[IO.File]::WriteAllText((Join-Path $fixtureInstall 'vpdl-worker.version'),'8.2')
$installerAssembly=[Reflection.Assembly]::LoadFile([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../downloads/VisionQC_Agent_Installer_v1.4.16.exe')))
$installerAssembly.GetType('VisionQC.AgentInstaller.Program').GetMethod('RemovePreviousInstallation',$flags).Invoke($null,[object[]]@([string]$fixtureInstall)) | Out-Null
foreach($name in @('data','logs','output')){if(!(Test-Path -LiteralPath (Join-Path $fixtureInstall ($name+'/fixture.txt')))){throw "Installer deleted preserved $name"}}
if(!(Test-Path -LiteralPath (Join-Path $fixtureInstall 'vpdl-worker.version'))){throw 'Installer deleted VPDL preference'}
foreach($name in @('Workers','Web','old-agent.exe')){if(Test-Path -LiteralPath (Join-Path $fixtureInstall $name)){throw "Installer left old application $name"}}
Write-Output 'PASS installer replacement removes synthetic old application and preserves data/logs/output/VPDL preference'
