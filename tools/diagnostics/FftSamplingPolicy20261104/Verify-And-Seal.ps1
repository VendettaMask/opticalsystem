param([Parameter(Mandatory)][string]$Repository)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($Repository)
$working=Join-Path $repo 'artifacts/validation/fft-sampling-policy-20261104'
$out=Join-Path $repo 'validation/rechecks/2026-11-04/fft-sampling-policy'
function Read($p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function Save($name,$value){[IO.File]::WriteAllText((Join-Path $out $name),(ConvertTo-Json -InputObject $value -Depth 100),[Text.UTF8Encoding]::new($false))}
function Hash($p){(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant()}
$comparison=@(Read (Join-Path $out 'final-comparison.json'))
if($comparison.Count-ne31){throw 'Expected 30 main/control requests plus independent afocal request'}
$lost=@(Import-Csv (Join-Path $repo 'validation/rechecks/2026-11-04/all-example-accuracy/lost-pass-cases.csv'))
$failures=@();$rows=@();$same=0;$equivalent=@()
$policy=Read (Join-Path $repo 'tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json')
foreach($item in $comparison){
 $afocal=$item.root.EndsWith('/afocal');$scope=if($afocal){'afocal'}else{'main'};$index=[int]$item.index
 $product=Read (Join-Path $working "$([IO.Path]::GetFileName($item.root))/original/$index/data.json")
 if($product.outcome-ne'Unavailable'-or@($product.plotSeries).Count-ne0){throw "Protection changed: $scope/$index"}
 if($product.values.PupilPhaseSampling.criterion-ne'LocalUnwrappedPupilPhaseStep/v1'-or$product.values.PupilPhaseSampling.certifiesConvergence){throw 'Untruthful criterion metadata'}
 $last=$item.records[-1];$rank=@{Pass=0;Close=1;Difference=2;Incomparable=3;Error=4}
 $status=@($last.againstOriginal.metrics.conclusion|Sort-Object {$rank[$_]} -Descending)[0]
 $previous=@($last.againstPrevious.metrics.conclusion|Sort-Object {$rank[$_]} -Descending)[0]
 foreach($record in $item.records){if($record.againstOriginal.missing-ne0){throw 'Incomplete original requested domain'}}
 $rows+= [pscustomobject]@{scope=$scope;index=$index;analysis=$item.analysis;priorPass=(!$afocal-and$index-in$lost.index);
  originalProductOutcome=$product.outcome;maximumStepWaves=$product.values.PupilPhaseSampling.maximumStepWaves;
  originalVs8x=$status;maximumDifference=$last.againstOriginal.maximum;maximumNrmse=($last.againstOriginal.metrics.nrmse|Measure-Object -Maximum).Maximum;
  refinement4xVs8x=$previous;comparedOriginalKnots=$last.againstOriginal.count;missingOriginalKnots=$last.againstOriginal.missing;
  nativeConvergenceCertified=$false}
 if(!$afocal){
  $oldRoot=@('convergence','convergence-rest','convergence-controls')|Where-Object{Test-Path -LiteralPath (Join-Path $working "$_/$index/1-data.json")}
  if($oldRoot.Count-ne1){throw 'Missing or duplicate preliminary result'}
  foreach($scale in 1,2,4,8){
   $oldPath=Join-Path $working "$oldRoot/$index/$scale-data.json";$newPath=Join-Path $working "final/$index/$scale-data.json"
   if((Hash $oldPath)-eq(Hash $newPath)){$same++;continue}
   $a=Read $oldPath;$b=Read $newPath;$maximum=0d;$count=0
   $tol=@($policy.analyses.($item.analysis).quantities.psobject.Properties.Value)[0]
   if(@($a.plotSeries).Count-ne@($b.plotSeries).Count){throw 'Series count changed'}
   for($s=0;$s-lt$a.plotSeries.Count;$s++){
    $p=$a.plotSeries[$s];$q=$b.plotSeries[$s]
    if($p.xUnit-ne$q.xUnit-or$p.yUnit-ne$q.yUnit-or$p.valueUnit-ne$q.valueUnit-or$p.points.Count-ne$q.points.Count){throw 'Physical domain changed'}
    for($k=0;$k-lt$p.points.Count;$k++){
     $v=$p.points[$k];$w=$q.points[$k];if($v.x-ne$w.x){throw 'Physical X axis changed'}
     if($null-ne$v.value){if($v.y-ne$w.y){throw 'Physical Y axis changed'};$before=$v.value;$after=$w.value}else{$before=$v.y;$after=$w.y}
     $difference=[Math]::Abs($before-$after);$maximum=[Math]::Max($maximum,$difference);$count++
     if($difference-gt$tol.absolute+$tol.relative*[Math]::Abs($before)){throw 'Final numerical output exceeds frozen point tolerance'}
    }
   }
   $equivalent+=[pscustomobject]@{index=$index;scale=$scale;maxAbsoluteDifference=$maximum;alignedValues=$count;withinOriginalPointTolerance=$true}
  }
 }
}
$rows|Export-Csv -LiteralPath (Join-Path $out 'request-audit.csv') -NoTypeInformation -Encoding utf8
function Counts($items){$value=[ordered]@{Pass=0;Close=0;Difference=0;Incomparable=0;Error=0};foreach($item in $items){$value[$item.originalVs8x]++};$value}
$identity=Read (Join-Path $working 'final/identity.json');$afIdentity=Read (Join-Path $working 'afocal/identity.json')
$testCore=Hash (Join-Path $working 'tests/OpticalSystemDesign.Core.dll')
if($identity.coreSha256-ne$afIdentity.coreSha256-or$identity.coreSha256-ne$testCore){throw 'Mixed final Core'}
[xml]$trx=Get-Content -LiteralPath (Join-Path $out 'fft-sampling.trx') -Raw
$c=$trx.TestRun.ResultSummary.Counters
if($c.passed-ne29-or$c.failed-ne0-or$c.total-ne29){throw 'Incomplete focused tests'}
$sources=@('src/OpticalSystemDesign.Core/Analysis/FftPupilSampling.cs','tests/OpticalSystemDesign.Tests/FirstOrderAndFftDefinitionTests.cs',
 'tools/diagnostics/FftSamplingPolicy20261104/Program.cs','tools/diagnostics/FftSamplingPolicy20261104/ConvergenceProbe.cs',
 'tools/diagnostics/FftSamplingPolicy20261104/FftSamplingPolicy20261104.csproj')|ForEach-Object{[pscustomobject]@{path=$_;sha256=Hash (Join-Path $repo $_)}}
Save 'summary.json' ([ordered]@{originalRequests=31;mainPhaseRejections=26;nativeInvalidControls=4;independentAfocal=1;
 originalOutcomes=[ordered]@{Unavailable=31};restoredPasses=0;selfRefinementCounts=Counts $rows;
 prior19SelfRefinementCounts=Counts @($rows|Where-Object priorPass);byteIdenticalPreliminaryAndFinalArrays=$same;
 preliminaryAndFinalArraysWithinOriginalPointTolerance=($same+$equivalent.Count);nonByteIdenticalComparisons=$equivalent;
 coreSha256=$identity.coreSha256;originalPolicySha256=$identity.originalPolicySha256;
 focusedTests=[ordered]@{passed=29;failed=0;skipped=0};sources=$sources;
 originalSamplingChanged=$false;prescriptionsChanged=$false;referencesChanged=$false;tolerancesChanged=$false;
 nativeCaptured=$false;full3024Rerun=$false;nativeRejectionAlgorithmReproduced=$false;samplingIssueClosed=$false})

# Reuse existing immutable payloads by content hash rather than copy them again.
$previousRoot=Join-Path $repo 'validation/rechecks/2026-11-04/all-example-accuracy'
$previous=@{};foreach($e in @(Read (Join-Path $previousRoot 'archive-index.json'))){if(!$previous.ContainsKey($e.sha256)){$previous[$e.sha256]=$e}}
$archivePath=Join-Path $out 'evidence.zip'
$file=[IO.File]::Open($archivePath,[IO.FileMode]::CreateNew)
$zip=[IO.Compression.ZipArchive]::new($file,[IO.Compression.ZipArchiveMode]::Create)
$manifest=[Collections.Generic.List[object]]::new();$stored=@{}
try{
 foreach($source in @(@{root=(Join-Path $working 'final');prefix='final'},@{root=(Join-Path $working 'afocal');prefix='afocal'},@{root=(Join-Path $working 'probe/phases');prefix='phase-probe'})){
  foreach($f in Get-ChildItem -LiteralPath $source.root -Recurse -File){
   $hash=Hash $f.FullName;$entry="payloads/$hash";$container='evidence.zip'
   if($previous.ContainsKey($hash)){$old=$previous[$hash];$container=[IO.Path]::GetRelativePath($out,[IO.Path]::GetFullPath((Join-Path $previousRoot $old.archive))).Replace('\','/');$entry=$old.entry}
   elseif(!$stored.ContainsKey($hash)){$target=$zip.CreateEntry($entry,[IO.Compression.CompressionLevel]::Optimal);$input=$f.OpenRead();$output=$target.Open();try{$input.CopyTo($output)}finally{$input.Dispose();$output.Dispose()};$stored[$hash]=$true}
   $manifest.Add([pscustomobject]@{logicalPath=$source.prefix+'/'+[IO.Path]::GetRelativePath($source.root,$f.FullName).Replace('\','/');archive=$container;entry=$entry;sha256=$hash;length=$f.Length})
  }
 }
}finally{$zip.Dispose();$file.Dispose()}
Save 'archive-index.json' @($manifest)
$opened=@{};$verified=@{};$total=0
try{
 foreach($item in $manifest){
  $absolute=[IO.Path]::GetFullPath((Join-Path $out $item.archive));$key=$absolute+'|'+$item.entry
  if(!$verified.ContainsKey($key)){
   if($null-eq$item.entry){$bytes=[IO.File]::ReadAllBytes($absolute)}else{
    if(!$opened.ContainsKey($absolute)){$opened[$absolute]=[IO.Compression.ZipFile]::OpenRead($absolute)}
    $input=$opened[$absolute].GetEntry($item.entry).Open();$memory=[IO.MemoryStream]::new();try{$input.CopyTo($memory);$bytes=$memory.ToArray()}finally{$input.Dispose();$memory.Dispose()}
   }
   if($bytes.Length-ne$item.length-or[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($bytes))-ne$item.sha256){throw 'Archive readback mismatch'}
   $verified[$key]=$true
  };$total++
 }
}finally{foreach($z in $opened.Values){$z.Dispose()}}
Save 'archive-verification.json' ([ordered]@{archive='evidence.zip';sha256=Hash $archivePath;bytes=(Get-Item -LiteralPath $archivePath).Length;
 logicalFiles=$total;newUniquePayloads=$stored.Count;allEntriesReadbackVerified=$true;expandedEvidenceRetired=$false})
Write-Output "Verified $total logical payloads, $($same+$equivalent.Count) numeric arrays within original tolerance ($same byte-identical), 29 tests."
