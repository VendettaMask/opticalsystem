param([string]$Repository='D:/Projects/opticalsystem')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath $Repository).Path
$report=Join-Path $repo 'validation/rechecks/2026-10-31/model-capabilities-stage1'
function Hash([string]$p){(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant()}
foreach($file in 'summary','native-controls-summary'){
    if(-not(Test-Path -LiteralPath "$report/$file.initial-seal.json")){
        Copy-Item -LiteralPath "$report/$file.json" -Destination "$report/$file.initial-seal.json"
    }
}
$summary=Get-Content -LiteralPath "$report/summary.initial-seal.json" -Raw|ConvertFrom-Json -AsHashtable
$progress=Get-Content -LiteralPath "$report/capability-progress.json" -Raw|ConvertFrom-Json
$counts=[ordered]@{};foreach($g in $progress|Where-Object modelGateRemoved|Group-Object currentConclusion){$counts[$g.Name]=$g.Count}
$summary.removedGateResultCounts=$counts
$summary|ConvertTo-Json -Depth 16|Set-Content -LiteralPath "$report/summary.json" -Encoding utf8
$controls=Get-Content -LiteralPath "$report/native-controls.json" -Raw|ConvertFrom-Json
@($controls|Group-Object capture,verdict|ForEach-Object {@{scope=$_.Name;count=$_.Count;maxAbsolute=($_.Group.absoluteError|Measure-Object -Maximum).Maximum}})|ConvertTo-Json -Depth 8|Set-Content -LiteralPath "$report/native-controls-summary.json" -Encoding utf8
$priorFolder=Join-Path $repo 'validation/rechecks/2026-10-31/all-example-accuracy'
$prior=Get-Content -LiteralPath "$priorFolder/manifest.json" -Raw|ConvertFrom-Json
$oldChecks=@(foreach($e in $prior.entries|Where-Object {$_.path -like 'report/*' -or $_.path -eq 'study/results.json'}){
    $path=if($e.path -like 'report/*'){Join-Path $priorFolder $e.path.Substring(7)}else{Join-Path $repo 'artifacts/validation/all-example-accuracy-20261031/results.json'}
    $actual=Hash $path;if($actual -ne $e.sha256){throw "Prior audit changed: $path"};@{path=$e.path;sha256=$actual;matched=$true}
})
$oldChecks|ConvertTo-Json -Depth 6|Set-Content -LiteralPath "$report/prior-audit-integrity.json" -Encoding utf8
$verification=Get-Content -LiteralPath "$report/verification.json" -Raw|ConvertFrom-Json
$receipt=[ordered]@{reason='PowerShell Group-Object did not resolve properties on in-memory OrderedDictionary records; summaries regenerated from the unchanged serialized records.'
    sourceRecordsUnchanged=$true;numericResultsChanged=$false;gatesChanged=$false;previousArchiveSha256=$verification.sha256;priorAuditFilesVerified=$oldChecks.Count
    replacements=@(foreach($file in 'summary.json','native-controls-summary.json','prior-audit-integrity.json'){@{path=$file;sha256=Hash "$report/$file";bytes=(Get-Item -LiteralPath "$report/$file").Length}})}
$receipt|ConvertTo-Json -Depth 8|Set-Content -LiteralPath "$report/metadata-correction.json" -Encoding utf8
@'
# Evidence receipt

`verification.json` identifies the immutable base archive and its metadata correction overlay. All 6,883 base payloads were read back and hashed. Apply the overlay's summary and native-controls-summary files to obtain the current aggregates; capability-progress.json, native-controls.json, numerical data, source snapshots, settings and gates are unchanged. The initial summaries remain recorded beside the corrected files and in the base archive. This correction does not change any numerical verdict.

The previous 95-example report and prior result file are separately checked against their frozen manifest in `prior-audit-integrity.json`.
'@|Set-Content -LiteralPath "$report/README.md" -Encoding utf8
$overlay=Join-Path $repo 'validation/repairs/2026-10-31/model-capabilities-stage1-metadata-overlay.zip'
if(Test-Path -LiteralPath $overlay){throw 'Existing overlay must not be replaced'}
$names=@('summary.json','native-controls-summary.json','summary.initial-seal.json','native-controls-summary.initial-seal.json','prior-audit-integrity.json','metadata-correction.json','README.md')
$zip=[IO.Compression.ZipFile]::Open($overlay,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($name in $names){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,"$report/$name",$name,[IO.Compression.CompressionLevel]::Optimal)|Out-Null}}finally{$zip.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($overlay)
try{foreach($name in $names){$stream=$zip.GetEntry($name).Open();try{$actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()}finally{$stream.Dispose()};if($actual -ne (Hash "$report/$name")){throw 'Overlay readback mismatch'}}}finally{$zip.Dispose()}
$verification|Add-Member -NotePropertyName metadataOverlay -NotePropertyValue ([IO.Path]::GetRelativePath($repo,$overlay).Replace('\','/'))
$verification|Add-Member -NotePropertyName metadataOverlaySha256 -NotePropertyValue (Hash $overlay)
$verification|Add-Member -NotePropertyName metadataOverlayEntriesVerified -NotePropertyValue $names.Count
$verification|Add-Member -NotePropertyName priorAuditFilesVerified -NotePropertyValue $oldChecks.Count
$verification|ConvertTo-Json -Depth 6|Set-Content -LiteralPath "$report/verification.json" -Encoding utf8
$summary|ConvertTo-Json -Depth 10
