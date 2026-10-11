param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) { throw 'Use a fresh test output directory.' }
[void](New-Item -ItemType Directory -Path $outputRoot)
$fixture = Join-Path $outputRoot 'fixture'
[void](New-Item -ItemType Directory -Path $fixture)
& git -C $fixture init --quiet
if ($LASTEXITCODE) { throw 'Fixture Git init failed.' }
$file = Join-Path $fixture 'reference.txt'
$manifestPath = Join-Path $outputRoot 'manifest.json'
$checker = Join-Path $PSScriptRoot 'GitFrozenIntegrity.ps1'
$results = [Collections.Generic.List[object]]::new()
function Invoke-FixtureGit([string[]]$Arguments) {
    & git -C $fixture @Arguments | Out-Null
    if ($LASTEXITCODE) { throw ('Fixture Git failed: ' + ($Arguments -join ' ')) }
}
function Commit {
    Invoke-FixtureGit @('-c','user.name=Evidence fixture','-c','user.email=fixture@example.invalid',
        'commit','--quiet','-m','Fixture evidence')
}
function Write-Manifest([string]$relative = 'reference.txt') {
    $entry = [ordered]@{path=$relative;sha256=(Get-FileHash -LiteralPath (Join-Path $fixture $relative)).Hash.ToLowerInvariant()}
    $aggregate = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($entry.path+'='+$entry.sha256+"`r`n"))).ToLowerInvariant()
    [IO.File]::WriteAllText($manifestPath,([ordered]@{count=1;sha256=$aggregate;files=@($entry)} | ConvertTo-Json -Depth 4))
}
function Check([string]$name, [bool]$pass, [string]$source = 'HEAD') {
    $output = Join-Path $outputRoot ($results.Count.ToString('00')+'.json')
    & pwsh -NoProfile -File $checker -RepositoryRoot $fixture -ManifestPath $manifestPath -Source $source -OutputPath $output | Out-Null
    $exitCode = $LASTEXITCODE
    $report = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
    if (($exitCode -eq 0) -ne $pass) { throw "Unexpected checker result: $name" }
    $results.Add([pscustomobject]@{name=$name;passed=$true;expectedSuccess=$pass;exitCode=$exitCode;
        gitMatched=$report.matched;workingMatched=$report.workingTree.matched;source=$source})
    return $report
}
[IO.File]::WriteAllText((Join-Path $fixture '.gitattributes'), '* text eol=lf')
[IO.File]::WriteAllBytes($file,[Text.Encoding]::UTF8.GetBytes("original`n"))
Write-Manifest
Invoke-FixtureGit @('add','.gitattributes','reference.txt'); Commit
[void](Check 'exact working and committed bytes pass' $true)

[IO.File]::WriteAllBytes($file,[Text.Encoding]::UTF8.GetBytes("original`r`n"))
Write-Manifest
$report = Check 'working bytes alone cannot hide Git EOL normalization' $false
if (!$report.workingTree.unchanged -or $report.unchanged) { throw 'EOL failure did not isolate Git.' }

[IO.File]::WriteAllText((Join-Path $fixture '.gitattributes'), "* text eol=lf`nreference.txt -text`n")
Invoke-FixtureGit @('add','--renormalize','reference.txt'); Invoke-FixtureGit @('add','.gitattributes')
[void](Check 'repaired index passes before commit' $true 'Index')
[void](Check 'staging a repair does not certify the old commit' $false)
Commit
[void](Check 'committed original CRLF passes without changing manifest' $true)

[IO.File]::WriteAllBytes($file,[Text.Encoding]::UTF8.GetBytes("changed`r`n"))
$report = Check 'correct commit cannot hide modified working bytes' $false
if (!$report.unchanged -or $report.workingTree.unchanged) { throw 'Working-copy failure did not isolate the working copy.' }

$untracked = Join-Path $fixture 'untracked.bin'
[IO.File]::WriteAllBytes($untracked,[byte[]]@(0,255,13,10,128))
Write-Manifest 'untracked.bin'
$report = Check 'untracked original cannot certify a missing Git blob' $false
if (!$report.workingTree.unchanged -or $report.mismatches[0].actualSha256) { throw 'Missing-blob failure differs.' }

[IO.File]::WriteAllText((Join-Path $fixture '.gitattributes'), "* -text`n")
Invoke-FixtureGit @('add','.gitattributes','untracked.bin'); Commit
[void](Check 'binary blob remains exact without decoding or EOL conversion' $true)
$pinnedHash = '0' * 64
& pwsh -NoProfile -File $checker -RepositoryRoot $fixture -ManifestPath $manifestPath -ExpectedAggregateSha256 $pinnedHash 2> (Join-Path $outputRoot 'pinned-rejection.log') | Out-Null
if ($LASTEXITCODE -eq 0) { throw 'A replacement manifest bypassed the pinned identity.' }
$results.Add([pscustomobject]@{name='replacement manifest cannot bypass pinned aggregate';passed=$true;expectedSuccess=$false;exitCode=$LASTEXITCODE})
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'results.json') -Encoding utf8
[ordered]@{total=$results.Count;passed=$results.Count;failed=0} | ConvertTo-Json
