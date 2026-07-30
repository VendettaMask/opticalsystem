[CmdletBinding()]
param(
    [string]$RepositoryRoot = '',
    [switch]$Refresh,
    [ValidateRange(1, 32)]
    [int]$Concurrency = 8
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
}
$repository = [IO.Path]::GetFullPath($RepositoryRoot)
$corpusRoot = Join-Path $repository 'local-data\lens-library\originals\user-zmx\public'
$downloadRoot = Join-Path $corpusRoot 'dan-reiley'
$manifestPath = Join-Path $corpusRoot 'dan-reiley-manifest.json'
$siteRoot = 'https://sites.google.com/site/danreiley'
$userAgent = 'OptilandWorkbench-Dan-Reiley-Lens-Exchange/1.0'
[IO.Directory]::CreateDirectory($downloadRoot) | Out-Null

$folders = @(
    [pscustomobject]@{
        Category = 'endoscopes'
        FolderId = '1eXA_cBzkEosQz7-Q1JuPc2gIcz0G3BNw'
    },
    [pscustomobject]@{
        Category = 'eyepieces'
        FolderId = '1F4GBcEohFNWh9kLPcS7aDgur_DHOADFO'
    },
    [pscustomobject]@{
        Category = 'microscope-objectives'
        FolderId = '1Gi4ZnWE4G_m-pWJbeuRQC7O9uXqP6WAO'
    },
    [pscustomobject]@{
        Category = 'photographic-lenses-prime'
        FolderId = '1P38j7dtEsnPvlGWBaxrLd4tA98hyiyds'
    },
    [pscustomobject]@{
        Category = 'photographic-lenses-zoom'
        FolderId = '1x8_9hb72-Gyvmlx-DE026UXR-zdPACB0'
    },
    [pscustomobject]@{
        Category = 'projectors'
        FolderId = '1rz0OD5erCPJG_8NLyLry5CSfbS4Cy32a'
    },
    [pscustomobject]@{
        Category = 'scan-lenses'
        FolderId = '13IjbkaoEkGwKN3MnEK-Yub9vNpucPHH4'
    },
    [pscustomobject]@{
        Category = 'spectro'
        FolderId = '18fU4UPQz2InGtwYzlBUIF91Q1SV2fIf7'
    },
    [pscustomobject]@{
        Category = 'telescopes'
        FolderId = '1leEyQualluLTwUfJSNr2TcgR5S1i0xoR'
    }
)

function Get-SafePathSegment {
    param([string]$Value)

    $safe = [Net.WebUtility]::HtmlDecode($Value)
    foreach ($character in [IO.Path]::GetInvalidFileNameChars()) {
        $safe = $safe.Replace([string]$character, '_')
    }

    $safe = $safe.Trim().TrimEnd('.')
    return $(if ([string]::IsNullOrWhiteSpace($safe)) { 'unnamed' } else { $safe })
}

