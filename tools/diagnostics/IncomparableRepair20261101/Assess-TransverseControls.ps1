param([Parameter(Mandatory)][string]$Study)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $Study).Path
$output=Join-Path $root 'native-core-chief-assessment.json'
if(Test-Path -LiteralPath $output){throw 'A completed assessment must not be overwritten'}
$core=Get-Content -LiteralPath "$root/shared-core-chief.json" -Raw|ConvertFrom-Json
$jobs=Get-Content -LiteralPath "$root/jobs.json" -Raw|ConvertFrom-Json
$execution=Get-Content -LiteralPath "$root/execution.json" -Raw|ConvertFrom-Json
if(@($execution).Count -ne @($jobs).Count -or @($execution|Where-Object{
 $_.result.ExitCode -ne 0 -or $_.result.TimedOut -or !$_.sourceUnchanged}).Count){throw 'Native controls are incomplete or invalid'}
$records=@(foreach($record in $core.records){
 $model=$record.model;$path=Join-Path $root "$($model.id)/model-controls.json"
 if((Get-FileHash -LiteralPath $model.input).Hash.ToLowerInvariant() -ne $model.sourceSha256){throw 'Control source drift'}
 if((Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($model.input,'.ggd'))).Hash.ToLowerInvariant() -ne $model.gridSha256){throw 'Control grid dependency drift'}
 $rows=(Get-Content -LiteralPath $path -Raw|ConvertFrom-Json).rows
 function Value([string]$code,[int]$surface){
  $values=@($rows|Where-Object{$_.input.code -eq $code -and $_.input.int1 -eq $surface})
  if($values.Count -ne 1){throw 'One native chief operand required'}
  return [double]$values[0].value
 }
 $body=(Value 'OPTH' 2)-(Value 'OPTH' 1)
 foreach($surface in 1,2){
  foreach($code in 'REAX','REAY','REAA','REAB'){if([Math]::Abs((Value $code $surface)) -gt 1e-12){throw 'Chief is not straight; index-only inference is invalid'}}
  if([Math]::Abs((Value 'REAC' $surface)-1) -gt 1e-12){throw 'Native chief is not axial'}
 }
 $length=[double]$model.bodyLength
 [pscustomobject]@{id=$model.id;kind=$model.kind;nodes=$model.nodes;nativeBodyOpticalPath=$body
  coreBodyOpticalPath=$record.bodyPath;nativeMinusCore=$body-$record.bodyPath
  nativeImpliedTransverseIndex=($body-.015*$length*$length/2)/$length
  coreVolumeIndexAtOrigin=$record.volumeIndexAtOrigin;coreInterfaceIndexAtOrigin=$record.interfaceIndexAtOrigin
  nativeStandaloneIndex=(Value 'INDX' 1);sourceSha256=$model.sourceSha256;gridSha256=$model.gridSha256
  nativeControlsSha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
  diagnosticOnly=$true;acceptanceSubstitution=$false}
})
@{records=$records;coreSha256=$core.coreSha256;diagnosticOnly=$true;gatesChanged=$false}|ConvertTo-Json -Depth 10|
 Set-Content -LiteralPath $output -Encoding utf8
$records|ConvertTo-Json -Depth 7
