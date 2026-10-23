#Requires -Version 7.0
# Shared by the EXE publisher and the private GitHub vault. No platform-bound protection.
Set-StrictMode -Version Latest

function Get-OtaKeyId([Security.Cryptography.RSA]$Rsa) {
    if ($Rsa.KeySize -ne 4096) { throw 'OTA requires an RSA-4096 key.' }
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant()
}

function Assert-OtaPublicKey([Security.Cryptography.RSA]$Rsa, [string]$TrustedPublicKey) {
    $trusted = [Security.Cryptography.RSA]::Create()
    try {
        $trusted.ImportFromPem($TrustedPublicKey)
        if ((Get-OtaKeyId $Rsa) -cne (Get-OtaKeyId $trusted)) { throw 'The signing key does not match the trusted product public key.' }
    } finally { $trusted.Dispose() }
}

function Assert-OtaRecoveryPassword([string]$Password) {
    if ([string]::IsNullOrWhiteSpace($Password) -or $Password.Length -lt 20 -or $Password.Length -gt 1024 -or $Password.Contains("`n") -or $Password.Contains("`r")) {
        throw 'Set OPTICAL_OTA_RECOVERY_PASSWORD to a unique password of 20 or more characters (or six random words), without line breaks. Never put it in a workflow input.'
    }
}

function Get-OtaBackupAad([string]$KeyId) {
    [Text.Encoding]::UTF8.GetBytes("OpticalSystemDesign-OTA-Backup-v3|VendettaMask/opticalsystem|OpticalSystemDesign-8D387EA8-F349-447F-B5D3-B58E27F46D4A|$KeyId")
}

function New-OtaPortableBackup([Security.Cryptography.RSA]$Rsa, [string]$Password, [string]$TrustedPublicKey) {
    Assert-OtaRecoveryPassword $Password
    Assert-OtaPublicKey $Rsa $TrustedPublicKey
    $keyId = Get-OtaKeyId $Rsa
    $salt = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    $nonce = [Security.Cryptography.RandomNumberGenerator]::GetBytes(12)
    $derived = [Security.Cryptography.Rfc2898DeriveBytes]::Pbkdf2($Password, $salt, 600000, [Security.Cryptography.HashAlgorithmName]::SHA256, 32)
    $private = $null; $aes = $null
    try {
        $private = $Rsa.ExportPkcs8PrivateKey()
        $ciphertext = [byte[]]::new($private.Length)
        $tag = [byte[]]::new(16)
        $aes = [Security.Cryptography.AesGcm]::new($derived, 16)
        $aes.Encrypt($nonce, $private, $ciphertext, $tag, (Get-OtaBackupAad $keyId))
        [ordered]@{
            Format = 3; Repository = 'VendettaMask/opticalsystem'
            ProductAppId = 'OpticalSystemDesign-8D387EA8-F349-447F-B5D3-B58E27F46D4A'
            KeyId = $keyId; Algorithm = 'AES-256-GCM'; Kdf = 'PBKDF2-HMAC-SHA256'; Iterations = 600000
            Salt = [Convert]::ToBase64String($salt); Nonce = [Convert]::ToBase64String($nonce)
            Tag = [Convert]::ToBase64String($tag); EncryptedPkcs8 = [Convert]::ToBase64String($ciphertext)
            CreatedUtc = [DateTime]::UtcNow.ToString('o')
        } | ConvertTo-Json
    } finally {
        if ($private) { [Security.Cryptography.CryptographicOperations]::ZeroMemory($private) }
        [Security.Cryptography.CryptographicOperations]::ZeroMemory($derived)
        if ($aes) { $aes.Dispose() }
    }
}

