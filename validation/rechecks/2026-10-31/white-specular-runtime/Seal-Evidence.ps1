$ErrorActionPreference = 'Stop'
$bsdfRepo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$bsdfManifestPath = Join-Path $PSScriptRoot 'evidence-manifest.json'
if (Test-Path -LiteralPath $bsdfManifestPath) { throw 'Evidence is already sealed. Use a new directory for another run.' }
$bsdfSummary = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'final-verification-summary.json') -Raw | ConvertFrom-Json
if ($bsdfSummary.Counters.Passed -ne 264 -or $bsdfSummary.Counters.Failed -ne 0 -or $bsdfSummary.NativePowerCasesPassed -ne 23 -or $bsdfSummary.NativePowerCasesUnsupported -ne 0) { throw 'Unexpected final verdict.' }
$bsdfSnapshot = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'after-runtime6-final2-run.json') -Raw | ConvertFrom-Json
foreach ($bsdfEntry in @($bsdfSnapshot.Binaries) + @($bsdfSnapshot.ScopedSources)) {
    if ((Get-FileHash -LiteralPath (Join-Path $bsdfRepo $bsdfEntry.Path)).Hash.ToLowerInvariant() -ne $bsdfEntry.Sha256) { throw "Changed after test: $($bsdfEntry.Path)" }
}
$bsdfPriorRoot = Join-Path $bsdfRepo 'validation/rechecks/2026-10-31/commercial-bsdf-calibration'
$bsdfPrior = Get-Content -LiteralPath (Join-Path $bsdfPriorRoot 'evidence-manifest.json') -Raw | ConvertFrom-Json
foreach ($bsdfEntry in $bsdfPrior.Files) {
    if ((Get-FileHash -LiteralPath (Join-Path $bsdfPriorRoot $bsdfEntry.Path)).Hash.ToLowerInvariant() -ne $bsdfEntry.Sha256) { throw "Changed prior evidence: $($bsdfEntry.Path)" }
}
$bsdfFiles = @(Get-ChildItem -LiteralPath $PSScriptRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{ Path = [IO.Path]::GetRelativePath($PSScriptRoot, $_.FullName).Replace('\','/'); Bytes = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() }
})
$bsdfDocs = @('docs/WHITE_SPECULAR_RUNTIME_2026-10-31.md','docs/CURRENT_STATUS.md','docs/STAROPT_FILE_FORMAT.md','docs/FILE_FORMATS_AND_PLUGINS.md','docs/CAMERA_IMAGING_LAB.md','docs/NON_SEQUENTIAL_TRACING.md','docs/NONSEQUENTIAL_PHASE2_STRAY_LIGHT.md','docs/NONSEQUENTIAL_ZEMAX_PARITY_ROADMAP.md','docs/NUMERICAL_PARITY.md','docs/README.md','docs/COMMERCIAL_BSDF_CALIBRATION_2026-10-31.md')
[ordered]@{
    CapturedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    Scope = 'White runtime power controls and focused product regressions. Real XYZ calibration and full commercial directional certification remain incomplete.'
    ProductTestsPassed = 264
    NativePowerCasesPassed = 23
    RealXyzCalibrationComplete = $false
    FullCommercialDirectionalCertification = $false
    PreviousFrozenEvidenceFilesVerified = $bsdfPrior.Files.Count
    Excludes = @('evidence-manifest.json')
    DocumentationAtSeal = @($bsdfDocs | ForEach-Object { [pscustomobject]@{ Path = $_; Sha256 = (Get-FileHash -LiteralPath (Join-Path $bsdfRepo $_)).Hash.ToLowerInvariant() } })
    Files = $bsdfFiles
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $bsdfManifestPath -Encoding utf8
Write-Output "Sealed $($bsdfFiles.Count) files; 264 focused tests, 23 native power controls, real XYZ incomplete."
