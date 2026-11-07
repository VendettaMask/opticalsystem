param([Parameter(Mandatory)][string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($OutputDirectory)
$prefix = $target.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ([IO.Directory]::Exists($target) -and [IO.Directory]::EnumerateFileSystemEntries($target).GetEnumerator().MoveNext()) {
    throw 'Use an empty evidence working directory.'
}
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
$local = 'fixed-window-references.zip'
$archives = @{}
try {
    foreach ($reference in @(@{path=$local;sha256=$manifest.archiveSha256}) + $manifest.referenceArchives) {
        $path = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $reference.path))
        if ((Get-FileHash -LiteralPath $path).Hash -ne $reference.sha256) { throw "Evidence archive hash differs: $path" }
        $archives[$reference.path] = [IO.Compression.ZipFile]::OpenRead($path)
    }
    foreach ($item in $manifest.files) {
        $destination = [IO.Path]::GetFullPath((Join-Path $target $item.path))
        if (!$destination.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Evidence path escapes its working directory: $($item.path)" }
        $entry = if ($item.referenceArchive) { $archives[$item.referenceArchive].GetEntry($item.referenceMember) }
            elseif ($item.sameArchiveMember) { $archives[$local].GetEntry($item.sameArchiveMember) }
            else { $archives[$local].GetEntry($item.path) }
        if ($null -eq $entry -or $entry.Length -ne $item.bytes) { throw "Missing or incomplete archive member: $($item.path)" }
        $buffer = [IO.MemoryStream]::new()
        try {
            $stream = $entry.Open()
            try { $stream.CopyTo($buffer) } finally { $stream.Dispose() }
            $bytes = $buffer.ToArray()
        } finally { $buffer.Dispose() }
        if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)) -ne $item.sha256) { throw "Evidence hash differs: $($item.path)" }
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
        [IO.File]::WriteAllBytes($destination, $bytes)
    }
} finally {
    foreach ($archive in $archives.Values) { $archive.Dispose() }
}
Write-Output "Verified and restored $($manifest.files.Count) logical evidence files."
