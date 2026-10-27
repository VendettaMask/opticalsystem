param([string]$Root='artifacts/validation/afocal-fft-energy-20261027')
$ErrorActionPreference='Stop'
$repo=(Get-Location).Path;$out='validation/rechecks/2026-10-27/afocal-fft-energy'
$files=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Add([string]$p){if(!(Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing evidence: $p"};[void]$files.Add((Resolve-Path -LiteralPath $p).Path)}
foreach($name in 'final-150-v2','final-fixed-v2'){
    foreach($f in Get-ChildItem "$Root/$name" -Recurse -File){Add $f.FullName}
    foreach($r in (Get-Content "$Root/$name/items.json" -Raw|ConvertFrom-Json)){
        Add $r.snapshot
        foreach($f in Get-ChildItem -LiteralPath $r.native -File){Add $f.FullName}
    }
}
$focalHashes=@()
foreach($name in 'focal-guards-v2','focal-supplemental-analyses-v2','focal-supplemental-points-v2','focal-guards','focal-supplemental-analyses','focal-supplemental-points'){
    foreach($p in 'summary.json','identity.json'){if(Test-Path "$Root/$name/$p"){Add "$Root/$name/$p"}}
    foreach($f in Get-ChildItem "$Root/$name" -Recurse -File -Filter '*.json'){
        $focalHashes+=@{stage=$name;completed=(Test-Path "$Root/$name/identity.json");path=[IO.Path]::GetRelativePath($repo,$f.FullName).Replace('\','/');bytes=$f.Length;sha256=(Get-FileHash -LiteralPath $f.FullName).Hash.ToLowerInvariant()}
    }
}
$focalHashes|ConvertTo-Json -Depth 5|Set-Content "$Root/focal-output-hashes.json" -Encoding utf8
foreach($name in 'final-150','final-fixed','aspect-v3','interval-v7','unstretched-v9','geometry-v2','energy-v4','energy-v5','energy-v8','energy-native-v6','energy-native-v8','native-energy-grid','native-energy-grid-v2','native-binocular','binocular-audit','tests'){
    foreach($f in Get-ChildItem "$Root/$name" -Recurse -File|Where-Object {$_.Extension -notin '.exe','.dll','.pdb'}){Add $f.FullName}
}
$hostRoot='artifacts/validation/afocal-reference-focus-20261027/native-centroid-rays/host'
$hostFiles=@(Get-ChildItem $hostRoot -File|Where-Object {$_.Extension -in '.cs','.json','.exe'})
foreach($f in $hostFiles){Add $f.FullName}
@{nativeHost=$hostRoot;files=@($hostFiles|ForEach-Object {@{path=$_.FullName;sha256=(Get-FileHash $_.FullName).Hash.ToLowerInvariant()}})}|ConvertTo-Json -Depth 6|Set-Content "$Root/native-host-identity.json" -Encoding utf8
$binaries=@(foreach($d in Get-ChildItem $Root -Directory){$p=Join-Path $d.FullName 'OpticalSystemDesign.Core.dll';if(Test-Path $p){@{stage=$d.Name;coreSha256=(Get-FileHash $p).Hash.ToLowerInvariant()}}})
$binaries|ConvertTo-Json -Depth 5|Set-Content "$Root/binary-identities.json" -Encoding utf8
# The caller's current packing log remains open until this script completes.
foreach($f in Get-ChildItem $Root -File|Where-Object {$_.Name -ne 'packing-final.log' -and $_.Extension -ne '.zip'}){Add $f.FullName}
foreach($f in Get-ChildItem $PSScriptRoot -File){Add $f.FullName}
foreach($f in Get-ChildItem validation/zemax/2026-r1/afocal-fft-energy-2026-10-27 -File){Add $f.FullName}
foreach($r in (Get-Content "$Root/final-150-v2/items.json" -Raw|ConvertFrom-Json|Group-Object lens|ForEach-Object {$_.Group[0]})){Add ('src/OpticalSystemDesign.App/Assets/SequentialExamples/'+$r.lens)}
foreach($p in @('src/OpticalSystemDesign.Core/Analysis/Shared/ImageSpaceAnalysisSupport.cs',
    'src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.cs','src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.CumulativeEnergy.cs',
    'tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalFftEnergyTests.cs','tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj',
    'docs/AFOCAL_FFT_ENERGY_REPAIR_2026-10-27.md','docs/validation/AFOCAL_FFT_ENERGY_REPAIR_2026-10-27.json',"$out/README.md",'tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json')){Add $p}
foreach($p in 'OpticalSystemDesign.Core.dll','OpticalSystemDesign.Application.dll','OpticalSystemDesign.ZemaxComparison.dll'){Add "$Root/final-runner-v2/$p"}
$manifest=@(foreach($p in ($files|Sort-Object)){
    if(!$p.StartsWith($repo.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Evidence escapes repository'}
    $f=Get-Item -LiteralPath $p;@{path=[IO.Path]::GetRelativePath($repo,$p).Replace('\','/');bytes=$f.Length;sha256=(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
})
$manifest|ConvertTo-Json -Depth 5|Set-Content "$out/evidence-manifest.json" -Encoding utf8
$diagnosticPattern='afocal-fft-energy-20261027/(final-150/|final-fixed/|aspect-v3/|interval-v7/|unstretched-v9/|geometry-v2/|energy-v4/|energy-v5/|energy-v8/|energy-native-v6/|energy-native-v8/|native-energy-grid/|binocular-audit/)'
$archives=@(foreach($kind in 'current','diagnostics'){
    $members=@($manifest|Where-Object {($_.path -match $diagnosticPattern) -eq ($kind -eq 'diagnostics')})
    $zipPath=Join-Path $repo "$out/evidence-$kind.zip";if(Test-Path $zipPath){throw 'Preserve existing archive'}
    $zip=[IO.Compression.ZipFile]::Open($zipPath,[IO.Compression.ZipArchiveMode]::Create)
    try{foreach($r in $members){[void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $repo $r.path),$r.path,[IO.Compression.CompressionLevel]::Optimal)}}finally{$zip.Dispose()}
    $zip=[IO.Compression.ZipFile]::OpenRead($zipPath)
    try{foreach($r in $members){
        $entry=$zip.GetEntry($r.path);if(!$entry -or $entry.Length -ne $r.bytes){throw 'Archive entry mismatch'}
        $stream=$entry.Open();try{$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()}finally{$stream.Dispose()}
        if($hash -cne $r.sha256){throw 'Archive content mismatch'}
    }}finally{$zip.Dispose()}
    $bytes=(Get-Item $zipPath).Length;if($bytes -ge 100MB){throw 'Split evidence into smaller independent ZIP archives'}
    @{path="evidence-$kind.zip";files=$members.Count;bytes=$bytes;sha256=(Get-FileHash $zipPath).Hash.ToLowerInvariant();readBackVerified=$true}
})
@{files=$manifest.Count;archives=$archives;allEntriesReadBackAndVerified=$true;focalOutputsHashed=$focalHashes.Count}|ConvertTo-Json -Depth 5|Set-Content "$out/archive-integrity.json" -Encoding utf8
Get-Content "$out/archive-integrity.json"
