param([Parameter(Mandatory)][string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($OutputDirectory)
$rootPrefix = $target.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ([IO.Directory]::Exists($target) -and [IO.Directory]::EnumerateFileSystemEntries($target).GetEnumerator().MoveNext()) {
    throw 'Use an empty evidence working directory.'
}

$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
$local = Join-Path $PSScriptRoot 'opd-references.zip'
$frozen = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $manifest.frozenArchive))
if ((Get-FileHash -LiteralPath $local).Hash -ne $manifest.archiveSha256 -or
    (Get-FileHash -LiteralPath $frozen).Hash -ne $manifest.frozenArchiveSha256) {
    throw 'Evidence archive hash differs from the manifest.'
}

$localZip = [IO.Compression.ZipFile]::OpenRead($local)
try {
    $frozenZip = [IO.Compression.ZipFile]::OpenRead($frozen)
    try {
        foreach ($item in $manifest.files) {
            $destination = [IO.Path]::GetFullPath((Join-Path $target $item.path))
            if (!$destination.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Evidence path escapes the working directory: $($item.path)"
            }
            $entry = if ($item.frozenArchiveMember) { $frozenZip.GetEntry($item.frozenArchiveMember) }
                elseif ($item.sameArchiveMember) { $localZip.GetEntry($item.sameArchiveMember) }
                else { $localZip.GetEntry($item.path) }
            if ($null -eq $entry -or $entry.Length -ne $item.bytes) {
                throw "Missing or incomplete archive member: $($item.path)"
            }
            $stream = $entry.Open()
            try {
                $buffer = [IO.MemoryStream]::new()
                try {
                    $stream.CopyTo($buffer)
                    $bytes = $buffer.ToArray()
                } finally { $buffer.Dispose() }
            } finally { $stream.Dispose() }
            if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)) -ne $item.sha256) {
                throw "Evidence member hash differs: $($item.path)"
            }
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
            [IO.File]::WriteAllBytes($destination, $bytes)
        }
    } finally { $frozenZip.Dispose() }
} finally { $localZip.Dispose() }

Write-Output "Verified and restored $($manifest.files.Count) logical evidence files."
