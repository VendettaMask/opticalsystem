# Shared signing helpers. No private keys or passwords are passed on a command line.
Set-StrictMode -Version Latest

function Get-WindowsSigningConfiguration {
    param([string]$CertificateThumbprint, [string]$SignToolPath, [string]$TimestampUrl,
          [switch]$MachineCertificateStore)
    if ($CertificateThumbprint -notmatch '^[a-fA-F0-9]{40}$') {
        throw 'SigningMode Required needs an explicit code-signing certificate thumbprint. Use SigningMode Unsigned for the default certificate-free package.'
    }
    $store = if ($MachineCertificateStore) { 'Cert:\LocalMachine\My' } else { 'Cert:\CurrentUser\My' }
    $certificate = Get-Item -LiteralPath (Join-Path $store $CertificateThumbprint) -ErrorAction Stop
    if (-not $certificate.HasPrivateKey -or $certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -le (Get-Date)) {
        throw 'The selected certificate is expired, not yet valid, or has no available private key.'
    }
    if (@($certificate.EnhancedKeyUsageList | Where-Object { $_.ObjectId.Value -eq '1.3.6.1.5.5.7.3.3' }).Count -eq 0) {
        throw 'The selected certificate is not a code-signing certificate.'
    }
    if (-not $SignToolPath) {
        $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
        if ($command) { $SignToolPath = $command.Source }
    }
    if (-not $SignToolPath -or -not (Test-Path -LiteralPath $SignToolPath -PathType Leaf)) {
        throw 'An explicit Windows SDK SignTool path is required.'
    }
    $timestamp = $null
    if (-not [Uri]::TryCreate($TimestampUrl, [UriKind]::Absolute, [ref]$timestamp) -or
        $timestamp.Scheme -notin @('https', 'http') -or $timestamp.UserInfo -or
        $TimestampUrl -match '["\r\n]') {
        throw 'An explicit RFC 3161 timestamp HTTP(S) URL is required.'
    }
    [pscustomobject]@{
        Thumbprint = $certificate.Thumbprint
        SignTool = (Get-Item -LiteralPath $SignToolPath).FullName
        TimestampUrl = $TimestampUrl
        MachineStore = [bool]$MachineCertificateStore
    }
}

function Invoke-WindowsFileSigning {
    param([Parameter(Mandatory)]$Configuration, [Parameter(Mandatory)][string]$Path)
    $arguments = @('sign', '/sha1', $Configuration.Thumbprint, '/s', 'My', '/fd', 'SHA256',
        '/tr', $Configuration.TimestampUrl, '/td', 'SHA256')
    if ($Configuration.MachineStore) { $arguments += '/sm' }
    & $Configuration.SignTool @arguments $Path | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Signing failed: $Path" }
    Assert-WindowsFileSignature -Configuration $Configuration -Path $Path
}

function Assert-WindowsFileSignature {
    param([Parameter(Mandatory)]$Configuration, [Parameter(Mandatory)][string]$Path)
    & $Configuration.SignTool verify /pa /all /tw $Path | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Trusted Authenticode verification failed: $Path" }
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or -not $signature.TimeStamperCertificate -or
        $signature.SignerCertificate.Thumbprint -ne $Configuration.Thumbprint) {
        throw "Signature, publisher or timestamp verification failed: $Path"
    }
}
