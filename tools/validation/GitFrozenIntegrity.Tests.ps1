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
function Check([string]$name, [bool]$pass, [string]$source = 'HEAD', [hashtable]$Options = @{}) {
    $output = Join-Path $outputRoot ($results.Count.ToString('00')+'.json')
    & $checker -RepositoryRoot $fixture -ManifestPath $manifestPath -Source $source -OutputPath $output @Options | Out-Null
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

# Keep the old manifest byte-for-byte while Git carries the moved evidence.
$original = [IO.File]::ReadAllBytes($untracked)
$manifestBytes = [IO.File]::ReadAllBytes($manifestPath)
Invoke-FixtureGit @('mv','untracked.bin','renamed.bin')
[void](Check 'rename without explicit map still fails' $false 'Index')
$mapPath = Join-Path $fixture 'relocations.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
[IO.File]::WriteAllText($mapPath,([ordered]@{schemaVersion=1;manifestAggregateSha256=$manifest.sha256;count=1;
    relocations=@([ordered]@{path='untracked.bin';resolvedPath='renamed.bin';sha256=$manifest.files[0].sha256})} | ConvertTo-Json -Depth 5))
$options = @{RelocationMapPath='relocations.json';ExpectedRelocationMapSha256=(Get-FileHash -LiteralPath $mapPath).Hash.ToLowerInvariant()}
$report = Check 'unstaged map cannot certify Git index' $false 'Index' $options
if (!$report.workingTree.unchanged -or $report.relocationMap.matches) { throw 'Missing Git map was not rejected.' }
Invoke-FixtureGit @('add','relocations.json')
[void](Check 'explicit staged relocation passes exact byte checks' $true 'Index' $options)
[void](Check 'staged relocation cannot certify pre-migration HEAD' $false 'HEAD' $options)
Commit
$report = Check 'committed relocation preserves frozen logical aggregate' $true 'HEAD' $options
if ($report.actualAggregateSha256 -cne $manifest.sha256 -or $report.relocations[0].path -cne 'untracked.bin' -or
    [Convert]::ToHexString([IO.File]::ReadAllBytes($manifestPath)) -cne [Convert]::ToHexString($manifestBytes)) {
    throw 'Migration changed the original manifest identity.'
}
$renamed = Join-Path $fixture 'renamed.bin'
[IO.File]::WriteAllBytes($renamed,[byte[]]@(1,2,3))
$report = Check 'committed relocation cannot hide modified working bytes' $false 'HEAD' $options
if (!$report.unchanged -or $report.workingTree.unchanged) { throw 'Relocated working mismatch was not isolated.' }
Invoke-FixtureGit @('add','renamed.bin')
[IO.File]::WriteAllBytes($renamed,$original)
$report = Check 'correct relocated working bytes cannot hide changed index blob' $false 'Index' $options
if (!$report.workingTree.unchanged -or $report.unchanged) { throw 'Relocated index mismatch was not isolated.' }
Invoke-FixtureGit @('add','renamed.bin')
Invoke-FixtureGit @('rm','--cached','renamed.bin')
[void](Check 'untracked relocated bytes cannot certify missing index blob' $false 'Index' $options)
Invoke-FixtureGit @('add','renamed.bin')
$mapBytes = [IO.File]::ReadAllBytes($mapPath)
[IO.File]::WriteAllText($mapPath,'{}')
Invoke-FixtureGit @('add','relocations.json')
[IO.File]::WriteAllBytes($mapPath,$mapBytes)
$report = Check 'correct working map cannot hide changed Git map' $false 'Index' $options
if (!$report.workingTree.unchanged -or $report.relocationMap.matches) { throw 'Git map mismatch was not isolated.' }
Invoke-FixtureGit @('add','relocations.json')
[IO.File]::WriteAllBytes($untracked,$original)
Invoke-FixtureGit @('add','untracked.bin')
Remove-Item -LiteralPath $untracked
$report = Check 'relocation cannot hide original path retained in Git' $false 'Index' $options
if (!$report.mismatches[0].originalPathPresent) { throw 'Original Git path was not detected.' }
Invoke-FixtureGit @('rm','--cached','untracked.bin')
$blob = (& git -C $fixture rev-parse 'HEAD:renamed.bin').Trim()
Invoke-FixtureGit @('update-index','--cacheinfo',"120000,$blob,renamed.bin")
$report = Check 'same blob hash in a Git symlink cannot certify relocated evidence' $false 'Index' $options
if ($report.mismatches[0].gitLinkPath -cne 'renamed.bin') { throw 'Git symlink mode was not rejected.' }
Invoke-FixtureGit @('update-index','--cacheinfo',"100644,$blob,renamed.bin")
$mapBlob = (& git -C $fixture rev-parse 'HEAD:relocations.json').Trim()
Invoke-FixtureGit @('update-index','--cacheinfo',"120000,$mapBlob,relocations.json")
$report = Check 'same map hash in a Git symlink is rejected' $false 'Index' $options
if ($report.relocationMap.matches -or !$report.relocationMap.gitLinkPath) { throw 'Git map link mode was not rejected.' }
Invoke-FixtureGit @('update-index','--cacheinfo',"100644,$mapBlob,relocations.json")
[void](Check 'repaired index passes after all negative controls' $true 'Index' $options)
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'results.json') -Encoding utf8
[ordered]@{total=$results.Count;passed=$results.Count;failed=0} | ConvertTo-Json
