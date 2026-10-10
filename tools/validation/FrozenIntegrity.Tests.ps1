param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'FrozenIntegrity.ps1')
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) { throw 'Use a fresh test output directory.' }
[void](New-Item -ItemType Directory -Path $outputRoot)
$fixture = Join-Path $outputRoot 'fixture'
[void](New-Item -ItemType Directory -Path $fixture)
$file = Join-Path $fixture 'reference.txt'
$manifestPath = Join-Path $fixture 'manifest.json'
function Write-FixtureManifest($entries, [int]$count, [string]$aggregate = '') {
    if (!$aggregate) {
        $provider=[Security.Cryptography.SHA256]::Create()
        try { $text = [string]::Join("`r`n",[string[]]@($entries | ForEach-Object {$_.path+'='+$_.sha256}))+"`r`n"
            $aggregate=[BitConverter]::ToString($provider.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))).Replace('-','').ToLowerInvariant() }
        finally {$provider.Dispose()}
    }
    [IO.File]::WriteAllText($manifestPath,([ordered]@{count=$count;sha256=$aggregate;unchanged=$true;files=@($entries)} | ConvertTo-Json -Depth 5))
}
function Must-Fail([string]$name, [scriptblock]$action) {
    $caught=$false
    try { & $action | Out-Null } catch {$caught=$true}
    if (!$caught) {throw "Expected failure: $name"}
    [pscustomobject]@{name=$name;passed=$true}
}
[IO.File]::WriteAllBytes($file,[Text.Encoding]::UTF8.GetBytes("original`n"))
$entry=[pscustomobject]@{path='reference.txt';sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()}
Write-FixtureManifest @($entry) 1
$results=@()
if (!(Assert-FrozenIntegrity $fixture $manifestPath).unchanged) {throw 'Exact bytes were rejected.'}
$results += [pscustomobject]@{name='exact bytes pass';passed=$true}
[IO.File]::WriteAllBytes($file,[Text.Encoding]::UTF8.GetBytes("original`r`n"))
$results += Must-Fail 'EOL difference cannot hide behind unchanged=true' {Assert-FrozenIntegrity $fixture $manifestPath}
[IO.File]::WriteAllBytes($file,[Text.Encoding]::UTF8.GetBytes("different`n"))
$results += Must-Fail 'changed content cannot hide behind unchanged=true' {Assert-FrozenIntegrity $fixture $manifestPath}
Write-FixtureManifest @($entry) 2
$results += Must-Fail 'incorrect count' {Assert-FrozenIntegrity $fixture $manifestPath}
Write-FixtureManifest @($entry,$entry) 2
$results += Must-Fail 'duplicate path' {Assert-FrozenIntegrity $fixture $manifestPath}
Write-FixtureManifest @([pscustomobject]@{path='../outside.txt';sha256=$entry.sha256}) 1
$results += Must-Fail 'escaping path' {Assert-FrozenIntegrity $fixture $manifestPath}
Write-FixtureManifest @($entry) 1 ('0'*64)
$results += Must-Fail 'incorrect aggregate' {Assert-FrozenIntegrity $fixture $manifestPath}
Write-FixtureManifest @([pscustomobject]@{path='missing.txt';sha256=$entry.sha256}) 1
$results += Must-Fail 'missing file' {Assert-FrozenIntegrity $fixture $manifestPath}
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputRoot 'results.json') -Encoding utf8
[pscustomobject]@{total=$results.Count;passed=@($results | Where-Object passed).Count;failed=0} | ConvertTo-Json
