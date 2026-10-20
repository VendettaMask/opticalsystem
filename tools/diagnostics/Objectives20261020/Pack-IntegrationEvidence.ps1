param(
    [Parameter(Mandatory)][string]$BaseArchivePath,
    [Parameter(Mandatory)][string]$BaseArchiveSha256,
    [Parameter(Mandatory)][string]$RunDirectory,
    [Parameter(Mandatory)][string]$ReportDirectory,
    [Parameter(Mandatory)][string]$AdditionalEvidenceDirectory,
    [Parameter(Mandatory)][string]$ArchivePath,
    [ValidateRange(1, 100000)][int]$ExpectedLensCount = 19
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../..')).Path
$baseArchive = (Resolve-Path -LiteralPath $BaseArchivePath).Path
$run = (Resolve-Path -LiteralPath $RunDirectory).Path
$report = (Resolve-Path -LiteralPath $ReportDirectory).Path
$additional = (Resolve-Path -LiteralPath $AdditionalEvidenceDirectory).Path
$archive = [IO.Path]::GetFullPath($ArchivePath)
if (-not $archive.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive must remain in this repository.' }
if (Test-Path -LiteralPath $archive) { throw 'Use a fresh integration archive.' }
if ((Get-FileHash -LiteralPath $baseArchive -Algorithm SHA256).Hash.ToLowerInvariant() -cne $BaseArchiveSha256) { throw 'Sealed base archive identity mismatch.' }
$summary = Get-Content -LiteralPath (Join-Path $report 'summary.json') -Raw | ConvertFrom-Json
if (-not $summary.completeExecutionAttempts -or $summary.sourceLenses -ne $ExpectedLensCount) { throw 'A complete selected-lens report is required.' }
$baseZip = [IO.Compression.ZipFile]::OpenRead($baseArchive)
try {
    $manifestEntry = $baseZip.GetEntry('payload-manifest.json')
    if ($null -eq $manifestEntry) { throw 'Base archive has no exact-byte payload manifest.' }
    $reader = [IO.StreamReader]::new($manifestEntry.Open())
    try { $baseFiles = @($reader.ReadToEnd() | ConvertFrom-Json) } finally { $reader.Dispose() }
    $byHash = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($file in $baseFiles) {
        if (-not $byHash.ContainsKey($file.sha256)) { $byHash.Add($file.sha256, $file) }
    }
    $files = [Collections.Generic.List[object]]::new()
    function Add-Payload([string]$Directory, [string]$Prefix, [switch]$SourcesOnly) {
        foreach ($file in Get-ChildItem -LiteralPath $Directory -File -Recurse) {
            $relative = [IO.Path]::GetRelativePath($Directory, $file.FullName).Replace('\','/')
            if ($relative -match '(^|/)(bin|obj)/' -or $file.Extension -in @('.dll','.exe','.pdb','.lock') -or $file.Name.StartsWith('.')) { continue }
            if ($SourcesOnly -and $file.Extension -notin @('.cs','.csproj','.ps1','.md','.json')) { continue }
            $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            $inherited = if ($byHash.ContainsKey($hash) -and $byHash[$hash].bytes -eq $file.Length) { $byHash[$hash].path } else { $null }
            $files.Add([pscustomobject]@{path="$Prefix/$relative";fullPath=$file.FullName;bytes=$file.Length;sha256=$hash;baseEntry=$inherited})
        }
    }
    Add-Payload $run 'run'
    Add-Payload $report 'report'
    Add-Payload $additional 'integration-evidence'
    Add-Payload $PSScriptRoot 'integration-diagnostic-source' -SourcesOnly
    if (@($files | Group-Object path | Where-Object Count -ne 1).Count) { throw 'Duplicate integration payload path.' }
    $fresh = @($files | Where-Object { $null -eq $_.baseEntry })
    [IO.Directory]::CreateDirectory((Split-Path -Parent $archive)) | Out-Null
    $stream = [IO.File]::Open($archive, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($file in $fresh) {
                $entry = $zip.CreateEntry($file.path, [IO.Compression.CompressionLevel]::Optimal)
                $destination = $entry.Open()
                $source = [IO.File]::OpenRead($file.fullPath)
                try { $source.CopyTo($destination) } finally { $source.Dispose(); $destination.Dispose() }
            }
            $manifest = [ordered]@{
                schemaVersion=2
                format='Exact-byte payload inheritance from one sealed base ZIP plus fresh ZIP entries.'
                baseArchive=[ordered]@{path=[IO.Path]::GetRelativePath($repository,$baseArchive).Replace('\','/');sha256=$BaseArchiveSha256}
                restore='For each file: read baseEntry from baseArchive when present; otherwise read path from this ZIP. Verify bytes and SHA-256, then write to the logical path. Both archives are required.'
                files=@($files | Select-Object path,bytes,sha256,baseEntry)
            }
            $writer = [IO.StreamWriter]::new($zip.CreateEntry('payload-manifest.json').Open(),[Text.UTF8Encoding]::new($false))
            try { $writer.Write(($manifest | ConvertTo-Json -Depth 7)) } finally { $writer.Dispose() }
        } finally { $zip.Dispose() }
    } finally { $stream.Dispose() }
    $readback = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        $verifiedBase = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($file in $files) {
            if ($null -ne $file.baseEntry) {
                if (-not $verifiedBase.Add($file.baseEntry)) { continue }
                $entry = $baseZip.GetEntry($file.baseEntry)
            } else { $entry = $readback.GetEntry($file.path) }
            if ($null -eq $entry -or $entry.Length -ne $file.bytes) { throw "Missing/truncated payload: $($file.path)" }
            $entryStream = $entry.Open()
            try { $hash = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($entryStream)) } finally { $entryStream.Dispose() }
            if ($hash -cne $file.sha256) { throw "Payload readback hash mismatch: $($file.path)" }
        }
        if ($readback.Entries.Count -ne $fresh.Count + 1) { throw 'Unexpected integration entry count.' }
    } finally { $readback.Dispose() }
    [ordered]@{
        path=$archive
        bytes=(Get-Item -LiteralPath $archive).Length
        sha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        logicalPayloadFiles=$files.Count
        inheritedPayloadFiles=$files.Count-$fresh.Count
        freshPayloadFiles=$fresh.Count
        verifiedReadbackIncludingInheritedPayloads=$true
        baseArchive=$manifest.baseArchive
        bothArchivesRequired=$true
        containsProprietaryApiBinaries=$false
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $report 'integration-archive.json') -Encoding utf8
    Get-Content -LiteralPath (Join-Path $report 'integration-archive.json')
} finally { $baseZip.Dispose() }
