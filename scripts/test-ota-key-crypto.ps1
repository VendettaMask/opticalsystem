#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ota-key-crypto.ps1')
$passed = 0
function Expect-Rejected([scriptblock]$Action) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    if (-not $rejected) { throw 'A negative security check was incorrectly accepted.' }
}
$rsa = [Security.Cryptography.RSA]::Create(4096)
$other = [Security.Cryptography.RSA]::Create(4096)
$password = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$public = $rsa.ExportSubjectPublicKeyInfoPem()
$savedKey = $env:OPTICAL_OTA_SIGNING_KEY; $savedBackup = $env:OPTICAL_OTA_ENCRYPTED_BACKUP; $savedPassword = $env:OPTICAL_OTA_RECOVERY_PASSWORD
try {
    $json = New-OtaPortableBackup $rsa $password $public
    if ($json.Contains('BEGIN PRIVATE KEY') -or $json.Contains($password)) { throw 'Plaintext was included in a backup.' }; $passed++
    $restored = Read-OtaPortableBackup $json $password $public
    try {
        if ((Get-OtaKeyId $restored) -cne (Get-OtaKeyId $rsa)) { throw 'Portable restore changed the key.' }
        $message = [Text.Encoding]::UTF8.GetBytes('portable cloud OTA key recovery fixture')
        $signature = $restored.SignData($message, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)
        if (-not $rsa.VerifyData($message, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)) { throw 'Recovered key cannot sign.' }
        $passed++
    } finally { $restored.Dispose() }
    $again = New-OtaPortableBackup $rsa $password $public
    if (($json | ConvertFrom-Json).Salt -ceq ($again | ConvertFrom-Json).Salt -or ($json | ConvertFrom-Json).Nonce -ceq ($again | ConvertFrom-Json).Nonce) { throw 'Salt or nonce was reused.' }; $passed++
    Expect-Rejected { Read-OtaPortableBackup $json ($password + 'wrong') $public }; $passed++
    Expect-Rejected { Read-OtaPortableBackup $json $password $other.ExportSubjectPublicKeyInfoPem() }; $passed++
    foreach ($field in @('EncryptedPkcs8', 'Tag', 'Nonce', 'Salt')) {
        $record = $json | ConvertFrom-Json
        $bytes = [Convert]::FromBase64String($record.$field); $bytes[0] = $bytes[0] -bxor 1
        $record.$field = [Convert]::ToBase64String($bytes)
        Expect-Rejected { Read-OtaPortableBackup ($record | ConvertTo-Json) $password $public }; $passed++
    }
    foreach ($field in @('Format', 'Repository', 'ProductAppId', 'KeyId', 'Algorithm', 'Kdf', 'Iterations')) {
        $record = $json | ConvertFrom-Json
        $record.$field = 'invalid'
        Expect-Rejected { Read-OtaPortableBackup ($record | ConvertTo-Json) $password $public }; $passed++
    }
    Expect-Rejected { Read-OtaPortableBackup '{}' $password $public }; $passed++
    Expect-Rejected { Read-OtaPortableBackup ('x' * 32769) $password $public }; $passed++
    Expect-Rejected { New-OtaPortableBackup $rsa 'short' $public }; $passed++
    Expect-Rejected { New-OtaPortableBackup $rsa $password $other.ExportSubjectPublicKeyInfoPem() }; $passed++
    $env:OPTICAL_OTA_SIGNING_KEY = $rsa.ExportPkcs8PrivateKeyPem()
    $env:OPTICAL_OTA_ENCRYPTED_BACKUP = $null; $env:OPTICAL_OTA_RECOVERY_PASSWORD = $null
    $primary = Get-OtaSigningRsa $public
    try { if ((Get-OtaKeyId $primary) -cne (Get-OtaKeyId $rsa)) { throw 'Primary Secret identity mismatch.' }; $passed++ }
    finally { $primary.Dispose() }
    $env:OPTICAL_OTA_SIGNING_KEY = $null; $env:OPTICAL_OTA_ENCRYPTED_BACKUP = $json; $env:OPTICAL_OTA_RECOVERY_PASSWORD = $password
    $fallback = Get-OtaSigningRsa $public
    try { if ((Get-OtaKeyId $fallback) -cne (Get-OtaKeyId $rsa)) { throw 'Cloud-only fallback identity mismatch.' }; $passed++ }
    finally { $fallback.Dispose() }
    $env:OPTICAL_OTA_SIGNING_KEY = 'invalid'
    Expect-Rejected { Get-OtaSigningRsa $public }; $passed++ # Corrupt primary must not silently fall back.
    $env:OPTICAL_OTA_SIGNING_KEY = $public
    Expect-Rejected { Get-OtaSigningRsa $public }; $passed++ # Public key is insufficient.
    $env:OPTICAL_OTA_SIGNING_KEY = $null; $env:OPTICAL_OTA_ENCRYPTED_BACKUP = $null
    Expect-Rejected { Get-OtaSigningRsa $public }; $passed++
    $package = [pscustomobject]@{ Runtime = 'win-x64'; Size = 123456; Name = 'OpticalSystemDesign-99.0.0-win-x64-Unsigned-Setup.exe'; Sha256 = 'a' * 64; SourceCommit = 'b' * 40; ReleaseId = '99.0.0-win-x64-0123456789ab' }
    $marker = New-OtaReleaseEnvelope $rsa $package '99.0.0' 'v99.0.0'
    $envelope = ($marker -split "`n")[1] -split '\.'
    $payloadBytes = [Convert]::FromBase64String($envelope[0]); $signature = [Convert]::FromBase64String($envelope[1])
    if (-not $rsa.VerifyData($payloadBytes, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)) { throw 'Release envelope signature failed.' }; $passed++
    $payload = [Text.Encoding]::UTF8.GetString($payloadBytes) | ConvertFrom-Json
    if ($payload.Version -cne '99.0.0' -or $payload.Name -cne $package.Name -or @($payload.PSObject.Properties).Count -ne 11) { throw 'Release signing fields mismatch.' }; $passed++
    $payloadBytes[0] = $payloadBytes[0] -bxor 1
    if ($rsa.VerifyData($payloadBytes, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)) { throw 'Tampered release signature accepted.' }; $passed++
    Expect-Rejected { New-OtaReleaseEnvelope $rsa $package '99.0.0' 'v99.0.0-test.20261022' }; $passed++
    $testPackage = [pscustomobject]@{ Runtime = 'win-x64'; Size = 123456; Name = 'OpticalSystemDesign-99.0.0-win-x64-UnsignedTest-Setup.exe'; Sha256 = 'a' * 64; SourceCommit = 'b' * 40; ReleaseId = '99.0.0-win-x64-0123456789ab'; BuildSequence = 40000000000L }
    $testMarker = New-OtaReleaseEnvelope $rsa $testPackage '99.0.0' 'v99.0.0-test.20261023'
    $parts = ($testMarker -split "`n")[1] -split '\.'
    $testBytes = [Convert]::FromBase64String($parts[0])
    if (-not $rsa.VerifyData($testBytes, [Convert]::FromBase64String($parts[1]), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)) { throw 'Test release signature failed.' }; $passed++
    $testPayload = [Text.Encoding]::UTF8.GetString($testBytes) | ConvertFrom-Json
    if ($testPayload.Format -ne 2 -or $testPayload.BuildSequence -ne 40000000000L -or @($testPayload.PSObject.Properties).Count -ne 12) { throw 'Test release build identity mismatch.' }; $passed++
    $testPackage.BuildSequence = 0L
    Expect-Rejected { New-OtaReleaseEnvelope $rsa $testPackage '99.0.0' 'v99.0.0-test.20261023' }; $passed++
    $testPackage.BuildSequence = 40000000000L
    Expect-Rejected { New-OtaReleaseEnvelope $rsa $testPackage '99.0.0' 'v99.0.0' }; $passed++
    foreach ($codedPackage in @($package, $testPackage)) {
        $originalName = $codedPackage.Name
        $codedPackage.Name = $originalName -creplace '\.exe$', '-bbbbbbbb.exe'
        $codedTag = if ($codedPackage -eq $testPackage) { 'v99.0.0-test.20261023' } else { 'v99.0.0' }
        $codedMarker = New-OtaReleaseEnvelope $rsa $codedPackage '99.0.0' $codedTag
        $codedParts = ($codedMarker -split "`n")[1] -split '\.'
        $codedBytes = [Convert]::FromBase64String($codedParts[0])
        if (-not $rsa.VerifyData($codedBytes, [Convert]::FromBase64String($codedParts[1]), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)) { throw 'Coded installer signature failed.' }; $passed++
        $codedPackage.Name = $originalName -creplace '\.exe$', '-aaaaaaaa.exe'
        Expect-Rejected { New-OtaReleaseEnvelope $rsa $codedPackage '99.0.0' $codedTag }; $passed++
        $codedPackage.Name = $originalName
    }
    $package.Name = '../untrusted.exe'
    Expect-Rejected { New-OtaReleaseEnvelope $rsa $package '99.0.0' 'v99.0.0' }; $passed++
    Write-Host "Portable OTA encryption and recovery: $passed checks passed. Throwaway keys only; no device-bound APIs or product installation."
} finally {
    $rsa.Dispose(); $other.Dispose(); $password = $null
    $env:OPTICAL_OTA_SIGNING_KEY = $savedKey; $env:OPTICAL_OTA_ENCRYPTED_BACKUP = $savedBackup; $env:OPTICAL_OTA_RECOVERY_PASSWORD = $savedPassword
}
