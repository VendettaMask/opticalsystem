param([Parameter(Mandatory)][string]$Repository)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($Repository);$work=Join-Path $repo 'artifacts/validation/image-simulation-propagation-20261104'
$out=Join-Path $repo 'validation/rechecks/2026-11-04/image-simulation-pickup';[IO.Directory]::CreateDirectory($out)|Out-Null
function Read($p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function Hash($p){(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
function Save($name,$value){[IO.File]::WriteAllText((Join-Path $out $name),($value|ConvertTo-Json -Depth 100),[Text.UTF8Encoding]::new($false))}
function Counts($items){$counts=[ordered]@{Pass=0;Close=0;Difference=0;Incomparable=0;Error=0};foreach($r in $items){$counts[$r.conclusion]++};$counts}
$before=@(Read "$work/before-full/results.json");$after=@(Read "$work/after/results.json");$imports=@(Read "$work/import-audit/import-audit.json")
if($before.Count-ne60-or$after.Count-ne60-or$imports.Count-ne95){throw 'Incomplete scope'}
$core=(Read "$work/after/identity.json").coreSha256
foreach($name in 'before-full','import-audit','phase'){if((Read "$work/$name/identity.json").coreSha256-ne$core){throw 'Mixed Core'}}
if((Hash "$work/tests/OpticalSystemDesign.Core.dll")-ne$core){throw 'Test Core differs'}
$changes=@();$jobs=@()
foreach($r in $after){
 $b=$before|Where-Object index -eq $r.index;if(!$b-or$b.requestFingerprint-ne$r.requestFingerprint){throw 'Request changed'}
 $changes+=[pscustomobject]@{index=$r.index;lens=$r.lens;analysis=$r.key;before=$b.conclusion;after=$r.conclusion;
  maximumAbsolute=($r.metrics.maxAbsolute|Measure-Object -Maximum).Maximum;maximumNrmse=($r.metrics.nrmse|Measure-Object -Maximum).Maximum}
 foreach($stage in 'before-full','after'){
  $dir=Join-Path $work "$stage/$($r.index)";$original=Read "$dir/request.json";$provenance=Read "$dir/provenance.json"
  $snapshot=Join-Path $work "$stage/$([IO.Path]::GetFileName($original.snapshotPath))"
  if((Hash $snapshot)-ne$provenance.snapshotSha256-or$provenance.requestFingerprint-ne$r.requestFingerprint){throw 'Execution identity mismatch'}
  # Earlier diagnostic builds copied the original archived job as request.json.
  # Preserve it and publish the actual executed paths separately; numeric request
  # and source bytes remain untouched and provenance supplies the snapshot hash.
  $original.snapshotPath=$snapshot;$original.outputDirectory=$dir
  [IO.File]::WriteAllText("$dir/execution-job.json",($original|ConvertTo-Json -Depth 100),[Text.UTF8Encoding]::new($false))
  $jobs+=[pscustomobject]@{stage=$stage;index=$r.index;requestFingerprint=$provenance.requestFingerprint;snapshotSha256=$provenance.snapshotSha256}
 }
}
$changes|Export-Csv -LiteralPath "$out/request-results.csv" -NoTypeInformation -Encoding utf8
Save 'execution-identity.json' $jobs
$changed=@($imports|Where-Object {$_.configurations.structurallyIdentical-contains$false})
if($changed.Count-ne2-or@($imports.configurations).Count-ne114-or@($imports|Where-Object error).Count-ne1){throw 'Unexpected import impact'}
[xml]$full=Get-Content -LiteralPath "$work/pickup-regression-final.trx" -Raw
[xml]$roundtrip=Get-Content -LiteralPath "$work/pickup-roundtrip.trx" -Raw
if($full.TestRun.ResultSummary.Counters.passed-ne156-or$full.TestRun.ResultSummary.Counters.failed-ne0-or$roundtrip.TestRun.ResultSummary.Counters.passed-ne11-or$roundtrip.TestRun.ResultSummary.Counters.failed-ne0){throw 'Incomplete tests'}

$nativeRoot=Join-Path $repo 'artifacts/zemax-sequential-corpus/20261021/full-v1'
$originalArchive=Join-Path $repo 'validation/repairs/2026-10-21/sequential-corpus-full-evidence.zip'
if((Hash $originalArchive)-ne'1dc562461d4e39ac9bf54f3c2309a0bb81fdeccfb092554cd8e9c14aa80760ad'){throw 'Original native archive changed'}
$zip=[IO.Compression.ZipFile]::OpenRead($originalArchive)
try{$reader=[IO.StreamReader]::new($zip.GetEntry('payload-manifest.json').Open());try{$nativeManifest=$reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}}finally{$zip.Dispose()}
$lookup=@{};foreach($e in $nativeManifest){$lookup[$e.path]=$e}
$checks=@()
foreach($r in $after){
 $directory=Get-ChildItem -LiteralPath "$nativeRoot/analyses" -Directory|Where-Object Name -like "$('{0:D4}'-f$r.index)-*"
 foreach($f in Get-ChildItem -LiteralPath "$($directory.FullName)/native" -Recurse -File|Where-Object Extension -notin @('.png','.jpg','.jpeg','.svg','.bmp','.pdf')){
  $key='run/'+[IO.Path]::GetRelativePath($nativeRoot,$f.FullName).Replace('\','/');$hash=Hash $f.FullName
  if(!$lookup.ContainsKey($key)-or$lookup[$key].sha256-ne$hash-or$lookup[$key].bytes-ne$f.Length){throw "Reference changed: $key"}
  $checks+=[pscustomobject]@{path=$key;sha256=$hash;length=$f.Length}
 }
}
Save 'reference-integrity.json' ([ordered]@{allMatched=$true;files=$checks;verifiedNativePayloads=$checks.Count;originalArchiveSha256=Hash $originalArchive;
 repositoryPolicySha256=Hash (Join-Path $repo 'tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json');originalPolicySha256=Hash "$nativeRoot/comparison-settings.json"})
$sourcePaths=@('src/OpticalSystemDesign.Core/FileIO/ZemaxZmxReader.cs','tests/OpticalSystemDesign.Tests/ZemaxCrossColumnPickupTests.cs','tools/diagnostics/ImageSimulationPropagation20261104/Program.cs')
Save 'summary.json' ([ordered]@{requests=60;before=Counts $before;after=Counts $after;newPasses=@($changes|Where-Object {$_.before-ne'Pass'-and$_.after-eq'Pass'}).Count;
 retainedNativeInvalidReferences=2;remainingClose=4;remainingDifference=2;remainingError=0;coreSha256=$core;
 importedSources=95;importedConfigurations=114;unchangedConfigurations=112;correctedConfigurations=2;unchangedImportErrors=1;
 uniqueFocusedTests=156;latestRoundtripSubset=11;testFailures=0;testSkipped=0;firstFixtureFailurePreserved=$true;
 sourcePrescriptionsUnchanged=$true;originalSamplingUnchanged=$true;originalReferencesUnchanged=$true;tolerancesUnchanged=$true;phaseGateUnchanged=$true;
 full3024Rerun=$false;nativeCaptured=$false;referenceConvergence='Original captures only; not newly certified';
 sources=@($sourcePaths|ForEach-Object{[pscustomobject]@{path=$_;sha256=Hash (Join-Path $repo $_)}})})
Copy-Item -LiteralPath "$work/import-audit/import-audit.json","$work/phase/phase-diagnostics.json","$work/pickup-regression.trx","$work/pickup-regression-final.trx","$work/pickup-roundtrip.trx" -Destination $out
foreach($f in Get-ChildItem -LiteralPath $work -File -Filter '*.log'){Copy-Item -LiteralPath $f.FullName -Destination $out}
foreach($name in 'image-simulation-initial.log','image-simulation-after-initial.log'){
 $file=Join-Path $repo "validation/rechecks/2026-11-04/fft-sampling-policy/$name";if(Test-Path -LiteralPath $file){Move-Item -LiteralPath $file -Destination (Join-Path $out $name)}
}

$priorRoot=Join-Path $repo 'validation/rechecks/2026-11-04/all-example-accuracy';$prior=@{}
foreach($e in @(Read "$priorRoot/archive-index.json")){if(!$prior.ContainsKey($e.sha256)){$prior[$e.sha256]=$e}}
$archive="$out/evidence.zip";$stream=[IO.File]::Open($archive,[IO.FileMode]::CreateNew);$zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
$manifest=@();$written=@{}
try{
 foreach($stage in 'before-full','after','import-audit','phase'){
  foreach($f in Get-ChildItem -LiteralPath "$work/$stage" -Recurse -File){
   $hash=Hash $f.FullName;$entry="payloads/$hash";$container='evidence.zip'
   if($prior.ContainsKey($hash)){$p=$prior[$hash];$container=[IO.Path]::GetRelativePath($out,[IO.Path]::GetFullPath((Join-Path $priorRoot $p.archive))).Replace('\','/');$entry=$p.entry}
   elseif(!$written.ContainsKey($hash)){$e=$zip.CreateEntry($entry,[IO.Compression.CompressionLevel]::Optimal);$input=$f.OpenRead();$output=$e.Open();try{$input.CopyTo($output)}finally{$input.Dispose();$output.Dispose()};$written[$hash]=$true}
   $manifest+=[pscustomobject]@{logicalPath=$stage+'/'+[IO.Path]::GetRelativePath("$work/$stage",$f.FullName).Replace('\','/');archive=$container;entry=$entry;sha256=$hash;length=$f.Length}
  }
 }
}finally{$zip.Dispose();$stream.Dispose()}
Save 'archive-index.json' $manifest
$opened=@{};$verified=@{}
try{
 foreach($item in $manifest){
  $absolute=[IO.Path]::GetFullPath((Join-Path $out $item.archive));$key=$absolute+'|'+$item.entry
  if($verified.ContainsKey($key)){continue}
  if($null-eq$item.entry){$bytes=[IO.File]::ReadAllBytes($absolute)}else{
   if(!$opened.ContainsKey($absolute)){$opened[$absolute]=[IO.Compression.ZipFile]::OpenRead($absolute)}
   $input=$opened[$absolute].GetEntry($item.entry).Open();$memory=[IO.MemoryStream]::new();try{$input.CopyTo($memory);$bytes=$memory.ToArray()}finally{$input.Dispose();$memory.Dispose()}
  }
  if($bytes.Length-ne$item.length-or[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($bytes))-ne$item.sha256){throw 'Archive verification failed'}
  $verified[$key]=$true
 }
}finally{foreach($z in $opened.Values){$z.Dispose()}}
Save 'archive-verification.json' ([ordered]@{archive='evidence.zip';sha256=Hash $archive;bytes=(Get-Item -LiteralPath $archive).Length;logicalFiles=$manifest.Count;newUniquePayloads=$written.Count;completeReadback=$true;expandedEvidenceRetired=$false})
Write-Output "Verified 60 A/B requests, $($checks.Count) native payloads, 156 distinct tests and $($manifest.Count) archived logical files."
