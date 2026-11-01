param([string]$Repository='D:/Projects/opticalsystem')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath $Repository).Path
$report=Join-Path $repo 'validation/rechecks/2026-10-31/model-capabilities-stage1'
function Hash([string]$p){(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant()}
$controls=Get-Content -LiteralPath "$report/native-controls.json" -Raw|ConvertFrom-Json
@($controls|Group-Object capture,id,verdict|ForEach-Object {@{scope=$_.Name;count=$_.Count;maxAbsolute=($_.Group.absoluteError|Measure-Object -Maximum).Maximum}})|ConvertTo-Json -Depth 8|Set-Content -LiteralPath "$report/native-controls-by-step.json" -Encoding utf8
$oldArchive=Join-Path $repo 'validation/rechecks/2026-10-31/all-example-accuracy/evidence-current.zip'
$oldHash=Hash $oldArchive
if($oldHash -ne '8ee6068cb8e0516a657dbc41c5a822a155e17fdd3b5d73b22a122ed7f9a172c6'){throw 'Prior archive changed'}
$verification=Get-Content -LiteralPath "$report/verification.json" -Raw|ConvertFrom-Json
if((Hash (Join-Path $repo $verification.archive)) -ne $verification.sha256){throw 'Base evidence changed'}
Add-Content -LiteralPath "$report/README.md" -Value "`nThe final receipt identified in verification.json contains the current report files and document. It also records the 14 coarse-step return controls that remain different; the converged 0.025-step mapped states pass. Apply this receipt after the base and initial metadata overlay."
$files=@(Get-ChildItem -LiteralPath $report -File|Where-Object {$_.Name -notin 'verification.json','final-receipt-manifest.json'}|ForEach-Object {@{path=$_.FullName;entry='report/'+$_.Name;sha256=Hash $_.FullName}})
$document=Join-Path $repo 'docs/MODEL_CAPABILITIES_STAGE1_2026-10-31.md'
$files+=@{path=$document;entry='docs/MODEL_CAPABILITIES_STAGE1_2026-10-31.md';sha256=Hash $document}
$manifest=[ordered]@{baseArchiveSha256=$verification.sha256;metadataOverlaySha256=$verification.metadataOverlaySha256;oldAuditArchiveSha256=$oldHash;numericResultsChanged=$false;files=$files}
$manifest|ConvertTo-Json -Depth 8|Set-Content -LiteralPath "$report/final-receipt-manifest.json" -Encoding utf8
$archive=Join-Path $repo 'validation/repairs/2026-10-31/model-capabilities-stage1-final-receipt.zip'
if(Test-Path -LiteralPath $archive){throw 'Receipt already exists'}
$zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($f in $files){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$f.path,$f.entry,[IO.Compression.CompressionLevel]::Optimal)|Out-Null}
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,"$report/final-receipt-manifest.json",'final-receipt-manifest.json',[IO.Compression.CompressionLevel]::Optimal)|Out-Null
}finally{$zip.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try{foreach($f in $files){$stream=$zip.GetEntry($f.entry).Open();try{$actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()}finally{$stream.Dispose()};if($actual -ne $f.sha256){throw 'Receipt readback mismatch'}}}finally{$zip.Dispose()}
$verification|Add-Member -NotePropertyName finalReceipt -NotePropertyValue ([IO.Path]::GetRelativePath($repo,$archive).Replace('\','/'))
$verification|Add-Member -NotePropertyName finalReceiptSha256 -NotePropertyValue (Hash $archive)
$verification|Add-Member -NotePropertyName finalReceiptPayloadsVerified -NotePropertyValue $files.Count
$verification|Add-Member -NotePropertyName oldAuditArchiveSha256Verified -NotePropertyValue $oldHash
$verification|ConvertTo-Json -Depth 6|Set-Content -LiteralPath "$report/verification.json" -Encoding utf8
$verification|ConvertTo-Json -Depth 6
