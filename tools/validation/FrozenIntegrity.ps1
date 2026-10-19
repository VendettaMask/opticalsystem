function Resolve-FrozenEvidencePath {
    param([string]$RepositoryRoot, [string]$RelativePath)
    # Require portable, unambiguous repository paths on every CI platform.
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath) -or
        $RelativePath -match '[\\:\r\n]' -or @($RelativePath.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count) {
        throw 'Invalid frozen repository-relative path.'
    }
    $prefix = $RepositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $path = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $RelativePath))
    if (!$path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Frozen path escapes repository.' }
    # Reject junctions/symlinks rather than hashing an unrelated target.
    $ancestor = $path
    while ($ancestor -and $ancestor.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        if (Test-Path -LiteralPath $ancestor) {
            $item = Get-Item -LiteralPath $ancestor -Force -ErrorAction Stop
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Frozen path contains a link.' }
        }
        $ancestor = Split-Path -Parent $ancestor
    }
    $path
}

function Get-FrozenAggregate {
    param($Rows, [string]$Property)
    $ordered = @($Rows)
    [Array]::Sort($ordered, [Collections.Generic.Comparer[object]]::Create({ param($a,$b) [StringComparer]::Ordinal.Compare($a.path,$b.path) }))
    $lines = [string[]]@($ordered | ForEach-Object { $_.path+'='+$_.$Property })
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(
        [string]::Join("`r`n", $lines)+"`r`n"))).ToLowerInvariant()
}

function Get-FrozenIntegrityPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryRoot,
          [Parameter(Mandatory)][string]$ManifestPath,
          [string]$RelocationMapPath,
          [string]$ExpectedRelocationMapSha256)
    $repository = (Resolve-Path -LiteralPath $RepositoryRoot -ErrorAction Stop).Path
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding utf8 -ErrorAction Stop | ConvertFrom-Json
    $files = @($manifest.files)
    if ($files.Count -eq 0 -or $files.Count -ne $manifest.count) { throw 'Frozen manifest count differs or is empty.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $byPath = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $entries = [Collections.Generic.List[object]]::new()
    foreach ($file in $files) {
        if ($file.sha256 -cnotmatch '^[0-9a-f]{64}$') { throw 'Invalid frozen manifest hash.' }
        $path = Resolve-FrozenEvidencePath $repository $file.path
        if (!$seen.Add($path)) { throw 'Frozen manifest path is duplicated.' }
        $entry = [pscustomobject]@{path=$file.path; sha256=$file.sha256; resolvedPath=$file.path}
        $entries.Add($entry)
        $byPath.Add($file.path, $entry)
    }
    # Preserve the published identity: ORIGINAL ordinal paths/case, lowercase
    # hashes, CRLF separators and terminal CRLF. Never normalize file bytes.
    $expectedAggregate = Get-FrozenAggregate $entries.ToArray() 'sha256'
    if ($manifest.sha256 -cne $expectedAggregate) { throw 'Frozen manifest aggregate does not match its entries.' }
    $mapIdentity = $null
    if ([bool]$RelocationMapPath -ne [bool]$ExpectedRelocationMapSha256) { throw 'A relocation map requires its pinned SHA-256.' }
    if ($RelocationMapPath) {
        if ($ExpectedRelocationMapSha256 -cnotmatch '^[0-9a-f]{64}$') { throw 'Invalid relocation map identity.' }
        $mapPath = Resolve-FrozenEvidencePath $repository $RelocationMapPath
        $mapHash = (Get-FileHash -LiteralPath $mapPath -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
        if ($mapHash -cne $ExpectedRelocationMapSha256) { throw 'Relocation map differs from its pinned identity.' }
        $map = Get-Content -LiteralPath $mapPath -Raw -Encoding utf8 -ErrorAction Stop | ConvertFrom-Json
        $relocations = @($map.relocations)
        if ($map.schemaVersion -ne 1 -or $map.manifestAggregateSha256 -cne $expectedAggregate -or
            $relocations.Count -eq 0 -or $map.count -ne $relocations.Count) { throw 'Invalid relocation map manifest identity or count.' }
        $mapped = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($relocation in $relocations) {
            if (!$byPath.ContainsKey([string]$relocation.path) -or !$mapped.Add($relocation.path)) { throw 'Unknown or duplicate relocation source.' }
            $entry = $byPath[$relocation.path]
            if ($relocation.sha256 -cne $entry.sha256) { throw 'A relocation cannot change the frozen hash.' }
            [void](Resolve-FrozenEvidencePath $repository $relocation.resolvedPath)
            if ($relocation.resolvedPath -ieq $entry.path -or (Test-Path -LiteralPath (Join-Path $repository $entry.path))) {
                throw 'A relocation cannot hide an existing original path.'
            }
            $entry.resolvedPath = $relocation.resolvedPath
        }
        $seen.Clear()
        foreach ($entry in $entries) {
            if (!$seen.Add($entry.resolvedPath)) { throw 'Frozen destinations collide.' }
        }
        $mapIdentity = [pscustomobject]@{path=$RelocationMapPath; sha256=$mapHash; count=$relocations.Count}
    }
    [pscustomobject]@{repository=$repository; entries=$entries.ToArray();
        expectedAggregateSha256=$expectedAggregate; relocationMap=$mapIdentity}
}

function Get-FrozenWorkingIntegrityReport {
    param([Parameter(Mandatory)]$Plan)
    $entries = [Collections.Generic.List[object]]::new()
    foreach ($entry in $Plan.entries) {
        $path = Join-Path $Plan.repository $entry.resolvedPath
        $actual = $null
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
        }
        $entries.Add([pscustomobject]@{path=$entry.path; resolvedPath=$entry.resolvedPath;
            expectedSha256=$entry.sha256; actualSha256=$actual; matches=($actual -ceq $entry.sha256)})
    }
    $actualAggregate = Get-FrozenAggregate $entries.ToArray() 'actualSha256'
    $mismatches = @($entries | Where-Object { !$_.matches })
    [pscustomobject]@{
        count=$entries.Count; matched=($entries.Count-$mismatches.Count)
        expectedAggregateSha256=$Plan.expectedAggregateSha256; actualAggregateSha256=$actualAggregate
        unchanged=($mismatches.Count -eq 0 -and $actualAggregate -ceq $Plan.expectedAggregateSha256)
        mismatches=$mismatches; relocationMap=$Plan.relocationMap
        relocations=@($entries | Where-Object { $_.path -cne $_.resolvedPath })
    }
}

function Get-FrozenIntegrityReport {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryRoot,
          [Parameter(Mandatory)][string]$ManifestPath,
          [string]$RelocationMapPath,
          [string]$ExpectedRelocationMapSha256)
    $plan = Get-FrozenIntegrityPlan @PSBoundParameters
    Get-FrozenWorkingIntegrityReport $plan
}

function Assert-FrozenIntegrity {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryRoot,
          [Parameter(Mandatory)][string]$ManifestPath,
          [string]$RelocationMapPath,
          [string]$ExpectedRelocationMapSha256)
    $report = Get-FrozenIntegrityReport @PSBoundParameters
    if (!$report.unchanged) { throw ('Frozen bytes differ: '+($report.mismatches.path -join ', ')) }
    $report
}
