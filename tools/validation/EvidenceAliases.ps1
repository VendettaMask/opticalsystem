#requires -Version 7.0
param(
    [ValidateSet('Verify','Materialize')][string]$Mode = 'Verify',
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '../..'),
    [string]$ManifestPath = 'validation/evidence-aliases.json',
    [string]$DestinationRoot,
    [switch]$FixtureLayout,
    [string]$ProjectPath,
    [string]$PathPrefix,
    [switch]$FetchLfs,
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'

function Restore-EvidenceLfsPayloads($Root, $Paths) {
    $needed = [Collections.Generic.List[string]]::new()
    foreach ($path in @($Paths | Sort-Object -Unique)) {
        $full = Resolve-EvidenceMember $Root $path
        $file = [IO.FileInfo]::new($full)
        if ($file.Exists -and $file.Length -lt 1024 -and
            [IO.File]::ReadAllText($full).StartsWith('version https://git-lfs.github.com/spec/v1', [StringComparison]::Ordinal)) {
            if ($path.Contains(',')) { throw 'Fetch the required LFS payload explicitly; its filename contains a comma.' }
            $needed.Add($path)
        }
    }
    if ($needed.Count) {
        & git -C $Root lfs pull ('--include=' + ($needed -join ',')) '--exclude='
        if ($LASTEXITCODE -ne 0) { throw 'Required canonical LFS payload download failed.' }
    }
}

