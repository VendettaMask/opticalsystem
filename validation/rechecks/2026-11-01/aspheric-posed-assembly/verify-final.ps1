$ErrorActionPreference='Stop'
$posedRepo=(Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
Set-Location -LiteralPath $posedRepo
function Read-Json([string]$Name){Get-Content -LiteralPath (Join-Path $PSScriptRoot $Name) -Raw | ConvertFrom-Json}
[xml]$posedTrx=Get-Content (Join-Path $PSScriptRoot 'final-focused.trx')
$posedCounters=$posedTrx.TestRun.ResultSummary.Counters
if([int]$posedCounters.total -ne 521 -or [int]$posedCounters.passed -ne 521 -or [int]$posedCounters.failed -ne 0){throw 'Final focused scope failed or is incomplete.'}
$posedNew=@($posedTrx.TestRun.TestDefinitions.UnitTest | Where-Object {$_.TestMethod.className -like '*PosedOpticalAssembly*'})
$posedWindow=@($posedTrx.TestRun.Results.UnitTestResult | Where-Object {$_.testName -match 'OpticalAssemblyWindowTests.*posed: True'})
if($posedNew.Count -ne 32 -or $posedWindow.Count -ne 1){throw 'The 33 added cases are incomplete.'}
$posedBefore=Read-Json 'before-final-run.json';$posedAfter=Read-Json 'after-final-run.json'
$posedDrift=@(Compare-Object @($posedBefore.Binaries | ForEach-Object {"$($_.Path):$($_.Sha256)"}) @($posedAfter.Binaries | ForEach-Object {"$($_.Path):$($_.Sha256)"}))
$posedDrift+=@(Compare-Object @($posedBefore.SourcesAndInputs | ForEach-Object {"$($_.Path):$($_.Sha256)"}) @($posedAfter.SourcesAndInputs | ForEach-Object {"$($_.Path):$($_.Sha256)"}))
if($posedDrift.Count){throw 'Execution files or scoped inputs changed during verification.'}
$posedMap=Read-Json 'source-snapshot-map.json'
$posedPostChanges=if(Test-Path (Join-Path $PSScriptRoot 'post-verification-worktree-changes.json')){(Read-Json 'post-verification-worktree-changes.json').Changes}else{@()}
foreach($posedEntry in $posedMap){
 if((Get-FileHash -LiteralPath $posedEntry.SnapshotPath).Hash.ToLowerInvariant() -ne $posedEntry.Sha256){throw "Tested source reference mismatch: $($posedEntry.OriginalPath)"}
 $posedCurrent=(Get-FileHash -LiteralPath $posedEntry.OriginalPath).Hash.ToLowerInvariant()
 if($posedCurrent -ne $posedEntry.Sha256){$posedRecorded=@($posedPostChanges | Where-Object Path -eq $posedEntry.OriginalPath);if($posedRecorded.Count -ne 1 -or $posedRecorded[0].TestedSha256 -ne $posedEntry.Sha256 -or $posedRecorded[0].CurrentSha256 -ne $posedCurrent -or $posedRecorded[0].IncludedInThisRun){throw "Unrecorded post-verification source change: $($posedEntry.OriginalPath)"}}
}
$posedPdb=Read-Json 'compiled-source-audit.json'
if($posedPdb.MatchedDocuments -ne 105 -or $posedPdb.Failures.Count){throw 'Compiled-source audit failed.'}
$posedExecution=(Read-Json 'execution-location.json').ExecutionDirectory
$posedFixtureCopies=0
foreach($posedEntry in $posedMap | Where-Object OriginalPath -like 'tests/OpticalSystemDesign.Tests/Fixtures/*'){
 $posedRelative=$posedEntry.OriginalPath.Substring('tests/OpticalSystemDesign.Tests/'.Length);$posedCopy=Join-Path $posedExecution $posedRelative
 if(Test-Path -LiteralPath $posedCopy){if((Get-FileHash -LiteralPath $posedCopy).Hash.ToLowerInvariant() -ne $posedEntry.Sha256){throw "Executed fixture differs: $posedRelative"};$posedFixtureCopies++}
}
$posedOldRoot=Join-Path $posedRepo 'validation/rechecks/2026-11-01/cemented-immersion'
$posedOldManifest=Get-Content (Join-Path $posedOldRoot 'evidence-manifest.json') -Raw | ConvertFrom-Json
$posedRetained=@()
foreach($posedName in @('air.staropt','air.starrdb','immersion.staropt','immersion.starrdb')){
 $posedPath="final-controls/$posedName";$posedOldEntry=@($posedOldManifest.Files | Where-Object Path -eq $posedPath)[0]
 if(-not $posedOldEntry -or (Get-FileHash -LiteralPath (Join-Path $posedOldRoot $posedPath)).Hash.ToLowerInvariant() -ne $posedOldEntry.Sha256){throw 'Retained v20 regression identity changed.'}
 $posedRetained+=[pscustomobject]@{Path="validation/rechecks/2026-11-01/cemented-immersion/$posedPath";Sha256=$posedOldEntry.Sha256}
}
$posedChains=@(Get-ChildItem (Join-Path $PSScriptRoot 'final-controls') -Filter 'chain-*.json' | ForEach-Object {Get-Content $_.FullName -Raw | ConvertFrom-Json})
if($posedChains.Count -ne 4){throw 'Missing complete chain cases.'}
foreach($posedCase in $posedChains){if($posedCase.ProjectVersion -ne 21 -or $posedCase.Bands -ne 3 -or -not $posedCase.ExactNscRetrace -or -not $posedCase.ExactFrozenReplay -or $posedCase.EnergyRelativeError -gt 1e-11 -or $posedCase.PhotonErrorElectrons -gt 1e-11 -or $posedCase.PartitionErrorWatts -gt 1e-25){throw 'Complete chain or energy/photon gate failed.'}}
$posedPoints=@(Get-ChildItem (Join-Path $PSScriptRoot 'final-controls') -Filter 'path-*.json' | ForEach-Object {(Get-Content $_.FullName -Raw | ConvertFrom-Json).Errors})
if($posedPoints.Count -ne 32){throw 'Eight four-surface path comparisons are incomplete.'}
$posedStatistics=Read-Json 'final-controls/tilted-plane-statistics.json'
$posedSigmaError=[Math]::Abs($posedStatistics.StochasticPower-$posedStatistics.ExpectedFiniteAperturePower)/$posedStatistics.Sigma
if($posedSigmaError -gt 6 -or -not $posedStatistics.ExactSeedReplay -or [Math]::Abs($posedStatistics.FullTracePower-$posedStatistics.ExpectedFiniteAperturePower) -gt 1e-12){throw 'Independent tilted-plate power or seeded statistics failed.'}
$posedBuild=Get-Content (Join-Path $PSScriptRoot 'build-app-image-plane.log') -Raw
if($posedBuild -notmatch '已成功生成' -or $posedBuild -notmatch '0 个警告' -or $posedBuild -notmatch 'OpticalSystemDesign.CoatingDesign.Engine ->' -or $posedBuild -notmatch 'OpticalSystemDesign.InitialStructure.Engine ->'){throw 'Final default App build is not verified.'}
if(-not(Test-Path (Join-Path $PSScriptRoot 'evidence-manifest.json'))){
 [ordered]@{
  CapturedUtc=[DateTimeOffset]::UtcNow.ToString('o');Counters=[ordered]@{Total=521;Passed=521;Failed=0;NotExecuted=0};AddedCases=33
  DefaultReleaseAppBuildPassed=$true;BothBuiltInModulesIncluded=$true;RootTestDriverBuildPassed=$true;CompiledSourceChecksumsMatched=105
  MappedSourceAndInputReceipts=$posedMap.Count;ChangedSourceFiles=@($posedMap | Where-Object StoredAsChangedSource).Count;ExecutedFixtureCopiesVerified=$posedFixtureCopies
  ExecutionFiles=$posedAfter.Binaries.Count;ExecutionAndInputDrift=0;CoreSha256=($posedAfter.Binaries | Where-Object Path -eq 'OpticalSystemDesign.Core.dll').Sha256
  ApplicationSha256=($posedAfter.Binaries | Where-Object Path -eq 'OpticalSystemDesign.Application.dll').Sha256
  RetainedV20Inputs=$posedRetained;OldBsdfProtocolsUnchanged=$true;PathCases=8;SurfaceComparisons=32;ThreeBandCameraChains=4
  ExactNscRetraces=4;ExactFrozenCameraReplays=4;ScatteringChains=2
  MaximumPositionErrorMillimeters=($posedPoints.PositionErrorMillimeters | Measure-Object -Maximum).Maximum
  MaximumDirectionError=($posedPoints.DirectionError | Measure-Object -Maximum).Maximum
  MaximumOpticalPathErrorMillimeters=($posedPoints.OplErrorMillimeters | Measure-Object -Maximum).Maximum
  MaximumCameraEnergyRelativeError=($posedChains.EnergyRelativeError | Measure-Object -Maximum).Maximum
  MaximumPhotonErrorElectrons=($posedChains.PhotonErrorElectrons | Measure-Object -Maximum).Maximum
  MaximumPartitionErrorWatts=($posedChains.PartitionErrorWatts | Measure-Object -Maximum).Maximum
  IndependentFiniteAperturePlatePower=$posedStatistics.ExpectedFiniteAperturePower;StochasticRays=$posedStatistics.Rays;SamplingErrorStandardDeviations=$posedSigmaError
  AnalyticalControlsNotMeasured=$true;NativeDesktopAcceptance=$false;CommercialCertification=$false;GeneralFreeformCadOrGpuImplemented=$false
  PostVerificationWorktreeChangesNotCovered=$posedPostChanges
 } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $PSScriptRoot 'final-verification-summary.json') -Encoding utf8
}
Write-Output 'Verified 521 focused tests, 33 added cases, 105 compiled-source checks, four exact three-band camera chains and retained v20 scene hashes.'