function Get-RelativePath {
    param([string]$Path)

    $rootUri = [Uri]::new(($corpusRoot.TrimEnd('\') + '\'))
    return [Uri]::UnescapeDataString(
        $rootUri.MakeRelativeUri([Uri]::new([IO.Path]::GetFullPath($Path))).ToString()
    ).Replace('/', '\')
}

function Test-DownloadedContent {
    param(
        [byte[]]$Bytes,
        [string]$FileName
    )

    if ($Bytes.Length -eq 0) {
        throw "Downloaded an empty file: $FileName"
    }

    $prefixLength = [Math]::Min(512, $Bytes.Length)
    $prefix = [Text.Encoding]::UTF8.GetString($Bytes, 0, $prefixLength)
    if ($prefix -match '(?i)<!doctype\s+html|<html(?:\s|>)') {
        throw "Google Drive returned HTML instead of file content: $FileName"
    }
}

Add-Type -AssemblyName System.Net.Http
$handler = [Net.Http.HttpClientHandler]::new()
$handler.MaxConnectionsPerServer = $Concurrency
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromMinutes(3)
$client.DefaultRequestHeaders.UserAgent.ParseAdd($userAgent)

$inventory = [Collections.Generic.List[object]]::new()
$errors = [Collections.Generic.List[object]]::new()
$entryPattern = (
    '<div class="flip-entry" id="entry-(?<id>[A-Za-z0-9_-]+)".*?' +
    '<div class="flip-entry-title">(?<name>.*?)</div>')

try {
    foreach ($folder in $folders) {
        $folderViewUrl = (
            'https://drive.google.com/embeddedfolderview?id={0}#list' -f
            $folder.FolderId)
        try {
            $html = $client.GetStringAsync($folderViewUrl).GetAwaiter().GetResult()
            $matches = [regex]::Matches(
                $html,
                $entryPattern,
                [Text.RegularExpressions.RegexOptions]::Singleline)
            $usedNames = [Collections.Generic.HashSet[string]]::new(
                [StringComparer]::OrdinalIgnoreCase)
            foreach ($match in $matches) {
                $fileId = $match.Groups['id'].Value
                $originalName = [Net.WebUtility]::HtmlDecode(
                    $match.Groups['name'].Value)
                $localName = Get-SafePathSegment $originalName
                if (-not $usedNames.Add($localName)) {
                    $baseName = [IO.Path]::GetFileNameWithoutExtension($localName)
                    $extension = [IO.Path]::GetExtension($localName)
                    $localName = "$baseName--$fileId$extension"
                    [void]$usedNames.Add($localName)
                }

                $destination = Join-Path (
                    Join-Path $downloadRoot $folder.Category) $localName
                $inventory.Add([pscustomobject]@{
                    Provider = 'Dan Reiley Lens Design Exchange'
                    SourceId = [string]$folder.Category
                    FileId = $fileId
                    SourceName = [string]$folder.Category
                    SourceUrl = "$siteRoot/$($folder.Category)"
                    License = 'Public domain'
                    LicenseUrl = "$siteRoot/a-file-exchange-site-for-lens-designs"
                    OriginalFileName = $originalName
                    LocalName = $localName
                    Destination = $destination
                    DownloadUrl = (
                        'https://drive.usercontent.google.com/download?' +
                        "id=$fileId&export=download&confirm=t")
                })
            }

            Write-Output (
                '{0}: discovered {1} files' -f
                $folder.Category,
                $matches.Count)
        }
        catch {
            $errors.Add([pscustomobject]@{
                Category = [string]$folder.Category
                FileId = ''
                File = ''
                Error = $_.Exception.Message
            })
        }
    }

    $queue = [Collections.Generic.List[object]]::new()
    foreach ($item in $inventory) {
        [IO.Directory]::CreateDirectory(
            (Split-Path -Parent $item.Destination)) | Out-Null
        if ($Refresh -or -not [IO.File]::Exists($item.Destination)) {
            $queue.Add($item)
        }
    }

    $downloaded = 0
    for ($offset = 0; $offset -lt $queue.Count; $offset += $Concurrency) {
        $last = [Math]::Min($offset + $Concurrency - 1, $queue.Count - 1)
        $batch = @($queue[$offset..$last])
        $remaining = @($batch)
        for ($attempt = 1; $attempt -le 3 -and $remaining.Count -gt 0; $attempt++) {
            $pending = @(
                foreach ($item in $remaining) {
                    [pscustomobject]@{
                        Item = $item
                        Task = $client.GetByteArrayAsync($item.DownloadUrl)
                    }
                })
            try {
                [Threading.Tasks.Task]::WaitAll(
                    [Threading.Tasks.Task[]]@($pending.Task))
            }
            catch {
                # Inspect each task below so successful downloads in the same
                # batch are retained and only failed requests are retried.
            }

            $retry = [Collections.Generic.List[object]]::new()
            foreach ($request in $pending) {
                $item = $request.Item
                if ($request.Task.Status -ne
                    [Threading.Tasks.TaskStatus]::RanToCompletion) {
                    if ($attempt -lt 3) {
                        $retry.Add($item)
                    }
                    else {
                        $message = if ($null -ne $request.Task.Exception) {
                            $request.Task.Exception.GetBaseException().Message
                        }
                        else {
                            "Download task ended with status $($request.Task.Status)."
                        }
                        $errors.Add([pscustomobject]@{
                            Category = $item.SourceId
                            FileId = $item.FileId
                            File = $item.OriginalFileName
                            Error = $message
                        })
                    }
                    continue
                }

                try {
                    [byte[]]$bytes = $request.Task.Result
                    Test-DownloadedContent -Bytes $bytes -FileName $item.OriginalFileName
                    $temporary = "$($item.Destination).download"
                    try {
                        [IO.File]::WriteAllBytes($temporary, $bytes)
                        Move-Item `
                            -LiteralPath $temporary `
                            -Destination $item.Destination `
                            -Force
                    }
                    finally {
                        if ([IO.File]::Exists($temporary)) {
                            Remove-Item -LiteralPath $temporary -Force
                        }
                    }
                    $downloaded++
                }
                catch {
                    if ($attempt -lt 3) {
                        $retry.Add($item)
                    }
                    else {
                        $errors.Add([pscustomobject]@{
                            Category = $item.SourceId
                            FileId = $item.FileId
                            File = $item.OriginalFileName
                            Error = $_.Exception.Message
                        })
                    }
                }
            }

            $remaining = @($retry)
            if ($remaining.Count -gt 0 -and $attempt -lt 3) {
                Start-Sleep -Seconds ([Math]::Pow(2, $attempt - 1))
            }
        }

        $completed = [Math]::Min($last + 1, $queue.Count)
        if ($completed -eq $queue.Count -or $completed % 100 -lt $Concurrency) {
            Write-Output "Download progress: $completed/$($queue.Count)"
        }
    }
}
finally {
    $client.Dispose()
    $handler.Dispose()
}

$entries = [Collections.Generic.List[object]]::new()
foreach ($item in $inventory) {
    if (-not [IO.File]::Exists($item.Destination)) {
        continue
    }

    $file = [IO.FileInfo]::new($item.Destination)
    $entries.Add([pscustomobject]@{
        Provider = $item.Provider
        SourceId = $item.SourceId
        FileId = $item.FileId
        SourceName = $item.SourceName
        SourceUrl = $item.SourceUrl
        License = $item.License
        LicenseUrl = $item.LicenseUrl
        OriginalFileName = $item.OriginalFileName
        LocalPath = Get-RelativePath $item.Destination
        DownloadUrl = $item.DownloadUrl
        Bytes = $file.Length
        Sha256 = (
            Get-FileHash -LiteralPath $item.Destination -Algorithm SHA256
        ).Hash.ToLowerInvariant()
        Status = 'downloaded'
    })
}

$firstByHash = @{}
foreach ($entry in ($entries | Sort-Object SourceId, OriginalFileName, FileId)) {
    if ($firstByHash.ContainsKey($entry.Sha256)) {
        $entry | Add-Member `
            -NotePropertyName DuplicateOf `
            -NotePropertyValue $firstByHash[$entry.Sha256]
    }
    else {
        $identity = "$($entry.Provider)/$($entry.SourceId)/$($entry.FileId)"
        $firstByHash[$entry.Sha256] = $identity
        $entry | Add-Member -NotePropertyName DuplicateOf -NotePropertyValue ''
    }
}

$extensionCounts = [ordered]@{}
foreach ($group in ($inventory |
        Group-Object { [IO.Path]::GetExtension($_.OriginalFileName).ToLowerInvariant() } |
        Sort-Object Name)) {
    $extensionCounts[$group.Name] = $group.Count
}

$manifest = [ordered]@{
    Version = 1
    GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    SourceUrl = "$siteRoot/a-file-exchange-site-for-lens-designs"
    Scope = (
        'All files exposed by the nine public Google Drive folders embedded ' +
        'in the Dan Reiley Lens Design Exchange category pages.')
    License = 'Public domain'
    InventoryCount = $inventory.Count
    ExtensionCounts = $extensionCounts
    Entries = @($entries | Sort-Object SourceId, OriginalFileName, FileId)
    Errors = @($errors)
}
[IO.File]::WriteAllText(
    $manifestPath,
    ($manifest | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))

$uniqueCount = @($entries |
    Where-Object { [string]::IsNullOrWhiteSpace($_.DuplicateOf) }).Count
Write-Output "Manifest: $manifestPath"
Write-Output "Discovered entries: $($inventory.Count)"
Write-Output "Downloaded this run: $downloaded"
Write-Output "Available entries: $($entries.Count)"
Write-Output "Unique content: $uniqueCount"
Write-Output "Errors: $($errors.Count)"
