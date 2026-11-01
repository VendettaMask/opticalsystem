param([string]$Repository='D:/Projects/opticalsystem',[string]$ConcurrentAudit)
$ErrorActionPreference='Stop';$repo=(Resolve-Path -LiteralPath $Repository).Path
$root=Join-Path $repo 'artifacts/validation/grid-mtf-final-core-20261101'
$regression=Join-Path $repo 'artifacts/validation/grid-mtf-final-regression-20261101'
$fine=Join-Path $repo 'artifacts/validation/grid-mtf-final-fine-reference-20261101'
$destination=Join-Path $repo 'validation/rechecks/2026-11-01/grid-grin-mtf-phase'
foreach($name in 'evidence.zip','manifest.json','verification.json','scope.json'){
 if(Test-Path -LiteralPath (Join-Path $destination $name)){throw 'Never overwrite sealed evidence'}
}
$identity=Get-Content -LiteralPath "$root/identity.json" -Raw|ConvertFrom-Json
$core=$identity.coreSha256
$defaultCore=(Get-FileHash -LiteralPath "$repo/src/OpticalSystemDesign.Core/bin/Release/net10.0/OpticalSystemDesign.Core.dll").Hash.ToLowerInvariant()
if($core -ne (Get-FileHash -LiteralPath "$root/runtime/OpticalSystemDesign.Core.dll").Hash.ToLowerInvariant() -or
 $identity.gatesChanged -or $identity.defaultAppBuildExit -ne 0){throw 'Frozen/default product identity mismatch'}
