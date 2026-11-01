$ErrorActionPreference = 'Stop'
$bsdfRepo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$bsdfManifestPath = Join-Path $PSScriptRoot 'evidence-manifest.json'
if (Test-Path -LiteralPath $bsdfManifestPath) { throw 'Already sealed. Use a new output directory.' }
$bsdfSummary = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'final-verification-summary.json') -Raw | ConvertFrom-Json
$bsdfIntegrity = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'final-run-integrity.json') -Raw | ConvertFrom-Json
if ($bsdfSummary.Counters.Passed -ne 280 -or $bsdfSummary.Counters.Failed -ne 0 -or $bsdfSummary.WhitePowerPassed -ne 23 -or $bsdfSummary.RealMaterialCalibrationApproved -or $bsdfIntegrity.Drift.Count) { throw 'Unexpected final verdict.' }
$bsdfSnapshot = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'after-gamut-final-run.json') -Raw | ConvertFrom-Json
foreach ($bsdfEntry in $bsdfSnapshot.ScopedSources) {
    if ((Get-FileHash -LiteralPath (Join-Path $bsdfRepo $bsdfEntry.Path)).Hash.ToLowerInvariant() -ne $bsdfEntry.Sha256) { throw "Scoped source changed: $($bsdfEntry.Path)" }
}
foreach ($bsdfEntry in $bsdfSnapshot.Binaries) {
    # This shared library-build copy was updated after the finished test. The actual
    # default App, test and both diagnostic copies must retain the tested bytes.
    if ($bsdfEntry.Path -eq 'src/OpticalSystemDesign.Core/bin/Release/net10.0/OpticalSystemDesign.Core.dll') { continue }
    if ((Get-FileHash -LiteralPath (Join-Path $bsdfRepo $bsdfEntry.Path)).Hash.ToLowerInvariant() -ne $bsdfEntry.Sha256) { throw "Tested binary copy changed: $($bsdfEntry.Path)" }
}
$bsdfPreviousCount = 0
foreach ($bsdfPrior in @('validation/rechecks/2026-10-31/commercial-bsdf-calibration','validation/rechecks/2026-10-31/white-specular-runtime')) {
    $bsdfPriorRoot = Join-Path $bsdfRepo $bsdfPrior
    $bsdfPriorManifest = Get-Content -LiteralPath (Join-Path $bsdfPriorRoot 'evidence-manifest.json') -Raw | ConvertFrom-Json
    foreach ($bsdfEntry in $bsdfPriorManifest.Files) {
        if ((Get-FileHash -LiteralPath (Join-Path $bsdfPriorRoot $bsdfEntry.Path)).Hash.ToLowerInvariant() -ne $bsdfEntry.Sha256) { throw "Prior frozen evidence changed: $($bsdfEntry.Path)" }
    }
    $bsdfPreviousCount += $bsdfPriorManifest.Files.Count
}
$bsdfFiles = @(Get-ChildItem -LiteralPath $PSScriptRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{ Path = [IO.Path]::GetRelativePath($PSScriptRoot, $_.FullName).Replace('\','/'); Bytes = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() }
})
$bsdfDocs = @('docs/XYZ_GAMUT_CALIBRATION_2026-11-01.md','docs/CURRENT_STATUS.md','docs/STAROPT_FILE_FORMAT.md','docs/FILE_FORMATS_AND_PLUGINS.md','docs/CAMERA_IMAGING_LAB.md','docs/NON_SEQUENTIAL_TRACING.md','docs/NONSEQUENTIAL_PHASE2_STRAY_LIGHT.md','docs/NONSEQUENTIAL_ZEMAX_PARITY_ROADMAP.md','docs/NUMERICAL_PARITY.md','docs/README.md','docs/WHITE_SPECULAR_RUNTIME_2026-10-31.md')
[ordered]@{
    CapturedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    Scope = 'Gamut-constrained XYZ fit and nonnegative radiometric sampling; synthetic complete chain and old White power recheck. Real-material approved calibration remains incomplete.'
    ProductTestsPassed = 280
    NativeWhitePowerCasesPassed = 23
    RealMaterialCalibrationApproved = $false
    FullCommercialXyzCertification = $false
    PreviousFrozenFilesVerified = $bsdfPreviousCount
    SharedCoreBuildCopyUpdatedAfterTest = $true
    TestedCoreSha256 = $bsdfSummary.CoreSha256
    Excludes = @('evidence-manifest.json')
    DocumentationAtSeal = @($bsdfDocs | ForEach-Object { [pscustomobject]@{ Path = $_; Sha256 = (Get-FileHash -LiteralPath (Join-Path $bsdfRepo $_)).Hash.ToLowerInvariant() } })
    Files = $bsdfFiles
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $bsdfManifestPath -Encoding utf8
Write-Output "Sealed $($bsdfFiles.Count) files. 280 focused tests, 23 White controls; no approved real-material calibration."
