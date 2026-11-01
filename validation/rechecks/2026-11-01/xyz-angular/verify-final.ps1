$ErrorActionPreference='Stop'
$bsdfRepo=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
function Read-Json([string] $relative) {Get-Content -LiteralPath (Join-Path $PSScriptRoot $relative) -Raw | ConvertFrom-Json}
$bsdfBefore=Read-Json 'before-isolated-final-run.json'
$bsdfAfter=Read-Json 'after-isolated-final-run.json'
$bsdfDrift=@()
foreach($bsdfGroup in @('Binaries','ScopedSources')) {
    foreach($bsdfEntry in $bsdfBefore.$bsdfGroup) {
        $bsdfMatch=@($bsdfAfter.$bsdfGroup | Where-Object Path -eq $bsdfEntry.Path)
        if($bsdfMatch.Count -ne 1 -or $bsdfMatch[0].Sha256 -ne $bsdfEntry.Sha256){$bsdfDrift+=$bsdfEntry.Path}
    }
}
$bsdfSharedBuildPaths=@()
$bsdfExecutionDrift=@($bsdfDrift | Where-Object {$_ -notin $bsdfSharedBuildPaths})
if($bsdfExecutionDrift.Count){throw 'Execution copies or scoped sources changed during regression.'}
# Final execution uses private copies. The earlier shared-output run and its
# concurrent build drift are retained separately and never substituted here.
$bsdfTestApp='tools/diagnostics/XyzAngular20261101/bin/FrozenAngular20261101/tests/OpticalSystemDesign.App.dll'
$bsdfExecutedAppSha=($bsdfBefore.Binaries | Where-Object Path -eq $bsdfTestApp).Sha256
if($bsdfExecutedAppSha -ne ($bsdfAfter.Binaries | Where-Object Path -eq $bsdfTestApp).Sha256){throw 'Executed test App changed.'}
$bsdfLater=@()
foreach($bsdfEntry in @($bsdfAfter.Binaries)+@($bsdfAfter.ScopedSources)) {
    $bsdfActual=(Get-FileHash -LiteralPath (Join-Path $bsdfRepo $bsdfEntry.Path)).Hash.ToLowerInvariant()
    if($bsdfActual -ne $bsdfEntry.Sha256){$bsdfLater += [pscustomobject]@{Path=$bsdfEntry.Path;TestedSha256=$bsdfEntry.Sha256;CurrentSha256=$bsdfActual}}
}
if(@($bsdfLater | Where-Object {$_.Path -notin $bsdfSharedBuildPaths}).Count){throw 'Tested execution copies or scoped sources changed.'}
[xml]$bsdfTrx=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'isolated-final-focused.trx') -Raw
$bsdfCounterNode=$bsdfTrx.SelectSingleNode("//*[local-name()='Counters']")
$bsdfCounters=[ordered]@{}
foreach($bsdfName in @('total','executed','passed','failed','notExecuted')){$bsdfCounters[$bsdfName]=[int]$bsdfCounterNode.GetAttribute($bsdfName)}
if($bsdfCounters.passed -ne 307 -or $bsdfCounters.failed -ne 0 -or $bsdfCounters.notExecuted -ne 0){throw 'Unexpected focused test verdict.'}
$bsdfCore=($bsdfAfter.Binaries | Where-Object Path -eq 'tools/diagnostics/XyzAngular20261101/bin/FrozenAngular20261101/tests/OpticalSystemDesign.Core.dll').Sha256
$bsdfApplication=($bsdfAfter.Binaries | Where-Object Path -eq 'tools/diagnostics/XyzAngular20261101/bin/FrozenAngular20261101/tests/OpticalSystemDesign.Application.dll').Sha256
if(@($bsdfAfter.Binaries | Where-Object {$_.Path -like '*OpticalSystemDesign.Core.dll' -and $_.Sha256 -ne $bsdfCore}).Count){throw 'Core copies differ.'}
if(@($bsdfAfter.Binaries | Where-Object {$_.Path -like '*OpticalSystemDesign.Application.dll' -and $_.Sha256 -ne $bsdfApplication}).Count){throw 'Application copies differ.'}
$bsdfChains=Read-Json 'final-measured-chain/summary.json'
$bsdfWhite=Read-Json 'final-white/runtime6-native-power-audit.json'
$bsdfFits=Read-Json 'final-calibration/summary.json'
if($bsdfChains.Cases.Count -ne 13 -or @($bsdfChains.Cases | Where-Object {-not $_.ExactReplay -or $_.BsdfStatistics.SampleCount -ne 8192}).Count -or $bsdfWhite.Passed -ne 23 -or $bsdfWhite.Unsupported -ne 0 -or @($bsdfFits.Cases | Where-Object Exported).Count -ne 2){throw 'Unexpected measured chain / calibration / White result.'}
if($bsdfChains.CoreSha256 -ne $bsdfCore -or $bsdfChains.ApplicationSha256 -ne $bsdfApplication -or $bsdfWhite.CoreAssemblySha256 -ne $bsdfCore -or $bsdfFits.Execution.CoreSha256 -ne $bsdfCore){throw 'Diagnostics used different computation assemblies.'}
$bsdfHistograms=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'final-measured-chain') -Filter 'angular-histogram.json' -File -Recurse)
if($bsdfHistograms.Count -ne 13){throw 'Missing angular histogram controls.'}
foreach($bsdfHistogramFile in $bsdfHistograms){$bsdfHistogram=Get-Content -LiteralPath $bsdfHistogramFile.FullName -Raw | ConvertFrom-Json;for($bsdfI=0;$bsdfI -lt 32;$bsdfI++){if($bsdfHistogram.Errors[$bsdfI] -gt $bsdfHistogram.SixSigmaPlusOneCountAllowance[$bsdfI]){throw 'Angular histogram gate failed.'}}}
$bsdfPriorResults=@()
foreach($bsdfPrior in @('validation/rechecks/2026-10-31/commercial-bsdf-calibration','validation/rechecks/2026-10-31/white-specular-runtime','validation/rechecks/2026-11-01/xyz-radiometry','validation/rechecks/2026-11-01/xyz-conditions')) {
    $bsdfPriorRoot=Join-Path $bsdfRepo $bsdfPrior
    $bsdfManifest=Get-Content -LiteralPath (Join-Path $bsdfPriorRoot 'evidence-manifest.json') -Raw | ConvertFrom-Json
    foreach($bsdfEntry in $bsdfManifest.Files){if((Get-FileHash -LiteralPath (Join-Path $bsdfPriorRoot $bsdfEntry.Path)).Hash.ToLowerInvariant() -ne $bsdfEntry.Sha256){throw "Frozen evidence changed: $bsdfPrior/$($bsdfEntry.Path)"}}
    $bsdfPriorResults += [pscustomobject]@{Root=$bsdfPrior;VerifiedFiles=$bsdfManifest.Files.Count}
}
if(-not(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'evidence-manifest.json'))) {
    [ordered]@{CapturedUtc=[DateTimeOffset]::UtcNow.ToString('o');BinaryCount=$bsdfAfter.Binaries.Count;ScopedSourceCount=$bsdfAfter.ScopedSources.Count;ExecutionAndSourceDrift=$bsdfExecutionDrift;SharedBuildOutputDrift=$bsdfDrift;LaterBuildCopyChanges=$bsdfLater;ExecutedTestAppSha256=$bsdfExecutedAppSha;PrivateExecutionCopiesFrozen=$true;PriorFrozenEvidence=$bsdfPriorResults} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'final-run-integrity.json') -Encoding utf8
    [ordered]@{
        CapturedUtc=[DateTimeOffset]::UtcNow.ToString('o');Counters=$bsdfCounters;CoreSha256=$bsdfCore;ApplicationSha256=$bsdfApplication
        PreviouslyApprovedKnownConditionDerivedCalibrationsStillApproved=2;NewlyApprovedRemainingThreeMaterials=0;RemainingMaterialsStillRejected=3
        FullPrecisionMatAlsoFailed=3;ExploratoryModelsAdoptedInProduct=$false;OriginalPublishedObserverVerified=$false;FullCommercialCertification=$false
        MeasuredAngularChainsPassed=13;CornellMeasuredOutgoingCutsPassed=10;ViaviMeasuredBtdfNormalIncidenceCasesPassed=3
        CornellAllEightIncidenceImportsRemainRejectedForNonpassiveInterpolants=$true;BackSideDeclaredIdenticalNotMeasured=$true
        HistogramsPassed=13;HistogramBins=416;IndependentDirectionSamples=425984;NscDirectionSamples=106496
        MaximumEnergyError=($bsdfChains.Cases.EnergyError | Measure-Object -Maximum).Maximum;MaximumClosureError=($bsdfChains.Cases.ClosureError | Measure-Object -Maximum).Maximum
        MaximumPhotonErrorElectrons=($bsdfChains.Cases.PhotonErrorElectrons | Measure-Object -Maximum).Maximum
        ExactFrozenRawIspReplays=13;WhitePowerPassed=$bsdfWhite.Passed;MaximumWhiteScenePowerErrorWatts=$bsdfWhite.MaximumScenePowerErrorWatts
        ScopedAppAndTestBuildsPassed=$true;LatestFullDefaultReleaseBuildPassed=$true
        PrivateExecutionCopiesUnchangedDuringRun=$true;ExecutedTestAppSha256=$bsdfExecutedAppSha
        PriorFrozenFilesVerified=($bsdfPriorResults.VerifiedFiles | Measure-Object -Sum).Sum;BinaryCount=$bsdfAfter.Binaries.Count;ScopedSourceCount=$bsdfAfter.ScopedSources.Count
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'final-verification-summary.json') -Encoding utf8
}
Write-Output 'Verified 307 tests, 13 measured angular controls, 416 histogram bins, 23 White controls; remaining three XYZ calibrations still rejected.'
