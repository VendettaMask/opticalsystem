$ErrorActionPreference='Stop'
$bsdfRepo=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
function Read-Json([string] $relative){Get-Content -LiteralPath (Join-Path $PSScriptRoot $relative) -Raw | ConvertFrom-Json}
$bsdfBefore=Read-Json 'before-isolated-final-run.json';$bsdfAfter=Read-Json 'after-isolated-final-run.json';$bsdfDrift=@()
foreach($bsdfGroup in @('Binaries','ScopedSources')){foreach($bsdfEntry in $bsdfBefore.$bsdfGroup){$bsdfMatch=@($bsdfAfter.$bsdfGroup | Where-Object Path -eq $bsdfEntry.Path);if($bsdfMatch.Count -ne 1 -or $bsdfMatch[0].Sha256 -ne $bsdfEntry.Sha256 -or (Get-FileHash -LiteralPath (Join-Path $bsdfRepo $bsdfEntry.Path)).Hash.ToLowerInvariant() -ne $bsdfEntry.Sha256){$bsdfDrift+=$bsdfEntry.Path}}}
if($bsdfDrift.Count){throw 'Executed binaries or scoped source snapshots changed.'}
$bsdfCore=($bsdfAfter.Binaries | Where-Object {$_.Path.EndsWith('/tests/OpticalSystemDesign.Core.dll')}).Sha256
$bsdfApplication=($bsdfAfter.Binaries | Where-Object {$_.Path.EndsWith('/tests/OpticalSystemDesign.Application.dll')}).Sha256
foreach($bsdfEntry in $bsdfAfter.Binaries){if($bsdfEntry.Path.EndsWith('OpticalSystemDesign.Core.dll') -and $bsdfEntry.Sha256 -ne $bsdfCore){throw 'Different Core copies'};if($bsdfEntry.Path.EndsWith('OpticalSystemDesign.Application.dll') -and $bsdfEntry.Sha256 -ne $bsdfApplication){throw 'Different Application copies'}}
[xml]$bsdfTrx=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'isolated-final-focused.trx') -Raw
$bsdfNode=$bsdfTrx.SelectSingleNode("//*[local-name()='Counters']");$bsdfCounters=[ordered]@{}
foreach($bsdfName in @('total','executed','passed','failed','notExecuted')){$bsdfCounters[$bsdfName]=[int]$bsdfNode.GetAttribute($bsdfName)}
if($bsdfCounters.passed -ne 332 -or $bsdfCounters.failed -ne 0 -or $bsdfCounters.notExecuted -ne 0){throw 'Unexpected focused regression verdict.'}
$bsdfFits=Read-Json 'final-rbf-fit/summary.json';$bsdfChains=Read-Json 'final-angular-chain/summary.json';$bsdfCalibrated=Read-Json 'final-calibrated-chain/summary.json';$bsdfImports=Read-Json 'final-log-import/summary.json';$bsdfWhite=Read-Json 'final-white/runtime6-native-power-audit.json';$bsdfPdb=Read-Json 'compiled-source-audit.json'
if($bsdfFits.Cases.Count -ne 6 -or @($bsdfFits.Cases | Where-Object Exported).Count -ne 2 -or @($bsdfFits.Cases | Where-Object {$_.Exported -and $_.Material -eq 'acryl_blue'}).Count){throw 'Unexpected XYZ qualification verdict.'}
if($bsdfChains.Cases.Count -ne 43 -or $bsdfCalibrated.Cases.Count -ne 4 -or $bsdfImports.Cases.Count -ne 5 -or $bsdfWhite.Passed -ne 23 -or $bsdfWhite.Unsupported -ne 0){throw 'Unexpected independent control counts.'}
foreach($bsdfRun in @($bsdfFits,$bsdfChains,$bsdfCalibrated,$bsdfImports)){if($bsdfRun.CoreSha256 -ne $bsdfCore){throw 'Diagnostic used a different Core'}}
if($bsdfChains.ApplicationSha256 -ne $bsdfApplication -or $bsdfCalibrated.ApplicationSha256 -ne $bsdfApplication -or $bsdfWhite.CoreAssemblySha256 -ne $bsdfCore){throw 'Different execution identities.'}
if($bsdfPdb.Failures.Count -or $bsdfPdb.MatchedDocuments -ne 94){throw 'Compiled source checksums do not match the declared snapshot.'}
foreach($bsdfCase in @($bsdfChains.Cases)+@($bsdfCalibrated.Cases)){
    if(-not $bsdfCase.ExactReplay -or $bsdfCase.BsdfStatistics.SampleCount -ne 8192 -or $bsdfCase.EnergyError -gt 1e-11 -or $bsdfCase.ClosureError -gt 1e-12 -or $bsdfCase.PhotonErrorElectrons -gt 1e-11){throw 'Energy/photon/sampling/frozen replay check failed.'}
}
if(@($bsdfChains.Cases | Where-Object {-not $_.Transmission -and ($_.ProjectVersion -ne 18 -or $_.ComputationProtocol -ne 'MeasuredBsdfGrid/10')}).Count -or @($bsdfCalibrated.Cases | Where-Object ProjectVersion -ne 19).Count){throw 'Wrong project or computation version.'}
$bsdfHistograms=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'final-angular-chain') -Filter 'angular-histogram.json' -File -Recurse)
if($bsdfHistograms.Count -ne 43){throw 'Missing histograms.'}
foreach($bsdfFile in $bsdfHistograms){$bsdfHistogram=Get-Content -LiteralPath $bsdfFile.FullName -Raw | ConvertFrom-Json;for($bsdfI=0;$bsdfI -lt 32;$bsdfI++){if($bsdfHistogram.Errors[$bsdfI] -gt $bsdfHistogram.SixSigmaPlusOneCountAllowance[$bsdfI]){throw 'Histogram failed.'}}}
$bsdfIndependent=Read-Json 'diagnosis.json';$bsdfPowerError=0d
foreach($bsdfImport in $bsdfImports.Cases){
    $bsdfReference=@($bsdfIndependent | Where-Object Material -eq $bsdfImport.Material)[0]
    if($bsdfImport.Powers.Count -ne 8 -or $bsdfImport.MaximumScaledOriginalKnotError -gt 2e-11){throw 'Original knot coverage failed.'}
    for($bsdfI=0;$bsdfI -lt 8;$bsdfI++){$bsdfDelta=[Math]::Abs($bsdfImport.Powers[$bsdfI].Power-$bsdfReference.Powers[$bsdfI].Power);$bsdfPowerError=[Math]::Max($bsdfPowerError,$bsdfDelta);if($bsdfDelta -gt 1e-11 -or $bsdfImport.Powers[$bsdfI].Power -gt 1){throw 'All-incidence power check failed.'}}
}
$bsdfPrior=@()
foreach($bsdfRelative in @('validation/rechecks/2026-10-31/commercial-bsdf-calibration','validation/rechecks/2026-10-31/white-specular-runtime','validation/rechecks/2026-11-01/xyz-radiometry','validation/rechecks/2026-11-01/xyz-conditions','validation/rechecks/2026-11-01/xyz-angular')){
    $bsdfRoot=Join-Path $bsdfRepo $bsdfRelative;$bsdfManifest=Get-Content -LiteralPath (Join-Path $bsdfRoot 'evidence-manifest.json') -Raw | ConvertFrom-Json
    foreach($bsdfEntry in $bsdfManifest.Files){if((Get-FileHash -LiteralPath (Join-Path $bsdfRoot $bsdfEntry.Path)).Hash.ToLowerInvariant() -ne $bsdfEntry.Sha256){throw "Frozen evidence changed: $bsdfRelative/$($bsdfEntry.Path)"}}
    $bsdfPrior+=[pscustomobject]@{Root=$bsdfRelative;VerifiedFiles=$bsdfManifest.Files.Count}
}
if(($bsdfPrior.VerifiedFiles | Measure-Object -Sum).Sum -ne 1694){throw 'Prior manifest count changed.'}
if(-not(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'evidence-manifest.json'))){
    [ordered]@{CapturedUtc=[DateTimeOffset]::UtcNow.ToString('o');BinaryCount=$bsdfAfter.Binaries.Count;ScopedSourceCount=$bsdfAfter.ScopedSources.Count;ExecutionAndSourceDrift=$bsdfDrift;CompiledSourceChecksumsMatched=$bsdfPdb.MatchedDocuments;PriorFrozenEvidence=$bsdfPrior} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $PSScriptRoot 'final-run-integrity.json') -Encoding utf8
    [ordered]@{
        CapturedUtc=[DateTimeOffset]::UtcNow.ToString('o');Counters=$bsdfCounters;CoreSha256=$bsdfCore;ApplicationSha256=$bsdfApplication
        NewlyApprovedRemainingMaterialCalibrations=2;RemainingXyzMaterialsRejected=1;OriginalPublishedObserverVerified=$false;IndependentBlindValidationPerformed=$false;FullCommercialCertification=$false
        CornellAllEightIncidencesAt550NanometersPassive=40;CornellAllSpectralBandsQualified=$false;MeasuredAngularChainsPassed=43;CalibratedXyzSyntheticShapeChainsPassed=4
        HistogramBins=1376;IndependentDirectionSamples=1409024;NscMeasuredDirectionSamples=352256;NscCalibratedDirectionSamples=32768
        MaximumIndependentPowerError=$bsdfPowerError;MaximumOriginalKnotScaledError=($bsdfImports.Cases.MaximumScaledOriginalKnotError | Measure-Object -Maximum).Maximum
        MaximumEnergyError=($bsdfChains.Cases.EnergyError | Measure-Object -Maximum).Maximum;MaximumClosureError=($bsdfChains.Cases.ClosureError | Measure-Object -Maximum).Maximum;MaximumPhotonErrorElectrons=($bsdfChains.Cases.PhotonErrorElectrons | Measure-Object -Maximum).Maximum
        ExactMeasuredAngularFrozenReplays=43;ExactCalibratedXyzFrozenReplays=4;BackSideDeclaredIdenticalNotMeasured=$true;WhitePowerPassed=23;MaximumWhiteScenePowerErrorWatts=$bsdfWhite.MaximumScenePowerErrorWatts
        DefaultReleaseAppBuildPassed=$true;PriorFrozenFilesVerified=1694;ExecutionBinaryCount=$bsdfAfter.Binaries.Count;ScopedSourceCount=$bsdfAfter.ScopedSources.Count;CompiledSourceChecksumsMatched=94;ExecutionAndSourceDriftCount=0
    } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $PSScriptRoot 'final-verification-summary.json') -Encoding utf8
}
Write-Output 'Verified 332 focused tests, 43 measured angular chains, 4 calibrated XYZ shape controls, 23 White controls; Blue Acrylic remains rejected.'
