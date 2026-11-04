$old=@{}
foreach($base in @('validation/rechecks/2026-10-31/all-example-accuracy','validation/rechecks/2026-11-01/testing-three','validation/rechecks/2026-11-02/huygens-common','validation/rechecks/2026-11-02/phase-common','validation/rechecks/2026-11-02/boundary-common','validation/rechecks/2026-11-02/biocular-reference','validation/rechecks/2026-11-03/aperture-footprint','validation/rechecks/2026-11-03/all-example-accuracy','validation/rechecks/2026-11-03/medium-reference','validation/rechecks/2026-11-03/residual-closure','validation/rechecks/2026-11-03/definition-reference','validation/rechecks/2026-11-04/all-example-accuracy','validation/rechecks/2026-11-04/fft-sampling-policy','validation/rechecks/2026-11-04/image-simulation-pickup','validation/rechecks/2026-11-04/zemax-definition-review','validation/rechecks/2026-11-04/analysis-surface-reference')){
 $folder=Join-Path $repo $base
 if(Test-Path "$folder/archive-index.json"){foreach($e in (Read "$folder/archive-index.json")){$container=if($e.archive){$e.archive}else{'evidence.zip'};$entry=if($e.retainedFile -or ($e.PSObject.Properties.Name -contains 'entry' -and $null -eq $e.entry)){$null}elseif($e.entry){$e.entry}else{'payloads/'+$e.sha256};$path=if($e.retainedFile){$e.retainedFile}else{$container};$old[$e.sha256]=@{path=[IO.Path]::GetFullPath((Join-Path $folder $path));entry=$entry}}}
 else{foreach($e in (Read "$folder/manifest.json").entries){$old[$e.sha256]=@{path=(Join-Path $folder 'evidence-current.zip');entry=$e.path}}}
}
$referenceFolder=Join-Path $repo 'validation/rechecks/2026-11-01/example-incomparable-repair'
foreach($e in (Read "$referenceFolder/manifest.json").entries){$old[$e.sha256]=@{path=(Join-Path $referenceFolder 'accepted-evidence.zip');entry=$e.path}}
foreach($f in Get-ChildItem -LiteralPath $root -File|Where-Object Extension -in @('.json','.csv','.md','.log')){$old[(Hash $f.FullName)]=@{path=$f.FullName;entry=$null}}
$zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create);$opened=@{};$new=@{};$verified=@{};$mapping=[Collections.Generic.List[object]]::new();$logical=@{}
try{foreach($f in $files){
 $sha=Hash $f.path;$length=(Get-Item -LiteralPath $f.path).Length
 if($logical.ContainsKey($f.logical)){if($logical[$f.logical] -ne $sha){throw 'Conflicting source'};continue};$logical[$f.logical]=$sha
 if($old.ContainsKey($sha)){
  $ref=$old[$sha]
  if(!$verified.ContainsKey($sha)){
   if($null -eq $ref.entry){$actual=Hash $ref.path}else{if(!$opened.ContainsKey($ref.path)){$opened[$ref.path]=[IO.Compression.ZipFile]::OpenRead($ref.path)};$s=$opened[$ref.path].GetEntry($ref.entry).Open();try{$actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($s)).ToLowerInvariant()}finally{$s.Dispose()}}
   if($actual -ne $sha){throw 'Prior authority hash mismatch'};$verified[$sha]=$true
  }
  $mapping.Add(@{logicalPath=$f.logical;sha256=$sha;length=$length;archive=[IO.Path]::GetRelativePath($root,$ref.path).Replace('\','/');entry=$ref.entry})
 }else{
  if(!$new.ContainsKey($sha)){$e=$zip.CreateEntry('payloads/'+$sha,[IO.Compression.CompressionLevel]::Optimal);$a=[IO.File]::OpenRead($f.path);$b=$e.Open();try{$a.CopyTo($b)}finally{$a.Dispose();$b.Dispose()};$new[$sha]=$length}
  $mapping.Add(@{logicalPath=$f.logical;sha256=$sha;length=$length;archive='evidence.zip';entry='payloads/'+$sha})
 }
}}finally{$zip.Dispose();foreach($z in $opened.Values){$z.Dispose()}}
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try{if($zip.Entries.Count -ne $new.Count){throw 'Payload count differs'};foreach($e in $zip.Entries){$sha=$e.FullName.Substring(9);$s=$e.Open();try{$actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($s)).ToLowerInvariant()}finally{$s.Dispose()};if($actual -ne $sha -or $e.Length -ne $new[$sha]){throw 'New payload mismatch'}}}finally{$zip.Dispose()}
foreach($f in $files){if((Hash $f.path) -ne $logical[$f.logical]){throw 'Run changed during seal'}}
Save 'archive-index.json' $mapping
Save 'archive-verification.json' @{archive='evidence.zip';sha256=(Hash $archive);bytes=(Get-Item $archive).Length;logicalFiles=$mapping.Count;newVerifiedPayloads=$new.Count;reusedVerifiedPayloads=$verified.Count;allReadBackBeforeRetirement=$true;finalRuntimeRetained=$true}
foreach($directory in @($studies+$retireRuntimes)){$path=[IO.Path]::GetFullPath($directory.FullName);$parent=[IO.Path]::GetFullPath($artifact)+[IO.Path]::DirectorySeparatorChar;if(!$path.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or $directory.Name -notmatch '^offaxis-opd-' -or $directory.Name -eq 'offaxis-opd-final-runtime-20261104'){throw 'Unsafe retirement target'};Remove-Item -LiteralPath $path -Recurse -Force}
Write-Output "Verified $($new.Count) new / $($verified.Count) reused payloads; $($mapping.Count) logical files."
