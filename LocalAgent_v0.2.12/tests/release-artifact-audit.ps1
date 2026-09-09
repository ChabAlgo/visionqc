$ErrorActionPreference='Stop'
$taskRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$manifest=Get-Content -Raw -LiteralPath (Join-Path $taskRepo 'RELEASE_MANIFEST.json') | ConvertFrom-Json
$installer=Join-Path $taskRepo $manifest.artifacts.agentInstaller
if ((Get-Item -LiteralPath $installer).VersionInfo.FileVersion -ne ($manifest.agentVersion+'.0')) { throw 'Installer version mismatch' }
$assembly=[Reflection.Assembly]::LoadFile($installer)
[xml]$project=Get-Content -Raw -LiteralPath (Join-Path $taskRepo 'LocalAgent_v0.2.12/OfflineInstaller/VisionQC.AgentInstaller.csproj')
$projectRoot=Join-Path $taskRepo 'LocalAgent_v0.2.12/OfflineInstaller'
$count=0
foreach($item in $project.SelectNodes("//*[local-name()='EmbeddedResource']")) {
  $stream=$assembly.GetManifestResourceStream([string]$item.LogicalName)
  if(!$stream){throw "Missing resource $($item.LogicalName)"}
  try {
    $sha=[Security.Cryptography.SHA256]::Create()
    $actual=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')
    $expected=(Get-FileHash -LiteralPath ([IO.Path]::GetFullPath((Join-Path $projectRoot $item.Include))) -Algorithm SHA256).Hash
    if($actual -ne $expected){throw "Stale embedded resource $($item.LogicalName)"}
    $count++
  } finally {$stream.Dispose()}
}
if($count -lt 15){throw 'Insufficient resources checked'}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream=$assembly.GetManifestResourceStream('VisionQC.AgentInstaller.Payload.WorkerBundle.vpdl-workers.zip')
$zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Read)
try {
 $names=@($zip.Entries | ForEach-Object FullName)
 if($names | Where-Object {$_ -match '(?i)GreenCompare|GreenBaseline|nvcuda|ViDi.NET|\.(vrws|vws|sqlite|jpg|png)$'}) {throw 'Forbidden runtime/data/diagnostic payload'}
 foreach($api in @('8.0','8.2','Universal')){
  if(!($names -match ([Regex]::Escape($api)+'[/\\]VisionQC.VpdlWorker.exe$'))){throw "Missing $api Worker"}
  if(!($names -match ([Regex]::Escape($api)+'[/\\]VisionQC.GreenRunner.exe$'))){throw "Missing $api GreenRunner"}
 }
 if(!($names -match 'Core[/\\]VisionQC.CoreWorker.exe$')){throw 'Missing no-VPDL Core Worker'}
} finally {$zip.Dispose();$stream.Dispose()}
$offline=Join-Path $taskRepo $manifest.artifacts.offlinePackage
$zip=[IO.Compression.ZipFile]::OpenRead($offline)
try {
 $expected=@([IO.Path]::GetFileName($installer),('OFFLINE_README_v'+$manifest.webVersion+'_KR.txt'),('RELEASE_NOTES_v'+$manifest.webVersion+'_KR.md'),'VERSION.txt')
 if(@($zip.Entries).Count -ne 4){throw 'Offline allowlist count mismatch'}
 foreach($entry in $zip.Entries){if($entry.FullName -notin $expected){throw 'Unexpected offline payload'}}
} finally {$zip.Dispose()}
foreach($key in @('agentInstaller','offlinePackage')){
 $path=Join-Path $taskRepo $manifest.artifacts.$key
 if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $manifest.artifacts.sha256.$key){throw "Manifest hash mismatch $key"}
}
Write-Output "PASS release audit | resources=$count | API=8.0,8.2,Universal,Core | No data/Cognex DLL/comparison executables"
