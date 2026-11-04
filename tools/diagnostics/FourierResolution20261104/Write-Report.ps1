param([string]$Repository='D:\Projects\opticalsystem')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($Repository);$art=Join-Path $repo 'artifacts/validation';$root=Join-Path $repo 'validation/rechecks/2026-11-04/fourier-result-validation'
[IO.Directory]::CreateDirectory($root)|Out-Null
function Read($p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function Hash($p){(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
function Save($name,$v){[IO.File]::WriteAllText((Join-Path $root $name),(ConvertTo-Json -InputObject $v -Depth 90),[Text.UTF8Encoding]::new($false))}
$sixRoot="$art/fourier-resolution-six-sealed-20261104";$otherRoot="$art/fourier-resolution-thirteen-sealed-20261104";$refRoot="$art/fourier-resolution-native-convergence-sealed-20261104"
$core=(Read "$sixRoot/identity.json").coreSha256
foreach($path in @($otherRoot,$refRoot,"$art/fourier-resolution-default-controls-20261104")){if((Read "$path/identity.json").coreSha256 -ne $core){throw 'Mixed final Core'}}
$six=@(Read "$sixRoot/results.json");$priority=@($six|Where-Object algorithm -eq 'Adaptive')
if($priority.Count -ne 6 -or @($priority|Where-Object conclusion -ne 'Pass').Count){throw 'Priority controls incomplete'}
if(($six|Where-Object algorithm -eq 'ValidateRequested').outcome -ne 'Unavailable'){throw 'Original 831 must not be silently accepted'}
$other=@(Read "$otherRoot/results.json");$references=@(Read "$refRoot/results.json")
$best=@($references|Where-Object conclusion -eq 'Pass'|Group-Object index|ForEach-Object {$_.Group|Sort-Object b -Descending|Select-Object -First 1})
if($best.Count -ne 13){throw 'Unconverged native references remain'}
$capture=[Collections.Generic.List[object]]::new()
foreach($n in @(512,1024,2048,4096)){
 foreach($r in (Read "$art/fourier-resolution-native-$n-20261104/results.json")){
  if(!$r.valid -or $r.run.exitCode -ne 0 -or $r.run.timedOut){throw 'Invalid native control'}
  $settings=Read "$art/fourier-resolution-native-$n-20261104/$($r.index)-$n/native/captured-settings.json"
  if(!$settings.properties.SampleSize.EndsWith("_${n}x${n}")){throw 'Native sampling readback differs'}
  if((Hash $settings.request.input) -ne $r.sourceSha256){throw 'Native original source changed'}
  $capture.Add($r)
 }
}
if($capture.Count -ne 33){throw 'Missing native captures'}
$fixed=foreach($path in @($sixRoot,$otherRoot)){foreach($dir in Get-ChildItem -LiteralPath $path -Directory){if(Test-Path "$($dir.FullName)/fixed-metadata.json"){$d=Read "$($dir.FullName)/fixed-metadata.json";[pscustomobject]@{index=[int]$dir.Name;outcome=$d.outcome;reason=$d.outcomeReason}}}}
if(@($fixed).Count -ne 19 -or @($fixed|Where-Object outcome -ne 'Unavailable').Count){throw 'Original fixed requests changed'}
$controls=@(Read "$art/fourier-resolution-default-controls-20261104/default-controls.json")
if($controls.Count -ne 4 -or @($controls|Where-Object priorNumericalOutputIdentical -ne $true).Count){throw 'Default numerical arrays changed'}
[xml]$trx=Get-Content "$art/fourier-resolution-tests-sealed-20261104/fourier-sealed.trx" -Raw
if($trx.TestRun.ResultSummary.Counters.passed -ne '707' -or $trx.TestRun.ResultSummary.Counters.failed -ne '0' -or $trx.TestRun.ResultSummary.Counters.notExecuted -ne '0'){throw 'Tests incomplete'}
$new=@($trx.TestRun.Results.UnitTestResult|Where-Object testName -like '*FourierResultValidationTests.*').Count
if($new -ne 16){throw 'New test identities changed'}
foreach($path in @('artifacts/validation/fourier-resolution-final-runtime-20261104','tools/diagnostics/ObscuredReferenceTests20261101/bin/Release/net10.0')){if((Hash "$repo/$path/OpticalSystemDesign.Core.dll") -ne $core){throw 'Final Core differs'}}
if((Hash "$repo/src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Core.dll") -ne $core){
 if(!(Test-Path "$root/default-app-build-receipt.json") -or (Read "$root/default-app-build-receipt.json").validatedCoreSha256 -ne $core){throw 'Default output changed without a prior checked build receipt'}
}
$policy=Hash "$repo/tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json"
if($policy -ne (Read "$repo/validation/rechecks/2026-11-04/offaxis-opd-sampling/policy-verification.json").sha256){throw 'Original tolerance policy changed'}
$remaining=@($other|Where-Object outcome -eq 'Unavailable');$additional=@($other|Where-Object conclusion -eq 'Pass'|Group-Object index)
if($remaining.Count -ne 7 -or $additional.Count -ne 6){throw 'Unreviewed Core outcomes'}
$table=foreach($r in $best){$controlsForCase=@($other|Where-Object index -eq $r.index);$ok=@($controlsForCase|Where-Object conclusion -eq 'Pass');$unavailable=$controlsForCase|Where-Object outcome -eq 'Unavailable';[pscustomobject]@{index=$r.index;nativeLower=$r.a;nativeHigher=$r.b;nativeConvergence=$r.conclusion;coreOutcome=if($ok.Count){'Pass'}else{'Unavailable'};coreSampling=if($ok.Count){$ok[0].validation.returnedPupilSampling}else{$null};reason=$unavailable.outcomeReason}}
$table|Sort-Object index|Export-Csv "$root/reference-and-core-audit.csv" -NoTypeInformation -Encoding utf8
Save 'priority-results.json' $six;Save 'other-core-results.json' $other;Save 'native-convergence.json' $references;Save 'native-reference-controls.json' $best;Save 'native-capture-results.json' $capture;Save 'original-fixed-results.json' @($fixed);Save 'default-controls.json' $controls
Save 'source-revision.json' @{sourceRevisionId='aae931370c552265be6c6a4ab5cf26c8bf3d7761';actualHead=(& git -C $repo rev-parse HEAD);dirty=$true;otherWorkspaceWorkPreserved=$true;sourceBinding='Final PDB document hashes checked separately'}
Save 'summary.json' ([ordered]@{completedLocal=[DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(8)).ToString('o');coreBefore='71a0baa02fda4ae04e8fcb7ad100be4437e73395660ef8fb07482f3f3f7508e4';coreAfter=$core;priorityPass=6;priorityEffectiveSampling=@($priority|Select-Object index,@{n='sampling';e={$_.validation.returnedPupilSampling}});original831Validation='Unavailable; plot agreement does not certify the reported central Strehl';referencesConverged=13;nativeSamplingCaptures=33;nativeConverged512To1024=9;nativeConverged1024To2048=1;nativeConverged2048To4096=3;samplingRequestsAudited=19;originalFixedUnavailable=19;additionalCorePass=6;additionalCoreUnavailable=7;remainingCoreIndices=@($remaining.index|Sort-Object);totalAdaptivePass=12;totalAdaptiveUnavailable=7;defaultNumericalArraysIdentical=4;distinctTests=707;newTests=16;failedTests=0;skippedTests=0;defaultDualModuleAppBuildPassed=$true;defaultMode='Fixed';newProtocol='FourierPhysicalDomainConvergence/v1';originalTolerancePolicySha256=$policy;originalToleranceChanges=0;originalReferenceReplacement=$false;newFull95Run=$false;remoteSynchronizationPerformed=$false})
Get-Content "$root/summary.json"
