param(
    [string]$EvidenceRoot = 'artifacts/validation/objectives-residuals-20261021',
    [string]$ArchivePath = 'validation/repairs/2026-10-21/objectives-energy128-acceptance-evidence.zip'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$evidence = [IO.Path]::GetFullPath((Join-Path $repo $EvidenceRoot))
$archive = [IO.Path]::GetFullPath((Join-Path $repo $ArchivePath))
if (-not $archive.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive must stay inside repository.' }
if (Test-Path -LiteralPath $archive) { throw 'Do not overwrite sealed evidence.' }
$files = [Collections.Generic.SortedDictionary[string, object]]::new([StringComparer]::Ordinal)
function Add-Payload([string]$Path) {
    $absolute = (Resolve-Path -LiteralPath $Path).Path
    if (-not $absolute.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Payload outside repository.' }
    if ([IO.Path]::GetExtension($absolute) -in @('.exe', '.dll', '.pdb', '.zip', '.lock')) { return }
    $relative = [IO.Path]::GetRelativePath($repo, $absolute).Replace('\', '/')
    $files[$relative] = @{ path = $relative; source = $absolute; bytes = (Get-Item -LiteralPath $absolute).Length; sha256 = (Get-FileHash -LiteralPath $absolute).Hash.ToLowerInvariant() }
}
foreach ($directory in @('acceptance128-rays-v1', 'acceptance128-grid-bits-v2')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $evidence $directory) -Recurse -File) { Add-Payload $file.FullName }
}
foreach ($pattern in @('acceptance128-*.json', '*acceptance128*.log')) {
    foreach ($file in Get-ChildItem -LiteralPath $evidence -Filter $pattern -File) { Add-Payload $file.FullName }
}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $evidence 'tests') -Filter '*acceptance128*.trx' -File) { Add-Payload $file.FullName }
foreach ($directory in @('validation/zemax/2026-r1/objectives-energy128-acceptance-2026-10-21', 'validation/zemax/2026-r1/objectives-residuals-2026-10-21')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repo $directory) -Recurse -File) { Add-Payload $file.FullName }
}
foreach ($path in @('tools/diagnostics/Objectives20261020/Energy128RayControls.cs',
    'tools/diagnostics/Objectives20261020/Program.cs',
    'tools/diagnostics/ObjectivesResiduals20261021/NativeConeControl.cs',
    'tools/diagnostics/ObjectivesResiduals20261021/Build-And-Capture.ps1',
    'tools/diagnostics/ObjectivesResiduals20261021/Pack-AcceptanceEvidence.ps1',
    'tools/diagnostics/ObjectivesResiduals20261021/README.md',
    'tests/OpticalSystemDesign.ZemaxComparison.Tests/ObjectivesResidualPrecisionTests.cs',
    'tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json')) { Add-Payload (Join-Path $repo $path) }
$manifest = [ordered]@{
    scope = 'User-selected additional 128-grid acceptance; all original 32-grid differences remain unresolved'
    baseArchive = @{ path = 'validation/repairs/2026-10-21/objectives-residual-controls-evidence.zip'; sha256 = '8e963519a7851253224d2ca8df56a25015fde831de3ad23a8c3cac19843f9eae' }
    files = @($files.Values | ForEach-Object { @{ path = $_.path; bytes = $_.bytes; sha256 = $_.sha256 } })
}
$stream = [IO.File]::Open($archive, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
try {
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($file in $files.Values) {
            $target = $zip.CreateEntry($file.path, [IO.Compression.CompressionLevel]::Optimal).Open()
            $source = [IO.File]::OpenRead($file.source)
            try { $source.CopyTo($target) } finally { $source.Dispose(); $target.Dispose() }
        }
        $writer = [IO.StreamWriter]::new($zip.CreateEntry('payload-manifest.json').Open(), [Text.UTF8Encoding]::new($false))
        try { $writer.Write(($manifest | ConvertTo-Json -Depth 12)) } finally { $writer.Dispose() }
    } finally { $zip.Dispose() }
} finally { $stream.Dispose() }
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    if ($zip.Entries.Count -ne $files.Count + 1) { throw 'Payload count differs.' }
    foreach ($file in $files.Values) {
        $entry = $zip.GetEntry($file.path)
        if ($null -eq $entry -or $entry.Length -ne $file.bytes) { throw 'Payload size differs.' }
        $source = $entry.Open()
        try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($source)).ToLowerInvariant() } finally { $source.Dispose() }
        if ($hash -ne $file.sha256) { throw 'Payload hash differs.' }
    }
} finally { $zip.Dispose() }
$proof = @{ path = $ArchivePath.Replace('\', '/'); bytes = (Get-Item -LiteralPath $archive).Length; sha256 = (Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant(); verifiedPayloads = $files.Count; proprietaryApiBinariesIncluded = $false }
$proof | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'acceptance128-archive-proof.json') -Encoding utf8NoBOM
$proof
