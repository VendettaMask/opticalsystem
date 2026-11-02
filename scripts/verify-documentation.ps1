#requires -Version 7.0
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputPath,
    [ValidatePattern('^\d{4}-\d{2}-\d{2}$')][string]$AuditDate = (Get-Date -Format 'yyyy-MM-dd'),
    [switch]$Strict
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($RepositoryRoot)
Push-Location $root
try {
    $paths = @(git -c core.quotepath=false ls-files --cached --others --exclude-standard | Sort-Object -Unique | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate repository files.' }
    $tracked = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $paths) { [void]$tracked.Add($path.Replace('\', '/')) }
    $documents = @($paths | Where-Object { $_ -match '\.(md|mdx|rst|txt|html|pdf|docx|odt)$' -or $_ -match '(^|/)(LICENSE|COPYING|NOTICE)(\.|$)' })
    $inventory = [Collections.Generic.List[object]]::new()
    $links = [Collections.Generic.List[object]]::new()
    $fenceIssues = [Collections.Generic.List[object]]::new()
    $paragraphs = [Collections.Generic.List[object]]::new()
    $anchorCache = @{}
    $lifecycleIssues = [Collections.Generic.List[object]]::new()
    $activePlans = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $historicalDocuments = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $lifecyclePath = Join-Path $root 'docs/documentation-lifecycle.json'
    $lifecycleHash = $null
    if (Test-Path -LiteralPath $lifecyclePath) {
        $lifecycle = Get-Content -LiteralPath $lifecyclePath -Raw | ConvertFrom-Json
        if ($lifecycle.SchemaVersion -ne 1) { throw 'Unsupported documentation lifecycle schema.' }
        $lifecycleHash = (Get-FileHash -LiteralPath $lifecyclePath -Algorithm SHA256).Hash.ToLowerInvariant()
        function Resolve-LifecyclePath([string]$Path) {
            if ([string]::IsNullOrWhiteSpace($Path) -or [IO.Path]::IsPathRooted($Path)) { throw 'Lifecycle paths must be repository-relative.' }
            $relative = [IO.Path]::GetRelativePath($root, [IO.Path]::GetFullPath((Join-Path $root $Path))).Replace('\', '/')
            if ($relative -eq '..' -or $relative.StartsWith('../')) { throw 'Lifecycle path leaves repository.' }
            return $relative
        }
        foreach ($path in @($lifecycle.ActivePlans | Where-Object { $_ })) {
            $relative = Resolve-LifecyclePath $path; [void]$activePlans.Add($relative)
            if (-not (Test-Path -LiteralPath (Join-Path $root $relative) -PathType Leaf)) { $lifecycleIssues.Add([pscustomobject]@{ Path=$relative; Issue='MissingActivePlan' }) }
        }
        foreach ($path in @($lifecycle.HistoricalDocuments | Where-Object { $_ })) {
            $relative = Resolve-LifecyclePath $path; [void]$historicalDocuments.Add($relative)
            if ($activePlans.Contains($relative)) { $lifecycleIssues.Add([pscustomobject]@{ Path=$relative; Issue='ConflictingLifecycle' }) }
            if (-not (Test-Path -LiteralPath (Join-Path $root $relative) -PathType Leaf)) { $lifecycleIssues.Add([pscustomobject]@{ Path=$relative; Issue='MissingHistoricalDocument' }) }
        }
        foreach ($entry in @($lifecycle.RetiredDocuments | Where-Object { $_ })) {
            $relative = Resolve-LifecyclePath $entry.Path; $replacement = Resolve-LifecyclePath $entry.Replacement
            if (Test-Path -LiteralPath (Join-Path $root $relative)) { $lifecycleIssues.Add([pscustomobject]@{ Path=$relative; Issue='RetiredDocumentPresent' }) }
            if (-not (Test-Path -LiteralPath (Join-Path $root $replacement) -PathType Leaf)) { $lifecycleIssues.Add([pscustomobject]@{ Path=$replacement; Issue='MissingReplacement' }) }
        }
    }

    function Get-Category([string]$Path) {
        if ($activePlans.Contains($Path)) { return 'Guide' }
        if ($historicalDocuments.Contains($Path)) { return 'Historical' }
        if ($Path -match '(?i)(LICENSE|COPYING|NOTICE|OFL)' -or $Path -match '^third_party/' -or $Path -eq 'packaging/windows/inno/SOURCE.md') { return 'ThirdParty' }
        if ($Path -match '^(artifacts/|validation/|labs/InitialStructure/benchmarks/|output/|reports/)') { return 'Evidence' }
        if ($Path -match '^docs/history/' -or $Path -match '^tools/diagnostics/' -or $Path -match '^docs/(ARCHITECTURE_CONVERGENCE_PLAN|INITIAL_STRUCTURE_FROM_FLAT_PROPOSAL|PYTHON_[A-Z_]+_REMOVAL|UI_DESIGN_REVIEW|ZEMAX_APODIZATION_LAYOUT_FIX|ZEMAX_38668_WORKING_F_NUMBER|ZEMAX_COMPARISON_TOOL|FFT_PSF_STOP_AIMING)\.md$' -or $Path -match '^docs/.*\d{4}-\d{2}-\d{2}\.md$' -and $Path -notmatch '(BUILT_IN_MODULES|WINDOWS_INSTALLATION_UPGRADE|OPEN_ISSUES)') { return 'Historical' }
        return 'Guide'
    }

    function Get-MarkdownProse([string]$Content) {
        $lines = [Collections.Generic.List[string]]::new()
        $fenceCharacter = $null
        $fenceLength = 0
        foreach ($line in ($Content -split '\r?\n')) {
            $marker = [regex]::Match($line, '^ {0,3}(?<fence>`{3,}|~{3,})(?<rest>.*)$')
            if ($marker.Success) {
                $fence = $marker.Groups['fence'].Value
                $rest = $marker.Groups['rest'].Value
                if (-not $fenceCharacter -and ($fence[0] -ne '`' -or -not $rest.Contains('`'))) {
                    $fenceCharacter = $fence[0]; $fenceLength = $fence.Length
                    $lines.Add(''); continue
                }
                if ($fenceCharacter -and $fence[0] -eq $fenceCharacter -and $fence.Length -ge $fenceLength -and -not $rest.Trim()) {
                    $fenceCharacter = $null; $fenceLength = 0
                    $lines.Add(''); continue
                }
            }
            if ($fenceCharacter -or $line -match '^(?: {4}|\t)') { $lines.Add('') } else { $lines.Add($line) }
        }
        [pscustomobject]@{ Text=[string]::Join("`n", $lines); Lines=$lines.ToArray(); UnclosedFence=($null -ne $fenceCharacter) }
    }

    function Get-Anchors([string]$File) {
        if ($anchorCache.ContainsKey($File)) { return $anchorCache[$File] }
        $anchors = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $counts = @{}
        $prose = Get-MarkdownProse ([IO.File]::ReadAllText($File))
        foreach ($line in $prose.Lines) {
            if ($line -match '^#{1,6}\s+(.+?)\s*#*\s*$') {
                $title = [regex]::Replace($Matches[1], '\[([^\]]+)\]\([^)]*\)', '$1')
                $slug = [regex]::Replace($title.ToLowerInvariant(), '[^\p{L}\p{N}\p{M}_\-\s]', '') -replace '\s', '-'
                if ($counts.ContainsKey($slug)) { $counts[$slug]++; $slug += '-' + $counts[$slug] } else { $counts[$slug] = 0 }
                [void]$anchors.Add($slug)
            }
            foreach ($match in [regex]::Matches($line, '(?:id|name)=["'']([^"'']+)["'']')) { [void]$anchors.Add($match.Groups[1].Value) }
        }
        $anchorCache[$File] = $anchors
        return ,$anchors
    }

    function Get-SrcsetTargets([string]$Value) {
        while ($Value) {
            $Value = $Value.TrimStart([char[]]" `t`r`n,")
            $item = [regex]::Match($Value, '^(?<url>\S+)(?<tail>[\s\S]*)$')
            if (-not $item.Success) { break }
            $url = $item.Groups['url'].Value; $tail = $item.Groups['tail'].Value
            $url.TrimEnd(',')
            if ($url.EndsWith(',')) { $Value = $tail }
            else { $comma = $tail.IndexOf(','); $Value = if ($comma -ge 0) { $tail.Substring($comma + 1) } else { '' } }
        }
    }

    foreach ($path in $documents) {
        $category = Get-Category $path
        $inventory.Add([pscustomobject]@{ Path=$path; Category=$category; Bytes=(Get-Item -LiteralPath $path).Length; Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() })
        if ($category -in @('ThirdParty', 'Evidence')) { continue }
        # Binary documents belong in the inventory, but Markdown parsing of
        # compressed PDF/Office bytes can cause false links and unbounded regex work.
        if ($path -match '\.(pdf|docx|odt)$') { continue }
        $content = [IO.File]::ReadAllText((Join-Path $root $path))
        $prose = Get-MarkdownProse $content
        foreach ($match in [regex]::Matches($prose.Text, '(?s)\S.*?(?=\n\s*\n|\z)')) {
            $paragraph = [regex]::Replace($match.Value.Trim(), '\s+', ' ')
            if ($paragraph.Length -ge 100 -and $paragraph -notmatch '^\|') {
                $paragraphLine = 1 + [regex]::Matches($prose.Text.Substring(0, $match.Index), '\n').Count
                $paragraphs.Add([pscustomobject]@{ Path=$path; Category=$category; Line=$paragraphLine; Text=$paragraph })
            }
        }
        $lineNumber = 0
        foreach ($line in $prose.Lines) {
            $lineNumber++
            $linkLine = [regex]::Replace($line, '`+[^`]*`+', '')
            $targets = [Collections.Generic.List[string]]::new()
            foreach ($match in [regex]::Matches($linkLine, '\[[^\]\r\n]*\]\((?<target><[^>]+>|[^\s)]+)(?:\s+"[^"]*")?\)')) { $targets.Add($match.Groups['target'].Value.Trim('<', '>')) }
            foreach ($match in [regex]::Matches($linkLine, '(?i)\b(?:href|src)\s*=\s*["''](?<target>[^"'']+)["'']')) { $targets.Add($match.Groups['target'].Value) }
            foreach ($match in [regex]::Matches($linkLine, '(?i)\bsrcset\s*=\s*["''](?<sources>[^"'']+)["'']')) {
                foreach ($candidate in @(Get-SrcsetTargets $match.Groups['sources'].Value)) { if ($candidate) { $targets.Add($candidate) } }
            }
            foreach ($target in @($targets | Sort-Object -Unique)) {
                if ($target -match '^[A-Za-z][A-Za-z0-9+.-]*:' -or $target.StartsWith('//')) { continue }
                $parts = $target -split '#', 2
                $filePart = [uri]::UnescapeDataString(($parts[0] -split '\?', 2)[0])
                $absolute = if ($filePart) { [IO.Path]::GetFullPath((Join-Path (Split-Path (Join-Path $root $path)) $filePart)) } else { Join-Path $root $path }
                $relative = [IO.Path]::GetRelativePath($root, $absolute).Replace('\', '/')
                $issue = $null
                if (-not (Test-Path -LiteralPath $absolute)) { $issue = 'MissingFile' }
                elseif ($parts.Count -gt 1 -and $parts[1] -and $absolute -match '\.md$') {
                    $anchor = [uri]::UnescapeDataString($parts[1])
                    if (-not (Get-Anchors $absolute).Contains($anchor)) { $issue = 'MissingAnchor' }
                }
                if (-not $issue -and -not $tracked.Contains($relative) -and (Test-Path -LiteralPath $absolute -PathType Leaf)) { $issue = 'LocalOnlyArtifact' }
                if ($issue) { $links.Add([pscustomobject]@{ Path=$path; Category=$category; Line=$lineNumber; Target=$target; Resolved=$relative; Issue=$issue }) }
            }
        }
        if ($prose.UnclosedFence) { $fenceIssues.Add([pscustomobject]@{ Path=$path; Issue='UnclosedCodeFence' }) }
    }
    $duplicates = @($paragraphs | Group-Object Text -CaseSensitive | Where-Object { @($_.Group.Path | Sort-Object -Unique).Count -ge 2 } | Sort-Object Count -Descending | ForEach-Object {
        $duplicatePaths = @($_.Group.Path | Sort-Object -Unique)
        $guidePaths = @($_.Group | Where-Object Category -eq 'Guide' | Select-Object -ExpandProperty Path -Unique)
        $duplicateScope = if ($guidePaths.Count -ge 2) { 'Guide' } elseif ($guidePaths.Count -eq 0) { 'Historical' } else { 'Mixed' }
        [pscustomobject]@{ Copies=$_.Count; Documents=$duplicatePaths.Count; Scope=$duplicateScope; Text=$_.Name; Paths=$duplicatePaths; Locations=@($_.Group | Select-Object Path,Category,Line) }
    })
    $guideDuplicates = @($duplicates | Where-Object Scope -eq 'Guide')
    $broken = @($links | Where-Object { $_.Issue -ne 'LocalOnlyArtifact' })
    $sourceCommit = git rev-parse --verify -q HEAD
    if ($LASTEXITCODE -ne 0) { $sourceCommit = $null }
    $report = [ordered]@{
        Date=$AuditDate; SourceCommit=$sourceCommit; SourceDirty=(@(git status --porcelain=v1).Count -gt 0); Scope='Versioned and non-ignored Markdown, RST, text, HTML, PDF, Word and license documents; evidence and third-party contents fingerprinted, not rewritten.'
        Documents=$inventory.Count; Categories=@($inventory | Group-Object Category | ForEach-Object { [pscustomobject]@{ Category=$_.Name; Count=$_.Count } })
        BrokenLinks=$broken; MarkdownFenceIssues=@($fenceIssues); LocalOnlyLinks=@($links | Where-Object Issue -eq 'LocalOnlyArtifact'); RepeatedParagraphs=$duplicates; GuideRepeatedParagraphs=$guideDuplicates; Inventory=$inventory
        LifecycleIssues=@($lifecycleIssues); LifecycleManifestSha256=$lifecycleHash; ActivePlans=@($activePlans | Sort-Object); HistoricalDocuments=@($historicalDocuments | Sort-Object)
        RepetitionPolicy=[ordered]@{ MinimumCharacters=100; MinimumDocuments=2; WhitespaceNormalized=$true; CodeAndTablesExcluded=$true; StrictRejects='Repeated prose in at least two current guides; historical/mixed groups remain reported.' }
        Limitations=@('Local Markdown links/headings and HTML href/src/srcset checked; remote URLs not fetched.', 'Binary PDF/Word/ODT documents are inventoried and hashed, not decoded as UTF-8 or scanned for Markdown syntax.', 'Exact normalized paragraphs only; thematic/near-duplicate content still needs editorial review.', 'Historical test results are preserved, not rerun or promoted to current acceptance.', 'Evidence and third-party prose is fingerprinted without rewriting by this checker; retirement still requires consumer and verified-archive review.')
    }
    if ($OutputPath) {
        $outputAbsolute = [IO.Path]::GetFullPath($OutputPath, $root)
        [void][IO.Directory]::CreateDirectory((Split-Path $outputAbsolute))
        [IO.File]::WriteAllText($outputAbsolute, ($report | ConvertTo-Json -Depth 12) + "`n", [Text.UTF8Encoding]::new($false))
    }
    [pscustomobject]@{ Documents=$inventory.Count; BrokenLinks=$broken.Count; GuideBrokenLinks=@($broken | Where-Object Category -eq 'Guide').Count; MarkdownFenceIssues=$fenceIssues.Count; LocalOnlyLinks=$report.LocalOnlyLinks.Count; RepeatedParagraphGroups=$duplicates.Count; GuideRepeatedParagraphGroups=$guideDuplicates.Count }
    if ($Strict -and $broken.Count -gt 0) { throw "$($broken.Count) documentation links require repair." }
    if ($Strict -and $fenceIssues.Count -gt 0) { throw "$($fenceIssues.Count) documentation code fences require repair." }
    if ($Strict -and $guideDuplicates.Count -gt 0) { throw "$($guideDuplicates.Count) repeated paragraph groups in current guides require consolidation." }
    if ($Strict -and $lifecycleIssues.Count -gt 0) { throw "$($lifecycleIssues.Count) documentation lifecycle issues require repair." }
} finally { Pop-Location }
