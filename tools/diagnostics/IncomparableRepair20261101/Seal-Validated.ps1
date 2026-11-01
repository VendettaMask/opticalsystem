param(
 [string]$Repository='D:/Projects/opticalsystem',
 [string]$Study='artifacts/validation/incomparable-final-batched-20261101',
 [string]$Destination='validation/rechecks/2026-11-01/example-incomparable-repair'
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath $Repository).Path
$root=(Resolve-Path -LiteralPath (Join-Path $repo $Study)).Path
$destinationPath=Join-Path $repo $Destination
$stage=Join-Path $root 'accepted-evidence-stage'
if(Test-Path -LiteralPath $stage){throw 'Fresh evidence stage required'}
if(Test-Path -LiteralPath "$destinationPath/accepted-evidence.zip"){throw 'Immutable evidence already exists'}
function Hash([string]$path){(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()}
function Json($value,[string]$path){$value|ConvertTo-Json -Depth 100|Set-Content -LiteralPath $path -Encoding utf8NoBOM}
function CopyEvidence([string]$from,[string]$relative){
 $resolved=(Resolve-Path -LiteralPath $from).Path
 if(!$resolved.StartsWith($repo.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Evidence must be inside repository'}
 $to=Join-Path $stage $relative
 New-Item -ItemType Directory -Path (Split-Path $to) -Force|Out-Null
 Copy-Item -LiteralPath $resolved -Destination $to
}
$baseline=Join-Path $repo 'artifacts/validation/model-capabilities-stage5-20261101'
$rows=Get-Content -LiteralPath "$baseline/rows.json" -Raw|ConvertFrom-Json
$old=Get-Content -LiteralPath "$baseline/results.json" -Raw|ConvertFrom-Json
$original=@($old|Where-Object conclusion -eq 'Incomparable'|ForEach-Object index)
$pending=@(495,497,499,502)
$accepted=@($original|Where-Object {$_ -notin $pending})
$regressions=@(100,130,160,190,220,242,264,286)
$results=@(Get-Content -LiteralPath "$root/compare-results.json" -Raw|ConvertFrom-Json)
$identity=Get-Content -LiteralPath "$root/identity.json" -Raw|ConvertFrom-Json
if($original.Count -ne 27 -or $accepted.Count -ne 23){throw 'Original incomparable scope changed'}
foreach($index in @($accepted)+@($regressions)){
 $r=@($results|Where-Object index -eq $index)
 if($r.Count -ne 1 -or $r[0].conclusion -ne 'Pass' -or $r[0].workbenchCoreSha256 -ne $identity.coreSha256){throw "Unvalidated current result: $index"}
}
$coreConvergence=@(Get-Content -LiteralPath "$root/core-convergence-results.json" -Raw|ConvertFrom-Json)
if($coreConvergence.Count -ne 6 -or @($coreConvergence|Where-Object conclusion -ne 'Pass').Count){throw 'Crystal Core convergence incomplete'}
$nativeConvergencePath=Join-Path $repo 'artifacts/validation/incomparable-psf-convergence-20261101/convergence-results.json'
$nativeConvergence=@(Get-Content -LiteralPath $nativeConvergencePath -Raw|ConvertFrom-Json)
if($nativeConvergence.Count -ne 6 -or @($nativeConvergence|Where-Object conclusion -ne 'Pass').Count){throw 'Crystal native convergence incomplete'}
$waveConvergencePath=Join-Path $repo 'artifacts/validation/incomparable-native-20261101/convergence-results.json'
$waveConvergence=@(Get-Content -LiteralPath $waveConvergencePath -Raw|ConvertFrom-Json)
if($waveConvergence.Count -ne 1 -or $waveConvergence[0].index -ne 494 -or $waveConvergence[0].conclusion -ne 'Pass'){throw 'Native wavefront convergence incomplete'}
New-Item -ItemType Directory -Path $stage|Out-Null
CopyEvidence "$root/identity.json" 'identity.json'
CopyEvidence "$root/compare-results.json" 'compare-results.json'
CopyEvidence "$root/core-convergence-results.json" 'core-convergence-results.json'
CopyEvidence $nativeConvergencePath 'native-psf-convergence-results.json'
CopyEvidence $waveConvergencePath 'native-wavefront-convergence-results.json'
CopyEvidence "$baseline/rows.json" 'original/rows.json'
CopyEvidence "$baseline/results.json" 'original/results.json'
CopyEvidence "$baseline/identity.json" 'original/identity.json'
$textIndex=@();$nativeFolders=@{}
foreach($result in $results){$nativeFolders[$result.native]=$true}
foreach($pair in $nativeConvergence){$nativeFolders[$pair.nativeA]=$true;$nativeFolders[$pair.nativeB]=$true}
$waveRoot=Join-Path $repo 'artifacts/validation/incomparable-native-20261101'
$nativeFolders["$waveRoot/wavefront-step.005"]=$true
$nativeFolders["$waveRoot/wavefront-step.001"]=$true
foreach($folder in $nativeFolders.Keys|Sort-Object){
 $relative='native/'+[IO.Path]::GetRelativePath($repo,(Resolve-Path -LiteralPath $folder).Path).Replace('\','/')
 foreach($file in Get-ChildItem -LiteralPath $folder -File){
  if($file.Extension -eq '.json' -or $file.Name -eq 'settings.CFG' -or ($file.Name -eq 'data.txt' -and $file.Length -lt 5MB)){
   CopyEvidence $file.FullName "$relative/$($file.Name)"
  }elseif($file.Name -eq 'data.txt'){
   $textIndex+=@{path=$file.FullName;sha256=(Hash $file.FullName);bytes=$file.Length;archived=$false;reason='Full text outside requested numerical buffer retained at source; direct requested buffer is archived'}
  }
 }
}
foreach($jobPath in Get-Content -LiteralPath "$root/workbench-jobs.json" -Raw|ConvertFrom-Json){
 $job=Get-Content -LiteralPath $jobPath -Raw|ConvertFrom-Json
 $index=[int]([regex]::Match((Split-Path $jobPath -Leaf),'core-(\d+)').Groups[1].Value)
 if($index -notin $accepted -and $index -notin $regressions){continue}
 CopyEvidence $jobPath "workbench/$(Split-Path $jobPath -Leaf)"
 foreach($file in Get-ChildItem -LiteralPath $job.outputDirectory -File){CopyEvidence $file.FullName "workbench/$(Split-Path $job.outputDirectory -Leaf)/$($file.Name)"}
 CopyEvidence $job.snapshotPath "models/$(Split-Path $job.snapshotPath -Leaf)"
}
foreach($file in Get-ChildItem -LiteralPath "$root/comparison" -File -Recurse|Where-Object Extension -in '.json','.csv'){
 CopyEvidence $file.FullName ('comparison/'+[IO.Path]::GetRelativePath("$root/comparison",$file.FullName))
}
foreach($file in Get-ChildItem -LiteralPath "$root/core-convergence" -File -Recurse|Where-Object Extension -in '.json','.csv'){
 CopyEvidence $file.FullName ('core-convergence/'+[IO.Path]::GetRelativePath("$root/core-convergence",$file.FullName))
}
foreach($file in 'source-controls.json','grid-step.005.ZMX','grid-step.001.ZMX'){CopyEvidence "$waveRoot/$file" "inputs/$file"}
CopyEvidence "$repo/src/OpticalSystemDesign.Core/Materials/Data/sample-grid-gradient.ggd" 'inputs/Sample.GGD'
foreach($index in $original){
 $row=$rows|Where-Object index -eq $index
 $oldJob=Get-Content -LiteralPath $row.job -Raw|ConvertFrom-Json
 CopyEvidence $row.job "original/workbench/$index/request.json"
 foreach($name in 'data.json','provenance.json'){
  $path=Join-Path $oldJob.outputDirectory $name
  if(Test-Path -LiteralPath $path){CopyEvidence $path "original/workbench/$index/$name"}
 }
 foreach($name in 'data.json','captured-settings.json','model.json','result-diagnostics.json'){
  $path="$($row.legacyPath)/native/$name"
  if(Test-Path -LiteralPath $path){CopyEvidence $path "original/native/$index/$name"}
 }
}
$controls=Join-Path $repo 'artifacts/validation/incomparable-grid-controls-corrected-20261101'
foreach($name in 'native-sphere-diagnostic.json','pupil-pencil-diagnostic.json','controls.request.json','execution.json'){
 CopyEvidence "$controls/$name" "pupil-controls/$name"
}
foreach($file in Get-ChildItem -LiteralPath "$controls/controls" -File|Where-Object Extension -eq '.json'){
 CopyEvidence $file.FullName "pupil-controls/native/$($file.Name)"
}
foreach($file in Get-ChildItem -LiteralPath "$root/runtime" -File){CopyEvidence $file.FullName "runtime/$($file.Name)"}
foreach($file in Get-ChildItem -LiteralPath "$root/runtime/assets" -File -Recurse){
 CopyEvidence $file.FullName ('runtime/assets/'+[IO.Path]::GetRelativePath("$root/runtime/assets",$file.FullName))
}
foreach($source in @(
 'src/OpticalSystemDesign.Core/Analysis/Wavefront/ParabasalPupilEngine.cs',
 'src/OpticalSystemDesign.Core/Analysis/Wavefront/ExitPupilGeometryEngine.cs',
 'src/OpticalSystemDesign.Core/Analysis/Wavefront/WavefrontAnalyses.cs',
 'src/OpticalSystemDesign.Core/Analysis/WavefrontEngine.cs',
 'src/OpticalSystemDesign.Core/Services/Paraxial.cs',
 'src/OpticalSystemDesign.Core/Rays/RayModels.cs',
 'src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.cs',
 'src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.CroppedFourier.cs',
 'src/OpticalSystemDesign.Core/Analysis/Diffraction/PsfAndMtfAnalyses.cs',
 'src/OpticalSystemDesign.Application/Runtime/WorkbenchRuntime.Analysis.cs',
 'src/OpticalSystemDesign.Application/Runtime/WorkbenchRuntime.Analysis.Parameters.cs',
 'tools/OpticalSystemDesign.ZemaxComparison/NativeRaySurfaceMap.cs',
 'tools/OpticalSystemDesign.ZemaxComparison/Normalization/ExtendedResultNormalizer.Geometry.cs',
 'tests/OpticalSystemDesign.Tests/ParabasalPupilTests.cs',
 'tests/OpticalSystemDesign.Tests/CroppedFourierTests.cs',
 'tests/OpticalSystemDesign.Tests/FourierPupilCorrelationTests.cs',
 'tests/OpticalSystemDesign.Tests/WavefrontTraceBatchTests.cs',
 'tests/OpticalSystemDesign.Tests/Fixtures/grid-gradient-pupil-native-v1.json',
 'tests/OpticalSystemDesign.ZemaxComparison.Tests/NativeRaySurfaceMapTests.cs'
)){CopyEvidence (Join-Path $repo $source) "source/$source"}
foreach($test in @(
 'tests/OpticalSystemDesign.Tests/TestResults/incomparable-batched-final-20261101.trx',
 'tests/OpticalSystemDesign.ZemaxComparison.Tests/TestResults/incomparable-batched-comparison-final-20261101.trx'
)){
 [xml]$trx=Get-Content -LiteralPath (Join-Path $repo $test) -Raw
 if([int]$trx.TestRun.ResultSummary.Counters.failed -ne 0 -or [int]$trx.TestRun.ResultSummary.Counters.passed -lt 1){throw 'Final focused tests did not pass'}
 CopyEvidence (Join-Path $repo $test) "tests/$(Split-Path $test -Leaf)"
}
foreach($name in 'core-test-identity.json','comparison-test-identity.json'){
 $tested=Get-Content -LiteralPath "$root/$name" -Raw|ConvertFrom-Json
 if($tested.coreSha256 -ne $identity.coreSha256 -or !$tested.unchangedDuringExecution){throw 'Focused test Core identity mismatch'}
 CopyEvidence "$root/$name" "tests/$name"
}
Json $textIndex "$stage/full-text-index.json"
$contract=@{scope='Stage5Original27Incomparables';acceptedOriginalIndices=$accepted;acceptedOriginalCount=23;
 pendingOriginalIndices=$pending;pendingOriginalCount=4;adjacentWavefrontRegressionIndices=$regressions;
 currentCoreSha256=$identity.coreSha256;applicationSha256=$identity.applicationSha256;
 originalTolerancesUnchanged=$true;physicalInputsUnchanged=$true;independentNumericalSamplingDeclared=$true;
 primaryPrecisionAuthority='Convergence-checked direct Zemax buffers under original per-analysis gates';
 historicalFailuresPreserved=$true;all95ExamplesPrecisionAccepted=$false;desktopInteraction=$false;
 createdUtc=[DateTimeOffset]::UtcNow;originalRowsSha256=(Hash "$baseline/rows.json");originalResultsSha256=(Hash "$baseline/results.json")}
Json $contract "$stage/contract.json"
New-Item -ItemType Directory -Path $destinationPath -Force|Out-Null
$archive="$destinationPath/accepted-evidence.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($stage,$archive,[IO.Compression.CompressionLevel]::Optimal,$false)
$entries=@();$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try{foreach($entry in $zip.Entries|Sort-Object FullName){
 $stream=$entry.Open();try{$sha=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()}
 $file=Join-Path $stage $entry.FullName
 if($sha -ne (Hash $file) -or $entry.Length -ne (Get-Item -LiteralPath $file).Length){throw 'Archive readback mismatch'}
 $entries+=@{path=$entry.FullName;sha256=$sha;bytes=$entry.Length}
}}finally{$zip.Dispose()}
Json @{sha256=(Hash $archive);bytes=(Get-Item -LiteralPath $archive).Length;entries=$entries} "$destinationPath/manifest.json"
Json @{sha256=(Hash $archive);verifiedEntries=$entries.Count;allMatched=$true;verifiedUtc=[DateTimeOffset]::UtcNow} "$destinationPath/verification.json"
Json $contract "$destinationPath/scope.json"
Write-Output "Sealed 23 accepted requests, 8 adjacent regressions; $($entries.Count) verified files; $(Hash $archive)"
