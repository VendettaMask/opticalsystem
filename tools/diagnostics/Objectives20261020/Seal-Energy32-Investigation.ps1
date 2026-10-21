param([Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$evidence = Join-Path $repo 'artifacts/validation/objectives32-repair-20261021'
$destination = [IO.Path]::GetFullPath($OutputPath)
if (!$destination.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive must remain in this repository.' }
if (Test-Path -LiteralPath $destination) { throw 'Fresh archive required.' }
Add-Type -AssemblyName System.IO.Compression
$payloads = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
function Add-File([string]$path) {
    $full = (Resolve-Path -LiteralPath $path).Path
    if (!$full.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Input escapes repository.' }
    $relative = [IO.Path]::GetRelativePath($repo, $full).Replace('\','/')
    $payloads[$relative] = $full
}
foreach ($file in Get-ChildItem -LiteralPath $evidence -Recurse -File) {
    # Native helper executables and transient compiler outputs are not evidence.
    if ($file.Extension -in @('.dll','.exe','.pdb','.config')) { continue }
    Add-File $file.FullName
}
foreach ($file in Get-ChildItem -LiteralPath $PSScriptRoot -File) {
    if ($file.Extension -in @('.cs','.csproj','.ps1')) { Add-File $file.FullName }
}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '../ObjectivesResiduals20261021') -File) {
    if ($file.Extension -in @('.cs','.ps1')) { Add-File $file.FullName }
}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repo 'tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost') -Filter '*.cs') { Add-File $file.FullName }
Add-File (Join-Path $repo 'tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json')
Add-File (Join-Path $repo 'artifacts/validation/objectives-20261020/final-v7-intersection-fallback/items.json')
Add-File (Join-Path $repo 'artifacts/validation/objectives-residuals-20261021/grid-v3/summary.json')
Add-File (Join-Path $repo 'artifacts/validation/objectives-residuals-20261021/native-cones-v3-mm/cones.json')
$matrix = Get-Content (Join-Path $repo 'artifacts/validation/objectives-20261020/final-v7-intersection-fallback/items.json') -Raw | ConvertFrom-Json
$controls = Get-Content (Join-Path $evidence 'coordinate-audit-v7/inputs.json') -Raw | ConvertFrom-Json
foreach ($control in $controls) {
    $item = @($matrix | Where-Object { $_.lens -eq $control.lens -and $_.run.key -eq 'Diffraction Encircled Energy' })
    if ($item.Count -ne 1) { throw 'Unique original request required.' }
    Add-File $item[0].snapshot
    Add-File (Join-Path $repo ('src/OpticalSystemDesign.App/Assets/SequentialExamples/' + $control.lens))
    foreach ($name in @('data.json','request.json','model.json','captured-settings.json')) {
        $candidate = Join-Path $item[0].path ('native/' + $name)
        if (Test-Path -LiteralPath $candidate) { Add-File $candidate }
    }
}
$manifest = @($payloads.Keys | Sort-Object | ForEach-Object {
    [pscustomobject]@{path=$_;bytes=(Get-Item -LiteralPath $payloads[$_]).Length;sha256=(Get-FileHash -LiteralPath $payloads[$_] -Algorithm SHA256).Hash.ToLowerInvariant()}
})
$zip = [IO.Compression.ZipFile]::Open($destination, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($record in $manifest) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$payloads[$record.path],$record.path,[IO.Compression.CompressionLevel]::Optimal) | Out-Null }
    $entry = $zip.CreateEntry('manifest.json')
    $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
    try { $writer.Write((@{schemaVersion=1;status='Unresolved';productChanged=$false;files=$manifest} | ConvertTo-Json -Depth 6)) } finally { $writer.Dispose() }
} finally { $zip.Dispose() }
$zip = [IO.Compression.ZipFile]::OpenRead($destination)
try {
    foreach ($record in $manifest) {
        $entry = $zip.GetEntry($record.path)
        if ($entry.Length -ne $record.bytes) { throw 'Archived byte count mismatch.' }
        $stream = $entry.Open()
        try { $hash = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
        if ($hash -cne $record.sha256) { throw 'Archived hash mismatch.' }
    }
} finally { $zip.Dispose() }
[pscustomobject]@{path=[IO.Path]::GetRelativePath($repo,$destination).Replace('\','/');payloads=$manifest.Count;bytes=(Get-Item -LiteralPath $destination).Length;sha256=(Get-FileHash -LiteralPath $destination).Hash.ToLowerInvariant();readBackVerified=$true} | ConvertTo-Json
