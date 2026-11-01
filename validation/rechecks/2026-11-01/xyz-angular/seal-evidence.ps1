$ErrorActionPreference='Stop'
$bsdfRepo=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$bsdfManifestPath=Join-Path $PSScriptRoot 'evidence-manifest.json'
if(Test-Path -LiteralPath $bsdfManifestPath){throw 'Already sealed. Use a new output directory.'}
& (Join-Path $PSScriptRoot 'verify-final.ps1')
$bsdfSummary=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'final-verification-summary.json') -Raw | ConvertFrom-Json
$bsdfIntegrity=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'final-run-integrity.json') -Raw | ConvertFrom-Json
if($bsdfSummary.Counters.passed -ne 307 -or $bsdfSummary.NewlyApprovedRemainingThreeMaterials -ne 0 -or $bsdfSummary.MeasuredAngularChainsPassed -ne 13 -or $bsdfSummary.WhitePowerPassed -ne 23 -or $bsdfSummary.PriorFrozenFilesVerified -ne 1212 -or $bsdfIntegrity.ExecutionAndSourceDrift.Count -or $bsdfIntegrity.LaterBuildCopyChanges.Count){throw 'Unexpected final verdict.'}
$bsdfDocs=@('docs/XYZ_MEASURED_ANGULAR_2026-11-01.md','docs/CURRENT_STATUS.md','docs/STAROPT_FILE_FORMAT.md','docs/FILE_FORMATS_AND_PLUGINS.md','docs/CAMERA_IMAGING_LAB.md','docs/NON_SEQUENTIAL_TRACING.md','docs/NONSEQUENTIAL_PHASE2_STRAY_LIGHT.md','docs/NONSEQUENTIAL_ZEMAX_PARITY_ROADMAP.md','docs/NUMERICAL_PARITY.md','docs/README.md','docs/XYZ_KNOWN_CONDITIONS_2026-11-01.md')
$bsdfFiles=@(Get-ChildItem -LiteralPath $PSScriptRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{Path=[IO.Path]::GetRelativePath($PSScriptRoot,$_.FullName).Replace('\','/');Bytes=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}
})
[ordered]@{
    CapturedUtc=[DateTimeOffset]::UtcNow.ToString('o')
    Scope='Finite measured outgoing angular cuts and normal-incidence BTDF through NSC/RAW/ISP. Remaining three XYZ calibrations and all-incidence nonpassive Cornell interpolants are not qualified.'
    ProductTestsPassed=307;NewRemainingMaterialCalibrationsApproved=0;RemainingXyzMaterialsRejected=3
    MeasuredAngularChainsPassed=13;AngularHistogramBinsPassed=416;CapturedWhitePowerControlsPassed=23
    FullCommercialCertification=$false;AllCornellIncidencesQualified=$false;MeasuredBackSideCertified=$false
    DefaultReleaseAppBuildPassed=$true;PriorFrozenFilesVerified=1212
    ExecutedCoreSha256=$bsdfSummary.CoreSha256;ExecutedApplicationSha256=$bsdfSummary.ApplicationSha256
    ExecutionCopies='External to this manifest: task-owned bin/FrozenAngular20261101; critical assemblies bound by before/after-isolated-final-run.json.'
    Excludes=@('evidence-manifest.json')
    DocumentationAtSeal=@($bsdfDocs | ForEach-Object {[pscustomobject]@{Path=$_;Sha256=(Get-FileHash -LiteralPath (Join-Path $bsdfRepo $_)).Hash.ToLowerInvariant()}})
    Files=$bsdfFiles
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $bsdfManifestPath -Encoding utf8
Write-Output "Sealed $($bsdfFiles.Count) files; 307 tests, 13 finite measured angular controls; zero new remaining-material calibration approvals."
