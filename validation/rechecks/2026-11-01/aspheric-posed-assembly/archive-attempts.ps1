$ErrorActionPreference='Stop'
$posedRoot=(Resolve-Path $PSScriptRoot).Path
if(Test-Path (Join-Path $posedRoot 'evidence-manifest.json')){throw 'Evidence already sealed.'}
$posedArchives=Join-Path $posedRoot 'attempts';New-Item -ItemType Directory -Path $posedArchives -Force | Out-Null
$posedIndex=@()
foreach($posedName in @('refined-controls','pre-image-plane-guard')){
 $posedDirectory=Join-Path $posedRoot $posedName
 if(-not(Test-Path -LiteralPath $posedDirectory)){throw "Missing attempt: $posedName"}
 $posedResolved=(Resolve-Path -LiteralPath $posedDirectory).Path
 if(-not $posedResolved.StartsWith($posedRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe attempt directory.'}
 $posedZip=Join-Path $posedArchives "$posedName.zip"
 if(Test-Path -LiteralPath $posedZip){throw 'Attempt archive already exists.'}
 $posedEntries=@(Get-ChildItem -LiteralPath $posedResolved -File -Recurse | ForEach-Object {[pscustomobject]@{Path=[IO.Path]::GetRelativePath($posedResolved,$_.FullName).Replace('\','/');Bytes=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}})
 [IO.Compression.ZipFile]::CreateFromDirectory($posedResolved,$posedZip,[IO.Compression.CompressionLevel]::Optimal,$false)
 $posedContainer=[IO.Compression.ZipFile]::OpenRead($posedZip)
 try{
  if(@($posedContainer.Entries | Where-Object Name -ne '').Count -ne $posedEntries.Count){throw 'Archive entry count differs.'}
  foreach($posedEntry in $posedEntries){$posedPacked=$posedContainer.GetEntry($posedEntry.Path);if(-not $posedPacked -or $posedPacked.Length -ne $posedEntry.Bytes){throw 'Missing archived entry.'};$posedStream=$posedPacked.Open();try{$posedSha=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($posedStream)).ToLowerInvariant()}finally{$posedStream.Dispose()};if($posedSha -ne $posedEntry.Sha256){throw 'Archived entry hash differs.'}}
 }finally{$posedContainer.Dispose()}
 # Delete only this explicitly checked task directory after complete-entry verification.
 Remove-Item -LiteralPath $posedResolved -Recurse -Force
 $posedIndex+=[pscustomobject]@{OriginalDirectory=$posedName;Archive="attempts/$posedName.zip";ArchiveSha256=(Get-FileHash -LiteralPath $posedZip).Hash.ToLowerInvariant();EntriesVerified=$posedEntries.Count;Entries=$posedEntries;SupersededBy='final-controls and final-focused.trx';UnpackedDuplicateRemoved=$true}
}
$posedIndex | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $posedRoot 'attempt-archive-index.json') -Encoding utf8
Write-Output 'Verified attempt archive entries and removed their redundant unpacked copies.'
