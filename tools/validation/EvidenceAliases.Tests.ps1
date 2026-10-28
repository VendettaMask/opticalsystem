#requires -Version 7.0
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Fresh test output required.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$fixture = Join-Path $output 'repository'
[IO.Directory]::CreateDirectory((Join-Path $fixture 'validation/sample/captures')) | Out-Null
. (Join-Path $PSScriptRoot 'EvidenceAliases.ps1')
$bytes = [byte[]]@(0, 10, 13, 255, 42)
$canonical = Join-Path $fixture 'validation/sample/captures/a.bin'
[IO.File]::WriteAllBytes($canonical, $bytes)
$hash = (Get-FileHash -LiteralPath $canonical).Hash.ToLowerInvariant()
$manifest = [ordered]@{schemaVersion=1;
    datasets=@(@{sourcePrefix='validation/sample/captures';fixturePrefix='Fixtures/sample'});
    scanRoots=@('validation/sample');scanFiles=@();
    groups=@(@{canonical='validation/sample/captures/a.bin';sha256=$hash;bytes=$bytes.Length;
        aliases=@('validation/sample/captures/b.bin','validation/sample/other.bin')})}
function Save-Plan { $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $fixture 'validation/evidence-aliases.json') -Encoding utf8 }
function Require($condition, [string]$message) { if (!$condition) { throw $message } }
$results = [Collections.Generic.List[object]]::new()
function Reject([string]$name, [scriptblock]$action) {
    $rejected = $false
    try { & $action | Out-Null } catch { $rejected = $true }
    Require $rejected ('Missing rejection: ' + $name)
    $results.Add(@{name=$name;passed=$true})
}
Save-Plan
$plan = Get-EvidenceAliasPlan $fixture
$verified = Test-EvidenceAliasPlan $plan
Require ($verified.valid -and $verified.aliases -eq 2 -and $verified.nonEmptyDuplicateGroups -eq 0) 'Valid plan failed'
$results.Add(@{name='Valid canonical and absent aliases';passed=$true})
$view = Join-Path $output 'fixtures'
$restored = Restore-EvidenceAliasPlan $plan $view -AsFixtures
Require ($restored.materializedFiles -eq 2) 'Fixture layout count changed'
foreach ($name in 'a.bin','b.bin') {
    Require ((Get-FileHash -LiteralPath (Join-Path $view ('Fixtures/sample/' + $name))).Hash.ToLowerInvariant() -ceq $hash) 'Fixture bytes changed'
}
$results.Add(@{name='Fixture view retains exact binary bytes';passed=$true})
$historical = Join-Path $output 'historical'
$restored = Restore-EvidenceAliasPlan $plan $historical
Require ($restored.materializedFiles -eq 3) 'Historical layout count changed'
foreach ($path in 'validation/sample/captures/a.bin','validation/sample/captures/b.bin','validation/sample/other.bin') {
    Require ((Get-FileHash -LiteralPath (Join-Path $historical $path)).Hash.ToLowerInvariant() -ceq $hash) 'Historical bytes changed'
}
$results.Add(@{name='Historical view restores both original logical roles';passed=$true})
[IO.File]::WriteAllBytes($canonical, [byte[]]@(1, 10, 13, 255, 42))
Reject 'Canonical corruption rejected' { Get-EvidenceAliasPlan $fixture }
[IO.File]::WriteAllBytes($canonical, $bytes)
$reintroduced = Join-Path $fixture 'validation/sample/captures/b.bin'
[IO.File]::WriteAllBytes($reintroduced, $bytes)
Reject 'Recreated physical alias rejected' { Test-EvidenceAliasPlan (Get-EvidenceAliasPlan $fixture) }
Remove-Item -LiteralPath $reintroduced
$manifest.groups[0].aliases += '../escape.bin'
Save-Plan
Reject 'Traversal rejected before materialization' { Get-EvidenceAliasPlan $fixture }
$manifest.groups[0].aliases = @('validation/sample/captures/b.bin','validation/sample/other.bin')
$manifest.groups[0].aliases += 'validation/sample/captures/a.bin'
Save-Plan
Reject 'Aliasing a canonical path rejected' { Get-EvidenceAliasPlan $fixture }
$manifest.groups[0].aliases = @('validation/sample/captures/b.bin','validation/sample/other.bin')
Save-Plan
Reject 'Destination cannot be captured source' { Restore-EvidenceAliasPlan (Get-EvidenceAliasPlan $fixture) (Join-Path $fixture 'validation/sample/output') }
$unmapped = Join-Path $fixture 'validation/sample/unmapped.bin'
[IO.File]::WriteAllBytes($unmapped, $bytes)
Reject 'New unregistered content duplicate rejected' { Test-EvidenceAliasPlan (Get-EvidenceAliasPlan $fixture) }
Remove-Item -LiteralPath $unmapped
$report = [ordered]@{passed=$results.Count;failed=0;opticalTests=$false;cases=$results.ToArray()}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding utf8
[pscustomobject]@{passed=$results.Count;failed=0} | ConvertTo-Json
