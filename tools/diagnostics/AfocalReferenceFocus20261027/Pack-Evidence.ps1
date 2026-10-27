param([string]$Root='artifacts/validation/afocal-reference-focus-20261027')
$ErrorActionPreference='Stop'
$repo=(Get-Location).Path
$rootPath=(Resolve-Path -LiteralPath $Root).Path
$out='validation/rechecks/2026-10-27/afocal-reference-focus'
New-Item -ItemType Directory -Path $out -Force|Out-Null
if(Test-Path -LiteralPath "$out/evidence.zip"){throw 'Never overwrite an evidence archive'}
$files=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Add([string]$path){if(Test-Path -LiteralPath $path -PathType Leaf){[void]$files.Add((Resolve-Path -LiteralPath $path).Path)}}
foreach($name in 'final-150','final-fixed'){
    foreach($p in 'summary.json','items.json','identity.json','workbench-results.json'){Add "$Root/$name/$p"}
    foreach($r in (Get-Content -LiteralPath "$Root/$name/items.json" -Raw|ConvertFrom-Json)){
        foreach($f in Get-ChildItem -LiteralPath "$Root/$name/$($r.id)/workbench" -File){Add $f.FullName}
        Add "$Root/$name/comparison/$($r.id)/comparison.json"
    }
}
$focalHashes=@()
foreach($name in 'focal-guards','focal-supplemental-analyses','focal-supplemental-points'){
    Add "$Root/$name/summary.json";Add "$Root/$name/identity.json"
    foreach($f in Get-ChildItem -LiteralPath "$Root/$name" -Recurse -File -Filter '*.json'){
        $focalHashes+=@{path=[IO.Path]::GetRelativePath($repo,$f.FullName).Replace('\','/');bytes=$f.Length;sha256=(Get-FileHash -LiteralPath $f.FullName).Hash.ToLowerInvariant()}
    }
}
ConvertTo-Json -InputObject $focalHashes -Depth 5|Set-Content -LiteralPath "$Root/focal-output-hashes.json" -Encoding utf8
foreach($name in 'native-reference-controls','native-centroid-32','native-centroid-rays'){
    foreach($f in Get-ChildItem -LiteralPath "$Root/$name" -Recurse -File|Where-Object {$_.Extension -notin '.exe','.dll','.pdb'}){Add $f.FullName}
}
foreach($name in 'reference-probe-v1','reference-probe-v3','reference-probe-v4','frequency-probe-v1'){Add "$Root/$name/summary.json"}
foreach($f in Get-ChildItem -LiteralPath "$Root/tests" -File){Add $f.FullName}
foreach($f in Get-ChildItem -LiteralPath $rootPath -File|Where-Object {$_.Extension -in '.json','.log','.txt'}){Add $f.FullName}
foreach($f in Get-ChildItem -LiteralPath $PSScriptRoot -File){Add $f.FullName}
foreach($p in @('src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.cs',
    'src/OpticalSystemDesign.Core/Analysis/WavefrontEngine.cs','src/OpticalSystemDesign.Core/Analysis/Radiometry/EncircledEnergyVariants.cs',
    'tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost/NativePointComparisonCapture.cs',
    'tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalReferenceFocusTests.cs',
    'tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj',
    'docs/AFOCAL_REFERENCE_FOCUS_REPAIR_2026-10-27.md',"$out/README.md")){Add $p}
foreach($f in Get-ChildItem -LiteralPath validation/zemax/2026-r1/afocal-reference-focus-2026-10-27 -File){Add $f.FullName}
foreach($p in 'OpticalSystemDesign.Core.dll','OpticalSystemDesign.Application.dll','OpticalSystemDesign.ZemaxComparison.dll'){Add "$Root/final-runner/$p"}
$prefix=$repo.TrimEnd('\')+'\'
$manifest=@(foreach($p in ($files|Sort-Object)){
    if(!$p.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Evidence escapes repository'}
    $f=Get-Item -LiteralPath $p
    @{path=[IO.Path]::GetRelativePath($repo,$p).Replace('\','/');bytes=$f.Length;sha256=(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
})
ConvertTo-Json -InputObject $manifest -Depth 5|Set-Content -LiteralPath "$out/evidence-manifest.json" -Encoding utf8
$zip=[IO.Compression.ZipFile]::Open((Join-Path $repo "$out/evidence.zip"),[IO.Compression.ZipArchiveMode]::Create)
try{foreach($r in $manifest){[void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $repo $r.path),$r.path,[IO.Compression.CompressionLevel]::Optimal)}}finally{$zip.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $repo "$out/evidence.zip"))
try{
    foreach($r in $manifest){
        $entry=$zip.GetEntry($r.path);if(!$entry -or $entry.Length -ne $r.bytes){throw 'Archive entry mismatch'}
        $stream=$entry.Open()
        try{$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()}finally{$stream.Dispose()}
        if($hash -cne $r.sha256){throw 'Archive content mismatch'}
    }
}finally{$zip.Dispose()}
@{files=$manifest.Count;bytes=(Get-Item -LiteralPath "$out/evidence.zip").Length;sha256=(Get-FileHash -LiteralPath "$out/evidence.zip").Hash.ToLowerInvariant();allPayloadsReadBackAndVerified=$true}|ConvertTo-Json|Set-Content -LiteralPath "$out/archive-integrity.json" -Encoding utf8
Copy-Item -LiteralPath "$Root/final-ledger.json" -Destination "$out/summary.json"
Copy-Item -LiteralPath "$Root/final-ledger.json" -Destination docs/validation/AFOCAL_REFERENCE_FOCUS_REPAIR_2026-10-27.json
Copy-Item -LiteralPath "$Root/final-150/summary.json" -Destination "$out/analysis-results.json"
Write-Output "Archived and read-back verified: $($manifest.Count) files."
