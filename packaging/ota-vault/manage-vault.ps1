#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('Backup', 'Verify')][string]$Mode)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_REPOSITORY -cne 'VendettaMask/opticalsystem-ota-vault' -or $env:GITHUB_REF -cne 'refs/heads/main' -or -not $env:GH_TOKEN) {
    throw 'Run this operation only on main in the private OTA vault GitHub workflow.'
}
. (Join-Path $PSScriptRoot 'ota-key-crypto.ps1')
$headers = @{ Authorization = "Bearer $env:GH_TOKEN"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28'; 'User-Agent' = 'OpticalSystemDesign-OTAVault' }
$api = 'https://api.github.com/repos/VendettaMask/opticalsystem-ota-vault'
$repository = Invoke-RestMethod -Uri $api -Headers $headers
if ($repository.private -ne $true -or $repository.full_name -cne $env:GITHUB_REPOSITORY) { throw 'Vault is not private; operation refused.' }
$public = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'trusted-public-key.pem') -Raw
$trusted = [Security.Cryptography.RSA]::Create()
try { $trusted.ImportFromPem($public); $keyId = Get-OtaKeyId $trusted } finally { $trusted.Dispose() }
if ($keyId -cne '1d360b38844b1ba09b91786eea7c2c86ce1d6ebf900359174483ada5b613b521') { throw 'Unapproved trust-key rotation refused.' }
Assert-OtaRecoveryPassword $env:OPTICAL_OTA_RECOVERY_PASSWORD
$path = "backups/ota-$keyId.json"
$existing = $null
try { $existing = Invoke-RestMethod -Uri "$api/contents/$path`?ref=main" -Headers $headers }
catch { if ([int]$_.Exception.Response.StatusCode -ne 404) { throw 'Cannot inspect existing vault backup; nothing changed.' } }
$rsa = $null; $verified = $null
try {
    if ($Mode -eq 'Verify') {
        if (-not $existing) { throw 'No durable encrypted backup yet. Run Backup first.' }
        $json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($existing.content))
        $verified = Read-OtaPortableBackup $json $env:OPTICAL_OTA_RECOVERY_PASSWORD $public
    } else {
        $rsa = Get-OtaSigningRsa $public
        if ($existing) {
            # Never replace an existing backup just because a different password was entered.
            $json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($existing.content))
            $verified = Read-OtaPortableBackup $json $env:OPTICAL_OTA_RECOVERY_PASSWORD $public
            if ((Get-OtaKeyId $verified) -cne (Get-OtaKeyId $rsa)) { throw 'Existing backup identity differs; preserved unchanged.' }
        } else {
            $json = New-OtaPortableBackup $rsa $env:OPTICAL_OTA_RECOVERY_PASSWORD $public
            $verified = Read-OtaPortableBackup $json $env:OPTICAL_OTA_RECOVERY_PASSWORD $public
            $body = @{ message = 'Preserve password-encrypted OTA key backup'; branch = 'main'; content = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json)) } | ConvertTo-Json
            $null = Invoke-RestMethod -Method Put -Uri "$api/contents/$path" -Headers $headers -Body $body -ContentType 'application/json'
            # Read back the committed ciphertext and decrypt it, rather than only testing local memory.
            $committed = Invoke-RestMethod -Uri "$api/contents/$path`?ref=main" -Headers $headers
            $committedJson = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($committed.content))
            $readback = Read-OtaPortableBackup $committedJson $env:OPTICAL_OTA_RECOVERY_PASSWORD $public
            try { if ((Get-OtaKeyId $readback) -cne $keyId) { throw 'Committed backup verification failed.' } }
            finally { $readback.Dispose() }
        }
    }
    $challenge = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    $signature = $verified.SignData($challenge, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)
    if (-not $verified.VerifyData($challenge, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)) { throw 'Recovered signing key failed its self-check.' }
    # Public identifiers only. No private key, password or plaintext file is emitted.
    Write-Host "$Mode passed. Trusted key: $keyId. Durable encrypted backup: $path"
} finally {
    if ($rsa) { $rsa.Dispose() }; if ($verified) { $verified.Dispose() }
    $headers.Clear(); $env:OPTICAL_OTA_SIGNING_KEY = $null; $env:OPTICAL_OTA_RECOVERY_PASSWORD = $null
}