function Resolve-EvidenceMember($Root, $RelativePath, [switch]$LexicalOnly) {
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
    if ($LexicalOnly) { return $absolute }
    $ancestor = $absolute
    while ($ancestor -and ($ancestor -eq $boundary -or $ancestor.StartsWith(
        $boundary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))) {
        if ([IO.File]::Exists($ancestor) -or [IO.Directory]::Exists($ancestor)) {
            if (([IO.File]::GetAttributes($ancestor) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Evidence paths cannot traverse links.'
            }
        }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
    $absolute
}

function Get-EvidenceAliasPlan([string]$Root, [string]$Manifest = 'validation/evidence-aliases.json', [switch]$MetadataOnly) {
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
        $canonical = Resolve-EvidenceMember $repository $group.canonical -LexicalOnly:$MetadataOnly
        if (!$allPaths.Add([string]$group.canonical)) { throw 'Duplicate or chained evidence path.' }
        if (!$MetadataOnly) {
            $file = [IO.FileInfo]::new($canonical)
            if (!$file.Exists -or $file.Length -ne $group.bytes -or
                (Get-FileHash -LiteralPath $canonical).Hash.ToLowerInvariant() -cne $group.sha256) {
                throw ('Canonical evidence bytes changed: ' + $group.canonical)
            }
        }
        foreach ($alias in $group.aliases) {
            $null = Resolve-EvidenceMember $repository $alias -LexicalOnly
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
    foreach ($path in @($data.scanRoots) + @($data.scanFiles) + @($data.scanTrackedRoots | Where-Object { $_ })) { $null = Resolve-EvidenceMember $repository $path }
    [pscustomobject]@{repository=$repository; manifest=$data; manifestPath=$manifestFile; aliases=$aliases.ToArray()}
}

function Test-EvidenceAliasPlan($Plan) {
    foreach ($alias in $Plan.aliases) {
        $aliasPath = Resolve-EvidenceMember $Plan.repository $alias.path -LexicalOnly
        if ([IO.File]::Exists($aliasPath) -or [IO.Directory]::Exists($aliasPath)) {
            throw ('Duplicate evidence alias reintroduced: ' + $alias.path)
        }
    }
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($prefix in $Plan.manifest.scanRoots) {
        $scanRoot = Resolve-EvidenceMember $Plan.repository $prefix
        foreach ($file in $(if (Test-Path -LiteralPath $scanRoot) { Get-ChildItem -LiteralPath $scanRoot -File -Recurse })) {
            [void]$paths.Add([IO.Path]::GetRelativePath($Plan.repository, $file.FullName).Replace('\','/'))
        }
    }
    foreach ($path in $Plan.manifest.scanFiles) { [void]$paths.Add([string]$path) }
    if ($Plan.manifest.scanTrackedRoots) {
        $trackedPaths = @(& git -C $Plan.repository -c core.quotepath=false ls-files --cached --others --exclude-standard)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate managed evidence.' }
        foreach ($path in $trackedPaths) {
            if ([IO.Path]::GetExtension($path) -in @($Plan.manifest.scanExcludeExtensions)) { continue }
            if (@($Plan.manifest.scanTrackedRoots | Where-Object { $path.StartsWith($_ + '/', [StringComparison]::Ordinal) }).Count -eq 0) { continue }
            if ([IO.File]::Exists([IO.Path]::Combine($Plan.repository, $path))) { [void]$paths.Add($path) }
        }
    }
    $files = @($paths | ForEach-Object {
        $full = Resolve-EvidenceMember $Plan.repository $_
        [pscustomobject]@{path=$_; full=$full; bytes=([IO.FileInfo]::new($full)).Length}
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

function Restore-EvidenceAliasPlan($Plan, [string]$Destination, [switch]$AsFixtures, [string]$Project, [string]$SelectionPrefix, [switch]$FetchLargeFiles) {
    if ($SelectionPrefix) { $null = Resolve-EvidenceMember $Plan.repository $SelectionPrefix }
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
    foreach ($prefix in @($Plan.manifest.scanRoots) + @($Plan.manifest.scanTrackedRoots | Where-Object { $_ })) {
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
    if ($AsFixtures -and $Project) {
        $projectFile = [IO.Path]::GetFullPath($Project)
        [xml]$projectXml = Get-Content -LiteralPath $projectFile -Raw
        $projectRoot = Split-Path -Parent $projectFile
        foreach ($item in $projectXml.Project.ItemGroup.None) {
            $include = [string]$item.Include
            $link = [string]$item.Link
            if (!$include -or !$link) { continue }
            foreach ($part in $include.Split(';')) {
                $absolutePattern = [IO.Path]::GetFullPath((Join-Path $projectRoot $part.Replace('\','/'))).Replace('\','/')
                $pattern = [IO.Path]::GetRelativePath($Plan.repository, $absolutePattern).Replace('\','/')
                if ($pattern.StartsWith('../')) { continue }
                $escaped = [regex]::Escape($pattern).Replace('\*\*/','(?<recursive>(?:.*/)?)').Replace('\*','[^/]*').Replace('\?','[^/]')
                foreach ($alias in $Plan.aliases) {
                    $match = [regex]::Match($alias.path, '^' + $escaped + '$', [Text.RegularExpressions.RegexOptions]::CultureInvariant)
                    if (!$match.Success) { continue }
                    $target = $link.Replace('\','/').Replace('%(RecursiveDir)', $match.Groups['recursive'].Value)
                    $target = $target.Replace('%(Filename)', [IO.Path]::GetFileNameWithoutExtension($alias.path)).Replace('%(Extension)', [IO.Path]::GetExtension($alias.path))
                    Add-RestoreEntry $alias.canonical $target $alias.sha256
                }
            }
        }
    } elseif ($AsFixtures) {
        foreach ($dataset in $Plan.manifest.datasets) {
            $sourceRoot = Resolve-EvidenceMember $repository $dataset.sourcePrefix
            foreach ($file in $(if (Test-Path -LiteralPath $sourceRoot) { Get-ChildItem -LiteralPath $sourceRoot -File -Recurse })) {
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
            $scanRoot = Resolve-EvidenceMember $repository $prefix
            foreach ($file in $(if (Test-Path -LiteralPath $scanRoot) { Get-ChildItem -LiteralPath $scanRoot -File -Recurse })) {
                [void]$retained.Add([IO.Path]::GetRelativePath($repository, $file.FullName).Replace('\','/'))
            }
        }
        foreach ($path in $Plan.manifest.scanFiles) { [void]$retained.Add([string]$path) }
        if ($Plan.manifest.scanTrackedRoots) {
            foreach ($path in @(& git -C $Plan.repository -c core.quotepath=false ls-files --cached --others --exclude-standard)) {
                if (@($Plan.manifest.scanTrackedRoots | Where-Object { $path.StartsWith($_ + '/', [StringComparison]::Ordinal) }).Count -and
                    (Test-Path -LiteralPath (Join-Path $Plan.repository $path) -PathType Leaf)) { [void]$retained.Add($path) }
            }
            if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate historical context.' }
        }
        foreach ($path in $retained) {
            if ($SelectionPrefix -and $path -cne $SelectionPrefix -and !$path.StartsWith($SelectionPrefix + '/', [StringComparison]::Ordinal)) { continue }
            Add-RestoreEntry $path $path ((Get-FileHash -LiteralPath (Resolve-EvidenceMember $repository $path)).Hash.ToLowerInvariant())
        }
        foreach ($alias in $Plan.aliases) {
            if ($SelectionPrefix -and $alias.path -cne $SelectionPrefix -and !$alias.path.StartsWith($SelectionPrefix + '/', [StringComparison]::Ordinal)) { continue }
            Add-RestoreEntry $alias.canonical $alias.path $alias.sha256
        }
    }
    # Validate every source and destination before writing any generated files.
    if ($FetchLargeFiles) { Restore-EvidenceLfsPayloads $repository @($entries.Values.source) }
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
            if (!$IsWindows -and @($Plan.manifest.groups | Where-Object {
                ($_.canonical -ceq $entry.target -and $_.canonicalExecutable) -or $entry.target -in @($_.executableAliases)
            }).Count) { [IO.File]::SetUnixFileMode($target, [IO.UnixFileMode]493) }
        } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
    }
    [pscustomobject]@{materializedFiles=$entries.Count; fixtureLayout=[bool]$AsFixtures; destination=$destinationPath;
        manifestSha256=(Get-FileHash -LiteralPath $Plan.manifestPath).Hash.ToLowerInvariant(); valid=$true}
}

if ($MyInvocation.InvocationName -ne '.') {
    if ($FetchLfs -and $Mode -eq 'Verify') {
        $metadata = Get-EvidenceAliasPlan $RepositoryRoot $ManifestPath -MetadataOnly
        Restore-EvidenceLfsPayloads $metadata.repository @($metadata.manifest.groups.canonical)
    }
    $plan = Get-EvidenceAliasPlan $RepositoryRoot $ManifestPath -MetadataOnly:($Mode -eq 'Materialize')
    $result = if ($Mode -eq 'Verify') { Test-EvidenceAliasPlan $plan }
        else { Restore-EvidenceAliasPlan $plan $DestinationRoot -AsFixtures:$FixtureLayout -Project $ProjectPath -SelectionPrefix $PathPrefix -FetchLargeFiles:$FetchLfs }
    $json = $result | ConvertTo-Json -Depth 5
    if ($OutputPath) {
        $parent = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OutputPath))
        [IO.Directory]::CreateDirectory($parent) | Out-Null
        [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $json, [Text.UTF8Encoding]::new($false))
    }
    $json
}
