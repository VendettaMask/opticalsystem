param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceCommit,
    [Parameter(Mandatory)][long]$CloudRunId
)
$ErrorActionPreference = 'Stop'
$source = $SourceCommit
$runId = $CloudRunId
$repository = 'VendettaMask/opticalsystem'
$tag = 'v1.0.4-test.20261019'
$release = gh api "repos/$repository/releases/tags/$tag" | ConvertFrom-Json
if ($LASTEXITCODE) { throw 'Cannot read published release' }
$run = gh api "repos/$repository/actions/runs/$runId" | ConvertFrom-Json
if ($LASTEXITCODE -or $run.head_sha -cne $source -or $run.conclusion -cne 'success') { throw 'Cloud run source or completion mismatch' }
if ($release.draft -or -not $release.prerelease -or $release.tag_name -cne $tag) { throw 'Unexpected release identity' }
$key = Join-Path $PSScriptRoot '../../../../src/OpticalSystemDesign.App/Assets/Updates/ota-public-key.pem'
$rsa = [Security.Cryptography.RSA]::Create()
try {
    $rsa.ImportFromPem([IO.File]::ReadAllText($key))
    $windows = @([regex]::Matches($release.body.Replace("`r`n","`n"), '<!-- optical-system-design-ota-v1\n([A-Za-z0-9+/=]+)\.([A-Za-z0-9+/=]+)\n-->') | ForEach-Object {
        $bytes = [Convert]::FromBase64String($_.Groups[1].Value)
        if (-not $rsa.VerifyData($bytes, [Convert]::FromBase64String($_.Groups[2].Value), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)) { throw 'Published Windows OTA signature rejected' }
        $p = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
        $a = @($release.assets | Where-Object name -CEQ $p.Name)
        if ($a.Count -ne 1 -or $p.SourceCommit -cne $source -or $p.BuildSequence -ne $runId -or $p.Tag -cne $tag -or $p.Format -ne 2 -or
            $p.Name -cnotmatch ("-Setup-" + $source.Substring(0,8) + '\.exe$') -or $p.Size -ne $a[0].size -or $a[0].digest -cne "sha256:$($p.Sha256)") { throw 'Published Windows source, build, size or SHA-256 mismatch' }
        [ordered]@{Runtime=$p.Runtime;Name=$p.Name;Bytes=$p.Size;Sha256=$p.Sha256;SourceCommit=$p.SourceCommit;BuildSequence=$p.BuildSequence;OtaSignatureVerified=$true;Url=$a[0].browser_download_url}
    })
} finally { $rsa.Dispose() }
if ($windows.Count -ne 2 -or @($windows.Runtime | Sort-Object -Unique).Count -ne 2 -or @($windows | Where-Object {$_.Runtime -notin @('win-x64','win-arm64')}).Count) { throw 'Unexpected Windows architecture set' }
$mac = @($release.assets | Where-Object name -CEQ 'OpticalSystemDesign-1.0.4-osx-arm64-AdHocTest.dmg')
if ($release.assets.Count -ne 3 -or $mac.Count -ne 1 -or $mac[0].digest -cnotmatch '^sha256:[0-9a-f]{64}$') { throw 'Unexpected release asset set' }
$macLine = @($release.body -split '\r?\n' | Where-Object { $_.Contains($mac[0].name) -and $_.Contains('SHA-256:') })
if ($macLine.Count -ne 1 -or -not $macLine[0].Contains($source) -or -not $macLine[0].Contains($mac[0].digest.Substring(7))) { throw 'Mac published provenance mismatch' }
$feedResponse = gh api "repos/$repository/contents/updates/macos/appcast.xml?ref=main" | ConvertFrom-Json
if ($LASTEXITCODE) { throw 'Cannot read active Mac feed' }
$feedBytes = [Convert]::FromBase64String($feedResponse.content)
[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'published-appcast.xml'), $feedBytes)
$feed = [xml][Text.Encoding]::UTF8.GetString($feedBytes)
$enclosure = @($feed.rss.channel.item.enclosure | Where-Object url -CEQ $mac[0].browser_download_url)
if ($enclosure.Count -ne 1 -or [long]$enclosure[0].length -ne $mac[0].size -or -not $enclosure[0].GetAttribute('edSignature','http://www.andymatuschak.org/xml-namespaces/sparkle')) { throw 'Active Mac appcast attachment mismatch' }
$result = [ordered]@{VerifiedAt=[DateTimeOffset]::Now.ToString('o');SourceCommit=$source;RunId=$runId;RunUrl=$run.html_url;ReleaseUrl=$release.html_url;Tag=$tag;RunConclusion=$run.conclusion;Windows=$windows;Mac=[ordered]@{Name=$mac[0].name;Bytes=$mac[0].size;Sha256=$mac[0].digest.Substring(7);Url=$mac[0].browser_download_url;ActiveFeedAttachmentVerified=$true;SignatureVerification='Sparkle archive/appcast signature validation performed by the successful cloud publish workflow; local check preserves feed bytes and verifies published attachment identity.'};Limitations=@('GitHub server digests checked against signed Windows metadata and published Mac provenance; complete installers were not downloaded again.','No local installation, restart or native user desktop acceptance.','The release workflow performs scoped delivery checks, not complete numerical certification.')}
$result | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $PSScriptRoot 'published-verification.json') -Encoding utf8
$result | ConvertTo-Json -Depth 8
