$ErrorActionPreference = 'Stop'
$bsdfRepo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
function Read-Json([string] $name) { Get-Content -LiteralPath (Join-Path $PSScriptRoot $name) -Raw | ConvertFrom-Json }
$bsdfBefore = Read-Json 'before-positive-final-run.json'
$bsdfAfter = Read-Json 'after-positive-final-run.json'
$bsdfDrift = @()
foreach ($bsdfGroup in @('Binaries','ScopedSources')) {
    foreach ($bsdfEntry in $bsdfBefore.$bsdfGroup) {
        $bsdfMatch = @($bsdfAfter.$bsdfGroup | Where-Object Path -eq $bsdfEntry.Path)
        if ($bsdfMatch.Count -ne 1 -or $bsdfMatch[0].Sha256 -ne $bsdfEntry.Sha256) { $bsdfDrift += $bsdfEntry.Path }
    }
}
if ($bsdfDrift.Count) { throw 'Inputs changed during final regression.' }
$bsdfLater = @()
foreach ($bsdfEntry in @($bsdfAfter.Binaries) + @($bsdfAfter.ScopedSources)) {
    $bsdfActual = (Get-FileHash -LiteralPath (Join-Path $bsdfRepo $bsdfEntry.Path)).Hash.ToLowerInvariant()
    if ($bsdfActual -ne $bsdfEntry.Sha256) { $bsdfLater += [pscustomobject]@{ Path=$bsdfEntry.Path; TestedSha256=$bsdfEntry.Sha256; CurrentSha256=$bsdfActual } }
}
if (@($bsdfLater | Where-Object Path -ne 'src/OpticalSystemDesign.Core/bin/Release/net10.0/OpticalSystemDesign.Core.dll').Count) { throw 'A tested execution copy or scoped source changed after final regression.' }
$bsdfCore = ($bsdfAfter.Binaries | Where-Object Path -eq 'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Core.dll').Sha256
$bsdfApplication = ($bsdfAfter.Binaries | Where-Object Path -eq 'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Application.dll').Sha256
if (@($bsdfAfter.Binaries | Where-Object { $_.Path -like '*OpticalSystemDesign.Core.dll' -and $_.Sha256 -ne $bsdfCore }).Count) { throw 'Final Core execution copies differ.' }
if (@($bsdfAfter.Binaries | Where-Object { $_.Path -like '*OpticalSystemDesign.Application.dll' -and $_.Sha256 -ne $bsdfApplication }).Count) { throw 'Final Application execution copies differ.' }
[xml]$bsdfTrx = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'positive-final-focused.trx') -Raw
$bsdfCounterNode = $bsdfTrx.SelectSingleNode("//*[local-name()='Counters']")
$bsdfCounters = [ordered]@{}
foreach ($bsdfName in @('total','executed','passed','failed','notExecuted')) { $bsdfCounters[$bsdfName] = [int]$bsdfCounterNode.GetAttribute($bsdfName) }
if ($bsdfCounters.passed -ne 295 -or $bsdfCounters.failed -ne 0 -or $bsdfCounters.notExecuted -ne 0) { throw 'Unexpected focused test outcome.' }
$bsdfFits = Read-Json 'final-positive/summary.json'
$bsdfChains = Read-Json 'final-real-chain-retry/summary.json'
$bsdfWhite = Read-Json 'final-white-power/runtime6-native-power-audit.json'
if (@($bsdfFits.Cases | Where-Object Exported).Count -ne 2 -or $bsdfChains.Cases.Count -ne 4 -or @($bsdfChains.Cases | Where-Object { -not $_.ExactReplay -or $_.ProjectVersion -ne 16 -or $_.BsdfStatistics.SampleCount -ne 8192 }).Count -or $bsdfWhite.Passed -ne 23 -or $bsdfWhite.Unsupported -ne 0) { throw 'Unexpected fit/chain/White outcome.' }
if ($bsdfChains.CoreSha256 -ne $bsdfCore -or $bsdfChains.ApplicationSha256 -ne $bsdfApplication -or $bsdfWhite.CoreAssemblySha256 -ne $bsdfCore) { throw 'Final diagnostic execution identities differ from tested App.' }
$bsdfPriorResults = @()
foreach ($bsdfPrior in @('validation/rechecks/2026-10-31/commercial-bsdf-calibration','validation/rechecks/2026-10-31/white-specular-runtime','validation/rechecks/2026-11-01/xyz-radiometry')) {
    $bsdfPriorRoot = Join-Path $bsdfRepo $bsdfPrior
    $bsdfManifest = Get-Content -LiteralPath (Join-Path $bsdfPriorRoot 'evidence-manifest.json') -Raw | ConvertFrom-Json
    foreach ($bsdfEntry in $bsdfManifest.Files) {
        if ((Get-FileHash -LiteralPath (Join-Path $bsdfPriorRoot $bsdfEntry.Path)).Hash.ToLowerInvariant() -ne $bsdfEntry.Sha256) { throw "Frozen evidence changed: $bsdfPrior/$($bsdfEntry.Path)" }
    }
    $bsdfPriorResults += [pscustomobject]@{ Root=$bsdfPrior; VerifiedFiles=$bsdfManifest.Files.Count }
}
if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'evidence-manifest.json'))) {
[ordered]@{ CapturedUtc=[DateTimeOffset]::UtcNow.ToString('o'); BinaryCount=$bsdfAfter.Binaries.Count; ScopedSourceCount=$bsdfAfter.ScopedSources.Count; Drift=$bsdfDrift; LaterBuildCopyChanges=$bsdfLater; PriorFrozenEvidence=$bsdfPriorResults } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'final-run-integrity.json') -Encoding utf8
[ordered]@{
    CapturedUtc=[DateTimeOffset]::UtcNow.ToString('o'); Counters=$bsdfCounters; CoreSha256=$bsdfCore; ApplicationSha256=$bsdfApplication
    KnownConditionDerivedXyzCalibrationsApproved=2; KnownConditionMaterialsRejected=3; OriginalPublishedObserverVerified=$false; FullCommercialXyzCertification=$false
    Conditions=$bsdfFits.Conditions; RealCalibrationSyntheticAngularChainsPassed=4; MaximumEnergyError=($bsdfChains.Cases.EnergyError | Measure-Object -Maximum).Maximum
    MaximumClosureError=($bsdfChains.Cases.ClosureError | Measure-Object -Maximum).Maximum; MaximumPhotonErrorElectrons=($bsdfChains.Cases.PhotonErrorElectrons | Measure-Object -Maximum).Maximum
    ExactFrozenRawIspReplayCases=4; WhitePowerPassed=$bsdfWhite.Passed; MaximumWhiteScenePowerErrorWatts=$bsdfWhite.MaximumScenePowerErrorWatts
    PriorFrozenFilesVerified=($bsdfPriorResults.VerifiedFiles | Measure-Object -Sum).Sum; BinaryCount=$bsdfAfter.Binaries.Count; ScopedSourceCount=$bsdfAfter.ScopedSources.Count
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'final-verification-summary.json') -Encoding utf8
}
Write-Output "Verified 295 tests, 2 approved known-condition derived calibrations, 4 synthetic angular chains, 23 White controls and all prior frozen files."
