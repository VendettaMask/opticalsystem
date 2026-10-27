#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ota-key-crypto.ps1')
$rsa = $null; $seed = $null
try {
    $public = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../src/OpticalSystemDesign.App/Assets/Updates/ota-public-key.pem') -Raw
    $rsa = Get-OtaSigningRsa $public
    $seed = [byte[]](Get-MacOtaSeed $rsa)
    # Caller uses a private temporary directory, removes it in finally, and
    # never includes it in Actions artifacts. This file contains only the seed.
    $stream = [IO.FileStream]::new($OutputPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        if (-not $IsWindows) { [IO.File]::SetUnixFileMode($OutputPath, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite) }
        $stream.Write($seed); $stream.Flush($true)
    } finally { $stream.Dispose() }
} finally {
    if ($seed) { [Security.Cryptography.CryptographicOperations]::ZeroMemory($seed) }
    if ($rsa) { $rsa.Dispose() }
    $env:OPTICAL_OTA_SIGNING_KEY = $null
    $env:OPTICAL_OTA_ENCRYPTED_BACKUP = $null
    $env:OPTICAL_OTA_RECOVERY_PASSWORD = $null
}
