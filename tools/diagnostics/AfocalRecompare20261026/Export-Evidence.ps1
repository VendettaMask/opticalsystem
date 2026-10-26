param([string]$RunDirectory='artifacts/validation/afocal-recompare-20261026')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$root=(Resolve-Path -LiteralPath (Join-Path $repo $RunDirectory)).Path
$out=Join-Path $repo 'validation/rechecks/2026-10-26/afocal-recompare'
New-Item -ItemType Directory -Force -Path $out|Out-Null
Copy-Item -LiteralPath (Join-Path $root 'final/analysis-results.json') -Destination (Join-Path $out 'analysis-results.json')
Copy-Item -LiteralPath (Join-Path $root 'final/summary.json') -Destination (Join-Path $repo 'docs/validation/AFOCAL_RECOMPARE_2026-10-26.json')
$archivePath=Join-Path $out 'evidence.zip'
if(Test-Path -LiteralPath $archivePath){throw 'Never overwrite a completed evidence archive'}
$paths=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($directory in @('baseline-v4','expanded','controls','ray-audit','relative-illumination-diagnostics','fixed-grid-controls','selected-sources','Objects','tests','final')){
    foreach($file in Get-ChildItem (Join-Path $root $directory) -Recurse -File){[void]$paths.Add($file.FullName)}
}
foreach($directory in @('runners-v4/SequentialCorpus20261019','runners-v4/Afocal20261024','expanded-runner-v6')){
    foreach($file in Get-ChildItem (Join-Path $root $directory) -File|Where-Object Extension -ne '.pdb'){[void]$paths.Add($file.FullName)}
}
foreach($file in Get-ChildItem (Join-Path $root 'runners-v4/SequentialCorpus20261019/runtimes/win-x64/native') -File -Filter '*.dll'){[void]$paths.Add($file.FullName)}
foreach($file in Get-ChildItem $root -File|Where-Object Extension -in @('.txt','.json','.log')){[void]$paths.Add($file.FullName)}
foreach($file in Get-ChildItem $PSScriptRoot -File){[void]$paths.Add($file.FullName)}
foreach($path in @('docs/AFOCAL_RECOMPARE_2026-10-26.md','docs/validation/AFOCAL_RECOMPARE_2026-10-26.json',
    'validation/rechecks/2026-10-26/afocal-recompare/README.md','validation/rechecks/2026-10-26/afocal-recompare/analysis-results.json',
    'tests/OpticalSystemDesign.ZemaxComparison.Tests/RegistryCatalogSynchronizationTests.cs','tools/OpticalSystemDesign.ZemaxComparison/AnalysisComparisonRegistry.cs',
    'tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost/Host.cs')){[void]$paths.Add((Join-Path $repo $path))}
$files=@(foreach($path in $paths|Sort-Object){$file=Get-Item -LiteralPath $path;if($file.Name -eq '.prepare.lock'){continue};
    [ordered]@{path=[IO.Path]::GetRelativePath($repo,$file.FullName).Replace('\','/');bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant()}})
$manifest=[ordered]@{date='2026-10-26';count=$files.Count;files=$files;semantics='Raw and derived evidence with frozen optical-run binaries; old failed attempts retained. Excludes native debug symbols and unrelated platform runtimes.'}
$manifestPath=Join-Path $out 'evidence-manifest.json'
$manifest|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
$zip=[IO.Compression.ZipFile]::Open($archivePath,[IO.Compression.ZipArchiveMode]::Create)
try{
    foreach($file in $files){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $repo $file.path),$file.path,[IO.Compression.CompressionLevel]::Optimal)|Out-Null}
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$manifestPath,[IO.Path]::GetRelativePath($repo,$manifestPath).Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal)|Out-Null
}finally{$zip.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($archivePath);$matched=0
try{
    if($zip.Entries.Count -ne $files.Count+1){throw 'Archive entry count mismatch'}
    foreach($file in $files){$entry=$zip.GetEntry($file.path);if(!$entry -or $entry.Length -ne $file.bytes){throw ('Missing or truncated entry: '+$file.path)}
        $stream=$entry.Open();try{$sha=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()}finally{$stream.Dispose()}
        if($sha -ne $file.sha256){throw ('Archive byte mismatch: '+$file.path)};$matched++}
}finally{$zip.Dispose()}
$record=[ordered]@{date='2026-10-26';files=$files.Count;matched=$matched;allEntriesReadAndVerified=$true;
    archiveBytes=(Get-Item $archivePath).Length;archiveSha256=(Get-FileHash $archivePath).Hash.ToLowerInvariant();manifestSha256=(Get-FileHash $manifestPath).Hash.ToLowerInvariant()}
$record|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $out 'archive-integrity.json') -Encoding utf8NoBOM
$record|ConvertTo-Json
