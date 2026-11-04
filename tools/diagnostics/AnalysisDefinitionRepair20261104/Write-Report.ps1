param([string]$Repository='D:\Projects\opticalsystem')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($Repository);$art=Join-Path $repo 'artifacts/validation';$root=Join-Path $repo 'validation/rechecks/2026-11-04/analysis-surface-reference'
[IO.Directory]::CreateDirectory($root)|Out-Null
function Read($p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function Hash($p){(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
function Save($name,$v){[IO.File]::WriteAllText((Join-Path $root $name),(ConvertTo-Json -InputObject $v -Depth 80),[Text.UTF8Encoding]::new($false))}
$controls=Join-Path $art 'analysis-definition-controls-20261104';$id=Read "$controls/identity.json";$core=$id.coreSha256
$defaults=@(Read "$controls/default-controls.json");$surfaces=@(Read "$controls/surfaces.json");$modes=@(Read "$controls/reference-modes.json");$primary=@(Read "$controls/primary-reference.json")
if($defaults.Count -ne 4 -or @($defaults|Where-Object {!$_.priorNumericalOutputIdentical -or $_.coreSha256 -ne $core}).Count){throw 'Default numerical output changed'}
if($surfaces.Count -ne 16 -or @($surfaces|Where-Object {$_.surface -eq 999 -and $_.outcome -eq 'Rejected'}).Count -ne 4){throw 'Surface validation incomplete'}
foreach($mode in @('Absolute','Absolute2')){$r=$modes|Where-Object mode -eq $mode;if($r.outcome -ne 'Success' -or @($r.controls|Where-Object {$_.actual -ne $_.imageOplDefinition}).Count){throw 'Absolute definition mismatch'}}
if(($modes|Where-Object mode -eq Infinity).outcome -ne 'Unsupported'){throw 'Unsupported Infinity silently accepted'}
foreach($name in @('default-controls','surfaces','reference-modes','primary-reference','identity')){Save "$name.json" (Read "$controls/$name.json")}
$image=@(Read "$art/analysis-definition-image-pair-20261104/results.json");$previous=@{};foreach($r in (Import-Csv "$repo/validation/rechecks/2026-11-04/image-simulation-pickup/request-results.csv")){$previous[[int]$r.index]=$r}
if($image.Count -ne 60 -or @($image|Where-Object {$_.conclusion -ne $previous[[int]$_.index].after}).Count){throw 'Original image-pair verdict changed'}
$cross=@(Read "$art/analysis-definition-cross-20261104/results.json")
if($cross.Count -ne 156 -or @($cross|Where-Object {$_.conclusion -ne 'Pass' -or $_.coreSha256 -ne $core}).Count){throw 'Cross-system regression'}
if((Read "$art/analysis-definition-image-pair-20261104/identity.json").coreSha256 -ne $core){throw 'Image controls used another Core'}
Save 'image-pair-results.json' $image;Save 'image-pair-identity.json' (Read "$art/analysis-definition-image-pair-20261104/identity.json");Save 'cross-analysis-results.json' $cross
[xml]$trx=Get-Content "$art/analysis-definition-final-tests-20261104/definition-final.trx" -Raw
if($trx.TestRun.ResultSummary.Counters.passed -ne '679' -or $trx.TestRun.ResultSummary.Counters.failed -ne '0' -or $trx.TestRun.ResultSummary.Counters.notExecuted -ne '0'){throw 'Final tests incomplete'}
$new=@($trx.TestRun.Results.UnitTestResult|Where-Object testName -like '*AnalysisSurfaceAndReferenceTests.*').Count
if($new -ne 27){throw 'New-control identities changed'}
foreach($runtime in @('artifacts/validation/analysis-definition-runtime-20261104','tools/diagnostics/ObscuredReferenceTests20261101/bin/Release/net10.0','src/OpticalSystemDesign.App/bin/Release/net10.0')){if((Hash "$repo/$runtime/OpticalSystemDesign.Core.dll") -ne $core){throw 'Final runtime Core mismatch'}}
Save 'source-revision.json' @{sourceRevisionId='aae931370c552265be6c6a4ab5cf26c8bf3d7761';actualHead=(& git -C $repo rev-parse HEAD);dirty=$true;sourceBinding='Actual final Core/Application/diagnostic/test PDB document hashes verified separately';otherWorkspaceWorkPreserved=$true}
Save 'summary.json' ([ordered]@{completedLocal=[DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(8)).ToString('o');coreBefore='1af8b46ae62b581a85b05b3954f0d8dd1a1414e989d1c826feaed272836036a3';coreAfter=$core;surfaceRequests=16;invalid999RequestsRejected=4;aimedBeforeStopRequestsExplicitlyRejected=4;intermediateOpdAndWavefrontActuallyComputed=2;intermediateFftRequestsStillUnderSamplingProtection=2;defaultNumericalArraysIdentical=4;absoluteModesCorrected=@('Absolute','Absolute2');absolutePupil08BeforeWaves=3.685951913949262;absolutePupil08AfterWaves=-15.081888459581972;absoluteDefinitionMismatchWaves=0;focalInfinityRemainsUnsupported=$true;primaryReferenceMaps=6;referenceToPrimaryImplemented=$true;imagePairRequests=60;imagePairAfter=@{Pass=52;Close=4;Difference=2;Incomparable=2;Error=0};imagePairVerdictChanges=0;crossAnalysisPriorPassChecked=156;crossAnalysisPriorPassLost=0;distinctTests=679;newTests=27;failedTests=0;skippedTests=0;defaultDualModuleAppBuildPassed=$true;mainTestAssemblyAdditionalAttempt='Incomplete: another testhost locked its output; no other process stopped';originalToleranceChanges=0;newNativeCaptures=0;newFull95Run=$false;sixOldOpdResidualsPromoted=$false;nineteenSamplingCandidatesPromoted=$false;remoteSynchronizationPerformed=$false})
Get-Content "$root/summary.json"
