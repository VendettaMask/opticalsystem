param([string]$Root='artifacts/validation/afocal-sampling-20261027')
$ErrorActionPreference='Stop'
$repo=(Get-Location).Path
$out='validation/rechecks/2026-10-27/afocal-pupil-sampling'
$files=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Add([string]$p){if(!(Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing evidence: $p"};[void]$files.Add((Resolve-Path -LiteralPath $p).Path)}
foreach($name in 'final-150','final-fixed'){
    foreach($f in Get-ChildItem -LiteralPath "$Root/$name" -Recurse -File){Add $f.FullName}
    foreach($r in (Get-Content "$Root/$name/items.json" -Raw|ConvertFrom-Json)){
        Add $r.snapshot
        foreach($f in Get-ChildItem -LiteralPath $r.native -File){Add $f.FullName}
    }
}
$focalHashes=@()
foreach($name in 'focal-guards','focal-supplemental-analyses','focal-supplemental-points'){
    foreach($p in 'summary.json','identity.json'){Add "$Root/$name/$p"}
    foreach($f in Get-ChildItem -LiteralPath "$Root/$name" -Recurse -File -Filter '*.json'){
        $focalHashes+=@{path=[IO.Path]::GetRelativePath($repo,$f.FullName).Replace('\','/');bytes=$f.Length;sha256=(Get-FileHash -LiteralPath $f.FullName).Hash.ToLowerInvariant()}
    }
}
$focalHashes|ConvertTo-Json -Depth 5|Set-Content "$Root/focal-output-hashes.json" -Encoding utf8
foreach($name in 'native-complex-pupil','native-complex-pupil-v2','native-grid-rays','pupil-probe-v1','pupil-inverse-v5','pupil-inverse-v6','pupil-inverse-v7','phase-v2','scaling-v3','tests'){
    foreach($f in Get-ChildItem -LiteralPath "$Root/$name" -Recurse -File|Where-Object {$_.Extension -notin '.exe','.dll','.pdb'}){Add $f.FullName}
}
# The caller redirects this run's output to packing-final.log; it is still
# open while the archive is built. Preserve the earlier failed packing log.
foreach($f in Get-ChildItem -LiteralPath $Root -File|Where-Object Name -ne 'packing-final.log'){Add $f.FullName}
foreach($f in Get-ChildItem -LiteralPath $PSScriptRoot -File){Add $f.FullName}
foreach($f in Get-ChildItem -LiteralPath 'validation/zemax/2026-r1/afocal-pupil-scale-2026-10-27' -File){Add $f.FullName}
foreach($r in (Get-Content "$Root/final-150/items.json" -Raw|ConvertFrom-Json|Group-Object lens|ForEach-Object {$_.Group[0]})){
    Add ('src/OpticalSystemDesign.App/Assets/SequentialExamples/'+$r.lens)
}
foreach($p in @('src/OpticalSystemDesign.Core/Analysis/Shared/ImageSpaceAnalysisSupport.cs',
    'tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalPupilScaleTests.cs',
    'tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj',
    'docs/AFOCAL_PUPIL_SAMPLING_REPAIR_2026-10-27.md','docs/validation/AFOCAL_PUPIL_SAMPLING_REPAIR_2026-10-27.json',
    "$out/README.md",'tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json')){Add $p}
foreach($p in 'OpticalSystemDesign.Core.dll','OpticalSystemDesign.Application.dll','OpticalSystemDesign.ZemaxComparison.dll'){Add "$Root/final-runner/$p"}
$manifest=@(foreach($p in ($files|Sort-Object)){
    if(!$p.StartsWith($repo.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Evidence escapes repository'}
    $f=Get-Item -LiteralPath $p
    @{path=[IO.Path]::GetRelativePath($repo,$p).Replace('\','/');bytes=$f.Length;sha256=(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
})
$manifest|ConvertTo-Json -Depth 5|Set-Content "$out/evidence-manifest.json" -Encoding utf8
$zipPath=Join-Path $repo "$out/evidence.zip"
if(Test-Path -LiteralPath $zipPath){throw 'Archive already exists; preserve evidence'}
$zip=[IO.Compression.ZipFile]::Open($zipPath,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($r in $manifest){[void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $repo $r.path),$r.path,[IO.Compression.CompressionLevel]::Optimal)}}finally{$zip.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($zipPath)
try{foreach($r in $manifest){
    $entry=$zip.GetEntry($r.path);if(!$entry -or $entry.Length -ne $r.bytes){throw 'Archive entry mismatch'}
    $stream=$entry.Open()
    try{$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()}finally{$stream.Dispose()}
    if($hash -cne $r.sha256){throw 'Archive content mismatch'}
}}finally{$zip.Dispose()}
@{files=$manifest.Count;bytes=(Get-Item -LiteralPath $zipPath).Length;sha256=(Get-FileHash -LiteralPath $zipPath).Hash.ToLowerInvariant();allEntriesReadBackAndVerified=$true;focalOutputsHashed=$focalHashes.Count}|ConvertTo-Json|Set-Content "$out/archive-integrity.json" -Encoding utf8
Get-Content "$out/archive-integrity.json"
