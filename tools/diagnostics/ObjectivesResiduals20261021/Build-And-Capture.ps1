param(
    [Parameter(Mandatory)][string]$RequestPath,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$requestFile = (Resolve-Path -LiteralPath $RequestPath).Path
$request = Get-Content -LiteralPath $requestFile -Raw | ConvertFrom-Json
$api = $request.zosApiPath
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh output directory.' }
$hostDirectory = Join-Path $output 'host'
New-Item -ItemType Directory -Path $hostDirectory | Out-Null
$sources = @((Get-ChildItem -LiteralPath (Join-Path $repo 'tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost') -Filter '*.cs').FullName)
$sources += Join-Path $PSScriptRoot 'NativeConeControl.cs'
if ($request.rawDoubleBits) {
    $sources += Join-Path $PSScriptRoot 'BitPreservingCapture.cs'
    $originalHost = Join-Path $repo 'tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost/Host.cs'
    $hostText = [IO.File]::ReadAllText($originalHost)
    $anchor = 'internal static void Write(string name, object value)'
    $start = $hostText.IndexOf($anchor, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw 'Host write method not found.' }
    $brace = $hostText.IndexOf('{', $start)
    $hostText = $hostText.Insert($brace + 1, "`r`n        BitPreservingCapture.Write(name, value);")
    $instrumentedHost = Join-Path $hostDirectory 'Host.BitCapture.cs'
    [IO.File]::WriteAllText($instrumentedHost, $hostText)
    $sources = @($sources | Where-Object { $_ -ne $originalHost }) + $instrumentedHost
}
$inputs = @($request.jobs.input | Sort-Object -Unique | ForEach-Object {
    @{ path = $_; sha256 = (Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant() }
})
$sourceIdentity = @($sources | ForEach-Object {
    @{ path = $_; sha256 = (Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant() }
})
$exe = Join-Path $hostDirectory 'NativeConeControl.exe'
$arguments = @('/nologo', '/target:exe', '/platform:x64', '/main:NativeConeControl', ('/out:' + $exe), '/r:System.Web.Extensions.dll')
foreach ($name in @('ZOSAPI.dll', 'ZOSAPI_Interfaces.dll', 'ZOSAPI_NetHelper.dll')) { $arguments += '/r:' + (Join-Path $api $name) }
$arguments += $sources
& (Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe') @arguments 2>&1 | Tee-Object (Join-Path $output 'build.log')
if ($LASTEXITCODE -ne 0) { throw 'Native diagnostic compilation failed.' }
[IO.File]::WriteAllText(($exe + '.config'), '<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup></configuration>')
& $exe $requestFile (Join-Path $output 'capture') 2>&1 | Tee-Object (Join-Path $output 'native.log')
$captureExit = $LASTEXITCODE
foreach ($sourceInput in $inputs) {
    if ((Get-FileHash -LiteralPath $sourceInput.path).Hash.ToLowerInvariant() -ne $sourceInput.sha256) { throw 'Source lens changed.' }
}
@{ exitCode = $captureExit; inputs = $inputs; sources = $sourceIdentity; requestSha256 = (Get-FileHash -LiteralPath $requestFile).Hash.ToLowerInvariant() } |
    ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $output 'identity.json') -Encoding utf8
exit $captureExit
