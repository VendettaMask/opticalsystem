#Requires -Version 7.0
# Produces signed public metadata only. Never publishes a release or writes GitHub.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$DistributionDirectory,
      [Parameter(Mandatory)][string]$Tag,
      [Parameter(Mandatory)][string]$ExistingFeed,
      [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'Mac signing requires the pinned native Sparkle tools.' }
$repository = Split-Path -Parent $PSScriptRoot
$distribution = (Resolve-Path -LiteralPath $DistributionDirectory).Path
$record = Get-Content -LiteralPath (Join-Path $distribution 'release-osx-arm64.json') -Raw | ConvertFrom-Json
if ($record.MacOta -ne $true -or $record.MacOtaFramework -cne 'Sparkle 2.10.1' -or
    $record.SourceDirty -ne $false -or $record.SourceCommit -cnotmatch '^[0-9a-f]{40}$') { throw 'Invalid Mac OTA build identity.' }
$privateDirectory = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($privateDirectory)
[IO.File]::SetUnixFileMode($privateDirectory, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute)
try {
    & (Join-Path $PSScriptRoot 'mac-ota-key.ps1') -OutputPath (Join-Path $privateDirectory 'seed')
    $signing = Join-Path $privateDirectory 'signing-key'
    $public = & swift (Join-Path $PSScriptRoot 'mac-ota-key.swift') (Join-Path $privateDirectory 'seed') $signing
    if ($LASTEXITCODE -or $public.Trim() -cne (Get-Content -LiteralPath (Join-Path $repository 'packaging/macos/ota-public-key.txt') -Raw).Trim()) { throw 'Derived Mac signing identity mismatch.' }
    & bash (Join-Path $PSScriptRoot 'fetch-sparkle.sh') (Join-Path $privateDirectory 'dependency')
    if ($LASTEXITCODE) { throw 'Pinned Sparkle download failed.' }
    $tool = Join-Path $privateDirectory 'dependency/Sparkle-2.10.1/bin/sign_update'
    $name = "OpticalSystemDesign-$($record.Version)-osx-arm64-$(if ($record.TestPackage) { 'AdHocTest' } else { 'AdHoc' }).dmg"
    $dmg = Join-Path $distribution $name
    if ((Get-FileHash -LiteralPath $dmg -Algorithm SHA256).Hash.ToLowerInvariant() -cne $record.PackageSha256) { throw 'Mac archive differs from its build record.' }
    $signature = & $tool --ed-key-file $signing -p $dmg
    if ($LASTEXITCODE) { throw 'Mac archive signing failed.' }
    & $tool --ed-key-file $signing --verify $dmg $signature
    if ($LASTEXITCODE) { throw 'Mac archive signature self-check failed.' }
    [void][IO.Directory]::CreateDirectory($OutputDirectory)
    $entryPath = Join-Path $OutputDirectory 'macos-ota-entry.json'
    @{ version = $record.Version; bundleVersion = $record.MacBundleVersion; name = $name; tag = $Tag;
       size = (Get-Item -LiteralPath $dmg).Length; signature = $signature.Trim() } |
        ConvertTo-Json | Set-Content -LiteralPath $entryPath -Encoding utf8NoBOM
    if ([IO.File]::ReadAllText((Resolve-Path -LiteralPath $ExistingFeed)).Contains('<item>')) {
        & $tool --ed-key-file $signing --verify $ExistingFeed
        if ($LASTEXITCODE) { throw 'Existing Mac feed signature rejected.' }
    }
    $feed = Join-Path $OutputDirectory 'appcast.xml'
    & python3 (Join-Path $PSScriptRoot 'macos-ota-feed.py') merge $ExistingFeed $entryPath $feed
    if ($LASTEXITCODE) { throw 'Mac feed validation failed.' }
    & $tool --ed-key-file $signing $feed
    if ($LASTEXITCODE) { throw 'Mac feed signing failed.' }
    & $tool --ed-key-file $signing --verify $feed
    if ($LASTEXITCODE) { throw 'Mac feed signature self-check failed.' }
    @{ BundleVersion = $record.MacBundleVersion; Archive = $name; PublicKey = $public.Trim();
       SourceCommit = $record.SourceCommit; ArchiveSha256 = $record.PackageSha256;
       ArchiveSignatureVerified = $true; FeedSignatureVerified = $true; Published = $false } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'macos-ota-verification.json') -Encoding utf8NoBOM
} finally { Remove-Item -LiteralPath $privateDirectory -Recurse -Force }
