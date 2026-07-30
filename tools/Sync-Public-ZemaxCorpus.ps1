[CmdletBinding()]
param(
    [string]$RepositoryRoot = '',
    [switch]$Refresh
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
}
$repository = [IO.Path]::GetFullPath($RepositoryRoot)
$corpusRoot = Join-Path $repository 'local-data\lens-library\originals\user-zmx\public'
$manifestPath = Join-Path $corpusRoot 'manifest.json'
$userAgent = 'OptilandWorkbench-Public-Zemax-Corpus/1.0'
[IO.Directory]::CreateDirectory($corpusRoot) | Out-Null

function Test-OpenLicense {
    param([string]$Name)

    return $Name -match '(?i)^(CC|Creative Commons|Public Domain|MIT|BSD|Apache|GPL|LGPL|ODC)'
}

function Get-SafePathSegment {
    param([string]$Value)

    $safe = $Value
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

function Get-Json {
    param([string]$Uri)

    return Invoke-RestMethod -Uri $Uri -Headers @{
        Accept = 'application/json'
        'User-Agent' = $userAgent
    }
}

function Save-PublicFile {
    param(
        [string]$Uri,
        [string]$Destination,
        [string]$ExpectedMd5,
        [hashtable]$Headers = @{}
    )

    [IO.Directory]::CreateDirectory((Split-Path -Parent $Destination)) | Out-Null
    if ($Refresh -or -not [IO.File]::Exists($Destination)) {
        $temporary = "$Destination.download"
        try {
            $requestHeaders = @{
                'User-Agent' = $userAgent
            }
            foreach ($key in $Headers.Keys) {
                $requestHeaders[$key] = $Headers[$key]
            }

            Invoke-WebRequest `
                -Uri $Uri `
                -Headers $requestHeaders `
                -UseBasicParsing `
                -OutFile $temporary
            Move-Item -LiteralPath $temporary -Destination $Destination -Force
        }
        finally {
            if ([IO.File]::Exists($temporary)) {
                Remove-Item -LiteralPath $temporary -Force
            }
        }
    }

    $md5 = (Get-FileHash -LiteralPath $Destination -Algorithm MD5).Hash.ToLowerInvariant()
    if (-not [string]::IsNullOrWhiteSpace($ExpectedMd5) -and
        $md5 -ne $ExpectedMd5.ToLowerInvariant()) {
        throw "MD5 mismatch: $Destination; expected $ExpectedMd5, actual $md5."
    }

    return @{
        Md5 = $md5
        Sha256 = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
        Bytes = [IO.FileInfo]::new($Destination).Length
    }
}

function Expand-ZipSafely {
    param(
        [string]$ArchivePath,
        [string]$Destination
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    $destinationRoot = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        foreach ($entry in $archive.Entries) {
            if ([string]::IsNullOrEmpty($entry.Name)) {
                continue
            }

            $target = [IO.Path]::GetFullPath((Join-Path $Destination $entry.FullName))
            if (-not $target.StartsWith($destinationRoot, [StringComparison]::OrdinalIgnoreCase)) {
                throw "ZIP entry escapes the destination directory: $($entry.FullName)"
            }

            [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
            $source = $entry.Open()
            try {
                $output = [IO.File]::Open(
                    $target,
                    [IO.FileMode]::Create,
                    [IO.FileAccess]::Write,
                    [IO.FileShare]::None)
                try {
                    $source.CopyTo($output)
                }
                finally {
                    $output.Dispose()
                }
            }
            finally {
                $source.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

$entries = [Collections.Generic.List[object]]::new()
$errors = [Collections.Generic.List[object]]::new()

# Figshare: search every public article whose metadata mentions Zemax, OpticStudio, or ZMX,
# then keep only real .zmx attachments from records with an explicit open licence.
$articleIds = [Collections.Generic.HashSet[long]]::new()
[void]$articleIds.Add(1481270)
foreach ($term in @('zemax', 'opticstudio', 'zmx')) {
    for ($offset = 0; ; $offset += 100) {
        $body = @{
            search_for = $term
            limit = 100
            offset = $offset
        } | ConvertTo-Json
        $page = Invoke-RestMethod `
            -Method Post `
            -Uri 'https://api.figshare.com/v2/articles/search' `
            -Headers @{ 'User-Agent' = $userAgent } `
            -ContentType 'application/json' `
            -Body $body
        foreach ($article in $page) {
            [void]$articleIds.Add([long]$article.id)
        }

        if ($page.Count -lt 100) {
            break
        }
    }
}

foreach ($articleId in ($articleIds | Sort-Object)) {
    try {
        $article = Get-Json "https://api.figshare.com/v2/articles/$articleId"
        $license = [string]$article.license.name
        if (-not (Test-OpenLicense $license)) {
            continue
        }

        $filesProperty = $article.PSObject.Properties['files']
        if ($null -eq $filesProperty) {
            continue
        }

        foreach ($file in @($filesProperty.Value | Where-Object { $_.name -match '(?i)\.zmx$' })) {
            $fileName = Get-SafePathSegment ([string]$file.name)
            $destination = Join-Path $corpusRoot (
                "figshare\$articleId\$($file.id)-$fileName")
            try {
                $hashes = Save-PublicFile `
                    -Uri ([string]$file.download_url) `
                    -Destination $destination `
                    -ExpectedMd5 ([string]$file.computed_md5)
                $entries.Add([pscustomobject]@{
                    Provider = 'Figshare'
                    SourceId = [string]$articleId
                    FileId = [string]$file.id
                    SourceName = [string]$article.title
                    SourceUrl = [string]$article.url_public_html
                    License = $license
                    LicenseUrl = [string]$article.license.url
                    OriginalFileName = [string]$file.name
                    LocalPath = Get-RelativePath $destination
                    DownloadUrl = [string]$file.download_url
                    Bytes = $hashes.Bytes
                    Md5 = $hashes.Md5
                    Sha256 = $hashes.Sha256
                    Status = 'downloaded'
                })
            }
            catch {
                $errors.Add([pscustomobject]@{
                    Provider = 'Figshare'
                    SourceId = [string]$articleId
                    File = [string]$file.name
                    Error = $_.Exception.Message
                })
            }
        }
    }
    catch {
        $errors.Add([pscustomobject]@{
            Provider = 'Figshare'
            SourceId = [string]$articleId
            File = ''
            Error = $_.Exception.Message
        })
    }
}

# Mendeley Data currently exposes public ZIP downloads but not an anonymous search API.
# These are the two directly discoverable CC BY 4.0 datasets with individual ZMX files.
$mendeleyDatasets = @(
    @{
        Id = 'hb8hv8b8ng'
        Version = 1
        Name = 'Project files for mathematical modelling for high precision ray tracing in optical design'
        Url = 'https://data.mendeley.com/datasets/hb8hv8b8ng/1'
        License = 'CC BY 4.0'
    },
    @{
        Id = 'j26rgc5rvd'
        Version = 3
        Name = 'ZEMAX Optical Simulation Files'
        Url = 'https://data.mendeley.com/datasets/j26rgc5rvd/3'
        License = 'CC BY 4.0'
    }
)
foreach ($dataset in $mendeleyDatasets) {
    try {
        $datasetDirectory = Join-Path $corpusRoot "mendeley\$($dataset.Id)-v$($dataset.Version)"
        $archivePath = Join-Path $datasetDirectory 'dataset.zip'
        [IO.Directory]::CreateDirectory($datasetDirectory) | Out-Null
        Save-PublicFile `
            -Uri "https://data.mendeley.com/public-api/zip/$($dataset.Id)/download/$($dataset.Version)" `
            -Destination $archivePath `
            -ExpectedMd5 '' `
            -Headers @{ Referer = $dataset.Url } | Out-Null
        $extracted = Join-Path $datasetDirectory 'files'
        if ($Refresh -or -not [IO.Directory]::Exists($extracted)) {
            Expand-ZipSafely -ArchivePath $archivePath -Destination $extracted
        }

        $ordinal = 0
        foreach ($file in @(Get-ChildItem -LiteralPath $extracted -Recurse -File |
                Where-Object { $_.Extension -eq '.zmx' } |
                Sort-Object FullName)) {
            $ordinal++
            $entries.Add([pscustomobject]@{
                Provider = 'Mendeley Data'
                SourceId = "$($dataset.Id)-v$($dataset.Version)"
                FileId = [string]$ordinal
                SourceName = $dataset.Name
                SourceUrl = $dataset.Url
                License = $dataset.License
                LicenseUrl = 'https://creativecommons.org/licenses/by/4.0/'
                OriginalFileName = $file.Name
                LocalPath = Get-RelativePath $file.FullName
                DownloadUrl = "https://data.mendeley.com/public-api/zip/$($dataset.Id)/download/$($dataset.Version)"
                Bytes = $file.Length
                Md5 = (Get-FileHash -LiteralPath $file.FullName -Algorithm MD5).Hash.ToLowerInvariant()
                Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                Status = 'downloaded'
            })
        }
    }
    catch {
        $errors.Add([pscustomobject]@{
            Provider = 'Mendeley Data'
            SourceId = "$($dataset.Id)-v$($dataset.Version)"
            File = ''
            Error = $_.Exception.Message
        })
    }
}

# Zenodo supports a file-key query, so this discovers every current public record
# whose deposited files include a .zmx suffix.
$zenodo = Get-Json (
    'https://zenodo.org/api/records?q=' +
    [Uri]::EscapeDataString('files.entries.key:*.zmx') +
    '&size=25')
foreach ($record in $zenodo.hits.hits) {
    $license = [string]$record.metadata.license.id
    if (-not (Test-OpenLicense $license)) {
        continue
    }

    foreach ($file in @($record.files | Where-Object { $_.key -match '(?i)\.zmx$' })) {
        $fileName = Get-SafePathSegment ([string]$file.key)
        $destination = Join-Path $corpusRoot "zenodo\$($record.id)\$fileName"
        $expectedMd5 = ([string]$file.checksum) -replace '^(?i)md5:', ''
        try {
            $hashes = Save-PublicFile `
                -Uri ([string]$file.links.self) `
                -Destination $destination `
                -ExpectedMd5 $expectedMd5
            $entries.Add([pscustomobject]@{
                Provider = 'Zenodo'
                SourceId = [string]$record.id
                FileId = [string]$file.id
                SourceName = [string]$record.metadata.title
                SourceUrl = "https://zenodo.org/records/$($record.id)"
                License = $license
                LicenseUrl = "https://creativecommons.org/licenses/by/4.0/"
                OriginalFileName = [string]$file.key
                LocalPath = Get-RelativePath $destination
                DownloadUrl = [string]$file.links.self
                Bytes = $hashes.Bytes
                Md5 = $hashes.Md5
                Sha256 = $hashes.Sha256
                Status = 'downloaded'
            })
        }
        catch {
            $errors.Add([pscustomobject]@{
                Provider = 'Zenodo'
                SourceId = [string]$record.id
                File = [string]$file.key
                Error = $_.Exception.Message
            })
        }
    }
}

$firstByHash = @{}
foreach ($entry in ($entries | Sort-Object Provider, SourceId, OriginalFileName)) {
    if ($firstByHash.ContainsKey($entry.Sha256)) {
        $entry | Add-Member -NotePropertyName DuplicateOf -NotePropertyValue $firstByHash[$entry.Sha256]
    }
    else {
        $identity = "$($entry.Provider)/$($entry.SourceId)/$($entry.FileId)"
        $firstByHash[$entry.Sha256] = $identity
        $entry | Add-Member -NotePropertyName DuplicateOf -NotePropertyValue ''
    }
}

$manifest = [ordered]@{
    Version = 1
    GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Scope = 'Public, directly downloadable .zmx attachments with explicit open licences from Figshare, Mendeley Data, and Zenodo.'
    Entries = @($entries | Sort-Object Provider, SourceId, OriginalFileName)
    Errors = @($errors)
}
$json = $manifest | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText($manifestPath, $json, [Text.UTF8Encoding]::new($false))

$uniqueCount = @($entries | Where-Object { [string]::IsNullOrEmpty($_.DuplicateOf) }).Count
Write-Output "Manifest: $manifestPath"
Write-Output "Downloaded entries: $($entries.Count)"
Write-Output "Unique content: $uniqueCount"
Write-Output "Errors: $($errors.Count)"
