param([string]$Repository='D:\Projects\opticalsystem')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($Repository);$art=Join-Path $repo 'artifacts/validation'
$root=Join-Path $repo 'validation/rechecks/2026-11-03/definition-reference'
[IO.Directory]::CreateDirectory($root)|Out-Null
function Read($path){Get-Content -LiteralPath $path -Raw|ConvertFrom-Json}
function Hash($path){(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()}
function Save($name,$value){[IO.File]::WriteAllText((Join-Path $root $name),(ConvertTo-Json -InputObject $value -Depth 80),[Text.UTF8Encoding]::new($false))}
function Counts($rows){$counts=[ordered]@{Pass=0;Close=0;Difference=0;Incomparable=0;Error=0};foreach($r in $rows){$counts[$r.conclusion]++};$counts}
$primary=@();$prior=@{}
foreach($scope in @('crystals','toroid')){
 $rows=@(Read "$art/definition-$scope-replay-20261103/results.json");$primary+=$rows
 Save "$scope-results.json" $rows;Save "$scope-identity.json" (Read "$art/definition-$scope-replay-20261103/identity.json")
}
foreach($name in @('crystals','toroid-annular')){foreach($r in (Read "$repo/validation/rechecks/2026-11-03/residual-closure/$name-results.json")){$prior[[int]$r.index]=$r}}
$core=$primary[0].coreSha256
if($primary.Count -ne 252 -or @($primary|Where-Object coreSha256 -ne $core).Count){throw 'Primary replay incomplete'}
$changed=@($primary|Where-Object {$_.conclusion -ne $prior[[int]$_.index].conclusion})
if($changed.Count){throw 'Original request verdict changed; review required'}
$cross=@(Read "$art/definition-cross-regression-20261103/results.json")
if($cross.Count -ne 156 -or @($cross|Where-Object {$_.conclusion -ne 'Pass' -or $_.coreSha256 -ne $core}).Count){throw 'Cross-analysis regression'}
Save 'cross-analysis-results.json' $cross
$convention=@(Read "$art/definition-convention-final-20261103/results.json")
if($convention.Count -ne 5 -or @($convention|Where-Object {$_.conclusion -ne 'Pass' -or !$_.legacyMixedConventionRejected -or $_.coreSha256 -ne $core}).Count){throw 'Convention checks incomplete'}
Save 'first-order-convention-results.json' $convention
$original=Read "$art/definition-crystal-probe-20261103/original-c2/model-controls.json"
$ignored=Read "$art/definition-crystal-probe-20261103/parax-ignore/model-controls.json"
$a=@($original.rows|Where-Object {$_.input.code -in @('REAY','REAB','REAC')});$b=@($ignored.rows|Where-Object {$_.input.code -in @('REAY','REAB','REAC')})
if($a.Count -ne 60 -or $b.Count -ne 60){throw 'Real-ray controls incomplete'}
for($i=0;$i -lt $a.Count;$i++){if((ConvertTo-Json $a[$i].input -Compress) -ne (ConvertTo-Json $b[$i].input -Compress) -or $a[$i].value -ne $b[$i].value){throw 'Paraxial override changed a real ray'}}
Save 'crystal-definition-controls.json' @{variants=(Read "$art/definition-crystal-probe-20261103/results.json");unchangedNativeRealRayValues=60;changedRealRayValues=0;efflWavelengthSlot='int2';nativeWorkingCopyOnly=$true;originalSourceChanged=$false}
$validity=@(Read "$art/definition-validity-final-20261103/results.json")
if($validity.Count -ne 4 -or @($validity|Where-Object {$_.outcome -ne 'Unavailable' -or $_.coreSha256 -ne $core}).Count){throw 'FFT availability mismatch'}
Save 'fft-availability-results.json' $validity
$huygens=@((Read "$art/definition-huygens-final-20261103/results.json")|Where-Object index -ne 1370)+@(Read "$art/definition-cross-convergence-20261103/results.json")
if($huygens.Count -ne 21 -or @($huygens|Where-Object {$_.conclusion -ne 'Pass' -or $_.coreSha256 -ne $core}).Count){throw 'Huygens convergence incomplete'}
Save 'huygens-converged-results.json' $huygens
Save 'huygens-cross-lower-sampling.json' @((Read "$art/definition-huygens-final-20261103/results.json")|Where-Object index -eq 1370)
$priorPsf=@(Read "$art/definition-prior-psf-final-20261103/results.json");$priorOther=@(Read "$art/definition-prior-convergence-20261103/results.json")
if($priorPsf.Count -ne 24 -or $priorOther.Count -ne 14 -or @(@($priorPsf+$priorOther)|Where-Object {$_.conclusion -ne 'Pass' -or $_.coreSha256 -ne $core}).Count){throw 'Prior converged controls regressed'}
Save 'prior-converged-results.json' @($priorPsf+$priorOther)
[xml]$tests=Get-Content "$art/definition-scoped-final-20261103/definition-scoped-final.trx" -Raw
[xml]$operands=Get-Content "$art/definition-operands-20261103/definition-operands.trx" -Raw
if($tests.TestRun.ResultSummary.Counters.passed -ne '611' -or $tests.TestRun.ResultSummary.Counters.failed -ne '0' -or $operands.TestRun.ResultSummary.Counters.passed -ne '134' -or $operands.TestRun.ResultSummary.Counters.failed -ne '0'){throw 'Tests incomplete'}
foreach($runtime in @('artifacts/validation/definition-final-runtime-20261103','src/OpticalSystemDesign.App/bin/Release/net10.0','tools/diagnostics/ObscuredReferenceTests20261101/bin/Release/net10.0','tests/OpticalSystemDesign.Tests/bin/Release/net10.0')){if((Hash "$repo/$runtime/OpticalSystemDesign.Core.dll") -ne $core){throw 'Final Core mismatch'}}
Save 'source-revision.json' @{sourceRevisionId='7ab4adc52cbb39107d094e1cd80b16f930f3c25a';actualHead=(& git -C $repo rev-parse HEAD);dirty=$true;capturedLocal=[DateTimeOffset]::Now.ToString('o');sourceBinding='Actual final portable PDB source hashes checked separately'}
Save 'summary.json' ([ordered]@{completedLocal=[DateTimeOffset]::Now.ToString('o');coreBefore=(Read "$repo/validation/rechecks/2026-11-03/residual-closure/summary.json").coreAfter;coreAfter=$core;primaryRequests=252;primaryAfter=(Counts $primary);originalVerdictChanges=0;primaryPriorPassLost=0;crossAnalysisPriorPassChecked=156;crossAnalysisPriorPassLost=0;firstOrderExplicitConventionCasesPassed=5;legacyMixedConventionRejections=5;unchangedNativeRealRayValues=60;originalFirstOrder125Remains='Difference';originalInvalidFftReferences=@(1365,1367,1369,1372);typedUnavailableFftRequests=4;newHuygensConvergenceChecksPassed=21;priorConvergenceChecksRevalidated=38;huygensCrossLowerSamplingConvergence='Close preserved';scopedTests=611;existingOperandTests=134;distinctTests=745;defaultAppBuildPassed=$true;builtInModules=2;sourceRevisionId='7ab4adc52cbb39107d094e1cd80b16f930f3c25a';originalSettingsAndTolerancesPreserved=$true;originalVerdictsPreserved=$true;newFull95Run=$false;huygensEnergyFromCroppedPsfAdded=$false;remoteSynchronizationPerformed=$false})
Get-Content "$root/summary.json"
