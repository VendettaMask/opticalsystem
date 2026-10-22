#requires -Version 5.1
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [string]$OutputRoot = '',
    [string]$InnoSetupCompiler = '',
    [string]$AppVersion = '',
    [switch]$UnsignedTestPackage,
    [ValidateSet('Unsigned', 'Required')]
    [string]$SigningMode = 'Unsigned',
    [string]$CertificateThumbprint = '',
    [string]$SignToolPath = '',
    [string]$TimestampUrl = '',
    [switch]$MachineCertificateStore,
    # Stay within the production identity's known exact-key range. Longer names
    # may be hashed/truncated by Inno, defeating exact registration lookup.
    [ValidatePattern('^[a-zA-Z0-9-]{1,56}$')]
    [string]$ProductAppId = 'OpticalSystemDesign-8D387EA8-F349-447F-B5D3-B58E27F46D4A',
    [switch]$EnableValidationHooks,
    [switch]$PassThru
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$AppVersion = & (Join-Path $PSScriptRoot 'get-product-version.ps1') -AppVersion $AppVersion
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot 'artifacts\installers'
} elseif (-not [IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot $OutputRoot
}
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$buildName = 'installer-{0}-{1}' -f $Runtime, ([Guid]::NewGuid().ToString('N').Substring(0, 8))
$buildDirectory = Join-Path $OutputRoot $buildName
$stagingDirectory = "$buildDirectory.partial"
if (-not $PSCmdlet.ShouldProcess($buildDirectory, 'Build a self-contained EXE with a Chinese installation wizard')) {
    return
}

$compiler = & (Join-Path $PSScriptRoot 'get-inno-setup.ps1') -CompilerPath $InnoSetupCompiler
$signing = $null
. (Join-Path $PSScriptRoot 'windows-signing.ps1')
if ($UnsignedTestPackage -and $SigningMode -eq 'Required') {
    throw 'An unsigned test package cannot require publisher signing.'
}
if ($SigningMode -eq 'Unsigned' -and $CertificateThumbprint) {
    throw 'Select -SigningMode Required to use a publisher certificate.'
}
if ($SigningMode -eq 'Required') {
    $signing = Get-WindowsSigningConfiguration -CertificateThumbprint $CertificateThumbprint `
        -SignToolPath $SignToolPath -TimestampUrl $TimestampUrl -MachineCertificateStore:$MachineCertificateStore
}
if ($EnableValidationHooks -and (-not $UnsignedTestPackage -or
    $ProductAppId -eq 'OpticalSystemDesign-8D387EA8-F349-447F-B5D3-B58E27F46D4A')) {
    throw 'Failure-injection hooks require an unsigned test package and an isolated product AppId.'
}
[void][IO.Directory]::CreateDirectory($stagingDirectory)
Write-Host '[1/2] Publishing the current application and resources...'
$package = & (Join-Path $PSScriptRoot 'publish-windows.ps1') -Runtime $Runtime `
    -OutputRoot (Join-Path $stagingDirectory 'payload') -AppVersion $AppVersion `
    -ProductAppId $ProductAppId -PassThru -CompactName
if ($null -eq $package -or -not (Test-Path -LiteralPath $package.Directory -PathType Container)) {
    throw 'The application publish did not return a complete package.'
}
$releaseId = "$AppVersion-$Runtime-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
if ($signing) {
    foreach ($name in @('OpticalSystemDesign.App.exe', 'OpticalSystemDesign.App.dll',
        'OpticalSystemDesign.Application.dll', 'OpticalSystemDesign.Core.dll',
        'OpticalSystemDesign.InitialStructure.Contracts.dll', 'OpticalSystemDesign.InitialStructure.Engine.dll',
        'OpticalSystemDesign.InitialStructure.Persistence.dll', 'OpticalSystemDesign.CoatingDesign.Engine.dll', 'OpticalSystemDesign.Modules.dll')) {
        Invoke-WindowsFileSigning -Configuration $signing -Path (Join-Path $package.Directory $name)
    }
}
# Manifest is created after signing. Third-party binaries are never re-signed.
$payloadRows = @(Get-ChildItem -LiteralPath $package.Directory -Recurse -File | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($package.Directory.Length + 1)
    if ($_.FullName.Length -gt 259) { throw 'Inno Setup 6 source path exceeds 259 characters. Use a shorter -OutputRoot; the previous release is preserved.' }
    if ($relative -match '[\r\n]' -or $relative.Contains('..')) { throw 'Unsafe payload file name.' }
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
})
[IO.File]::WriteAllLines((Join-Path $package.Directory 'release.files.sha256'), $payloadRows, [Text.UTF8Encoding]::new($false))
$head = (& git -C $repositoryRoot rev-parse HEAD).Trim()
$dirty = @(& git -C $repositoryRoot status --porcelain).Count -gt 0
$metadata = [ordered]@{ FormatVersion = 1; ReleaseId = $releaseId; Version = $AppVersion; Runtime = $Runtime;
    ProductAppId = $ProductAppId; SourceCommit = $head; SourceDirty = $dirty;
    Signed = [bool]$signing; TestPackage = [bool]$UnsignedTestPackage; ValidationHooks = [bool]$EnableValidationHooks;
    RequiredModules = @('InitialStructure', 'CoatingDesign'); ModulesInProcess = $true;
    InstallerScriptSha256 = (Get-FileHash -LiteralPath (Join-Path $repositoryRoot 'packaging/windows/OpticalSystemDesign.iss')).Hash.ToLowerInvariant();
    RecoveryScriptSha256 = (Get-FileHash -LiteralPath (Join-Path $repositoryRoot 'packaging/windows/InstallationRecovery.iss')).Hash.ToLowerInvariant();
    InstallationPolicyScriptSha256 = (Get-FileHash -LiteralPath (Join-Path $repositoryRoot 'packaging/windows/InstallationPolicy.iss')).Hash.ToLowerInvariant();
    Files = $payloadRows.Count;
    ManifestSha256 = (Get-FileHash -LiteralPath (Join-Path $package.Directory 'release.files.sha256')).Hash.ToLowerInvariant() }
[IO.File]::WriteAllText((Join-Path $stagingDirectory 'release.json'), ($metadata | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$suffix = if ($UnsignedTestPackage) { '-UnsignedTest' } elseif (-not $signing) { '-Unsigned' } else { '' }
$outputName = "OpticalSystemDesign-$AppVersion-$Runtime$suffix-Setup"
$setupPath = Join-Path $stagingDirectory "$outputName.exe"
$script = Join-Path $repositoryRoot 'packaging\windows\OpticalSystemDesign.iss'
Write-Host '[2/2] Compiling the installation wizard...'
$compilerArguments = @('/Qp', "/DPublishDir=$($package.Directory)", "/DAppVersion=$AppVersion", "/DTargetRuntime=$Runtime",
    "/DProductAppId=$ProductAppId", "/DReleaseId=$releaseId", "/DManifestHash=$($metadata.ManifestSha256)",
    "/O$stagingDirectory", "/F$outputName")
if ($EnableValidationHooks) { $compilerArguments += '/DValidationHooks=1' }
if ($signing) {
    # Inno signs Setup and Uninstall with exactly the selected publisher and a
    # RFC 3161 SHA-256 timestamp. $f is expanded and quoted by Inno itself.
    foreach ($argument in @($signing.SignTool, $signing.TimestampUrl)) {
        if ($argument -match '["\r\n$]') { throw 'Unsafe signing argument.' }
    }
    $machineArgument = if ($signing.MachineStore) { ' /sm' } else { '' }
    $signCommand = '$q{0}$q sign /sha1 {1} /s My /fd SHA256 /tr $q{2}$q /td SHA256{3} $f' -f
        $signing.SignTool, $signing.Thumbprint, $signing.TimestampUrl, $machineArgument
    $compilerArguments += @('/DTrustedSigning=1', "/SOpticalPublisher=$signCommand")
}
& $compiler @compilerArguments $script | Out-Host
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
    throw "Installer compilation failed (exit $LASTEXITCODE). Output retained at: $stagingDirectory"
}
if ($signing) { Assert-WindowsFileSignature -Configuration $signing -Path $setupPath }
$hash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$setupPath.sha256", "$hash  $outputName.exe`r`n", [Text.Encoding]::ASCII)
[IO.Directory]::Move($stagingDirectory, $buildDirectory)
Write-Host ''
Write-Host 'Installer completed. Distribute this Setup EXE; it contains all required application files:'
Write-Host (Join-Path $buildDirectory "$outputName.exe")
Write-Host $(if ($signing) { 'Publisher signature and timestamp verified. Building does not install or start the application.' }
    else { 'UNSIGNED PACKAGE: publisher identity is not authenticated. Windows or organizational policies may block it. Building does not install or start the application.' })
if ($PassThru) { [pscustomobject]@{ Directory = $buildDirectory; Setup = (Join-Path $buildDirectory "$outputName.exe");
    ReleaseId = $releaseId; Version = $AppVersion; Runtime = $Runtime; Signed = [bool]$signing } }