function Read-OtaPortableBackup([string]$Json, [string]$Password, [string]$TrustedPublicKey) {
    Assert-OtaRecoveryPassword $Password
    if (-not $Json -or $Json.Length -gt 32768) { throw 'Invalid OTA backup size.' }
    $derived = $null; $private = $null; $aes = $null; $rsa = $null
    try {
        $record = $Json | ConvertFrom-Json -ErrorAction Stop
        if ($record.Format -ne 3 -or $record.Repository -cne 'VendettaMask/opticalsystem' -or
            $record.ProductAppId -cne 'OpticalSystemDesign-8D387EA8-F349-447F-B5D3-B58E27F46D4A' -or
            $record.Algorithm -cne 'AES-256-GCM' -or $record.Kdf -cne 'PBKDF2-HMAC-SHA256' -or
            $record.Iterations -ne 600000 -or $record.KeyId -cnotmatch '^[0-9a-f]{64}$') { throw 'Invalid backup format.' }
        $salt = [Convert]::FromBase64String($record.Salt); $nonce = [Convert]::FromBase64String($record.Nonce)
        $tag = [Convert]::FromBase64String($record.Tag); $ciphertext = [Convert]::FromBase64String($record.EncryptedPkcs8)
        if ($salt.Length -ne 32 -or $nonce.Length -ne 12 -or $tag.Length -ne 16 -or $ciphertext.Length -lt 1024 -or $ciphertext.Length -gt 8192) { throw 'Invalid backup lengths.' }
        $derived = [Security.Cryptography.Rfc2898DeriveBytes]::Pbkdf2($Password, $salt, 600000, [Security.Cryptography.HashAlgorithmName]::SHA256, 32)
        $private = [byte[]]::new($ciphertext.Length)
        $aes = [Security.Cryptography.AesGcm]::new($derived, 16)
        $aes.Decrypt($nonce, $ciphertext, $tag, $private, (Get-OtaBackupAad $record.KeyId))
        $rsa = [Security.Cryptography.RSA]::Create()
        $read = 0
        $rsa.ImportPkcs8PrivateKey($private, [ref]$read)
        if ($read -ne $private.Length -or (Get-OtaKeyId $rsa) -cne $record.KeyId) { throw 'Backup identity mismatch.' }
        Assert-OtaPublicKey $rsa $TrustedPublicKey
        $result = $rsa; $rsa = $null
        return $result # Caller owns/disposes the RSA; never emit its private bytes.
    } catch { throw 'OTA backup recovery rejected. Check the password, backup integrity and trusted public key; no signing key was changed.' }
    finally {
        if ($derived) { [Security.Cryptography.CryptographicOperations]::ZeroMemory($derived) }
        if ($private) { [Security.Cryptography.CryptographicOperations]::ZeroMemory($private) }
        if ($aes) { $aes.Dispose() }
        if ($rsa) { $rsa.Dispose() }
    }
}

function Get-OtaSigningRsa([string]$TrustedPublicKey) {
    $rsa = $null
    try {
        if ($env:OPTICAL_OTA_SIGNING_KEY) {
            $rsa = [Security.Cryptography.RSA]::Create()
            try { $rsa.ImportFromPem($env:OPTICAL_OTA_SIGNING_KEY) }
            catch { throw 'The signing Secret is not a valid private key.' }
        } elseif ($env:OPTICAL_OTA_ENCRYPTED_BACKUP -and $env:OPTICAL_OTA_RECOVERY_PASSWORD) {
            $rsa = Read-OtaPortableBackup $env:OPTICAL_OTA_ENCRYPTED_BACKUP $env:OPTICAL_OTA_RECOVERY_PASSWORD $TrustedPublicKey
        } else { throw 'Configure the OTA signing Secret, or the encrypted backup and recovery password Secrets.' }
        Assert-OtaPublicKey $rsa $TrustedPublicKey
        # Prove possession of the private key; an imported public key alone must fail.
        $challenge = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
        $signature = $rsa.SignData($challenge, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)
        if (-not $rsa.VerifyData($challenge, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)) { throw 'Signing-key self-check failed.' }
        $result = $rsa; $rsa = $null
        return $result
    } finally { if ($rsa) { $rsa.Dispose() } }
}

function New-OtaReleaseEnvelope([Security.Cryptography.RSA]$Rsa, $Package, [string]$Version, [string]$Tag) {
    $test = $Tag -cmatch ('^v' + [regex]::Escape($Version) + '-test\.[0-9]{8}$')
    $expectedName = "OpticalSystemDesign-$Version-$($Package.Runtime)-$(if ($test) { 'UnsignedTest' } else { 'Unsigned' })-Setup.exe"
    if ($Version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or (-not $test -and $Tag -cne "v$Version") -or
        $Package.Runtime -cnotin @('win-x64', 'win-arm64') -or $Package.Size -le 2 -or $Package.Size -gt 2147483648 -or
        $Package.Name -cne $expectedName -or
        $Package.Sha256 -cnotmatch '^[0-9a-f]{64}$' -or $Package.SourceCommit -cnotmatch '^[0-9a-f]{40}$' -or
        $Package.ReleaseId -cnotmatch ('^' + [regex]::Escape("$Version-$($Package.Runtime)-") + '[0-9a-f]{12}$')) {
        throw 'Invalid OTA signing payload.'
    }
    if ($test -and ($Package.PSObject.Properties['BuildSequence'] -eq $null -or $Package.BuildSequence -isnot [long] -or $Package.BuildSequence -le 0)) {
        throw 'Test OTA requires a positive cloud build sequence.'
    }
    $payload = [ordered]@{
        Format = if ($test) { 2 } else { 1 }; ProductAppId = 'OpticalSystemDesign-8D387EA8-F349-447F-B5D3-B58E27F46D4A'
        Repository = 'VendettaMask/opticalsystem'; Version = $Version; Runtime = $Package.Runtime
        Name = $Package.Name; Size = [long]$Package.Size; Sha256 = $Package.Sha256; Tag = $Tag
        ReleaseId = $Package.ReleaseId; SourceCommit = $Package.SourceCommit
    }
    if ($test) { $payload.BuildSequence = $Package.BuildSequence }
    $payload = $payload | ConvertTo-Json -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($payload)
    $signature = $Rsa.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)
    "<!-- optical-system-design-ota-v1`n$([Convert]::ToBase64String($bytes)).$([Convert]::ToBase64String($signature))`n-->"
}
