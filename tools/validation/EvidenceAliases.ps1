#requires -Version 7.0
param(
    [ValidateSet('Verify','Materialize')][string]$Mode = 'Verify',
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '../..'),
    [string]$ManifestPath = 'validation/evidence-aliases.json',
    [string]$DestinationRoot,
    [switch]$FixtureLayout,
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'

function Resolve-EvidenceMember($Root, $RelativePath) {
    if ($RelativePath -isnot [string] -or [string]::IsNullOrWhiteSpace($RelativePath) -or
        [IO.Path]::IsPathRooted($RelativePath) -or $RelativePath -match '[\\:\r\n]' -or
        @($RelativePath.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count) {
        throw 'Evidence path must be an unambiguous repository-relative path.'
    }
    $boundary = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $absolute = [IO.Path]::GetFullPath((Join-Path $boundary $RelativePath))
    if (!$absolute.StartsWith($boundary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Evidence path escapes its root.'
    }
    $ancestor = $absolute
    while ($ancestor -and ($ancestor -eq $boundary -or $ancestor.StartsWith(
        $boundary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))) {
        if (Test-Path -LiteralPath $ancestor) {
            if (((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Evidence paths cannot traverse links.'
            }
        }
        $ancestor = Split-Path -Parent $ancestor
    }
    $absolute
}

function Get-EvidenceAliasPlan([string]$Root, [string]$Manifest = 'validation/evidence-aliases.json') {
    $repository = (Resolve-Path -LiteralPath $Root).Path
    $manifestFile = Resolve-EvidenceMember $repository $Manifest
    $data = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
    if ($data.schemaVersion -ne 1 -or @($data.groups).Count -eq 0) { throw 'Invalid evidence alias schema.' }
    $allPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $hashes = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $aliases = [Collections.Generic.List[object]]::new()
    foreach ($group in $data.groups) {
        if ($group.sha256 -cnotmatch '^[0-9a-f]{64}$' -or $group.bytes -le 0 -or
            @($group.aliases).Count -eq 0 -or !$hashes.Add([string]$group.sha256)) { throw 'Invalid or repeated evidence group.' }
        $canonical = Resolve-EvidenceMember $repository $group.canonical
        if (!$allPaths.Add([string]$group.canonical)) { throw 'Duplicate or chained evidence path.' }
        $file = Get-Item -LiteralPath $canonical
        if ($file.PSIsContainer -or $file.Length -ne $group.bytes -or
            (Get-FileHash -LiteralPath $canonical).Hash.ToLowerInvariant() -cne $group.sha256) {
            throw ('Canonical evidence bytes changed: ' + $group.canonical)
        }
        foreach ($alias in $group.aliases) {
            $null = Resolve-EvidenceMember $repository $alias
            if (!$allPaths.Add([string]$alias)) { throw 'Duplicate or chained evidence path.' }
            $aliases.Add([pscustomobject]@{path=$alias; canonical=$group.canonical; sha256=$group.sha256; bytes=$group.bytes})
        }
    }
    $fixtureNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $sourceNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($dataset in $data.datasets) {
        $null = Resolve-EvidenceMember $repository $dataset.sourcePrefix
        $null = Resolve-EvidenceMember $repository $dataset.fixturePrefix
        if (!$fixtureNames.Add([string]$dataset.fixturePrefix) -or !$sourceNames.Add([string]$dataset.sourcePrefix)) {
            throw 'Repeated fixture mapping.'
        }
    }
    foreach ($path in @($data.scanRoots) + @($data.scanFiles)) { $null = Resolve-EvidenceMember $repository $path }
    [pscustomobject]@{repository=$repository; manifest=$data; manifestPath=$manifestFile; aliases=$aliases.ToArray()}
}

function Test-EvidenceAliasPlan($Plan) {
    foreach ($alias in $Plan.aliases) {
        if (Test-Path -LiteralPath (Resolve-EvidenceMember $Plan.repository $alias.path)) {
            throw ('Duplicate evidence alias reintroduced: ' + $alias.path)
        }
    }
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($prefix in $Plan.manifest.scanRoots) {
        foreach ($file in Get-ChildItem -LiteralPath (Resolve-EvidenceMember $Plan.repository $prefix) -File -Recurse) {
            [void]$paths.Add([IO.Path]::GetRelativePath($Plan.repository, $file.FullName).Replace('\','/'))
        }
    }
    foreach ($path in $Plan.manifest.scanFiles) { [void]$paths.Add([string]$path) }
    $files = @($paths | ForEach-Object {
        $full = Resolve-EvidenceMember $Plan.repository $_
        [pscustomobject]@{path=$_; full=$full; bytes=(Get-Item -LiteralPath $full).Length}
    })
    $duplicates = @($files | Where-Object bytes -gt 0 | Group-Object bytes | Where-Object Count -gt 1 |
        ForEach-Object { foreach ($file in $_.Group) {
            [pscustomobject]@{path=$file.path; sha256=(Get-FileHash -LiteralPath $file.full).Hash}
        } } | Group-Object sha256 | Where-Object Count -gt 1)
    if ($duplicates.Count) { throw ('Unmapped duplicate evidence: ' + (($duplicates.Group.path) -join ', ')) }
    [pscustomobject]@{groups=@($Plan.manifest.groups).Count; aliases=$Plan.aliases.Count;
        removedDuplicateBytes=($Plan.aliases | Measure-Object -Property bytes -Sum).Sum;
        scannedFiles=$files.Count; nonEmptyDuplicateGroups=$duplicates.Count;
        manifestSha256=(Get-FileHash -LiteralPath $Plan.manifestPath).Hash.ToLowerInvariant(); valid=$true}
}

function Restore-EvidenceAliasPlan($Plan, [string]$Destination, [switch]$AsFixtures) {
    if ([string]::IsNullOrWhiteSpace($Destination)) { throw 'An explicit generated destination is required.' }
    $destinationPath = [IO.Path]::GetFullPath($Destination).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $repository = $Plan.repository.TrimEnd([IO.Path]::DirectorySeparatorChar)
    $ancestor = $destinationPath
    while ($ancestor) {
        if ((Test-Path -LiteralPath $ancestor) -and
            (((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw 'Generated destination cannot traverse links.'
        }
        $ancestor = Split-Path -Parent $ancestor
    }
    if ($destinationPath -eq $repository -or $repository.StartsWith(
        $destinationPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Cannot materialize aliases into the repository or an ancestor.'
    }
    foreach ($prefix in $Plan.manifest.scanRoots) {
        $sourceRoot = Resolve-EvidenceMember $repository $prefix
        if ($destinationPath -eq $sourceRoot -or $destinationPath.StartsWith(
            $sourceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Generated output must be separate from captured evidence.'
        }
    }
    $entries = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
    function Add-RestoreEntry([string]$Source, [string]$Target, [string]$Hash) {
        $null = Resolve-EvidenceMember $destinationPath $Target
        if ($entries.ContainsKey($Target)) { throw 'Colliding materialized evidence paths.' }
        $entries.Add($Target, [pscustomobject]@{source=$Source; target=$Target; sha256=$Hash})
    }
    if ($AsFixtures) {
        foreach ($dataset in $Plan.manifest.datasets) {
            $sourceRoot = Resolve-EvidenceMember $repository $dataset.sourcePrefix
            foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -File -Recurse) {
                $source = [IO.Path]::GetRelativePath($repository, $file.FullName).Replace('\','/')
                $tail = [IO.Path]::GetRelativePath($sourceRoot, $file.FullName).Replace('\','/')
                Add-RestoreEntry $source ($dataset.fixturePrefix + '/' + $tail) ((Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant())
            }
            foreach ($alias in $Plan.aliases | Where-Object { $_.path.StartsWith($dataset.sourcePrefix + '/', [StringComparison]::Ordinal) }) {
                $tail = $alias.path.Substring($dataset.sourcePrefix.Length + 1)
                Add-RestoreEntry $alias.canonical ($dataset.fixturePrefix + '/' + $tail) $alias.sha256
            }
        }
    } else {
        # Recreate a self-contained historical view, including retained context.
        $retained = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($prefix in $Plan.manifest.scanRoots) {
            foreach ($file in Get-ChildItem -LiteralPath (Resolve-EvidenceMember $repository $prefix) -File -Recurse) {
                [void]$retained.Add([IO.Path]::GetRelativePath($repository, $file.FullName).Replace('\','/'))
            }
        }
        foreach ($path in $Plan.manifest.scanFiles) { [void]$retained.Add([string]$path) }
        foreach ($path in $retained) {
            Add-RestoreEntry $path $path ((Get-FileHash -LiteralPath (Resolve-EvidenceMember $repository $path)).Hash.ToLowerInvariant())
        }
        foreach ($alias in $Plan.aliases) { Add-RestoreEntry $alias.canonical $alias.path $alias.sha256 }
    }
    # Validate every source and destination before writing any generated files.
    foreach ($entry in $entries.Values) {
        $null = Resolve-EvidenceMember $repository $entry.source
        $null = Resolve-EvidenceMember $destinationPath $entry.target
    }
    foreach ($entry in $entries.Values) {
        $target = Resolve-EvidenceMember $destinationPath $entry.target
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        $temporary = $target + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
        try {
            [IO.File]::Copy((Resolve-EvidenceMember $repository $entry.source), $temporary, $false)
            if ((Get-FileHash -LiteralPath $temporary).Hash.ToLowerInvariant() -cne $entry.sha256) { throw 'Materialized evidence hash mismatch.' }
            [IO.File]::Move($temporary, $target, $true)
        } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
    }
    [pscustomobject]@{materializedFiles=$entries.Count; fixtureLayout=[bool]$AsFixtures; destination=$destinationPath;
        manifestSha256=(Get-FileHash -LiteralPath $Plan.manifestPath).Hash.ToLowerInvariant(); valid=$true}
}

if ($MyInvocation.InvocationName -ne '.') {
    $plan = Get-EvidenceAliasPlan $RepositoryRoot $ManifestPath
    $result = if ($Mode -eq 'Verify') { Test-EvidenceAliasPlan $plan }
        else { Restore-EvidenceAliasPlan $plan $DestinationRoot -AsFixtures:$FixtureLayout }
    $json = $result | ConvertTo-Json -Depth 5
    if ($OutputPath) {
        $parent = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OutputPath))
        [IO.Directory]::CreateDirectory($parent) | Out-Null
        [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $json, [Text.UTF8Encoding]::new($false))
    }
    $json
}
