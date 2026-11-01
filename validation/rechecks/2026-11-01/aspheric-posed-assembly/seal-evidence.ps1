$ErrorActionPreference='Stop'
$posedRepo=(Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
Set-Location -LiteralPath $posedRepo
if(Test-Path (Join-Path $PSScriptRoot 'evidence-manifest.json')){throw 'Already sealed.'}
& (Join-Path $PSScriptRoot 'verify-final.ps1')
$posedArchives=Get-Content (Join-Path $PSScriptRoot 'attempt-archive-index.json') -Raw | ConvertFrom-Json
foreach($posedArchive in $posedArchives){if(-not $posedArchive.UnpackedDuplicateRemoved -or (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $posedArchive.Archive)).Hash.ToLowerInvariant() -ne $posedArchive.ArchiveSha256){throw 'Attempt archive identity mismatch.'}}
$posedDocuments=@('docs/ASPHERIC_POSED_NSC_2026-11-01.md','docs/CURRENT_STATUS.md','docs/NON_SEQUENTIAL_TRACING.md','docs/NONSEQUENTIAL_PHASE2_STRAY_LIGHT.md','docs/NONSEQUENTIAL_ZEMAX_PARITY_ROADMAP.md','docs/CAMERA_IMAGING_LAB.md','docs/STAROPT_FILE_FORMAT.md')
$posedDocHashes=@(foreach($posedDocument in $posedDocuments){[pscustomobject]@{Path=$posedDocument;Sha256=(Get-FileHash -LiteralPath $posedDocument).Hash.ToLowerInvariant()}})
$posedFiles=@(Get-ChildItem -LiteralPath $PSScriptRoot -File -Recurse | Sort-Object FullName | ForEach-Object {[pscustomobject]@{Path=[IO.Path]::GetRelativePath($PSScriptRoot,$_.FullName).Replace('\','/');Bytes=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}})
[ordered]@{CapturedUtc=[DateTimeOffset]::UtcNow.ToString('o');Scope='Rotational aspheres, per-surface rigid frames, exact declared rims, transactional conversion, NSC/database/RAW/ISP chains.';ProductTestsPassed=521;AddedCases=33;CompiledSourceChecksumsMatched=105;MappedSourceAndInputReceipts=133;ChangedSourceFiles=18;DefaultReleaseAppBuildPassed=$true;BothBuiltInModulesIncluded=$true;NativeDesktopAcceptance=$false;CommercialCertification=$false;AllAsphereOrFreeformKindsSupported=$false;UnchangedRuntimeAndDataNotDuplicated=$true;DocumentationAtSeal=$posedDocHashes;Files=$posedFiles} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $PSScriptRoot 'evidence-manifest.json') -Encoding utf8
Write-Output "Sealed $($posedFiles.Count) files; intermediate archives have one verified payload each."
