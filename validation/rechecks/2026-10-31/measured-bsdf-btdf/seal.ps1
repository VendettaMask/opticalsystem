$ErrorActionPreference = 'Stop'
$bsdfRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$bsdfBefore = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'before-final-run.json') -Raw | ConvertFrom-Json
$bsdfAfter = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'after-final-run.json') -Raw | ConvertFrom-Json
[xml]$bsdfTrx = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'final-focused.trx') -Raw
$bsdfCases = @($bsdfTrx.SelectNodes("//*[local-name()='UnitTestResult']"))
if (@($bsdfCases | Where-Object outcome -ne 'Passed').Count -gt 0) { throw 'Final focused test run is not fully passing.' }
$bsdfSourceDrift = @($bsdfBefore.ScopedSources | Where-Object {
    $entry = $_; ($bsdfAfter.ScopedSources | Where-Object Path -eq $entry.Path).Sha256 -ne $entry.Sha256
})
if ($bsdfSourceDrift.Count -gt 0) { throw 'Scoped sources changed during the final run.' }
$bsdfBinaryDrift = @($bsdfBefore.Binaries | Where-Object {
    $entry = $_; ($bsdfAfter.Binaries | Where-Object Path -eq $entry.Path).Sha256 -ne $entry.Sha256
})
$bsdfEnergies = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'energy-*.json' | ForEach-Object {
    $data = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
    [ordered]@{ R = $data.R; T = $data.T; RelativeEnergyClosureError = [Math]::Abs($data.e.AccountedPowerWatts / $data.e.SourcePowerWatts - 1)
        AbsorbedFraction = $data.e.AbsorbedPowerWatts / $data.e.SourcePowerWatts; DetectorFraction = $data.e.DetectorPowerWatts / $data.e.SourcePowerWatts
        ExpectedDetectorFraction = $data.ExpectedDetectorFraction; SampleCount = $data.s.SampleCount }
})
$bsdfArtifacts = @(Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object Name -ne 'manifest.json' | Sort-Object Name | ForEach-Object {
    [ordered]@{ Path = $_.Name; Bytes = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$bsdfDocs = @('docs/CURRENT_STATUS.md','docs/MEASURED_BSDF_BTDF_2026-10-31.md','docs/STAROPT_FILE_FORMAT.md','docs/CAMERA_IMAGING_LAB.md',
    'docs/FILE_FORMATS_AND_PLUGINS.md','docs/NON_SEQUENTIAL_TRACING.md','docs/NONSEQUENTIAL_PHASE2_STRAY_LIGHT.md','docs/NONSEQUENTIAL_ZEMAX_PARITY_ROADMAP.md',
    'docs/LAMBERTIAN_BSDF_2026-10-31.md','docs/README.md','samples/non-sequential/measured-bsdf/README.md','samples/non-sequential/measured-bsdf/synthetic-r02-t05.bsdf.csv')
$bsdfManifest = [ordered]@{
    CreatedUtc = [DateTimeOffset]::UtcNow.ToString('o'); BaseCommit = $bsdfBefore.BaseCommit
    Scope = 'Measured isotropic grid BSDF/BTDF; analytical controls and existing NSC/RAW/ISP regressions. Shared dirty checkout; no commercial certification.'
    Passed = $bsdfCases.Count; Failed = 0
    TestClasses = @($bsdfCases | Group-Object { ($_.testName -split '\.')[2] } | Sort-Object Name | ForEach-Object { [ordered]@{ Class=$_.Name; Passed=$_.Count } })
    EnergyControls = $bsdfEnergies
    BeforeRun = $bsdfBefore; AfterRun = $bsdfAfter; BinaryChangesDuringRun = $bsdfBinaryDrift
    Documents = @($bsdfDocs | ForEach-Object { [ordered]@{ Path=$_; Sha256=(Get-FileHash -LiteralPath (Join-Path $bsdfRoot $_) -Algorithm SHA256).Hash.ToLowerInvariant() } })
    Artifacts = $bsdfArtifacts
}
$bsdfManifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Encoding utf8
[pscustomobject]@{ Passed=$bsdfManifest.Passed; Failed=0; SourceChangesDuringRun=$bsdfSourceDrift.Count; BinaryChangesDuringRun=$bsdfBinaryDrift.Count; Artifacts=$bsdfArtifacts.Count } | ConvertTo-Json
