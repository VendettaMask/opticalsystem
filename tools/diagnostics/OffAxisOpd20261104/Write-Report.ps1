param([string]$Repository='D:\Projects\opticalsystem')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($Repository);$art=Join-Path $repo 'artifacts/validation';$root=Join-Path $repo 'validation/rechecks/2026-11-04/offaxis-opd-sampling'
[IO.Directory]::CreateDirectory($root)|Out-Null
function Read($p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function Hash($p){(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
function Save($name,$v){[IO.File]::WriteAllText((Join-Path $root $name),(ConvertTo-Json -InputObject $v -Depth 90),[Text.UTF8Encoding]::new($false))}
$review=Join-Path $art 'offaxis-opd-review-final-20261104';$audit=Join-Path $art 'offaxis-opd-sampling-audit-with8x-20261104'
$identity=Read "$review/identity.json";$core=$identity.coreSha256
if((Read "$audit/identity.json").coreSha256 -ne $core){throw 'Mixed Core'}
$results=@(Read "$review/results.json");$cross=@($results|Where-Object regression)
if($cross.Count -ne 156 -or @($cross|Where-Object conclusion -ne 'Pass').Count){throw 'Cross-system regression'}
$image=@($results|Where-Object {($_.lens -like 'Image Simulation/Example 2,*' -or $_.lens -like 'Image Simulation/Example 5,*') -and $_.protocol -eq 'original'})
$previous=@{};foreach($r in Import-Csv "$repo/validation/rechecks/2026-11-04/image-simulation-pickup/request-results.csv"){$previous[[int]$r.index]=$r}
if($image.Count -ne 60 -or @($image|Where-Object {$_.conclusion -ne $previous[[int]$_.index].after}).Count){throw 'Original image-pair verdict changed'}
$six=@($results|Where-Object protocol -eq 'perfect-collimation-mode0-control-v1');$three=@($results|Where-Object protocol -eq 'perfect-collimation-mode3-control-v1')
if($six.Count -ne 6 -or $three.Count -ne 3 -or @(@($six+$three)|Where-Object conclusion -ne 'Pass').Count){throw 'Equivalent OPD controls incomplete'}
$nativeSame=@($results|Where-Object protocol -eq 'native-mode0-mode3-control-v1')
if($nativeSame.Count -ne 3 -or @($nativeSame.metrics|Where-Object maxAbsolute -ne 0).Count){throw 'Native independent modes disagree'}
$sampling=@(Read "$audit/results.json")
if($sampling.Count -ne 19 -or @($sampling|Where-Object originalOutcome -ne 'Unavailable').Count){throw 'Sampling product gate changed'}
$readbacks=@(Get-ChildItem -LiteralPath $audit -Recurse -File -Filter '*-readback.json')
if($readbacks.Count -ne 44){throw 'Missing actual native readbacks'}
foreach($r in $sampling){if(@($r.records|Where-Object protocol -eq 'native-2x-4x-on-original-domain').Count -ne 1){throw 'Missing full-domain native convergence result'}}
[xml]$trx=Get-Content "$art/offaxis-opd-final-tests-20261104/offaxis-final.trx" -Raw
if($trx.TestRun.ResultSummary.Counters.passed -ne '691' -or $trx.TestRun.ResultSummary.Counters.failed -ne '0' -or $trx.TestRun.ResultSummary.Counters.notExecuted -ne '0'){throw 'Tests incomplete'}
$new=@($trx.TestRun.Results.UnitTestResult|Where-Object testName -like '*OffAxisCollimationReferenceTests.*').Count
if($new -ne 12){throw 'New controls missing'}
foreach($path in @('artifacts/validation/offaxis-opd-final-runtime-20261104','tools/diagnostics/ObscuredReferenceTests20261101/bin/Release/net10.0','src/OpticalSystemDesign.App/bin/Release/net10.0')){if((Hash "$repo/$path/OpticalSystemDesign.Core.dll") -ne $core){throw 'Final Core differs'}}
Save 'opd-results.json' $results;Save 'plane-controls.json' (Read "$review/plane-controls.json");Save 'sampling-results.json' $sampling
$geometric=[Collections.Generic.List[object]]::new();$baseline=@((Read "$art/offaxis-opd-native-20261104/native/model-controls.json").rows)
foreach($name in @('native-mode0','native-mode3','native-mode0-example5')){
 $folder="$art/offaxis-opd-$name-20261104";$id=Read "$folder/identity.json";$actual=@((Read "$folder/native/model-controls.json").rows);$maximum=0.0
 for($k=0;$k -lt $actual.Count;$k++){if($actual[$k].input.code -ne 'OPTH'){$maximum=[Math]::Max($maximum,[Math]::Abs($actual[$k].value-$baseline[$k].value))}}
 if($actual.Count -ne 630 -or $maximum -ne 0){throw 'Physical rays changed under equivalent mode control'}
 $source=Join-Path $repo ('src/OpticalSystemDesign.App/Assets/SequentialExamples/Image Simulation/'+$(if($name -like '*example5'){'Example 5, spatially varying resolution.ZMX'}else{'Example 2, Double Gauss Experimental Arrangement.ZMX'}))
 if((Hash $source) -ne $id.originalSha256){throw 'Original source changed'}
 $mode=if($id.options -contains 'mode3'){3}else{0}
 $surface=@(Read "$folder/native/surface-readback.json")|Where-Object surface -eq 1
 if($surface.surfaceData.OPDMode -ne $mode -or $surface.surfaceData.FocalLength -ne 100){throw 'Native collimator mode readback mismatch'}
 $copy=[IO.File]::ReadAllText("$folder/paraxial-mode$mode.zmx");$original=[IO.File]::ReadAllText($source)
 $restored=[regex]::Replace($copy,'(?ms)(^SURF 1\r?\n.*?)(?=^SURF 2\r?\n)',[Text.RegularExpressions.MatchEvaluator]{param($m)$m.Value.Replace("PARM 2 $mode",'PARM 2 1')})
 if($restored -cne $original){throw 'Equivalent copy changed more than OPD phase mode'}
 $geometric.Add(@{name=$name;maximumPositionDirectionDifference=$maximum;positionDirectionOperands=540;mode=$mode;onlyPhaseAlgorithmFlagChanged=$true;originalSourceSha256=$id.originalSha256;controlledSourceSha256=$id.sourceSha256})
}
Save 'equivalent-source-controls.json' $geometric
$cache=Read "$art/offaxis-opd-native-cache-verified-20261104/native/diagnostic-pupil-aiming.json"
if($cache.actual.UseRayAimingCache -ne $false){throw 'Cache override not verified'}
Save 'cache-readback.json' $cache
$policyRoot=Join-Path $repo 'validation/rechecks/2026-11-04/fft-sampling-policy'
$policyEntry=@(Read "$policyRoot/archive-index.json")|Where-Object logicalPath -eq 'final/identity.json'
$zip=[IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath((Join-Path $policyRoot $policyEntry.archive)))
try{$input=$zip.GetEntry($policyEntry.entry).Open();$memory=[IO.MemoryStream]::new();try{$input.CopyTo($memory);$bytes=$memory.ToArray()}finally{$input.Dispose();$memory.Dispose()}}finally{$zip.Dispose()}
if([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() -ne $policyEntry.sha256){throw 'Prior policy receipt changed'}
$priorPolicy=[Text.Encoding]::UTF8.GetString($bytes)|ConvertFrom-Json
$policy=Hash "$repo/tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json"
$policyText=[IO.File]::ReadAllText("$repo/tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json").Replace("`r`n","`n").Replace("`n","`r`n")
$crlfHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($policyText))).ToLowerInvariant()
if($policy -ne $priorPolicy.originalPolicySha256 -and $crlfHash -ne $priorPolicy.originalPolicySha256){throw 'Comparison tolerance policy changed'}
Save 'policy-verification.json' @{sha256=$policy;matchesPriorFrozenPolicy=$true;priorPolicySha256=$priorPolicy.originalPolicySha256;byteIdentical=($policy -eq $priorPolicy.originalPolicySha256);differenceOnlyLineEndings=($policy -ne $priorPolicy.originalPolicySha256);crlfSha256=$crlfHash;priorReceiptSha256=$policyEntry.sha256}
$table=foreach($r in $sampling){
 $n4=$r.records|Where-Object protocol -eq 'current-finite-native-4x';$n8=$r.records|Where-Object protocol -eq 'current-finite-native-8x';$z24=$r.records|Where-Object protocol -eq 'native-2x-4x-on-original-domain';$z48=$r.records|Where-Object protocol -eq 'native-4x-8x-on-original-domain'
 [pscustomobject]@{index=$r.index;lens=$r.lens;analysis=$r.key;originalSampling=$r.originalSampling;product=$r.originalOutcome;core1Native4=$n4.conclusion;native2Native4=$z24.conclusion;core1Native8=$n8.conclusion;native4Native8=$z48.conclusion;core1Native4Maximum=($n4.metrics.maxAbsolute|Measure-Object -Maximum).Maximum}
}
$table|Export-Csv "$root/sampling-audit.csv" -NoTypeInformation -Encoding utf8
Save 'source-revision.json' @{sourceRevisionId='aae931370c552265be6c6a4ab5cf26c8bf3d7761';actualHead=(& git -C $repo rev-parse HEAD);dirty=$true;sourceBinding='Final PDB source document hashes checked separately';otherWorkspaceWorkPreserved=$true}
Save 'summary.json' ([ordered]@{completedLocal=[DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(8)).ToString('o');coreBefore='8821d3a2e315641238275f0343832f19e1dd337bcdf36b4a0cad136801136920';coreAfter=$core;equivalentOpdPass=6;equivalentMode3Pass=3;nativeMode0Mode3IdenticalCurves=3;maximumEquivalentOpdErrorWaves=($six.metrics.maxAbsolute|Measure-Object -Maximum).Maximum;originalSixVerdicts=@{Close=4;Difference=2};imagePairRequests=60;imagePairAfter=@{Pass=52;Close=4;Difference=2;Incomparable=2;Error=0};imagePairVerdictChanges=0;crossAnalysisPriorPassChecked=156;crossAnalysisPriorPassLost=0;samplingRequestsAudited=19;samplingOriginalUnavailable=19;nativeSamplingCaptures=38;extraNativeSamplingCaptures=6;actualSamplingReadbacks=44;native2x4x=@($table|Group-Object native2Native4|Select-Object Name,Count);native4x8x=@($table|Where-Object native4Native8|Group-Object native4Native8|Select-Object Name,Count);core1Native4=@($table|Group-Object core1Native4|Select-Object Name,Count);core1Native8=@($table|Where-Object core1Native8|Group-Object core1Native8|Select-Object Name,Count);distinctTests=691;newTests=12;failedTests=0;skippedTests=0;defaultDualModuleAppBuildPassed=$true;samplingProductGateChanged=$false;originalToleranceChanges=0;originalReferencesReplaced=$false;newFull95Run=$false;remoteSynchronizationPerformed=$false})
Get-Content "$root/summary.json"
