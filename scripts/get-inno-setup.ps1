#requires -Version 5.1
[CmdletBinding()]
param([string]$CompilerPath = '')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not [string]::IsNullOrWhiteSpace($CompilerPath)) {
    if (-not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
        throw "Inno Setup compiler not found: $CompilerPath"
    }
    return (Get-Item -LiteralPath $CompilerPath).FullName
}
$installed = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($installed) { return $installed.Source }
foreach ($base in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
    if ([string]::IsNullOrWhiteSpace($base)) { continue }
    foreach ($version in @('7', '6')) {
        $candidate = Join-Path $base "Inno Setup $version\ISCC.exe"
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
}

# Pinned official release. Portable mode neither registers Inno Setup nor
# creates shortcuts or file associations on the developer's computer.
$toolRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\tools\inno-setup-6.7.3'
$compiler = Join-Path $toolRoot 'ISCC.exe'
if (Test-Path -LiteralPath $compiler -PathType Leaf) { return $compiler }
$download = "$toolRoot.download.exe"
$expectedSha256 = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
[void][IO.Directory]::CreateDirectory((Split-Path -Parent $toolRoot))
if (-not (Test-Path -LiteralPath $download -PathType Leaf)) {
    Write-Host 'Downloading the pinned Inno Setup compiler from its official GitHub release...'
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' -OutFile $download
}
if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $expectedSha256) {
    throw "Inno Setup download hash mismatch. Nothing was executed. Inspect or replace: $download"
}
Write-Host "Preparing the portable compiler at: $toolRoot"
$setup = Start-Process -FilePath $download -ArgumentList @(
    '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', '/PORTABLE=1', '/NOICONS',
    ('/DIR="{0}"' -f $toolRoot), ('/LOG="{0}"' -f "$toolRoot.log")
) -WindowStyle Hidden -Wait -PassThru
if ($setup.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw "Portable compiler preparation failed (exit $($setup.ExitCode)). See: $toolRoot.log"
}
return $compiler
