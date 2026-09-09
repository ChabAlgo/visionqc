param([Parameter(Mandatory=$true)][string]$BaselineRoot)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$stage=Join-Path ([IO.Path]::GetTempPath()) ('VisionQC-repeat-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$results=@{}
$baselineResult=Get-Content -Raw -LiteralPath (Join-Path $BaselineRoot 'original/result.json') | ConvertFrom-Json
$results['original-first']=$baselineResult
$results['current-first']=Get-Content -Raw -LiteralPath (Join-Path $BaselineRoot 'standalone/result.json') | ConvertFrom-Json
foreach($mode in @('original-repeat','previous-release','current-repeat')) {
 $folder=Join-Path $stage $mode
 New-Item -ItemType Directory -Path $folder | Out-Null
 $config=Get-Content -Raw -LiteralPath (Join-Path $BaselineRoot 'original/request.json') | ConvertFrom-Json
 $config.OutputRoot=Join-Path $folder 'output'
 $request=Join-Path $folder 'request.json'
 $config | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $request -Encoding UTF8
 $exe=switch($mode) {
  'original-repeat' { Join-Path $BaselineRoot 'OriginalParity.exe' }
  'previous-release' { Join-Path $env:LOCALAPPDATA 'VisionQC/LocalAgent/Workers/8.0/VisionQC.GreenRunner.exe' }
  'current-repeat' { Join-Path $repo 'LocalAgent_v0.2.12/Launcher/bin/x64/Release/Workers/8.0/VisionQC.GreenRunner.exe' }
 }
 $start=[Diagnostics.ProcessStartInfo]::new()
 $start.FileName=$exe;$start.WorkingDirectory=$folder;$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden'
 $start.Arguments=if($mode -eq 'original-repeat') {'"'+$request+'" "'+(Join-Path $folder 'result.json')+'"'}else{'--request "'+$request+'"'}
 $start.EnvironmentVariables['COGNEX_VPDL_DLL_DIR']='C:\Program Files\Cognex\VisionPro Deep Learning\4.0\Cognex Deep Learning Studio'
 $start.EnvironmentVariables['VISIONQC_VPDL_API_VERSION']='8.0'
 $start.EnvironmentVariables['VISIONQC_AGENT_HOME']=$folder
 $p=[Diagnostics.Process]::Start($start)
 if(!$p.WaitForExit(180000)){$p.Kill();throw 'Repeat timed out'}
 if($p.ExitCode -ne 0){throw "$mode exited $($p.ExitCode)"}
 $results[$mode]=Get-Content -Raw -LiteralPath (Join-Path $folder 'result.json') | ConvertFrom-Json
 Write-Output "RUN $mode completed"
}
$comparisons=@()
foreach($mode in $results.Keys | Where-Object {$_ -ne 'original-first'}) {
 $max=0.0;$count=0;$changes=0
 foreach($pos in $baselineResult.SlotCsvPaths.PSObject.Properties.Name) {
  $a=@(Import-Csv -LiteralPath $baselineResult.SlotCsvPaths.$pos)
  $b=@(Import-Csv -LiteralPath $results[$mode].SlotCsvPaths.$pos)
  if($a.Count -ne $b.Count){throw 'Row count mismatch'}
  foreach($row in $a) {
   $match=@($b | Where-Object {$_.FullPath -eq $row.FullPath -and $_.Position -eq $row.Position})
   if($match.Count -ne 1){throw 'Identity mismatch'}
   foreach($col in $row.PSObject.Properties.Name) {
    if($col -match '_score$'){$max=[Math]::Max($max,[Math]::Abs([double]$row.$col-[double]$match[0].$col));$count++}
    elseif($col -match '_result$|^Judgement$|^Cell ID$') {if($row.$col -ne $match[0].$col){$changes++}}
   }
  }
 }
 $comparisons+= [pscustomobject]@{mode=$mode;scoreCount=$count;maxScoreDelta=$max;decisionChanges=$changes}
}
$comparisons | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'repeatability.json') -Encoding UTF8
$comparisons | Format-Table
Write-Output "REPORT $stage"
if($comparisons | Where-Object decisionChanges -gt 0){throw 'Decision regression'}
