function Get-FrozenIntegrityReport {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryRoot,
          [Parameter(Mandatory)][string]$ManifestPath)
    $repository = (Resolve-Path -LiteralPath $RepositoryRoot -ErrorAction Stop).Path
    $prefix = $repository.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding utf8 -ErrorAction Stop | ConvertFrom-Json
    $files = @($manifest.files)
    if ($files.Count -eq 0 -or $files.Count -ne $manifest.count) { throw 'Frozen manifest count differs or is empty.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $entries = [Collections.Generic.List[object]]::new()
    foreach ($file in $files) {
        if ([string]::IsNullOrWhiteSpace($file.path) -or [IO.Path]::IsPathRooted($file.path) -or
            $file.sha256 -cnotmatch '^[0-9a-f]{64}$') { throw 'Invalid frozen manifest entry.' }
        $path = [IO.Path]::GetFullPath((Join-Path $repository $file.path))
        if (!$path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or !$seen.Add($path)) {
            throw 'Frozen manifest path escapes repository or is duplicated.'
        }
        # Reject junctions/symlinks rather than hashing an unrelated target.
        $ancestor = $path
        while ($ancestor -and $ancestor.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            if (Test-Path -LiteralPath $ancestor) {
                $item = Get-Item -LiteralPath $ancestor -Force -ErrorAction Stop
                if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Frozen path contains a link.' }
            }
            $ancestor = Split-Path -Parent $ancestor
        }
        $actual = $null
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
        }
        $entries.Add([pscustomobject]@{ path=$file.path; expectedSha256=$file.sha256; actualSha256=$actual; matches=($actual -ceq $file.sha256) })
    }
    # Preserve the published 2026-10-09 definition: ordinal paths with original
    # case, lowercase hashes, CRLF separators and a terminal CRLF. No file EOL
    # normalization is allowed, even when semantic contents are equivalent.
    $ordered = $entries.ToArray()
    [Array]::Sort($ordered, [Collections.Generic.Comparer[object]]::Create({ param($a,$b) [StringComparer]::Ordinal.Compare($a.path,$b.path) }))
    function Aggregate($rows, [string]$property) {
        $lines = [string[]]@($rows | ForEach-Object { $_.path+'='+$_.$property })
        $provider = [Security.Cryptography.SHA256]::Create()
        try { [BitConverter]::ToString($provider.ComputeHash([Text.Encoding]::UTF8.GetBytes(
            [string]::Join("`r`n", $lines)+"`r`n"))).Replace('-','').ToLowerInvariant() }
        finally { $provider.Dispose() }
    }
    $expectedAggregate = Aggregate $ordered 'expectedSha256'
    if ($manifest.sha256 -cne $expectedAggregate) { throw 'Frozen manifest aggregate does not match its entries.' }
    $actualAggregate = Aggregate $ordered 'actualSha256'
    $mismatches = @($entries | Where-Object { !$_.matches })
    [pscustomobject]@{
        count=$entries.Count; matched=($entries.Count-$mismatches.Count)
        expectedAggregateSha256=$expectedAggregate; actualAggregateSha256=$actualAggregate
        unchanged=($mismatches.Count -eq 0 -and $actualAggregate -ceq $expectedAggregate)
        mismatches=$mismatches
    }
}

function Assert-FrozenIntegrity {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryRoot,
          [Parameter(Mandatory)][string]$ManifestPath)
    $report = Get-FrozenIntegrityReport -RepositoryRoot $RepositoryRoot -ManifestPath $ManifestPath
    if (!$report.unchanged) { throw ('Frozen bytes differ: '+($report.mismatches.path -join ', ')) }
    $report
}
