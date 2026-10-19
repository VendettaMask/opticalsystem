param(
    [Parameter(Mandatory)][string]$RunDirectory,
    [Parameter(Mandatory)][string]$ReportDirectory,
    [Parameter(Mandatory)][string]$ArchivePath,
    [string]$AdditionalRunDirectory,
    [string]$AdditionalReportDirectory
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../..')).Path
$run = (Resolve-Path -LiteralPath $RunDirectory).Path
$report = (Resolve-Path -LiteralPath $ReportDirectory).Path
$archive = [IO.Path]::GetFullPath($ArchivePath)
if (-not $archive.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence archive must remain in the repository.' }
if (Test-Path -LiteralPath $archive) { throw 'Evidence archive already exists.' }
$summary = Get-Content -LiteralPath (Join-Path $report 'summary.json') -Raw | ConvertFrom-Json
if (-not $summary.completeExecutionAttempts -or $summary.sourceLenses -ne 95) { throw 'A complete 95-lens report is required.' }
$files = [Collections.Generic.List[object]]::new()
function Add-Payload([string]$Directory, [string]$Prefix, [switch]$SourcesOnly) {
    foreach ($file in Get-ChildItem -LiteralPath $Directory -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($Directory, $file.FullName).Replace('\','/')
        if ($relative -match '(^|/)(bin|obj)/' -or $file.Extension -in @('.dll','.exe','.pdb','.lock') -or $file.Name.StartsWith('.')) { continue }
        if ($SourcesOnly -and $file.Extension -notin @('.cs','.csproj','.ps1','.md','.json')) { continue }
        $files.Add([pscustomobject]@{ path = "$Prefix/$relative"; fullPath = $file.FullName; bytes = $file.Length; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() })
    }
}
Add-Payload $run 'run'
Add-Payload $report 'report'
if ($AdditionalRunDirectory) { Add-Payload (Resolve-Path -LiteralPath $AdditionalRunDirectory).Path 'zos-variant-run' }
if ($AdditionalReportDirectory) { Add-Payload (Resolve-Path -LiteralPath $AdditionalReportDirectory).Path 'zos-variant-report' }
Add-Payload $PSScriptRoot 'diagnostic-source' -SourcesOnly
Add-Payload (Join-Path $repository 'tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost') 'native-helper-source' -SourcesOnly
foreach ($log in Get-ChildItem -LiteralPath (Join-Path $repository 'artifacts/validation') -File -Filter 'sequential-corpus*') {
    $files.Add([pscustomobject]@{ path = 'orchestration-logs/' + $log.Name; fullPath=$log.FullName; bytes=$log.Length; sha256=(Get-FileHash -LiteralPath $log.FullName -Algorithm SHA256).Hash.ToLowerInvariant() })
}
if (@($files | Group-Object path | Where-Object Count -ne 1).Count -ne 0) { throw 'Duplicate archive entry.' }
New-Item -ItemType Directory -Path (Split-Path -Parent $archive) -Force | Out-Null
$stream = [IO.File]::Open($archive, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($file in $files) {
            $entry = $zip.CreateEntry($file.path, [IO.Compression.CompressionLevel]::Optimal)
            $entryStream = $entry.Open()
            $sourceStream = [IO.File]::OpenRead($file.fullPath)
            try { $sourceStream.CopyTo($entryStream) } finally { $sourceStream.Dispose(); $entryStream.Dispose() }
        }
        $manifest = $zip.CreateEntry('payload-manifest.json', [IO.Compression.CompressionLevel]::Optimal)
        $writer = [IO.StreamWriter]::new($manifest.Open(), [Text.UTF8Encoding]::new($false))
        try { $writer.Write((@($files | Select-Object path,bytes,sha256) | ConvertTo-Json -Depth 4)) } finally { $writer.Dispose() }
    } finally { $zip.Dispose() }
} finally { $stream.Dispose() }
$readback = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($file in $files) {
        $entry = $readback.GetEntry($file.path)
        if ($null -eq $entry -or $entry.Length -ne $file.bytes) { throw "Missing/truncated payload: $($file.path)" }
        $entryStream = $entry.Open()
        try { $hash = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($entryStream)) } finally { $entryStream.Dispose() }
        if ($hash -cne $file.sha256) { throw "Readback hash mismatch: $($file.path)" }
    }
    if ($readback.Entries.Count -ne $files.Count + 1) { throw 'Unexpected archive entry count.' }
} finally { $readback.Dispose() }
[ordered]@{ path = $archive; bytes = (Get-Item -LiteralPath $archive).Length; payloadFiles = $files.Count; entries = $files.Count + 1; verifiedReadback = $true;
    sha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $report 'archive.json') -Encoding utf8
Get-Content -LiteralPath (Join-Path $report 'archive.json')
