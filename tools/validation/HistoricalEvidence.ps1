#requires -Version 7.0
param(
    [ValidateSet('Verify','Restore')][string]$Mode = 'Verify',
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '../..'),
    [string]$ManifestPath = 'validation/historical-compression.json',
    [string]$DestinationRoot,
    [string]$PathPrefix,
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'

function Resolve-HistoricalPath([string]$Root, [string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or [IO.Path]::IsPathRooted($Path) -or
        $Path -match '[\\:\r\n]' -or @($Path.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count) {
        throw 'Historical evidence paths must be unambiguous relative paths.'
    }
    $boundary = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $absolute = [IO.Path]::GetFullPath((Join-Path $boundary $Path))
    if (!$absolute.StartsWith($boundary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Historical evidence path escapes its root.'
    }
    for ($ancestor = $absolute; $ancestor; $ancestor = [IO.Path]::GetDirectoryName($ancestor)) {
        if ((Test-Path -LiteralPath $ancestor) -and
            ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Historical evidence paths cannot traverse links.'
        }
    }
    return $absolute
}

function Read-HistoricalPayload($Entry, [string]$Source, [string]$Destination) {
    $file = [IO.FileInfo]::new($Source)
    if (!$file.Exists -or $file.Length -ne $Entry.storedBytes) { throw ('Compressed length changed: ' + $Entry.path) }
    $input = [IO.File]::OpenRead($Source)
    $gzip = $null; $output = $null
    $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        $storedHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($input)).ToLowerInvariant()
        if ($storedHash -cne $Entry.storedSha256) { throw ('Compressed hash changed: ' + $Entry.path) }
        $input.Position = 0
        $gzip = [IO.Compression.GZipStream]::new($input, [IO.Compression.CompressionMode]::Decompress, $true)
        if ($Destination) { $output = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write) }
        $buffer = [byte[]]::new(65536); $total = [long]0
        while (($count = $gzip.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $total += $count
            if ($total -gt $Entry.bytes) { throw ('Expanded length exceeds original: ' + $Entry.path) }
            $hash.AppendData($buffer, 0, $count)
            if ($output) { $output.Write($buffer, 0, $count) }
        }
        $actual = [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
        if ($total -ne $Entry.bytes -or $actual -cne $Entry.sha256) { throw ('Original bytes changed: ' + $Entry.path) }
    } finally {
        if ($output) { $output.Dispose() }
        if ($gzip) { $gzip.Dispose() }
        $hash.Dispose(); $input.Dispose()
    }
}

function Invoke-HistoricalEvidence {
    $root = (Resolve-Path -LiteralPath $RepositoryRoot).Path.TrimEnd([IO.Path]::DirectorySeparatorChar)
    $manifestFile = Resolve-HistoricalPath $root $ManifestPath
    $manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or @($manifest.files).Count -eq 0) { throw 'Invalid historical compression manifest.' }
    if ($PathPrefix) { $null = Resolve-HistoricalPath $root $PathPrefix }
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $selected = [Collections.Generic.List[object]]::new()
    foreach ($entry in $manifest.files) {
        $original = Resolve-HistoricalPath $root $entry.path
        $source = Resolve-HistoricalPath $root $entry.storedPath
        if (!$entry.path.StartsWith('validation/rechecks/', [StringComparison]::Ordinal) -or
            $entry.storedPath -cne ($entry.path + '.gz') -or
            !$paths.Add([string]$entry.path) -or !$paths.Add([string]$entry.storedPath) -or
            $entry.sha256 -cnotmatch '^[0-9a-f]{64}$' -or $entry.storedSha256 -cnotmatch '^[0-9a-f]{64}$' -or
            $entry.bytes -le 0 -or $entry.storedBytes -le 0) { throw 'Invalid or repeated compressed evidence entry.' }
        if (Test-Path -LiteralPath $original) { throw ('Uncompressed historical duplicate reintroduced: ' + $entry.path) }
        if (!$PathPrefix -or $entry.path -ceq $PathPrefix -or $entry.path.StartsWith($PathPrefix + '/', [StringComparison]::Ordinal)) {
            $selected.Add([pscustomobject]@{entry=$entry; source=$source})
        }
    }
    if (!$selected.Count) { throw 'No historical evidence matches the selection.' }
    # Validate the complete selected input before creating a destination or publishing any restored file.
    foreach ($item in $selected) { Read-HistoricalPayload $item.entry $item.source }
    if ($Mode -eq 'Restore') {
        if ([string]::IsNullOrWhiteSpace($DestinationRoot)) { throw 'Restore requires a fresh destination outside the repository.' }
        $destination = [IO.Path]::GetFullPath($DestinationRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
        if ($destination -eq $root -or $destination.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
            $root.StartsWith($destination + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
            (Test-Path -LiteralPath $destination)) { throw 'Restore requires a fresh destination outside the repository.' }
        foreach ($item in $selected) { $null = Resolve-HistoricalPath $destination $item.entry.path }
        foreach ($item in $selected) {
            $target = Resolve-HistoricalPath $destination $item.entry.path
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
            $temporary = $target + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
            try {
                Read-HistoricalPayload $item.entry $item.source $temporary
                [IO.File]::Move($temporary, $target)
            } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
        }
    }
    $result = [pscustomobject]@{valid=$true; mode=$Mode; files=$selected.Count;
        originalBytes=($selected.entry | Measure-Object -Property bytes -Sum).Sum;
        storedBytes=($selected.entry | Measure-Object -Property storedBytes -Sum).Sum;
        manifestSha256=(Get-FileHash -LiteralPath $manifestFile).Hash.ToLowerInvariant()}
    if ($OutputPath) {
        $report = [IO.Path]::GetFullPath($OutputPath)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($report)) | Out-Null
        $result | ConvertTo-Json | Set-Content -LiteralPath $report -Encoding utf8
    }
    $result
}

if ($MyInvocation.InvocationName -ne '.') { Invoke-HistoricalEvidence | ConvertTo-Json }
