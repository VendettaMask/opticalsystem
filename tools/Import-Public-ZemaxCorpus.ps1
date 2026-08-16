[CmdletBinding()]
param(
    [string]$RepositoryRoot = '',
    [string]$ImporterDll = '',
    [string]$ExamplesDirectory = '',
    [string]$LibraryDirectory = '',
    [string]$ConversionReportPath = '',
    [switch]$RetrySuccessful
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
}
$repository = [IO.Path]::GetFullPath($RepositoryRoot)
$corpusRoot = Join-Path $repository 'local-data\lens-library\originals\user-zmx\public'
$examplesDirectory = if ([string]::IsNullOrWhiteSpace($ExamplesDirectory)) {
    Join-Path $repository 'samples\lenses'
} else {
    [IO.Path]::GetFullPath($ExamplesDirectory)
}
$libraryDirectory = if ([string]::IsNullOrWhiteSpace($LibraryDirectory)) {
    Join-Path $repository 'src\OptilandWorkbench.App\Assets\LensLibrary'
} else {
    [IO.Path]::GetFullPath($LibraryDirectory)
}
$conversionReportPath = if ([string]::IsNullOrWhiteSpace($ConversionReportPath)) {
    Join-Path $corpusRoot 'conversion-report.json'
} else {
    [IO.Path]::GetFullPath($ConversionReportPath)
}
$manifestPaths = @(
    (Join-Path $corpusRoot 'manifest.json'),
    (Join-Path $corpusRoot 'dan-reiley-manifest.json')
) | Where-Object { [IO.File]::Exists($_) }
if ($manifestPaths.Count -eq 0) {
    throw (
        "No download manifest found under $corpusRoot. Run " +
        'tools\Sync-Public-ZemaxCorpus.ps1 or ' +
        'tools\Sync-DanReileyLensExchange.ps1 first.')
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

$manifestEntries = [Collections.Generic.List[object]]::new()
foreach ($manifestPath in $manifestPaths) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 |
        ConvertFrom-Json
    foreach ($entry in @($manifest.Entries |
            Where-Object { $_.OriginalFileName -match '(?i)\.zmx$' })) {
        $manifestEntries.Add($entry)
    }
}
$previous = @{}
if (-not $RetrySuccessful -and [IO.File]::Exists($conversionReportPath)) {
    $oldReport = Get-Content -LiteralPath $conversionReportPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($item in $oldReport.Results) {
        if ($item.Status -eq 'converted') {
            $previous["$($item.Provider)/$($item.SourceId)/$($item.FileId)"] = $item
        }
    }
}

$results = [Collections.Generic.List[object]]::new()
foreach ($entry in $manifestEntries) {
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
        '--examples', $examplesDirectory,
        '--library', $libraryDirectory,
        '--source-id', $sourceId,
        '--source-name', $sourceName,
        '--source-url', [string]$entry.SourceUrl,
        '--license', [string]$entry.License,
        '--category', 'Public Zemax Designs',
        '--name', $baseName,
        '--example-file', $exampleFile
    )

    $lensType = if ($null -eq $entry.PSObject.Properties['LensType']) {
        ''
    }
    else {
        [string]$entry.LensType
    }
    $application = if ($null -eq $entry.PSObject.Properties['Application']) {
        ''
    }
    else {
        [string]$entry.Application
    }
    $designOrganization = if ($null -eq $entry.PSObject.Properties['DesignOrganization']) {
        ''
    }
    else {
        [string]$entry.DesignOrganization
    }

    if (-not [string]::IsNullOrWhiteSpace($lensType)) {
        $arguments += @('--lens-type', $lensType)
    }
    if (-not [string]::IsNullOrWhiteSpace($application)) {
        $arguments += @('--application', $application)
    }
    if (-not [string]::IsNullOrWhiteSpace($designOrganization)) {
        $arguments += @('--design-organization', $designOrganization)
    }

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
    Manifests = @($manifestPaths)
    Results = @($results)
}
$reportDirectory = [IO.Path]::GetDirectoryName($conversionReportPath)
if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
    [IO.Directory]::CreateDirectory($reportDirectory) | Out-Null
}
[IO.File]::WriteAllText(
    $conversionReportPath,
    ($report | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))

$converted = @($results | Where-Object Status -eq 'converted').Count
$failed = @($results | Where-Object Status -eq 'failed').Count
$duplicates = @($results | Where-Object Status -eq 'duplicate-skipped').Count
Write-Output "Conversion report: $conversionReportPath"
Write-Output "Converted: $converted; failed: $failed; duplicates skipped: $duplicates"
