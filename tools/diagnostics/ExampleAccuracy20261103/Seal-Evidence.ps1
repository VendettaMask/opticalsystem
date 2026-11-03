param([string]$Repository='D:\Projects\opticalsystem',[switch]$RecoverIncompleteArchive)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($Repository)
$root=[IO.Path]::GetFullPath((Join-Path $repo 'validation/rechecks/2026-11-03/all-example-accuracy'))
$study=Join-Path $repo 'artifacts/validation/all-example-accuracy-20261103'
$afocal=Join-Path $repo 'artifacts/validation/all-example-afocal-20261103'
$runtime=Join-Path $repo 'artifacts/validation/all-example-accuracy-runner-20261103'
function Hash($p){(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
function Read($p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function Save($name,$v){[IO.File]::WriteAllText((Join-Path $root $name),(ConvertTo-Json -InputObject $v -Depth 100),[Text.UTF8Encoding]::new($false))}
$summary=Read "$root/summary.json";$id=Read "$study/identity.json"
if($summary.productExamples -ne 95 -or $summary.mainRequests -ne 3024 -or $summary.executedRequests -ne $id.eligible -or $summary.afocal.requests -ne 170){throw 'Incomplete report'}
if(@($summary.scopedConsistency|Where-Object {$_.classificationDifferences.Count}).Count){throw 'Unresolved same-Core scoped differences'}
foreach($binary in $id.assemblies){if((Hash $binary.path) -ne $binary.sha256){throw 'Execution binary changed'}}
if((Hash "$runtime/OpticalSystemDesign.Core.dll") -ne $summary.coreSha256 -or (Hash "$afocal/runner/OpticalSystemDesign.Core.dll") -ne $summary.coreSha256){throw 'Core mismatch'}
$files=[Collections.Generic.List[object]]::new()
function Add($path,$logical){$files.Add(@{path=[IO.Path]::GetFullPath($path);logical=$logical})}
function AddTree($path,$prefix){foreach($f in Get-ChildItem -LiteralPath $path -Recurse -File){$rel=[IO.Path]::GetRelativePath($path,$f.FullName).Replace('\','/');if($rel -match '(^|/)(runner|bin|obj)/' -or $f.Extension -in @('.png','.svg','.pdf','.zip')){continue};Add $f.FullName "$prefix/$rel"}}
$provenance=[Collections.Generic.List[object]]::new()
foreach($pdb in @("$runtime/OpticalSystemDesign.Core.pdb","$runtime/OpticalSystemDesign.Application.pdb","$runtime/OpticalSystemDesign.ZemaxComparison.pdb","$runtime/ExampleAccuracy20261031.pdb","$afocal/runner/FinalAfocalRecheck20261029.pdb")){
 $stream=[IO.File]::OpenRead($pdb);$provider=[System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream);$documents=[Collections.Generic.List[object]]::new()
 try{$reader=$provider.GetMetadataReader();foreach($handle in $reader.Documents){$doc=$reader.GetDocument($handle);$path=$reader.GetString($doc.Name);if(!(Test-Path -LiteralPath $path) -or $path -match '[\\/]obj[\\/]'){continue};$expected=[Convert]::ToHexString($reader.GetBlobBytes($doc.Hash)).ToLowerInvariant();if((Hash $path) -ne $expected){throw "Source/PDB mismatch: $path"};$rel=[IO.Path]::GetRelativePath($repo,$path).Replace('\','/');Add $path "source/$rel";$documents.Add(@{path=$rel;sha256=$expected})}}finally{$provider.Dispose();$stream.Dispose()}
 $provenance.Add(@{pdb=[IO.Path]::GetRelativePath($repo,$pdb).Replace('\','/');sha256=(Hash $pdb);documents=$documents})
}
Save 'source-verification.json' @{coreSha256=$summary.coreSha256;allListedSourcesMatchCompiled=$true;assemblies=$provenance;defaultAppCoreMatches=((Hash (Join-Path $repo 'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Core.dll')) -eq $summary.coreSha256)}
Save 'runtime-dependencies.json' @(foreach($dir in @($runtime,"$afocal/runner")){foreach($f in Get-ChildItem -LiteralPath $dir -Recurse -File|Where-Object Extension -eq '.dll'){@{runtime=[IO.Path]::GetRelativePath($repo,$dir).Replace('\','/');path=[IO.Path]::GetRelativePath($dir,$f.FullName).Replace('\','/');sha256=(Hash $f.FullName);bytes=$f.Length}}})
AddTree $study 'study';AddTree $afocal 'afocal-study'
foreach($dir in @($runtime,"$afocal/runner")){foreach($f in Get-ChildItem -LiteralPath $dir -File|Where-Object {$_.Name -match '^(OpticalSystemDesign.Core|OpticalSystemDesign.Application|OpticalSystemDesign.ZemaxComparison|ExampleAccuracy20261031|FinalAfocalRecheck20261029)\.(dll|pdb|deps.json|runtimeconfig.json)$'}){Add $f.FullName ('runtime/'+(Split-Path $dir -Leaf)+'/'+$f.Name)}}
foreach($dir in @('tools/diagnostics/ExampleAccuracy20261031','tools/diagnostics/ExampleAccuracy20261103','tools/diagnostics/FinalAfocalRecheck20261029')){foreach($f in Get-ChildItem -LiteralPath (Join-Path $repo $dir) -File){Add $f.FullName "source/$dir/$($f.Name)"}}
foreach($f in Get-ChildItem (Join-Path $repo 'artifacts/validation') -File -Filter '*all-example*-20261103.log'|Where-Object Name -notlike '*all-example-seal*'){Add $f.FullName ('logs/'+$f.Name)}
if($RecoverIncompleteArchive){Add (Join-Path $repo 'artifacts/validation/all-example-seal-20261103.log') 'attempts/failed-seal-log-lock.log'}
foreach($name in @('tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json','src/OpticalSystemDesign.App/Assets/SequentialExamples/index.json')){Add (Join-Path $repo $name) ('inputs/'+$name)}
# Existing reports remain direct authorities; reuse their bytes rather than archiving duplicates.
$old=@{}
foreach($base in @('validation/rechecks/2026-10-31/all-example-accuracy','validation/rechecks/2026-11-01/testing-three','validation/rechecks/2026-11-02/huygens-common','validation/rechecks/2026-11-02/phase-common','validation/rechecks/2026-11-02/boundary-common','validation/rechecks/2026-11-02/biocular-reference','validation/rechecks/2026-11-03/aperture-footprint')){
 $folder=Join-Path $repo $base
 if(Test-Path "$folder/archive-index.json"){
  foreach($e in (Read "$folder/archive-index.json")){$archive=if($e.archive){$e.archive}else{'evidence.zip'};$entry=if($e.retainedFile -or ($e.PSObject.Properties.Name -contains 'entry' -and $null -eq $e.entry)){$null}elseif($e.entry){$e.entry}else{'payloads/'+$e.sha256};$path=if($e.retainedFile){$e.retainedFile}else{$archive};$old[$e.sha256]=@{path=[IO.Path]::GetFullPath((Join-Path $folder $path));entry=$entry}}
 }else{
  $manifest=Read "$folder/manifest.json";foreach($e in $manifest.entries){$old[$e.sha256]=@{path=(Join-Path $folder 'evidence-current.zip');entry=$e.path}}
 }
}
foreach($f in Get-ChildItem -LiteralPath $root -File|Where-Object Extension -in @('.json','.csv','.md')){$hash=Hash $f.FullName;$old[$hash]=@{path=$f.FullName;entry=$null}}
$recovered=@{};$recoveryArchive=$null;$recoveryHash=$null
if($RecoverIncompleteArchive){
 if((Test-Path "$root/archive-index.json") -or (Test-Path "$root/archive-verification.json")){throw 'Cannot recover a finalized archive'}
 $receipt=Read "$root/seal-recovery.json";$recoveryArchive=Join-Path $root $receipt.partialArchive;$recoveryHash=Hash $recoveryArchive
 if($recoveryHash -ne $receipt.partialArchiveSha256){throw 'Incomplete archive identity changed'}
 $partial=[IO.Compression.ZipFile]::OpenRead($recoveryArchive)
 try{foreach($e in $partial.Entries){
  if($e.FullName -notmatch '^payloads/([0-9a-f]{64})$'){throw 'Invalid incomplete archive entry'};$expected=$Matches[1];$s=$e.Open();try{$actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($s)).ToLowerInvariant()}finally{$s.Dispose()}
  if($actual -ne $expected -or $recovered.ContainsKey($expected)){throw 'Incomplete payload mismatch or duplicate'}
  $recovered[$expected]=$e.Length;$old[$expected]=@{path=$recoveryArchive;entry=$e.FullName}
 }}finally{$partial.Dispose()}
}
$archive=Join-Path $root $(if($RecoverIncompleteArchive){'evidence-recovery.zip'}else{'evidence.zip'});if(Test-Path $archive){throw 'Already sealed'}
$zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create);$opened=@{};$new=@{};$verified=@{};$mapping=[Collections.Generic.List[object]]::new();$logical=@{}
try{foreach($f in $files){
 $hash=Hash $f.path;$length=(Get-Item -LiteralPath $f.path).Length
 if($logical.ContainsKey($f.logical)){if($logical[$f.logical] -ne $hash){throw 'Conflicting logical identity'};continue};$logical[$f.logical]=$hash
 if($old.ContainsKey($hash)){
  $ref=$old[$hash]
  if(!$verified.ContainsKey($hash)){
   if($null -eq $ref.entry){$actual=Hash $ref.path}else{if(!$opened.ContainsKey($ref.path)){$opened[$ref.path]=[IO.Compression.ZipFile]::OpenRead($ref.path)};$s=$opened[$ref.path].GetEntry($ref.entry).Open();try{$actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($s)).ToLowerInvariant()}finally{$s.Dispose()}}
   if($actual -ne $hash){throw 'Prior authority mismatch'};$verified[$hash]=$true
  }
  $mapping.Add(@{logicalPath=$f.logical;sha256=$hash;length=$length;archive=[IO.Path]::GetRelativePath($root,$ref.path).Replace('\','/');entry=$ref.entry})
 }else{
  if(!$new.ContainsKey($hash)){$e=$zip.CreateEntry('payloads/'+$hash,[IO.Compression.CompressionLevel]::Optimal);$s=[IO.File]::OpenRead($f.path);$t=$e.Open();try{$s.CopyTo($t)}finally{$s.Dispose();$t.Dispose()};$new[$hash]=$length}
  $mapping.Add(@{logicalPath=$f.logical;sha256=$hash;length=$length;archive=[IO.Path]::GetFileName($archive);entry='payloads/'+$hash})
 }
}}finally{$zip.Dispose();foreach($z in $opened.Values){$z.Dispose()}}
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try{if($zip.Entries.Count -ne $new.Count){throw 'Payload count mismatch'};foreach($e in $zip.Entries){$hash=$e.FullName.Substring(9);$s=$e.Open();try{$actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($s)).ToLowerInvariant()}finally{$s.Dispose()};if($actual -ne $hash -or $e.Length -ne $new[$hash]){throw 'New payload mismatch'}}}finally{$zip.Dispose()}
foreach($f in $files){if((Hash $f.path) -ne $logical[$f.logical]){throw 'Input changed during seal'}}
if($RecoverIncompleteArchive){$used=[Collections.Generic.HashSet[string]]::new();foreach($m in $mapping){[void]$used.Add($m.sha256)};foreach($hash in $recovered.Keys){if(!$used.Contains($hash)){$mapping.Add(@{logicalPath='attempts/initial-seal/payloads/'+$hash;sha256=$hash;length=$recovered[$hash];archive=[IO.Path]::GetFileName($recoveryArchive);entry='payloads/'+$hash})}}}
Save 'archive-index.json' $mapping
Save 'archive-verification.json' @{archive=[IO.Path]::GetFileName($archive);sha256=(Hash $archive);bytes=(Get-Item $archive).Length;logicalFiles=$mapping.Count;newVerifiedPayloads=$new.Count;reusedVerifiedPayloads=$verified.Count;recoveredContainer=$(if($RecoverIncompleteArchive){@{archive=[IO.Path]::GetFileName($recoveryArchive);sha256=$recoveryHash;bytes=(Get-Item $recoveryArchive).Length;verifiedPayloads=$recovered.Count}}else{$null});completeReadbackBeforeRetirement=$true}
# Retire the expanded numerical outputs only; keep current runner dependencies available.
foreach($target in @($study,$afocal)){
 $resolved=[IO.Path]::GetFullPath($target);$intended=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts/validation'))+[IO.Path]::DirectorySeparatorChar
 if(!$resolved.StartsWith($intended,[StringComparison]::OrdinalIgnoreCase) -or !(Split-Path $resolved -Leaf).EndsWith('-20261103')){throw 'Unsafe retirement target'}
 foreach($f in Get-ChildItem -LiteralPath $resolved -File){Remove-Item -LiteralPath $f.FullName -Force}
 foreach($dir in Get-ChildItem -LiteralPath $resolved -Directory|Where-Object Name -ne 'runner'){Remove-Item -LiteralPath $dir.FullName -Recurse -Force}
}
Write-Output "Verified $($new.Count) new and $($verified.Count) reused payloads; $($mapping.Count) logical files."
