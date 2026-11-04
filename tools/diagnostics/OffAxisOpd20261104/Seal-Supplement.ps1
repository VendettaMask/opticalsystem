param([string]$Repository='D:\Projects\opticalsystem')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($Repository);$artifact=Join-Path $repo 'artifacts/validation';$root=Join-Path $repo 'validation/rechecks/2026-11-04/offaxis-opd-sampling';$run=Join-Path $artifact 'offaxis-opd-extra-edge-20261104';$runtime=Join-Path $artifact 'offaxis-opd-final-runtime-20261104'
function Read($p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function Hash($p){(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
function Save($name,$v){[IO.File]::WriteAllText((Join-Path $root $name),(ConvertTo-Json -InputObject $v -Depth 90),[Text.UTF8Encoding]::new($false))}
$summary=Read "$root/summary.json";$core=$summary.coreAfter;$results=@(Read "$run/results.json")
if($results.Count -ne 99 -or @($results|Where-Object {$_.conclusion -ne 'Pass' -or $_.coreSha256 -ne $core}).Count){throw 'Extra edge controls incomplete'}
$pass=@{};foreach($r in Import-Csv "$repo/validation/rechecks/2026-11-04/all-example-accuracy/request-results.csv"){if($r.analysis -eq 'Optical Path Difference' -and $r.verdict -eq 'Pass'){$pass[[int]$r.index]=$true}}
$original=@(Read "$repo/artifacts/validation/all-example-accuracy-20261031/rows.json")
$expected=@($original|Where-Object {$pass.ContainsKey([int]$_.index) -and $_.run.request.field -gt 1})
if($expected.Count -ne 99 -or @($results|Where-Object index -notin $expected.index).Count){throw 'Original selection changed'}
foreach($r in $expected){$q=$r.run.request;$f=@($q.definedFields)[$q.field-1];if(!$f -or ([double]$f[0] -eq 0 -and [double]$f[1] -eq 0)){throw 'Not an off-axis control'}}
Save 'extra-edge-results.json' $results
Save 'extra-edge-verification.json' @{coreSha256=$core;controls=99;pass=99;lost=0;allSelectedActualFieldsNonzero=$true;selection='All prior Pass OPD requests with selected field >1 in the November 4 matrix';priorResultsSha256=(Hash "$repo/validation/rechecks/2026-11-04/all-example-accuracy/request-results.csv");freshSourceImports=$true;newNativeCapture=$false}
$summary|Add-Member extraOffAxisPriorPassChecked 99 -Force;$summary|Add-Member extraOffAxisPriorPassLost 0 -Force;$summary|Add-Member finalReviewLocal ([DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(8)).ToString('o')) -Force
Save 'summary.json' $summary
$files=[Collections.Generic.List[object]]::new()
function Add($path,$logical){$files.Add(@{path=[IO.Path]::GetFullPath($path);logical=$logical})}
foreach($f in Get-ChildItem -LiteralPath $run -Recurse -File|Where-Object {$_.Extension -notin @('.png','.svg','.pdf','.dll','.pdb','.exe')}){Add $f.FullName ('runs/offaxis-opd-extra-edge-20261104/'+[IO.Path]::GetRelativePath($run,$f.FullName).Replace('\','/'))}
foreach($name in @('offaxis-opd-extra-edge-plan-20261104.json','offaxis-opd-extra-edge-20261104.log','offaxis-opd-documentation-final-20261104.json','offaxis-opd-documentation-final-20261104.log','offaxis-opd-seal-20261104.log')){Add "$artifact/$name" "logs/$name"}
$pdb="$runtime/ResidualClosure20261103.pdb";$documents=[Collections.Generic.List[object]]::new();$s=[IO.File]::OpenRead($pdb);$provider=[Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($s)
try{$reader=$provider.GetMetadataReader();foreach($h in $reader.Documents){$d=$reader.GetDocument($h);$path=$reader.GetString($d.Name);if(!(Test-Path -LiteralPath $path) -or $path -match '[\\/]obj[\\/]'){continue};$sha=[Convert]::ToHexString($reader.GetBlobBytes($d.Hash)).ToLowerInvariant();if((Hash $path) -ne $sha){throw "Compiled supplemental source changed: $path"};$rel=[IO.Path]::GetRelativePath($repo,$path).Replace('\','/');Add $path "source/$rel";$documents.Add(@{path=$rel;sha256=$sha})}}finally{$provider.Dispose();$s.Dispose()}
Save 'extra-edge-source-verification.json' @{coreSha256=$core;coreMatchesFinalRuntime=((Hash "$runtime/OpticalSystemDesign.Core.dll") -eq $core);pdbSha256=(Hash $pdb);documents=$documents;compiledSourcesMatch=$true;toolReusedFrom='artifacts/validation/analysis-definition-runtime-20261104'}
foreach($ext in @('dll','pdb','deps.json','runtimeconfig.json')){Add "$runtime/ResidualClosure20261103.$ext" "runtime/final/ResidualClosure20261103.$ext"}
foreach($name in @('summary.json','extra-edge-results.json','extra-edge-verification.json','extra-edge-source-verification.json','README.md')){Add "$root/$name" "final/$name"}
Add "$repo/docs/OFFAXIS_OPD_SAMPLING_REPAIR_2026-11-04.md" 'final/docs/OFFAXIS_OPD_SAMPLING_REPAIR_2026-11-04.md'
Add $PSCommandPath 'source/tools/diagnostics/OffAxisOpd20261104/Seal-Supplement.ps1'
$index=@(Read "$root/archive-index.json");$old=@{}
foreach($base in @($root,(Join-Path $repo 'validation/rechecks/2026-11-04/analysis-surface-reference'))){foreach($e in (Read "$base/archive-index.json")){$old[$e.sha256]=@{path=[IO.Path]::GetFullPath((Join-Path $base $e.archive));entry=$e.entry}}}
$archive=Join-Path $root 'supplemental-evidence.zip';if(Test-Path $archive){throw 'Supplement already sealed'}
$zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create);$opened=@{};$new=@{};$verified=@{};$mapping=[Collections.Generic.List[object]]::new();$logical=@{}
try{foreach($f in $files){
 $sha=Hash $f.path;$length=(Get-Item -LiteralPath $f.path).Length
 if($logical.ContainsKey($f.logical)){if($logical[$f.logical] -ne $sha){throw 'Conflicting supplement'};continue};$logical[$f.logical]=$sha
 if($old.ContainsKey($sha)){
  $ref=$old[$sha]
  if(!$verified.ContainsKey($sha)){
   if($null -eq $ref.entry){$actual=Hash $ref.path}else{if(!$opened.ContainsKey($ref.path)){$opened[$ref.path]=[IO.Compression.ZipFile]::OpenRead($ref.path)};$input=$opened[$ref.path].GetEntry($ref.entry).Open();try{$actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($input)).ToLowerInvariant()}finally{$input.Dispose()}}
   if($actual -ne $sha){throw 'Reused supplemental payload mismatch'};$verified[$sha]=$true
  }
  $mapping.Add(@{logicalPath=$f.logical;sha256=$sha;length=$length;archive=[IO.Path]::GetRelativePath($root,$ref.path).Replace('\','/');entry=$ref.entry})
 }else{
  if(!$new.ContainsKey($sha)){$entry=$zip.CreateEntry('payloads/'+$sha,[IO.Compression.CompressionLevel]::Optimal);$input=[IO.File]::OpenRead($f.path);$output=$entry.Open();try{$input.CopyTo($output)}finally{$input.Dispose();$output.Dispose()};$new[$sha]=$length}
  $mapping.Add(@{logicalPath=$f.logical;sha256=$sha;length=$length;archive='supplemental-evidence.zip';entry='payloads/'+$sha})
 }
}}finally{$zip.Dispose();foreach($z in $opened.Values){$z.Dispose()}}
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try{foreach($e in $zip.Entries){$sha=$e.FullName.Substring(9);$input=$e.Open();try{$actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($input)).ToLowerInvariant()}finally{$input.Dispose()};if($actual -ne $sha -or $e.Length -ne $new[$sha]){throw 'Supplement readback mismatch'}}}finally{$zip.Dispose()}
foreach($f in $files){if((Hash $f.path) -ne $logical[$f.logical]){throw 'Supplement source changed'}}
$all=@($index)+@($mapping);if(@($all|Group-Object logicalPath|Where-Object Count -gt 1).Count){throw 'Supplement logical identity conflict'}
Save 'archive-index.json' $all
Save 'supplemental-verification.json' @{archive='supplemental-evidence.zip';sha256=(Hash $archive);bytes=(Get-Item $archive).Length;newVerifiedPayloads=$new.Count;reusedVerifiedPayloads=$verified.Count;addedLogicalFiles=$mapping.Count;totalLogicalFiles=$all.Count;allReadBackBeforeRetirement=$true;baseArchiveUnchanged=$true}
$path=[IO.Path]::GetFullPath($run);$parent=[IO.Path]::GetFullPath($artifact)+[IO.Path]::DirectorySeparatorChar
if(!$path.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($path) -ne 'offaxis-opd-extra-edge-20261104'){throw 'Unsafe supplemental retirement target'}
Remove-Item -LiteralPath $path -Recurse -Force
Get-Content "$root/supplemental-verification.json"
