param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Fresh output required.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$fixture = Join-Path $output 'repository'
[IO.Directory]::CreateDirectory((Join-Path $fixture 'validation')) | Out-Null
. (Join-Path $PSScriptRoot 'FrozenIntegrity.ps1')
$bytes = [byte[]]@(0, 10, 13, 255, 42)
$canonical = Join-Path $fixture 'validation/authority.bin'
[IO.File]::WriteAllBytes($canonical, $bytes)
$hash = (Get-FileHash -LiteralPath $canonical).Hash.ToLowerInvariant()
$rows = @(@{path='validation/original-a.bin';sha256=$hash}, @{path='validation/original-b.bin';sha256=$hash})
$aggregate = Get-FrozenAggregate $rows 'sha256'
$frozenManifest = Join-Path $fixture 'frozen.json'
@{count=2;sha256=$aggregate;files=$rows} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $frozenManifest -Encoding utf8
$storage = @{schemaVersion=1; groups=@(@{canonical='validation/authority.bin';sha256=$hash;bytes=$bytes.Length;aliases=@('validation/original-a.bin','validation/original-b.bin')})}
function Save-Storage { $storage | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $fixture 'validation/evidence-aliases.json') -Encoding utf8 }
function Require($value, $message) { if (!$value) { throw $message } }
$results = [Collections.Generic.List[object]]::new()
function Reject($name, [scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    Require $rejected ('Missing rejection: ' + $name)
    $results.Add(@{name=$name;passed=$true})
}
Save-Storage
[IO.File]::WriteAllText((Join-Path $fixture '.gitattributes'), "* -text`n")
$report = Get-FrozenIntegrityReport -RepositoryRoot $fixture -ManifestPath $frozenManifest
Require ($report.unchanged -and $report.count -eq 2 -and $report.actualAggregateSha256 -ceq $aggregate) 'Original frozen identity changed.'
$results.Add(@{name='Two logical roles retain the original aggregate through one stored file';passed=$true})
[IO.File]::WriteAllBytes($canonical, [byte[]]@(1, 10, 13, 255, 42))
$report = Get-FrozenIntegrityReport -RepositoryRoot $fixture -ManifestPath $frozenManifest
Require (!$report.unchanged -and $report.matched -eq 0) 'Corruption must affect both logical roles.'
$results.Add(@{name='Corrupt authority cannot pass either logical role';passed=$true})
[IO.File]::WriteAllBytes($canonical, $bytes)
$original = Join-Path $fixture 'validation/original-a.bin'
[IO.File]::WriteAllBytes($original, $bytes)
Reject 'Reintroduced duplicate cannot hide behind storage mapping' { Get-FrozenIntegrityReport -RepositoryRoot $fixture -ManifestPath $frozenManifest }
Remove-Item -LiteralPath $original
$storage.groups[0].canonical = '../escape.bin'; Save-Storage
Reject 'Storage traversal rejected' { Get-FrozenIntegrityReport -RepositoryRoot $fixture -ManifestPath $frozenManifest }
$storage.groups[0].canonical = 'validation/authority.bin'
$storage.groups[0].sha256 = '0' * 64; Save-Storage
Reject 'Storage mapping cannot alter frozen expected hash' { Get-FrozenIntegrityReport -RepositoryRoot $fixture -ManifestPath $frozenManifest }
$storage.groups[0].sha256 = $hash; Save-Storage
& git -C $fixture init --quiet
if ($LASTEXITCODE) { throw 'Fixture Git init failed.' }
& git -C $fixture add --all
if ($LASTEXITCODE) { throw 'Fixture staging failed.' }
& git -C $fixture -c user.name=EvidenceStorageTest -c user.email=evidence@example.invalid -c commit.gpgsign=false commit --quiet -m baseline
if ($LASTEXITCODE) { throw 'Fixture commit failed.' }
$checker = Join-Path $PSScriptRoot 'GitFrozenIntegrity.ps1'
& pwsh -NoProfile -File $checker -RepositoryRoot $fixture -ManifestPath $frozenManifest -Source HEAD -OutputPath (Join-Path $output 'head.json') | Out-Null
Require ($LASTEXITCODE -eq 0) 'Committed storage authority did not validate.'
$results.Add(@{name='Committed map and single authority validate both original roles';passed=$true})
$storage.groups[0].aliases += 'validation/extra-logical-role.bin'; Save-Storage
& pwsh -NoProfile -File $checker -RepositoryRoot $fixture -ManifestPath $frozenManifest -Source HEAD -OutputPath (Join-Path $output 'unstaged-map.json') | Out-Null
Require ($LASTEXITCODE -ne 0) 'Uncommitted mapping certified HEAD.'
$results.Add(@{name='Working map cannot certify a different committed map';passed=$true})
& git -C $fixture add validation/evidence-aliases.json
& pwsh -NoProfile -File $checker -RepositoryRoot $fixture -ManifestPath $frozenManifest -Source Index -OutputPath (Join-Path $output 'index.json') | Out-Null
Require ($LASTEXITCODE -eq 0) 'Matching staged map did not validate Index.'
$results.Add(@{name='Index validates its own exact map identity';passed=$true})
@{passed=$results.Count;failed=0;opticalTests=$false;cases=$results.ToArray()} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding utf8
[pscustomobject]@{passed=$results.Count;failed=0} | ConvertTo-Json
