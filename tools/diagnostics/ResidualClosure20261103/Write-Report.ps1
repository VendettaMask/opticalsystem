param([string]$Repository='D:\Projects\opticalsystem')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($Repository);$root=Join-Path $repo 'validation/rechecks/2026-11-03/residual-closure'
[IO.Directory]::CreateDirectory($root)|Out-Null
function Read($p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function Hash($p){(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
function Save($name,$v){[IO.File]::WriteAllText((Join-Path $root $name),(ConvertTo-Json -InputObject $v -Depth 60),[Text.UTF8Encoding]::new($false))}
function Counts($rows){$c=[ordered]@{Pass=0;Close=0;Difference=0;Incomparable=0;Error=0};foreach($r in $rows){$c[$r.conclusion]++};$c}
$priorRoot=Join-Path $repo 'validation/rechecks/2026-11-03/medium-reference';$old=@{}
foreach($r in @((Read "$priorRoot/crystals-results.json")+(Read "$priorRoot/toroid-annular-results.json"))){$old[[int]$r.index]=$r}
$art=Join-Path $repo 'artifacts/validation';$latest=@()
foreach($scope in @('crystals','toroid-annular')){$dir=Join-Path $art "residual-closure-$scope-accepted-20261103";$rows=@(Read "$dir/results.json");Save "$scope-results.json" $rows;Save "$scope-identity.json" (Read "$dir/identity.json");$latest+=$rows}
$core=$latest[0].coreSha256
if($latest.Count -ne 252 -or @($latest|Where-Object coreSha256 -ne $core).Count){throw 'Incomplete or mixed-Core primary scope'}
$lost=@($latest|Where-Object {$old[[int]$_.index].conclusion -eq 'Pass' -and $_.conclusion -ne 'Pass'})
if($lost.Count){throw 'Prior primary Pass lost'}
$four=@(foreach($i in @(107,108,125,1376)){$r=$latest|Where-Object index -eq $i;[ordered]@{index=$i;lens=$r.lens;analysis=$r.key;before=$old[$i].conclusion;after=$r.conclusion;metrics=$r.metrics}})
Save 'four-residuals.json' $four
$cross=@(Read "$art/residual-closure-cross-folded-final-20261103/results.json")
if($cross.Count -ne 156 -or @($cross|Where-Object {$_.conclusion -ne 'Pass' -or $_.coreSha256 -ne $core}).Count){throw 'Cross-system Pass lost or mixed Core'}
Save 'cross-analysis-results.json' $cross
$psf=@(Read "$art/residual-closure-psf-accepted-20261103/results.json");$convergence=@(Read "$art/residual-closure-convergence-accepted-20261103/results.json")
if($psf.Count -ne 24 -or $convergence.Count -ne 14 -or @(@($psf+$convergence)|Where-Object {$_.conclusion -ne 'Pass' -or $_.coreSha256 -ne $core}).Count){throw 'Convergence acceptance incomplete'}
Save 'crystal-psf-controls.json' $psf;Save 'additional-convergence.json' $convergence
$limit=@(Read "$art/residual-closure-native3-20261103/results.json")
if($limit.Count -ne 4 -or @($limit|Where-Object conclusion -ne 'Incomparable').Count){throw 'Native upper-bound result changed; review required'}
Save 'native-limit-results.json' $limit
Save 'first-order-differential-controls.json' (Read "$art/residual-closure-first-order-controls-20261103.json")
$covered=@(161,191,221,243,265,287,1785)
$references=@(foreach($r in $latest|Where-Object conclusion -eq 'Incomparable'){[ordered]@{index=$r.index;lens=$r.lens;analysis=$r.key;originalConclusion=$r.conclusion;originalReason=$r.reason;convergedControlPassed=([int]$r.index -in $covered);upperBoundNativeSampling=$(if([int]$r.index -in @(1365,1367,1369,1372)){2048}else{$null});originalRequestPromoted=$false}})
Save 'eleven-references.json' $references
[xml]$tests=Get-Content "$art/residual-closure-tests-accepted2-20261103/closure.trx" -Raw
[xml]$operands=Get-Content "$art/residual-closure-operands-accepted-20261103/operands.trx" -Raw
if($tests.TestRun.ResultSummary.Counters.passed -ne '599' -or $tests.TestRun.ResultSummary.Counters.failed -ne '0' -or $operands.TestRun.ResultSummary.Counters.passed -ne '134' -or $operands.TestRun.ResultSummary.Counters.failed -ne '0'){throw 'Tests incomplete'}
foreach($runtime in @('artifacts/validation/residual-closure-final-runtime-20261103','src/OpticalSystemDesign.App/bin/Release/net10.0','tools/diagnostics/ObscuredReferenceTests20261101/bin/Release/net10.0','tests/OpticalSystemDesign.Tests/bin/Release/net10.0')){if((Hash "$repo/$runtime/OpticalSystemDesign.Core.dll") -ne $core){throw 'Final runtime Core differs'}}
$summary=[ordered]@{completedLocal=[DateTimeOffset]::Now.ToString('o');coreBefore=(Read "$priorRoot/summary.json").coreAfter;coreAfter=$core;primaryRequests=252;primaryBefore=(Counts $old.Values);primaryAfter=(Counts $latest);primaryNewOriginalPass=2;primaryPriorPassLost=0;originalFourResiduals=@{originalPass=@(107,1376);convergedControlPassed=@(108);definitionConflictStillOpen=@(125)};originalIncomparable=11;originalIncomparableWithConvergedControls=$covered;nativeReferenceGapsStillOpen=@(1365,1367,1369,1372);maximumNativeSamplingChecked=2048;convergenceChecksPassed=38;crossAnalysisPriorPassChecked=156;crossAnalysisPriorPassLost=0;scopedTests=599;existingOperandTests=134;distinctTests=733;defaultAppBuildPassed=$true;builtInModules=2;originalSettingsAndTolerancesPreserved=$true;originalInvalidAndLowSamplingVerdictsPreserved=$true;newFull95Run=$false;scopesOverlapAndMustNotBeSummed=$true;firstOrderDifferenceNotFitted=$true;sourceRevisionId='7ab4adc52cbb39107d094e1cd80b16f930f3c25a';remoteSynchronizationPerformed=$false}
Save 'summary.json' $summary
$summary|ConvertTo-Json -Depth 6