if($defaultCore -ne $core){
 # Concurrent work may rebuild the shared assembly. Never relabel frozen
 # numerical runs or overwrite that work. Require exact optical source and
 # resource parity plus separately identified checks on that rebuilt Core.
 $audit=Get-Content -LiteralPath "$root/pdb-source-audit.json" -Raw|ConvertFrom-Json
 $resources=Get-Content -LiteralPath "$root/embedded-resource-audit.json" -Raw|ConvertFrom-Json
 $proofRoot=Join-Path $repo 'artifacts/validation/grid-mtf-default-core-proof-20261101'
 $proofIdentity=Get-Content -LiteralPath "$proofRoot/identity.json" -Raw|ConvertFrom-Json
 $proof=Get-Content -LiteralPath "$proofRoot/compatibility.json" -Raw|ConvertFrom-Json
 if(!$ConcurrentAudit){throw 'Fresh audit required for concurrent default builds'}
 $latestAudit=Get-Content -LiteralPath $ConcurrentAudit -Raw|ConvertFrom-Json
 if(!$audit.documentSetsEqual -or !$resources.allResourcesEqual -or !$latestAudit.onlyUnrelatedChanges -or
  !$latestAudit.resourcesUnchanged -or $latestAudit.defaultCore -ne $defaultCore -or $latestAudit.acceptanceCore -ne $core -or
  !$proof.wavefrontDataByteIdentical -or $proof.checksPassed -ne 12 -or
  $proof.acceptanceRecalculationRelabeled){throw 'Unverified concurrent build drift'}
 foreach($doc in $audit.documents){
  $relative=[IO.Path]::GetRelativePath((Join-Path $repo 'src/OpticalSystemDesign.Core'),$doc.path).Replace('\','/')
  $reviewedStore=$relative -eq 'Serialization/StarOptProjectStore.cs' -and
   (Get-FileHash -LiteralPath $doc.path).Hash.ToLowerInvariant() -eq 'ce59ff1bf623842d8a0e32f08f29459a6e26b9f492fdd3fa24551c2f34c1ff6e'
  $unrelated=$relative -eq 'Optimization/StationaryPointSearch.cs' -or $relative.StartsWith('NonSequential/') -or $reviewedStore
  if(!$unrelated -and $doc.expected -ne (Get-FileHash -LiteralPath $doc.path).Hash.ToLowerInvariant()){throw 'Optical source drift'}
 }
}
foreach($s in (Get-Content -LiteralPath "$root/source-fingerprint.json" -Raw|ConvertFrom-Json)){
 if($s.sha256 -ne (Get-FileHash -LiteralPath (Join-Path $repo $s.path)).Hash.ToLowerInvariant()){throw 'Final source drift'}
}
$execution=@(Get-Content -LiteralPath "$root/execution.json" -Raw|ConvertFrom-Json)+@(Get-Content -LiteralPath "$regression/execution.json" -Raw|ConvertFrom-Json)
if($execution.Count -ne 34 -or @($execution|Where-Object {!$_.complete -or $_.exitCode -ne 0 -or $_.coreSha256 -ne $core}).Count){throw '34 same-Core final executions required'}
$mtf=@(Get-Content -LiteralPath "$root/compare-results.json" -Raw|ConvertFrom-Json)
$reg=@(Get-Content -LiteralPath "$regression/compare-results.json" -Raw|ConvertFrom-Json)
$fin=@(Get-Content -LiteralPath "$fine/compare-results.json" -Raw|ConvertFrom-Json)
if($mtf.Count -ne 2 -or $reg.Count -ne 32 -or $fin.Count -ne 2 -or
 @(@($mtf+$reg+$fin)|Where-Object {$_.conclusion -ne 'Pass' -or $_.workbenchCoreSha256 -ne $core}).Count){throw 'Final direct comparisons must pass original gates'}
$convergence=Get-Content -LiteralPath "$root/core-convergence-results.json" -Raw|ConvertFrom-Json
$native=Get-Content -LiteralPath "$repo/artifacts/validation/incomparable-final-mtf-2048-assessment-20261101/convergence-results.json" -Raw|ConvertFrom-Json
$nativeStep=Get-Content -LiteralPath "$repo/artifacts/validation/incomparable-grid-mtf-native-high-corrected-step-assessment-20261101/convergence-results.json" -Raw|ConvertFrom-Json
if($convergence.conclusion -ne 'Pass' -or $native.conclusion -ne 'Pass' -or $nativeStep.conclusion -ne 'Pass'){throw 'Independent convergence missing'}
[xml]$trx=Get-Content -LiteralPath (Join-Path $repo $identity.testReport) -Raw
if($trx.TestRun.ResultSummary.Counters.passed -ne '125' -or $trx.TestRun.ResultSummary.Counters.failed -ne '0'){throw '125 focused tests must pass'}
$previous=Join-Path $repo 'validation/rechecks/2026-11-01/example-incomparable-repair'
$prior=@(foreach($item in @(
 @{name='accepted-evidence.zip';sha='4c8bb49a9546f4bfcd076f0f46c7f2fc203b04450f047f1008a7d0c1f37408cd'},
 @{name='psf-evidence.zip';sha='87e0fc48419b7f37b11c152cd4a04b47897b37140dd7266890f94f2f562d8671'},
 @{name='bounded-supplement.zip';sha='59cbc409be950b7388c627d3af79b45fba10b79c074e1ccb7e0b9cb6c61b0866'},
 @{name='native-frontier.zip';sha='04d9d31dd71491ba022b8166b88b4e7616c57d474aae65ab6e0a32ef34598254'})){
 $sha=(Get-FileHash -LiteralPath (Join-Path $previous $item.name)).Hash.ToLowerInvariant()
 if($sha -ne $item.sha){throw 'Earlier evidence changed'};@{name=$item.name;sha256=$sha;unchanged=$true}
})
$accepted=Get-Content -LiteralPath "$previous/bounded-scope.json" -Raw|ConvertFrom-Json
$closed=@($accepted.closedIndices)+@(497)|Sort-Object -Unique
foreach($index in $accepted.closedIndices){if(!@($reg|Where-Object {$_.index -eq $index -and $_.conclusion -eq 'Pass'}).Count){throw 'Prior accepted row missing'}}
$scope=@{closedRequestCount=25;closedIndices=$closed;openIndices=@(499,502);all27Closed=$false;gatesChanged=$false;coreSha256=$core
 focusedTestsPassed=125;workbenchRequestsCompleted=34;adjacentRegressions=8;desktopUsed=$false;defaultAppBuildPassedAtFrozenVersion=$true
 defaultAppBuildCoreSha256=$core;defaultCoreAtSeal=$defaultCore;defaultCoreMatchesAcceptance=($defaultCore -eq $core)
 concurrentBuildCompatibilityChecks=if($defaultCore -ne $core){12}else{0};concurrentBuildResultsNotRelabeled=$true
 compatibilityCheckCore=if($defaultCore -ne $core){$proofIdentity.testedCoreSha256}else{$core}
 unrelatedHistoricalEnergyFailuresNotUpgraded=16;customGridExactZeroChiefRemainsDifference=$true;nativeSampling=2048;nativeMaximumStep=.01;nativeStepControlSampling=1024;nativeStepControl=.005
 coreSampling=@(1024,2048);fineReferenceAlsoCompared=$true;priorArchives=$prior;recordedUtc=[DateTimeOffset]::UtcNow}
$files=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Add-File([string]$path){$p=(Resolve-Path -LiteralPath $path).Path;if(!$p.StartsWith($repo+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Evidence outside repository'};[void]$files.Add($p)}
$studies=@('grid-mtf-final-core-20261101','grid-mtf-final-regression-20261101','grid-mtf-final-controls-20261101','grid-mtf-final-fine-reference-20261101',
 'grid-rule-rays-native-20261101','grid-rule-holdout-native-20261101','grid-rule-holdout-index-native-20261101','grid-window-rounding-native-20261101',
 'grid-floor-query-candidate-20261101','grid-floor-query-index-20261101','grid-rule-tensor-windows-20261101','grid-canonical-1024-assessment-20261101',
 'grid-canonical-mtf-20261101','grid-mtf-product-core-20261101','grid-mtf-product-regression-20261101',
 'grid-read-cache-candidate-20261101','grid-read-cache-baseline-20261101',
 'grid-mtf-native-cross-control-20261101','grid-mtf-final-1024-assessment-20261101',
 'grid-mtf-zero-chief-diagnostic-20261101','grid-mtf-zero-chief-retry-20261101','grid-zero-chief-native-step-20261101',
 'grid-mtf-default-core-proof-20261101',
 'incomparable-grid-mtf-native-high-corrected-20261101','incomparable-grid-mtf-native-high-corrected-step-assessment-20261101','incomparable-final-mtf-2048-assessment-20261101')
foreach($study in $studies){
 $folder=Join-Path $repo "artifacts/validation/$study";$paths=& rg --files --hidden $folder
 if($LASTEXITCODE -ne 0){throw 'Evidence enumeration failed'}
 foreach($p in $paths){if($p -notmatch '[\\/](runtime|runner|core-source|private-core|bin|obj)[\\/]' -and [IO.Path]::GetExtension($p).ToLowerInvariant() -in '.json','.txt','.csv','.ggd','.zmx'){Add-File $p}}
 if(Test-Path -LiteralPath "$folder/models.json"){
  foreach($m in (Get-Content -LiteralPath "$folder/models.json" -Raw|ConvertFrom-Json)){
   if($m.input -and (Test-Path -LiteralPath $m.input)){Add-File $m.input;$s=[IO.File]::ReadAllText($m.input);$g=[regex]::Match($s,'TYPE GRINSU11 ([^\r\n]+)').Groups[1].Value.Trim();if($g -and (Test-Path -LiteralPath $g)){Add-File $g}}
  }
 }
}
foreach($job in $execution){$request=Get-Content -LiteralPath $job.request -Raw|ConvertFrom-Json;Add-File $request.snapshotPath}
foreach($row in @($mtf+$reg+$fin)){
 foreach($name in 'data.json','data.txt','model.json','environment.json','captured-settings.json','result-diagnostics.json'){
  $path=Join-Path $row.native $name;if(Test-Path -LiteralPath $path){Add-File $path}
 }
}
foreach($name in 'OpticalSystemDesign.Core.dll','OpticalSystemDesign.Application.dll','IncomparableRepair20261101.dll','OpticalSystemDesign.ZemaxComparison.dll'){Add-File "$root/runtime/$name"}
if($defaultCore -ne $core){foreach($name in 'OpticalSystemDesign.Core.dll','OpticalSystemDesign.Tests.dll'){Add-File "$proofRoot/runtime/$name"}}
if($ConcurrentAudit){Add-File $ConcurrentAudit}
foreach($p in 'src/OpticalSystemDesign.Core/Materials/GridGradientIndexProfile.cs','src/OpticalSystemDesign.Core/Propagation/GradientIndexRayIntegrator.cs',
 'src/OpticalSystemDesign.Core/Materials/Data/sample-grid-gradient.ggd','tests/OpticalSystemDesign.Tests/GridGradientPhaseTests.cs',
 'tests/OpticalSystemDesign.Tests/Fixtures/grid-gradient-phase-native-v1.json','docs/GRID_GRIN_MTF_PHASE_REPAIR_2026-11-01.md',
 'tests/OpticalSystemDesign.Tests/TestResults/grid-grin-phase-first-20261101.trx','tests/OpticalSystemDesign.Tests/TestResults/grid-grin-phase-refinement-20261101.trx'){Add-File (Join-Path $repo $p)}
foreach($p in Get-ChildItem -LiteralPath $PSScriptRoot -File){Add-File $p.FullName}
New-Item -ItemType Directory -Path $destination -Force|Out-Null
$scope|ConvertTo-Json -Depth 12|Set-Content -LiteralPath "$destination/scope.json";Add-File "$destination/scope.json"
$manifest=@(foreach($p in @($files|Sort-Object)){@{entry=[IO.Path]::GetRelativePath($repo,$p).Replace('\','/');source=$p;length=(Get-Item -LiteralPath $p).Length;sha256=(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}})
Add-Type -AssemblyName System.IO.Compression;$archive=Join-Path $destination 'evidence.zip';$zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($p in $manifest){[void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$p.source,$p.entry,[IO.Compression.CompressionLevel]::Optimal)}}finally{$zip.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try{foreach($p in $manifest){$stream=$zip.GetEntry($p.entry).Open();try{if([Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream)) -ne $p.sha256){throw 'Archive hash mismatch'}}finally{$stream.Dispose()}}}finally{$zip.Dispose()}
$manifest|ConvertTo-Json -Depth 8|Set-Content -LiteralPath "$destination/manifest.json"
@{archive=$archive;sha256=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant();bytes=(Get-Item -LiteralPath $archive).Length;entries=$manifest.Count
 allEntriesReadBackAndMatched=$true;coreSha256=$core;closedRequests=25;all27Closed=$false;sealedUtc=[DateTimeOffset]::UtcNow}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath "$destination/verification.json"
Get-Content -LiteralPath "$destination/verification.json"
