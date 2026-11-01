$ErrorActionPreference = 'Stop'
$bsdfRepo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$bsdfManifestPath = Join-Path $PSScriptRoot 'evidence-manifest.json'
if (Test-Path -LiteralPath $bsdfManifestPath) { throw 'Already sealed. Use a new output directory.' }
& (Join-Path $PSScriptRoot 'verify-final.ps1')
$bsdfSummary = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'final-verification-summary.json') -Raw | ConvertFrom-Json
$bsdfIntegrity = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'final-run-integrity.json') -Raw | ConvertFrom-Json
if ($bsdfSummary.Counters.passed -ne 295 -or $bsdfSummary.KnownConditionDerivedXyzCalibrationsApproved -ne 2 -or $bsdfSummary.RealCalibrationSyntheticAngularChainsPassed -ne 4 -or $bsdfSummary.WhitePowerPassed -ne 23 -or $bsdfSummary.PriorFrozenFilesVerified -ne 993 -or $bsdfIntegrity.Drift.Count) { throw 'Unexpected final verdict.' }
$bsdfDocs = @('docs/XYZ_KNOWN_CONDITIONS_2026-11-01.md','docs/CURRENT_STATUS.md','docs/STAROPT_FILE_FORMAT.md','docs/FILE_FORMATS_AND_PLUGINS.md','docs/CAMERA_IMAGING_LAB.md','docs/NON_SEQUENTIAL_TRACING.md','docs/NONSEQUENTIAL_PHASE2_STRAY_LIGHT.md','docs/NONSEQUENTIAL_ZEMAX_PARITY_ROADMAP.md','docs/NUMERICAL_PARITY.md','docs/README.md','docs/XYZ_GAMUT_CALIBRATION_2026-11-01.md')
$bsdfFiles = @(Get-ChildItem -LiteralPath $PSScriptRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{ Path=[IO.Path]::GetRelativePath($PSScriptRoot,$_.FullName).Replace('\','/'); Bytes=$_.Length; Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() }
})
[ordered]@{
    CapturedUtc=[DateTimeOffset]::UtcNow.ToString('o')
    Scope='Known-condition spectral-derived XYZ; positive empirical colour model; two approved 550 nm calibrations; four real-calibration synthetic angular NSC/RAW/ISP chains; retained failed materials and unknown original published observer.'
    ProductTestsPassed=295; ApprovedKnownConditionDerivedXyzCalibrations=2; RejectedKnownConditionMaterials=3
    RealCalibrationSyntheticAngularChainsPassed=4; NativeWhiteCapturedPowerControlsPassed=23
    OriginalPublishedObserverVerified=$false; FullCommercialXyzCertification=$false; PreviousFrozenFilesVerified=993
    TestedCoreSha256=$bsdfSummary.CoreSha256; TestedApplicationSha256=$bsdfSummary.ApplicationSha256
    Excludes=@('evidence-manifest.json')
    DocumentationAtSeal=@($bsdfDocs | ForEach-Object { [pscustomobject]@{ Path=$_; Sha256=(Get-FileHash -LiteralPath (Join-Path $bsdfRepo $_)).Hash.ToLowerInvariant() } })
    Files=$bsdfFiles
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $bsdfManifestPath -Encoding utf8
Write-Output "Sealed $($bsdfFiles.Count) files; 295 tests, two known-condition derived calibrations, four synthetic angular chains, 23 captured White controls."
