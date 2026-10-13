#requires -Version 7.0
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputPath,
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

    function Get-Category([string]$Path) {
        if ($Path -match '(?i)(LICENSE|COPYING|NOTICE|OFL)' -or $Path -match '^third_party/' -or $Path -eq 'packaging/windows/inno/SOURCE.md') { return 'ThirdParty' }
        if ($Path -match '^(artifacts/|validation/|labs/InitialStructure/benchmarks/|output/pdf/|reports/)') { return 'Evidence' }
        if ($Path -match '^docs/history/' -or $Path -match '^tools/diagnostics/' -or $Path -match '^docs/(ARCHITECTURE_CONVERGENCE_PLAN|INITIAL_STRUCTURE_FROM_FLAT_PROPOSAL|PYTHON_OPTILAND_REMOVAL|UI_DESIGN_REVIEW|ZEMAX_APODIZATION_LAYOUT_FIX|ZEMAX_38668_WORKING_F_NUMBER|FFT_PSF_STOP_AIMING)\.md$' -or $Path -match '^docs/.*\d{4}-\d{2}-\d{2}\.md$' -and $Path -notmatch '(BUILT_IN_MODULES|WINDOWS_INSTALLATION_UPGRADE|OPEN_ISSUES)') { return 'Historical' }
        return 'Guide'
    }

    function Get-Anchors([string]$File) {
        if ($anchorCache.ContainsKey($File)) { return $anchorCache[$File] }
        $anchors = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $counts = @{}
        $fenced = $false
        foreach ($line in [IO.File]::ReadAllLines($File)) {
            if ($line -match '^\s*(```|~~~)') { $fenced = -not $fenced; continue }
            if ($fenced) { continue }
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

    foreach ($path in $documents) {
        $category = Get-Category $path
        $inventory.Add([pscustomobject]@{ Path=$path; Category=$category; Bytes=(Get-Item -LiteralPath $path).Length; Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() })
        if ($category -in @('ThirdParty', 'Evidence')) { continue }
        $content = [IO.File]::ReadAllText((Join-Path $root $path))
        foreach ($paragraph in ($content -split '(?:\r?\n){2,}')) {
            if ($paragraph.Length -ge 180) { $paragraphs.Add([pscustomobject]@{ Path=$path; Text=$paragraph.Trim() }) }
        }
        $fenced = $false
        $lineNumber = 0
        foreach ($line in ($content -split '\r?\n')) {
            $lineNumber++
            if ($line -match '^\s*(```|~~~)') { $fenced = -not $fenced; continue }
            if ($fenced) { continue }
            $linkLine = [regex]::Replace($line, '`+[^`]*`+', '')
            foreach ($match in [regex]::Matches($linkLine, '\[[^\]\r\n]*\]\((?<target><[^>]+>|[^\s)]+)(?:\s+"[^"]*")?\)')) {
                $target = $match.Groups['target'].Value.Trim('<', '>')
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
        if ($fenced) { $fenceIssues.Add([pscustomobject]@{ Path=$path; Issue='UnclosedCodeFence' }) }
    }
    $duplicates = @($paragraphs | Group-Object Text | Where-Object Count -ge 5 | Sort-Object Count -Descending | ForEach-Object { [pscustomobject]@{ Copies=$_.Count; Text=$_.Name; Paths=@($_.Group.Path) } })
    $broken = @($links | Where-Object { $_.Issue -ne 'LocalOnlyArtifact' })
    $sourceCommit = git rev-parse --verify -q HEAD
    if ($LASTEXITCODE -ne 0) { $sourceCommit = $null }
    $report = [ordered]@{
        Date=(Get-Date -Format 'yyyy-MM-dd'); SourceCommit=$sourceCommit; Scope='Versioned and non-ignored Markdown, RST, text, HTML, PDF, Word and license documents; evidence and third-party contents fingerprinted, not rewritten.'
        Documents=$inventory.Count; Categories=@($inventory | Group-Object Category | ForEach-Object { [pscustomobject]@{ Category=$_.Name; Count=$_.Count } })
        BrokenLinks=$broken; MarkdownFenceIssues=@($fenceIssues); LocalOnlyLinks=@($links | Where-Object Issue -eq 'LocalOnlyArtifact'); RepeatedParagraphs=$duplicates; Inventory=$inventory
        Limitations=@('Local paths and Markdown inline links/headings checked; remote URLs not fetched.', 'Historical test results are preserved, not rerun or promoted to current acceptance.', 'Evidence and third-party source prose is immutable; only inventory and hashes are checked.')
    }
    if ($OutputPath) {
        $outputAbsolute = [IO.Path]::GetFullPath((Join-Path $root $OutputPath))
        [void][IO.Directory]::CreateDirectory((Split-Path $outputAbsolute))
        [IO.File]::WriteAllText($outputAbsolute, ($report | ConvertTo-Json -Depth 12) + "`n", [Text.UTF8Encoding]::new($false))
    }
    [pscustomobject]@{ Documents=$inventory.Count; BrokenLinks=$broken.Count; GuideBrokenLinks=@($broken | Where-Object Category -eq 'Guide').Count; MarkdownFenceIssues=$fenceIssues.Count; LocalOnlyLinks=$report.LocalOnlyLinks.Count; RepeatedParagraphGroups=$duplicates.Count }
    if ($Strict -and $broken.Count -gt 0) { throw "$($broken.Count) documentation links require repair." }
    if ($Strict -and $fenceIssues.Count -gt 0) { throw "$($fenceIssues.Count) documentation code fences require repair." }
} finally { Pop-Location }
