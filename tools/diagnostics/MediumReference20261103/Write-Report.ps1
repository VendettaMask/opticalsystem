param([string]$Repository='D:\Projects\opticalsystem')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($Repository);$root=Join-Path $repo 'validation/rechecks/2026-11-03/medium-reference'
function Read($p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function Hash($p){(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
function Save($name,$v){[IO.File]::WriteAllText((Join-Path $root $name),(ConvertTo-Json -InputObject $v -Depth 80),[Text.UTF8Encoding]::new($false))}
function Counts($rows){$c=[ordered]@{Pass=0;Close=0;Difference=0;Incomparable=0;Error=0};foreach($r in $rows){$c[$r.conclusion]++};$c}
$beforeFile=Join-Path $repo 'validation/rechecks/2026-11-03/all-example-accuracy/request-results.csv'
$baseline=Import-Csv $beforeFile;$before=@{};foreach($r in $baseline){$before[[int]$r.index]=$r}
$core=(Read (Join-Path $repo 'artifacts/validation/medium-reference-crystals-accepted-20261103/identity.json')).coreSha256
$scopes=[Collections.Generic.List[object]]::new();$union=@{}
foreach($scope in @('crystals','toroid-annular','categories','apertures')){
 $directory=Join-Path $repo "artifacts/validation/medium-reference-$scope-accepted-20261103"
 $id=Read "$directory/identity.json";$rows=@(Read "$directory/results.json")
 $expected=switch($scope){'crystals'{208};'toroid-annular'{44};'categories'{426};'apertures'{420}}
 if($rows.Count -ne $expected -or $id.coreSha256 -ne $core -or @($rows|Where-Object coreSha256 -ne $core).Count){throw 'Incomplete or mixed-Core scope'}
 $lost=@($rows|Where-Object {$before[[int]$_.index].verdict -eq 'Pass' -and $_.conclusion -ne 'Pass'})
 if($lost.Count){throw ('Prior Pass lost in '+$scope+': '+($lost.index -join ','))}
 $new=@($rows|Where-Object {$before[[int]$_.index].verdict -ne 'Pass' -and $_.conclusion -eq 'Pass'})
 $scopes.Add(@{scope=$scope;requests=$rows.Count;before=(Counts @($rows|ForEach-Object {@{conclusion=$before[[int]$_.index].verdict}}));after=(Counts $rows);newPass=@($new.index);priorPassLost=$lost})
 Save "$scope-results.json" $rows;Save "$scope-identity.json" $id
 foreach($r in $rows){$key=[int]$r.index;if($union.ContainsKey($key) -and $union[$key].conclusion -ne $r.conclusion){throw 'Overlapping same-Core results disagree'};$union[$key]=$r}
}
$crystalErrors=@($baseline|Where-Object cause -eq 'CrystalFrameOrInterfaceContext')
if($crystalErrors.Count -ne 142){throw 'Original 142 identities changed'}
$target=@(foreach($r in $crystalErrors){$a=$union[[int]$r.index];[pscustomobject]@{index=$a.index;lens=$a.lens;analysis=$a.key;before='Error';after=$a.conclusion;reason=$a.reason}})
if(@($target|Where-Object after -eq Error).Count -or @($target|Where-Object after -eq Pass).Count -ne 136 -or @($target|Where-Object after -eq Incomparable).Count -ne 6){throw 'Unexpected 142-case result'}
Save 'crystal-142.json' $target;$target|Export-Csv "$root/crystal-142.csv" -NoTypeInformation -Encoding utf8BOM
$nine=@(1371,1375,1378,1379,1782,1783,1792,1799,1800)
$nineRows=@(foreach($i in $nine){$r=$union[$i];if($r.conclusion -ne 'Pass'){throw 'Original regression not closed'};@{index=$i;lens=$r.lens;analysis=$r.key;before=$before[$i].verdict;after=$r.conclusion;metrics=$r.metrics}})
Save 'nine-restored.json' $nineRows
$psf=@(Read (Join-Path $repo 'artifacts/validation/medium-reference-psf-accepted-20261103/results.json'))
$rays=@(Read (Join-Path $repo 'artifacts/validation/medium-reference-rays-accepted-20261103/results.json'))
$native=@(Read (Join-Path $repo 'artifacts/validation/medium-reference-toroid-native-valid-20261103/results.json'))
if($psf.Count -ne 24 -or @($psf|Where-Object {$_.conclusion -ne 'Pass' -or $_.coreSha256 -ne $core}).Count){throw 'PSF controls incomplete'}
if($rays.Count -ne 2 -or @($rays|Where-Object {$_.conclusion -ne 'Pass' -or $_.coreSha256 -ne $core}).Count -or ($rays|Measure-Object pairs -Sum).Sum -ne 2232){throw 'Fixed rays incomplete'}
if($native.Count -ne 7 -or @($native|Where-Object conclusion -ne Pass).Count -or ($native|Measure-Object comparisons -Sum).Sum -ne 3591){throw 'Native toroid controls incomplete'}
Save 'psf-controls.json' $psf;Save 'fixed-ray-controls.json' $rays;Save 'native-toroid-capture-checks.json' $native
$primary=@($union.Values|Where-Object {$_.lens.StartsWith('Birefringent prisms/') -or $_.index -in @((Read "$root/toroid-annular-results.json").index)}|Sort-Object index)
if($primary.Count -ne 252){throw 'Primary scope incomplete'}
$changes=@(foreach($r in $primary){if($before[[int]$r.index].verdict -ne $r.conclusion){[pscustomobject]@{index=$r.index;lens=$r.lens;analysis=$r.key;before=$before[[int]$r.index].verdict;after=$r.conclusion;reason=$r.reason}}})
$changes|Export-Csv "$root/primary-changes.csv" -NoTypeInformation -Encoding utf8BOM
$fixtures=Join-Path $repo 'tests/OpticalSystemDesign.Tests/Fixtures/toroidal-native-v1.json'
if((Hash $fixtures) -ne (Hash (Join-Path $repo 'artifacts/validation/medium-reference-toroid-native-valid-20261103/toroidal-native-v1.json'))){throw 'Native fixture identity changed'}
$runtimeRoots=@('artifacts/validation/medium-reference-accepted-runtime-20261103','artifacts/validation/medium-reference-accepted-controls-runtime-20261103','tools/diagnostics/ObscuredReferenceTests20261101/bin/Release/net10.0','tests/OpticalSystemDesign.Tests/bin/Release/net10.0','src/OpticalSystemDesign.App/bin/Release/net10.0')
foreach($runtime in $runtimeRoots){if((Hash (Join-Path $repo "$runtime/OpticalSystemDesign.Core.dll")) -ne $core){throw 'Final runtime Core differs'}}
[xml]$trx=Get-Content (Join-Path $repo 'artifacts/validation/medium-reference-tests-accepted-20261103/medium-reference-accepted.trx') -Raw
[xml]$history=Get-Content (Join-Path $repo 'artifacts/validation/medium-reference-history-accepted-20261103/medium-reference-history-accepted.trx') -Raw
if($trx.TestRun.ResultSummary.Counters.passed -ne '571' -or $trx.TestRun.ResultSummary.Counters.failed -ne '0' -or $history.TestRun.ResultSummary.Counters.passed -ne '1'){throw 'Tests incomplete'}
$summary=@{coreBefore=(Read (Join-Path $repo 'validation/rechecks/2026-11-03/all-example-accuracy/summary.json')).coreSha256;coreAfter=$core;scopes=$scopes;primaryRequests=252;primaryBefore=(Counts @($primary|ForEach-Object {@{conclusion=$before[[int]$_.index].verdict}}));primaryAfter=(Counts $primary);primaryNewPass=@($changes|Where-Object after -eq Pass).Count;primaryPriorPassLost=0;targetCrystalErrors=142;crystalErrorsRemaining=0;crystalOriginalPass=136;crystalOriginalInvalidReference=6;originalPassRegressionsRestored=$nine;psfControlsPassed=24;fixedRayInputs=($rays|Measure-Object inputs -Sum).Sum;fixedRaySurfacePairs=2232;nativeToroidCases=7;nativeToroidValues=3591;nativeFixtureSha256=(Hash $fixtures);scopedTests=571;historicalFormatTests=1;distinctTests=572;defaultAppBuildPassed=$true;builtInModules=2;originalSettingsAndTolerances=$true;newFull95Run=$false;overlappingScopesNotSummed=$true;baselineCsvSha256=(Hash $beforeFile);completedLocal=[DateTimeOffset]::Now.ToString('o')}
$summary['nativeCaptureComparisonCore']=($native|Select-Object -First 1).coreSha256
$summary['nativeFixtureRevalidatedByFinalCoreCases']=21
Save 'summary.json' $summary
$summary|ConvertTo-Json -Depth 8
