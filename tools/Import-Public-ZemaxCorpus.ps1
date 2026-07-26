[CmdletBinding()]
param(
    [string]$RepositoryRoot = '',
    [string]$ImporterDll = '',
    [switch]$RetrySuccessful
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
}
$repository = [IO.Path]::GetFullPath($RepositoryRoot)
$corpusRoot = Join-Path $repository 'local-data\lens-library\originals\public-zmx'
$manifestPath = Join-Path $corpusRoot 'manifest.json'
$reportPath = Join-Path $corpusRoot 'conversion-report.json'
if (-not [IO.File]::Exists($manifestPath)) {
    throw "Download manifest not found: $manifestPath. Run tools\Sync-Public-ZemaxCorpus.ps1 first."
}

if ([string]::IsNullOrWhiteSpace($ImporterDll)) {
    $ImporterDll = Join-Path $repository (
        '.tmp\public-zmx-importer\ZemaxLibraryImporter.dll')
}
$ImporterDll = [IO.Path]::GetFullPath($ImporterDll)
if (-not [IO.File]::Exists($ImporterDll)) {
    throw "Importer not found: $ImporterDll. Build OptilandWorkbench.ZemaxLibraryImporter first."
}

function Get-SafeName {
    param(
        [string]$Value,
        [int]$MaximumLength = 100
    )

    $safe = $Value -replace '[^A-Za-z0-9._-]+', '-'
    $safe = $safe.Trim('-', '.', '_')
    if ($safe.Length -gt $MaximumLength) {
        $safe = $safe.Substring(0, $MaximumLength).TrimEnd('-', '.', '_')
    }
    return $(if ([string]::IsNullOrWhiteSpace($safe)) { 'zemax-design' } else { $safe })
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$previous = @{}
if (-not $RetrySuccessful -and [IO.File]::Exists($reportPath)) {
    $oldReport = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($item in $oldReport.Results) {
        if ($item.Status -eq 'converted') {
            $previous["$($item.Provider)/$($item.SourceId)/$($item.FileId)"] = $item
        }
    }
}

$results = [Collections.Generic.List[object]]::new()
foreach ($entry in $manifest.Entries) {
    $key = "$($entry.Provider)/$($entry.SourceId)/$($entry.FileId)"
    if ($previous.ContainsKey($key)) {
        $results.Add($previous[$key])
        continue
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$entry.DuplicateOf)) {
        $results.Add([pscustomobject]@{
            Provider = $entry.Provider
            SourceId = $entry.SourceId
            FileId = $entry.FileId
            OriginalFileName = $entry.OriginalFileName
            Status = 'duplicate-skipped'
            DuplicateOf = $entry.DuplicateOf
            ExitCode = 0
            Output = ''
        })
        continue
    }

    $inputPath = Join-Path $corpusRoot ([string]$entry.LocalPath)
    $sourceId = Get-SafeName ("$($entry.Provider)-$($entry.SourceId)") 80
    $baseName = [IO.Path]::GetFileNameWithoutExtension([string]$entry.OriginalFileName)
    $sourceName = ([string]$entry.SourceName).Replace('"', "'")
    $exampleFile = Get-SafeName (
        "$sourceId-$($entry.FileId)-$baseName.staropt") 180
    $arguments = @(
        $ImporterDll,
        $inputPath,
        '--repo-root', $repository,
        '--source-id', $sourceId,
        '--source-name', $sourceName,
        '--source-url', [string]$entry.SourceUrl,
        '--license', [string]$entry.License,
        '--category', 'Public Zemax Designs',
        '--name', $baseName,
        '--example-file', $exampleFile
    )

    $originalErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $captured = & dotnet @arguments 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $originalErrorActionPreference
    }
    $results.Add([pscustomobject]@{
        Provider = $entry.Provider
        SourceId = $entry.SourceId
        FileId = $entry.FileId
        OriginalFileName = $entry.OriginalFileName
        Status = $(if ($exitCode -eq 0) { 'converted' } else { 'failed' })
        DuplicateOf = ''
        ExitCode = $exitCode
        Output = $captured.Trim()
    })
    $statusText = if ($exitCode -eq 0) { 'converted' } else { 'failed' }
    Write-Output (
        '{0}/{1}/{2}: {3}' -f
        $entry.Provider,
        $entry.SourceId,
        $entry.OriginalFileName,
        $statusText)
}

$report = [ordered]@{
    Version = 1
    GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Importer = $ImporterDll
    Manifest = $manifestPath
    Results = @($results)
}
[IO.File]::WriteAllText(
    $reportPath,
    ($report | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))

$converted = @($results | Where-Object Status -eq 'converted').Count
$failed = @($results | Where-Object Status -eq 'failed').Count
$duplicates = @($results | Where-Object Status -eq 'duplicate-skipped').Count
Write-Output "Conversion report: $reportPath"
Write-Output "Converted: $converted; failed: $failed; duplicates skipped: $duplicates"
