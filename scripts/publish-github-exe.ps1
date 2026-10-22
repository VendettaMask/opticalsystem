#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository,
    [Parameter(Mandatory)][ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+(?:-test\.[0-9]{8})?$')][string]$Tag,
    [Parameter(Mandatory)][string]$DistributionDirectory,
    [string]$NotesFile,
    [switch]$ReplaceExisting,
    [switch]$PlanOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Validate the local handoff before credentials, networking or release mutations.
$directory = (Resolve-Path -LiteralPath $DistributionDirectory).Path
$version = ($Tag -replace '^v', '') -replace '-test\.[0-9]{8}$', ''
$testPackage = $Tag.Contains('-test.')
$suffix = if ($testPackage) { 'UnsignedTest' } else { 'Unsigned' }
$files = @(Get-ChildItem -LiteralPath $directory -File -Filter '*.exe')
if ($files.Count -eq 0) { throw 'No EXE installers in the handoff.' }
$packages = @($files | ForEach-Object {
    $file = $_
    $pattern = '^OpticalSystemDesign-' + [regex]::Escape($version) + '-(win-x64|win-arm64)-' + $suffix + '-Setup\.exe$'
    if ($file.Name -cnotmatch $pattern) { throw "Unexpected installer name: $($file.Name)" }
    $runtime = $Matches[1]
    $record = Get-Content -LiteralPath (Join-Path $directory "release-$runtime.json") -Raw | ConvertFrom-Json
    if ($record.Version -cne $version -or $record.Runtime -cne $runtime -or
        $record.TestPackage -isnot [bool] -or $record.TestPackage -ne $testPackage -or
        $record.SourceDirty -isnot [bool] -or $record.SourceDirty -or
        $record.ValidationHooks -isnot [bool] -or $record.ValidationHooks -or
        $record.Signed -isnot [bool] -or $record.Signed -or
        $record.ModulesInProcess -isnot [bool] -or -not $record.ModulesInProcess -or
        $record.ProductAppId -cne 'OpticalSystemDesign-8D387EA8-F349-447F-B5D3-B58E27F46D4A' -or
        (@($record.RequiredModules | Sort-Object) -join ',') -cne 'CoatingDesign,InitialStructure' -or
        $record.SourceCommit -cnotmatch '^[0-9a-f]{40}$' -or
        $record.ReleaseId -cnotmatch ('^' + [regex]::Escape("$version-$runtime-") + '[0-9a-f]{12}$')) {
        throw "Invalid build identity: $runtime"
    }
    if ($env:GITHUB_SHA -and $record.SourceCommit -cne $env:GITHUB_SHA) { throw 'Build source differs from the publishing workflow.' }
    if ($file.Length -lt 2) { throw 'Empty installer.' }
    $stream = $file.OpenRead()
    try { if ($stream.ReadByte() -ne 0x4d -or $stream.ReadByte() -ne 0x5a) { throw 'Installer is not a Windows EXE.' } }
    finally { $stream.Dispose() }
    [pscustomobject]@{
        Name = $file.Name; Path = $file.FullName; Size = $file.Length; Runtime = $runtime
        Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        SourceCommit = $record.SourceCommit; ReleaseId = $record.ReleaseId
    }
})
if (@($packages.Runtime | Sort-Object -Unique).Count -ne $packages.Count -or
    @($packages.SourceCommit | Sort-Object -Unique).Count -ne 1) { throw 'Duplicate architecture or mixed source commits.' }
$plan = [pscustomobject]@{ Repository = $Repository; Tag = $Tag; TestPackage = $testPackage; Packages = $packages }
if ($PlanOnly) { $plan | ConvertTo-Json -Depth 6; return }
if ($Repository -cne 'VendettaMask/opticalsystem') { throw 'OTA signing is restricted to the product repository.' }
if (-not $env:GH_TOKEN) { throw 'GH_TOKEN is required only for publishing.' }
if (-not $NotesFile) { throw 'Release notes are required.' }
$notes = Get-Content -LiteralPath $NotesFile -Raw
# Fail closed BEFORE any release mutation. The private key is used only in memory.
# Test packages stay outside the stable OTA channel; a bootstrap is installed manually.
$otaMarkers = @()
if (-not $testPackage) {
    . (Join-Path $PSScriptRoot 'ota-key-crypto.ps1')
    $otaPublicKey = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'src/OpticalSystemDesign.App/Assets/Updates/ota-public-key.pem') -Raw
    $otaRsa = Get-OtaSigningRsa $otaPublicKey
    try { $otaMarkers = @($packages | ForEach-Object { New-OtaReleaseEnvelope $otaRsa $_ $version $Tag }) }
    finally { $otaRsa.Dispose(); $env:OPTICAL_OTA_SIGNING_KEY = $null; $env:OPTICAL_OTA_RECOVERY_PASSWORD = $null; $env:OPTICAL_OTA_ENCRYPTED_BACKUP = $null }
}
$evidence = Join-Path $directory 'release-evidence'
[void][IO.Directory]::CreateDirectory($evidence)
$plan | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'plan.json') -Encoding utf8
$base = "https://api.github.com/repos/$Repository"
$headers = @{ Authorization = "Bearer $env:GH_TOKEN"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28'; 'User-Agent' = 'OpticalSystemDesign-Release' }
function Invoke-ReleaseApi([string]$Method, [string]$Route, $Body = $null) {
    $arguments = @{ Method = $Method; Uri = "$base/$Route"; Headers = $headers }
    if ($null -ne $Body) { $arguments.Body = $Body | ConvertTo-Json -Depth 8; $arguments.ContentType = 'application/json' }
    # Invoke-RestMethod can emit a JSON array as one pipeline object. Explicitly
    # enumerate its result so callers do not receive a nested asset collection.
    $result = Invoke-RestMethod @arguments
    foreach ($item in $result) { $item }
}
function Assert-UploadedAsset($Asset, $Package) {
    if ($Asset.state -cne 'uploaded' -or $Asset.size -ne $Package.Size -or
        $Asset.digest -cne "sha256:$($Package.Sha256)") { throw "Uploaded EXE verification failed: $($Package.Name)" }
}
function Send-Installer($Release, $Package, [string]$Name) {
    $upload = "https://uploads.github.com/repos/$Repository/releases/$($Release.id)/assets?name=$([Uri]::EscapeDataString($Name))"
    $client = [Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromMinutes(25)
    foreach ($key in $headers.Keys) { [void]$client.DefaultRequestHeaders.TryAddWithoutValidation($key, $headers[$key]) }
    $stream = [IO.File]::OpenRead($Package.Path)
    $content = [Net.Http.StreamContent]::new($stream)
    $content.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('application/vnd.microsoft.portable-executable')
    $response = $null
    try {
        $response = $client.PostAsync($upload, $content).GetAwaiter().GetResult()
        [void]$response.EnsureSuccessStatusCode()
        $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    } finally {
        if ($response) { $response.Dispose() }
        $content.Dispose(); $stream.Dispose(); $client.Dispose()
    }
}

# A manual refresh may use newer main sources but must never move the old tag.
$null = Invoke-ReleaseApi GET "git/ref/tags/$Tag"
$release = $null
try { $release = Invoke-ReleaseApi GET "releases/tags/$Tag" }
catch { if ([int]$_.Exception.Response.StatusCode -ne 404) { throw } }
if (-not $release) {
    $release = Invoke-ReleaseApi POST 'releases' @{ tag_name = $Tag; name = "Optical System Design $version"; draft = $true; prerelease = $testPackage; make_latest = 'false' }
}
if ($release.immutable) { throw 'The release is immutable.' }
$assets = @(Invoke-ReleaseApi GET "releases/$($release.id)/assets?per_page=100")
if ($assets.Count -ge 100) { throw 'Too many release assets; review manually.' }
if ($assets.Count -gt 0 -and -not $ReplaceExisting) { throw 'Existing attachments require explicit -ReplaceExisting.' }
$canonicalPattern = '^OpticalSystemDesign-' + [regex]::Escape($version) + '-win-(?:x64|arm64)-' + $suffix + '-Setup\.exe$'
$sidecarPattern = '^(?:release(?:-win-(?:x64|arm64))?\.json|OpticalSystemDesign-' + [regex]::Escape($version) + '-win-(?:x64|arm64)-' + $suffix + '-Setup\.exe\.sha256)$'
foreach ($asset in $assets) {
    if ($asset.name -cnotmatch $canonicalPattern -and $asset.name -cnotmatch $sidecarPattern) { throw "Unrecognized release asset; preserved for review: $($asset.name)" }
}
$previous = @($assets | Where-Object { $_.name -cin $packages.Name })
$sidecars = @($assets | Where-Object { $_.name -cmatch $sidecarPattern })
# Back up all managed attachments before changing them; retain the backup as an
# Actions workflow artifact, never as a user-facing release attachment.
foreach ($asset in @($previous) + @($sidecars)) {
    $backup = Join-Path $evidence $asset.name
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $backup
    if ((Get-Item -LiteralPath $backup).Length -ne $asset.size -or
        "sha256:$((Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash.ToLowerInvariant())" -cne $asset.digest) { throw 'Previous asset backup verification failed; release unchanged.' }
}
$assets | ConvertTo-Json -Depth 8 -AsArray | Set-Content -LiteralPath (Join-Path $evidence 'previous-assets.json') -Encoding utf8
$operation = if ($env:GITHUB_RUN_ID) { "$env:GITHUB_RUN_ID-$env:GITHUB_RUN_ATTEMPT" } else { [Guid]::NewGuid().ToString('N') }
$staged = [Collections.Generic.List[object]]::new()
$renamed = [Collections.Generic.List[object]]::new()
$promoted = [Collections.Generic.List[object]]::new()
try {
    foreach ($package in $packages) {
        $pendingName = "pending-$operation-$($package.Name)"
        $uploaded = Send-Installer $release $package $pendingName
        $staged.Add([pscustomobject]@{ Asset = $uploaded; Package = $package; PendingName = $pendingName })
        Assert-UploadedAsset $uploaded $package
    }
    foreach ($asset in $previous) {
        $renamed.Add($asset)
        $null = Invoke-ReleaseApi PATCH "releases/assets/$($asset.id)" @{ name = "previous-$operation-$($asset.name)" }
    }
    foreach ($entry in $staged) {
        $promoted.Add($entry)
        $uploaded = Invoke-ReleaseApi PATCH "releases/assets/$($entry.Asset.id)" @{ name = $entry.Package.Name }
        Assert-UploadedAsset $uploaded $entry.Package
    }
} catch {
    $failure = $_
    # Restore canonical old download names on promotion errors. Never delete an old EXE.
    $rollbackComplete = $true
    foreach ($entry in $promoted) {
        try { $null = Invoke-ReleaseApi PATCH "releases/assets/$($entry.Asset.id)" @{ name = $entry.PendingName } }
        catch { $rollbackComplete = $false; Write-Warning 'New asset could not be renamed during rollback.' }
    }
    foreach ($asset in $renamed) {
        try { $null = Invoke-ReleaseApi PATCH "releases/assets/$($asset.id)" @{ name = $asset.name } }
        catch { $rollbackComplete = $false; Write-Warning 'Previous download name needs manual recovery; previous bytes and local backup retained.' }
    }
    if ($rollbackComplete) {
        foreach ($entry in $staged) {
            try { $null = Invoke-ReleaseApi DELETE "releases/assets/$($entry.Asset.id)" }
            catch { Write-Warning 'A pending upload remains for manual review.' }
        }
    }
    throw $failure
}
# Every replacement EXE is now fully uploaded and hash-verified under its download name.
foreach ($asset in @($renamed) + @($sidecars)) { $null = Invoke-ReleaseApi DELETE "releases/assets/$($asset.id)" }
$runUrl = if ($env:GITHUB_RUN_ID) { "https://github.com/$Repository/actions/runs/$env:GITHUB_RUN_ID" } else { '' }
$details = @('', '## 构建来源', '', "源码提交：$($packages[0].SourceCommit)", "云端记录：$runUrl", '')
foreach ($package in $packages) { $details += "- $($package.Name) | SHA-256: $($package.Sha256) | 发布 ID: $($package.ReleaseId)" }
$details += $otaMarkers
$latest = if ($testPackage) { 'false' } else { 'true' }
$release = Invoke-ReleaseApi PATCH "releases/$($release.id)" @{ name = "Optical System Design $version$(if ($testPackage) { ' 测试版' })"; body = $notes.TrimEnd() + "`n" + ($details -join "`n"); draft = $false; prerelease = $testPackage; make_latest = $latest }
$final = @(Invoke-ReleaseApi GET "releases/$($release.id)/assets?per_page=100")
foreach ($asset in $final) { if ($asset.name -cnotmatch $canonicalPattern) { throw 'Unexpected non-EXE attachment remains.' } }
foreach ($package in $packages) {
    $matches = @($final | Where-Object name -CEQ $package.Name)
    if ($matches.Count -ne 1) { throw 'Canonical installer is missing or duplicated.' }
    Assert-UploadedAsset $matches[0] $package
}
[pscustomobject]@{ PublishedUtc = [DateTime]::UtcNow.ToString('o'); ReleaseUrl = $release.html_url; WorkflowUrl = $runUrl; Plan = $plan; Assets = $final; RemovedAssetIds = @(@($renamed) + @($sidecars) | ForEach-Object id) } |
    ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $evidence 'published.json') -Encoding utf8
Write-Host "Published and verified EXE-only release: $($release.html_url)"
