$ErrorActionPreference='Stop'
$taskRepo='D:\Projects\opticalsystem'
$taskFiles=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Add-TaskFile([string]$taskRelative){
    $taskPath=Join-Path $taskRepo $taskRelative
    if(!(Test-Path -LiteralPath $taskPath -PathType Leaf)){throw "Missing selected sync evidence: $taskRelative"}
    if((Get-Item -LiteralPath $taskPath).Length -gt 50MB){throw "Oversized selected sync evidence: $taskRelative"}
    [void]$taskFiles.Add($taskRelative.Replace('\','/'))
}
foreach($taskFolder in @('audit-reliability-fixes-20261005','commercial-phase1-20261006','zemax-standard-sample-opd-20261006','zemax-tessar-vignetting-20261006','zemax-rms-field-20261007','zemax-tessar-rms-20261007','zemax-ra256-single-ray-20261007')){
    Get-ChildItem -LiteralPath (Join-Path $taskRepo "artifacts/validation/$taskFolder") -File | Where-Object {$_.Extension -in '.json','.log','.trx','.ps1' -or $_.Name -eq 'current-baseline-documents.txt'} | ForEach-Object {Add-TaskFile "artifacts/validation/$taskFolder/$($_.Name)"}
}
foreach($taskFolder in @('rms-original132-recalculated','rms-controls-recalculated','tessar-rms-repair-final-v2-original132','tessar-rms-repair-final-v2-controls','ra256-single-ray-old-snapshot-original132','ra256-single-ray-fresh-import-original132','ra256-single-ray-old-snapshot-controls17','ra256-single-ray-fresh-import-controls17','ra256-single-ray-old-snapshot-controls6','ra256-single-ray-fresh-import-controls6')){
    foreach($taskName in @('summary.json','immutable-evidence.json')){Add-TaskFile "artifacts/zemax-standard-samples/20261007/$taskFolder/$taskName"}
}
foreach($taskRelative in @('artifacts/zemax-standard-samples/20261006/STANDARD_SAMPLE_COMPARISON_SUMMARY.json','artifacts/zemax-standard-samples/20261007/RMS_CONTROL_SUMMARY.json','artifacts/zemax-standard-samples/20261007/RA256_CONTROL_SUMMARY.json','artifacts/zemax-standard-samples/20261006/core-replay/Program.cs','artifacts/zemax-standard-samples/20261006/core-replay/Replay.csproj','artifacts/zemax-standard-samples/20261007/ra256-verifier/Program.cs','artifacts/zemax-standard-samples/20261007/ra256-verifier/Verifier.csproj','artifacts/zemax-standard-samples/20261007/ra256-ray-audit/tessar-ra256-remove/failure.json')){Add-TaskFile $taskRelative}
foreach($taskCase in @('tessar-ra256-remove-streamed','tessar-ra256-retain','cooke-40-degree-field-ra256-remove','double-gauss-28-degree-field-ra256-remove','relay-lens-ra256-remove','even-asphere-ra256-remove')){
    foreach($taskName in @('verification.json','verification-import-source.json','snapshot.json','current-import-snapshot.json','manifest.json','job.json')){Add-TaskFile "artifacts/zemax-standard-samples/20261007/ra256-ray-audit/$taskCase/$taskName"}
}
$taskSorted=@($taskFiles | Sort-Object)
$taskBytes=($taskSorted | ForEach-Object {(Get-Item -LiteralPath (Join-Path $taskRepo $_)).Length} | Measure-Object -Sum).Sum
$taskRecords=@(foreach($taskRelative in $taskSorted){@{path=$taskRelative;bytes=(Get-Item -LiteralPath (Join-Path $taskRepo $taskRelative)).Length;sha256=(Get-FileHash -LiteralPath (Join-Path $taskRepo $taskRelative) -Algorithm SHA256).Hash.ToLowerInvariant()}})
@{date='2026-10-07';evidenceFiles=$taskRecords;totalBytes=$taskBytes;scope='Selected final and historical verification records, immutable replay summaries/manifests and audit summaries. Full native capture directories and GB-scale ray arrays remain local. Source/tests/docs and frozen validation fixtures are staged separately.'} | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $taskRepo 'docs/validation/PROJECT_SYNC_2026-10-07.json') -Encoding utf8NoBOM
$taskSorted | Set-Content -LiteralPath (Join-Path $taskRepo 'artifacts/validation/zemax-ra256-single-ray-20261007/sync-evidence-paths.txt') -Encoding utf8NoBOM
'Selected {0} evidence files, {1:N2} MiB; largest files:' -f $taskSorted.Count,($taskBytes/1MB)
$taskRecords | Sort-Object bytes -Descending | Select-Object -First 5 | ConvertTo-Json -Compress
