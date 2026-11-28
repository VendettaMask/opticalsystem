param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

function Test-NumericalAcceptanceScope {
    param([Parameter(Mandatory)][string]$Root)

    $Root = [IO.Path]::GetFullPath($Root)
    function Resolve-ScopedFile([string]$Relative) {
        if ([IO.Path]::IsPathRooted($Relative) -or $Relative -match '(^|[\\/])\.\.([\\/]|$)') { throw "Invalid scoped path: $Relative" }
        $full = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
        if (-not $full.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Scoped path leaves repository: $Relative" }
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "Missing scoped file: $Relative" }
        for ($item = Get-Item -LiteralPath $full; $null -ne $item -and $item.FullName -ne $Root; $item = $item.Parent ?? $item.Directory) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Scoped path traverses a link: $Relative" }
        }
        return $full
    }

    $policy = Get-Content -LiteralPath (Resolve-ScopedFile 'validation/acceptance-policy.json') -Raw | ConvertFrom-Json
    if ($policy.schemaVersion -ne 1 -or $policy.currentCorpus.count -ne 95 -or $policy.testFilter -cne 'AcceptanceScope!=RetiredReference') { throw 'Unsupported acceptance policy.' }
    $indexFile = Resolve-ScopedFile $policy.currentCorpus.index
    $indexText = [IO.File]::ReadAllText($indexFile).Replace("`r`n", "`n").Replace("`r", "`n")
    $indexHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($indexText))).ToLowerInvariant()
    if ($indexHash -cne $policy.currentCorpus.indexLfSha256) { throw 'The frozen 95-lens index changed.' }
    $files = @(($indexText | ConvertFrom-Json).Files)
    if ($files.Count -ne 95 -or @($files.RelativePath | Sort-Object -Unique).Count -ne 95) { throw 'The acceptance corpus must contain exactly 95 unique lens paths.' }
    $assetRoot = Split-Path $policy.currentCorpus.index -Parent
    foreach ($file in $files) {
        $path = Resolve-ScopedFile "$assetRoot/$($file.RelativePath)"
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $file.Sha256.ToLowerInvariant()) { throw "Frozen lens bytes changed: $($file.RelativePath)" }
    }

    $retiredHashes = @{}
    foreach ($entry in $policy.retiredCaptureSources) {
        if ($entry.sha256 -in $files.Sha256 -or $retiredHashes.ContainsKey($entry.sha256)) { throw 'A current lens source cannot be retired; retired hashes must be unique.' }
        $retiredHashes[$entry.sha256] = $true
        if (@($entry.captures).Count -eq 0) { throw 'A retired source needs archived provenance.' }
        foreach ($capture in $entry.captures) {
            $zip = [IO.Compression.ZipFile]::OpenRead((Resolve-ScopedFile $capture.archive))
            try {
                $item = $zip.GetEntry($capture.entry)
                if ($null -eq $item) { throw "Missing archived source: $($capture.entry)" }
                $stream = $item.Open()
                try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() } finally { $stream.Dispose() }
                if ($hash -cne $entry.sha256) { throw "Archived retirement source changed: $($capture.entry)" }
            } finally { $zip.Dispose() }
        }
    }

    $registered = @{}
    foreach ($entry in $policy.retiredTests) {
        if ([string]::IsNullOrWhiteSpace($entry.reason)) { throw 'A retirement needs its source and reason.' }
        $key = "$($entry.file)::$($entry.method)"
        if ($registered.ContainsKey($key)) { throw "Duplicate retirement: $key" }
        $registered[$key] = $entry
        $source = [IO.File]::ReadAllText((Resolve-ScopedFile $entry.file))
        $pattern = '\[Trait\("AcceptanceScope", "RetiredReference"\)\]\s+public\s+(?:async\s+)?[\w<>\[\]?]+\s+' + [regex]::Escape($entry.method) + '\s*\('
        if ([regex]::Matches($source, $pattern).Count -ne 1) { throw "Missing or ambiguous retirement trait: $key" }
    }
    $found = 0
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $Root 'tests') -Recurse -Filter '*.cs' -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }) {
        $source = [IO.File]::ReadAllText($file.FullName)
        $relative = [IO.Path]::GetRelativePath($Root, $file.FullName).Replace('\', '/')
        foreach ($match in [regex]::Matches($source, '\[Trait\("AcceptanceScope", "RetiredReference"\)\]\s+public\s+(?:async\s+)?[\w<>\[\]?]+\s+(\w+)\s*\(')) {
            $key = "${relative}::$($match.Groups[1].Value)"
            if (-not $registered.ContainsKey($key)) { throw "Unregistered retirement: $key" }
            $found++
        }
    }
    if ($found -ne $registered.Count) { throw 'Retirement registry and test metadata disagree.' }
    $workflow = [IO.File]::ReadAllText((Resolve-ScopedFile '.github/workflows/ci.yml'))
    if (-not $workflow.Contains("CURRENT_TEST_FILTER: '" + $policy.testFilter + "'")) { throw 'CI does not declare the current acceptance filter.' }
    $filteredRuns = [regex]::Matches($workflow, '--filter "\$\{\{ env\.CURRENT_TEST_FILTER \}\}"').Count
    if ($filteredRuns -ne 2) { throw 'Both main and comparison CI test runs must apply the acceptance filter.' }
    return [ordered]@{ schemaVersion = 1; valid = $true; currentLensCount = $files.Count; indexLfSha256 = $indexHash; retiredTestMethods = $found; retiredCaptureSourceHashes = $retiredHashes.Count; testFilter = $policy.testFilter; numericalTolerancesChanged = $false }
}

if ($MyInvocation.InvocationName -ne '.') {
    $receipt = Test-NumericalAcceptanceScope -Root $RepositoryRoot
    $json = $receipt | ConvertTo-Json -Depth 8
    if ($OutputPath) {
        $full = [IO.Path]::GetFullPath($OutputPath)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($full)) | Out-Null
        [IO.File]::WriteAllText($full, $json + "`n", [Text.UTF8Encoding]::new($false))
    }
    $json
}
