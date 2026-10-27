#Requires -Version 7.0
# Activates only metadata whose Mac archive is already verified as published.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$DistributionDirectory)
$ErrorActionPreference = 'Stop'
$directory = (Resolve-Path -LiteralPath $DistributionDirectory).Path
$evidence = Join-Path $directory 'release-evidence'
$entryPath = Join-Path $evidence 'macos-ota-entry.json'
if (-not (Test-Path -LiteralPath $entryPath)) { return } # Windows-only refresh
if (-not $env:GH_TOKEN) { throw 'Publishing credentials are required.' }
$entry = Get-Content -LiteralPath $entryPath -Raw | ConvertFrom-Json
$verification = Get-Content -LiteralPath (Join-Path $evidence 'macos-ota-verification.json') -Raw | ConvertFrom-Json
$published = Get-Content -LiteralPath (Join-Path $evidence 'published.json') -Raw | ConvertFrom-Json
$base = Get-Content -LiteralPath (Join-Path $evidence 'macos-feed-base.json') -Raw | ConvertFrom-Json
$asset = @($published.Assets | Where-Object name -CEQ $entry.name)
if ($verification.ArchiveSignatureVerified -ne $true -or $verification.FeedSignatureVerified -ne $true -or
    $verification.Archive -cne $entry.name -or $asset.Count -ne 1 -or $asset[0].size -ne $entry.size -or
    $asset[0].digest -cne "sha256:$($verification.ArchiveSha256)" -or
    $published.ReleaseUrl -cne "https://github.com/VendettaMask/opticalsystem/releases/tag/$($entry.tag)") {
    throw 'Mac feed target is not the verified published archive.'
}
$feed = Join-Path $evidence 'appcast.xml'
$expected = [IO.File]::ReadAllBytes($feed)
$api = 'https://api.github.com/repos/VendettaMask/opticalsystem/contents/updates/macos/appcast.xml'
$headers = @{ Authorization = "Bearer $env:GH_TOKEN"; Accept = 'application/vnd.github+json'; 'User-Agent' = 'OpticalSystemDesign-MacOTA' }
$current = Invoke-RestMethod -Uri "$($api)?ref=main" -Headers $headers
if ($current.sha -cne $base.sha) { throw 'The Mac feed changed during publishing; regenerate instead of overwriting it.' }
$payload = @{ message = "Update signed Mac OTA feed for $($entry.version) ($($entry.bundleVersion)) [skip ci]";
    content = [Convert]::ToBase64String($expected); sha = $base.sha; branch = 'main' } | ConvertTo-Json
$commit = Invoke-RestMethod -Method Put -Uri $api -Headers $headers -ContentType 'application/json' -Body $payload
$readback = Invoke-RestMethod -Uri "$($api)?ref=main" -Headers $headers
if ($readback.sha -cne $commit.content.sha -or -not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals(
    [Convert]::FromBase64String($readback.content), $expected)) { throw 'Published Mac feed readback mismatch.' }
@{ Feed = 'https://raw.githubusercontent.com/VendettaMask/opticalsystem/main/updates/macos/appcast.xml';
   FeedCommit = $commit.commit.sha; FeedSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($expected)).ToLowerInvariant();
   BundleVersion = $entry.bundleVersion; Archive = $entry.name; ArchiveSha256 = $verification.ArchiveSha256;
   PublicKey = $verification.PublicKey; SourceCommit = $verification.SourceCommit;
   ArchiveSignatureVerified = $true; FeedSignatureVerified = $true; Published = $true } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'macos-ota-published.json') -Encoding utf8
Write-Host 'Verified Mac archive and signed appcast are published.'
