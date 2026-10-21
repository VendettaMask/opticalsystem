param([Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$destination = [IO.Path]::GetFullPath($OutputPath)
if (!$destination.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive must remain in repository.' }
if (Test-Path -LiteralPath $destination) { throw 'Fresh archive required.' }
Add-Type -AssemblyName System.IO.Compression
$payloads = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
function Add-File([string]$Path) {
    $full = (Resolve-Path -LiteralPath $Path).Path
    if (!$full.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Input escapes repository.' }
    $payloads[[IO.Path]::GetRelativePath($repo,$full).Replace('\','/')]=$full
}
foreach ($folder in @('artifacts/validation/objectives32-continuation-20261021', 'validation/zemax/2026-r1/objectives-energy32-repair-2026-10-21')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repo $folder) -File -Recurse) {
        if ($file.Extension -in @('.dll','.exe','.pdb','.obj','.lib','.exp','.ilk','.config','.h')) { continue }
        Add-File $file.FullName
    }
}
foreach ($folder in @($PSScriptRoot, (Join-Path $PSScriptRoot '../ObjectivesResiduals20261021'))) {
    foreach ($file in Get-ChildItem -LiteralPath $folder -File) {
        if ($file.Extension -in @('.cs','.csproj','.ps1','.cpp')) { Add-File $file.FullName }
    }
}
foreach ($file in @(& git -C $repo diff --name-only -- src/OpticalSystemDesign.Core tests)) { Add-File (Join-Path $repo $file) }
Add-File (Join-Path $repo 'src/OpticalSystemDesign.Core/Geometries/StandardGeometry.VertexPlane.cs')
Add-File (Join-Path $repo 'src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.CumulativeEnergy32.cs')
Add-File (Join-Path $repo 'tests/OpticalSystemDesign.ZemaxComparison.Tests/ObjectivesEnergy32RepairTests.cs')
Add-File (Join-Path $repo 'tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json')
Add-File (Join-Path $repo 'artifacts/validation/objectives-20261020/final-v7-intersection-fallback/items.json')
foreach ($source in (Get-Content (Join-Path $repo 'validation/zemax/2026-r1/objectives-energy32-repair-2026-10-21/cases.json') -Raw|ConvertFrom-Json).cases) {
    Add-File (Join-Path $repo ('src/OpticalSystemDesign.App/Assets/SequentialExamples/'+$source.lens))
}
$manifest = @($payloads.Keys | Sort-Object | ForEach-Object {
    [ordered]@{path=$_;bytes=(Get-Item -LiteralPath $payloads[$_]).Length;sha256=(Get-FileHash -LiteralPath $payloads[$_]).Hash.ToLowerInvariant()}
})
$zip = [IO.Compression.ZipFile]::Open($destination,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($record in $manifest) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$payloads[$record.path],$record.path,[IO.Compression.CompressionLevel]::Optimal)|Out-Null }
    $writer=[IO.StreamWriter]::new($zip.CreateEntry('manifest.json').Open(),[Text.UTF8Encoding]::new($false))
    try { $writer.Write((@{schemaVersion=1;status='original32-repaired';original10Passed=10;perturbed8Passed=8;proprietarySdkIncluded=$false;files=$manifest}|ConvertTo-Json -Depth 8)) } finally {$writer.Dispose()}
} finally {$zip.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($destination)
try {
    foreach ($record in $manifest) {
        $entry=$zip.GetEntry($record.path)
        if ($entry.Length -ne $record.bytes) {throw 'Archived byte count mismatch.'}
        $stream=$entry.Open()
        try {$hash=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream))} finally {$stream.Dispose()}
        if ($hash -cne $record.sha256) {throw 'Archived hash mismatch.'}
    }
} finally {$zip.Dispose()}
[ordered]@{path=[IO.Path]::GetRelativePath($repo,$destination).Replace('\','/');payloads=$manifest.Count;bytes=(Get-Item -LiteralPath $destination).Length;sha256=(Get-FileHash -LiteralPath $destination).Hash.ToLowerInvariant();readBackVerified=$true}|ConvertTo-Json
