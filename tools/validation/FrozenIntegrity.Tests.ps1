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

# A relocation changes only physical storage, never the published identity.
$destination = Join-Path $fixture 'renamed.bin'
[IO.File]::WriteAllBytes($destination,[Text.Encoding]::UTF8.GetBytes("original`n"))
Remove-Item -LiteralPath $file
Write-FixtureManifest @($entry) 1
$mapPath = Join-Path $fixture 'relocations.json'
$manifestAggregate = (Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json).sha256
function Write-Relocations($relocations, [string]$aggregate = $manifestAggregate) {
    [IO.File]::WriteAllText($mapPath,([ordered]@{schemaVersion=1;manifestAggregateSha256=$aggregate;
        count=@($relocations).Count;relocations=@($relocations)} | ConvertTo-Json -Depth 5))
}
function Assert-Mapped {
    Assert-FrozenIntegrity $fixture $manifestPath -RelocationMapPath 'relocations.json' `
        -ExpectedRelocationMapSha256 (Get-FileHash -LiteralPath $mapPath).Hash.ToLowerInvariant()
}
$relocation = [pscustomobject]@{path=$entry.path;resolvedPath='renamed.bin';sha256=$entry.sha256}
Write-Relocations @($relocation)
$report = Assert-Mapped
if ($report.actualAggregateSha256 -cne $manifestAggregate -or $report.relocations[0].path -cne $entry.path) { throw 'Relocation changed logical identity.' }
$results += [pscustomobject]@{name='explicit relocation preserves original aggregate and exact bytes';passed=$true}
$results += Must-Fail 'no implicit fallback without relocation map' {Assert-FrozenIntegrity $fixture $manifestPath}
$results += Must-Fail 'relocation map must be pinned' {Assert-FrozenIntegrity $fixture $manifestPath -RelocationMapPath 'relocations.json'}
$results += Must-Fail 'modified map cannot bypass pinned hash' {
    Assert-FrozenIntegrity $fixture $manifestPath -RelocationMapPath 'relocations.json' -ExpectedRelocationMapSha256 ('0'*64)
}
Write-Relocations @($relocation) ('0'*64)
$results += Must-Fail 'map must bind the original manifest aggregate' {Assert-Mapped}
Write-Relocations @([pscustomobject]@{path=$entry.path;resolvedPath='renamed.bin';sha256=('0'*64)})
$results += Must-Fail 'relocation cannot substitute a new content hash' {Assert-Mapped}
Write-Relocations @([pscustomobject]@{path='unknown.txt';resolvedPath='renamed.bin';sha256=$entry.sha256})
$results += Must-Fail 'unknown relocation source' {Assert-Mapped}
Write-Relocations @($relocation,$relocation)
$results += Must-Fail 'duplicate relocation source' {Assert-Mapped}
foreach ($invalid in @('../outside.bin', '/absolute.bin', 'C:/absolute.bin', 'dir\reference.bin', "line`nbreak.bin")) {
    Write-Relocations @([pscustomobject]@{path=$entry.path;resolvedPath=$invalid;sha256=$entry.sha256})
    $results += Must-Fail "invalid relocation destination: $invalid" {Assert-Mapped}
}
Write-Relocations @([pscustomobject]@{path=$entry.path;resolvedPath='missing.bin';sha256=$entry.sha256})
$results += Must-Fail 'missing relocated bytes' {Assert-Mapped}
Write-Relocations @($relocation)
[IO.File]::WriteAllBytes($destination,[Text.Encoding]::UTF8.GetBytes("original`r`n"))
$results += Must-Fail 'relocated EOL difference is not normalized' {Assert-Mapped}
[IO.File]::WriteAllText($destination,'changed')
$results += Must-Fail 'relocated changed content is rejected' {Assert-Mapped}
[IO.File]::WriteAllBytes($destination,[Text.Encoding]::UTF8.GetBytes("original`n"))
[IO.File]::WriteAllText($file,'unexpected original')
$results += Must-Fail 'relocation cannot hide a recreated original path' {Assert-Mapped}
Remove-Item -LiteralPath $file
$second = [pscustomobject]@{path='second.txt';sha256=$entry.sha256}
Write-FixtureManifest @($entry,$second) 2
$secondAggregate = (Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json).sha256
Write-Relocations @($relocation,[pscustomobject]@{path=$second.path;resolvedPath='renamed.bin';sha256=$entry.sha256}) $secondAggregate
$results += Must-Fail 'two logical entries cannot share one destination' {Assert-Mapped}
Write-FixtureManifest @($entry,[pscustomobject]@{path='renamed.bin';sha256=$entry.sha256}) 2
$secondAggregate = (Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json).sha256
Write-Relocations @($relocation) $secondAggregate
$results += Must-Fail 'relocation cannot collide with an unmapped frozen entry' {Assert-Mapped}
Write-FixtureManifest @($entry) 1
$linkTarget = Join-Path $outputRoot 'link-target'
[void](New-Item -ItemType Directory -Path $linkTarget)
[IO.File]::WriteAllBytes((Join-Path $linkTarget 'reference.txt'),[Text.Encoding]::UTF8.GetBytes("original`n"))
$linkType = if ($IsWindows) { 'Junction' } else { 'SymbolicLink' }
[void](New-Item -ItemType $linkType -Path (Join-Path $fixture 'linked') -Target $linkTarget -ErrorAction Stop)
Write-Relocations @([pscustomobject]@{path=$entry.path;resolvedPath='linked/reference.txt';sha256=$entry.sha256})
$results += Must-Fail 'relocation cannot traverse a working link' {Assert-Mapped}
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputRoot 'results.json') -Encoding utf8
[pscustomobject]@{total=$results.Count;passed=@($results | Where-Object passed).Count;failed=0} | ConvertTo-Json
