param([string]$Repository='D:/Projects/opticalsystem',
 [string]$Study='artifacts/validation/incomparable-grid-native-current-runner-20261101')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath $Repository).Path
$root=(Resolve-Path -LiteralPath (Join-Path $repo $Study)).Path
$changes=@();$deadline=[DateTimeOffset]::UtcNow.AddHours(1)
foreach($index in 499,502){
 $coarse="grid-$index-8192-step.02" # Historical directory ID; actual probe sampling is 2048.
 $fine="grid-$index-8192-step.005"
 $receipt="$root/job-$coarse/execution.json"
 $execution=$null
 while($null -eq $execution){
  if([DateTimeOffset]::UtcNow -ge $deadline){throw 'Coarse native probe did not complete within refinement deadline'}
  if(Test-Path -LiteralPath $receipt){try{$execution=@(Get-Content -LiteralPath $receipt -Raw|ConvertFrom-Json)[-1]}catch{}}
  if($null -eq $execution){Start-Sleep -Milliseconds 100}
 }
 if(!$execution.sourceUnchanged){throw 'Source integrity cannot be repaired by numerical sampling'}
 $dataPath="$root/job-$coarse/$coarse/data.json"
 $data=if(Test-Path -LiteralPath $dataPath){Get-Content -LiteralPath $dataPath -Raw|ConvertFrom-Json}else{$null}
 $valid=$execution.result.ExitCode -eq 0 -and $null -ne $data -and @($data.dataSeries).Count -gt 0 -and @($data.messages).Count -eq 0
 if($valid -or $execution.result.TimedOut){
  $changes+=@{index=$index;action='KeepPendingSampling';coarseValid=$valid;coarseTimedOut=$execution.result.TimedOut}
 }else{
  if(Test-Path -LiteralPath "$root/job-$fine"){throw 'Cannot revise a started native request'}
  $path="$root/$fine.request.json";$request=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
  $before=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
  $request.pupilSampling=8192
  if($request.adapter -eq 'contract'){$request.zemaxSettings.SampleSize='S_8192x8192'}
  $temporary="$path.pending"
  $request|ConvertTo-Json -Depth 40|Set-Content -LiteralPath $temporary -Encoding utf8
  [IO.File]::Move($temporary,$path,$true)
  $manifest=Get-Content -LiteralPath "$root/jobs.json" -Raw|ConvertFrom-Json
  ($manifest|Where-Object id -eq $fine).effectivePupilSampling=8192
  $manifest|ConvertTo-Json -AsArray -Depth 12|Set-Content -LiteralPath "$root/jobs.json" -Encoding utf8
  $changes+=@{index=$index;action='RefineUnstartedRequest';fromSampling=2048;toSampling=8192;
   beforeRequestSha256=$before;afterRequestSha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant();
   receipt=$receipt;coarseExitCode=$execution.result.ExitCode;changedUtc=[DateTimeOffset]::UtcNow;
   reason='No valid native coarse curve; test finer numerical sampling with unchanged physical prescription and gates'}
 }
 $changes|ConvertTo-Json -AsArray -Depth 12|Set-Content -LiteralPath "$root/conditional-sampling-revision.json" -Encoding utf8
}
