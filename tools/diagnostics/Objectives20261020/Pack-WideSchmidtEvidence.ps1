param(
    [string]$EvidenceRoot = 'artifacts/validation/wide-schmidt-20261021',
    [string]$ArchivePath = 'validation/repairs/2026-10-21/wide-schmidt-repair-evidence.zip'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$root = (Resolve-Path -LiteralPath $EvidenceRoot).Path
$archive = [IO.Path]::GetFullPath((Join-Path $repo $ArchivePath))
if (!$archive.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $archive)) { throw 'Fresh repository archive required.' }
$guards = @((Get-Content (Join-Path $root 'pass-guard-final-a/summary.json') -Raw | ConvertFrom-Json -Depth 100)) + @((Get-Content (Join-Path $root 'pass-guard-final-b/summary.json') -Raw | ConvertFrom-Json -Depth 100))
if ($guards.Count -ne 1131 -or @($guards | Where-Object conclusion -ne 'Pass').Count -or @($guards | Where-Object target).Count -ne 29) { throw 'Final 1131-request guard incomplete.' }
foreach ($pair in @(@('core-final-release.trx',132), @('native-fixture-final.trx',2))) {
    [xml]$trx = Get-Content (Join-Path $root ('tests/' + $pair[0])) -Raw
    $c = $trx.TestRun.ResultSummary.Counters
    if ([int]$c.passed -ne $pair[1] -or [int]$c.failed -ne 0 -or [int]$c.total -ne $pair[1]) { throw 'Final focused tests incomplete.' }
}
[xml]$native = Get-Content (Join-Path $root 'tests/native-final-release.trx') -Raw
[xml]$retry = Get-Content (Join-Path $root 'tests/native-fixture-final.trx') -Raw
$successful = @($native.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'Passed') + @($retry.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'Passed')
$failed = @($native.TestRun.Results.UnitTestResult | Where-Object outcome -ne 'Passed')
if ($failed.Count -ne 1 -or $failed[0].testName -notlike '*ConicGaussianPupilContinuation*' -or
    @($successful.testName | Sort-Object -Unique).Count -ne 52 -or $failed[0].testName -notin $successful.testName) { throw 'The exact 52-test native guard has unresolved cases.' }
$files = [Collections.Generic.List[object]]::new()
function Add-File([string]$FullPath, [string]$Path) {
    $f = Get-Item -LiteralPath $FullPath
    $files.Add([pscustomobject]@{ path=$Path; fullPath=$f.FullName; bytes=$f.Length; sha256=(Get-FileHash -LiteralPath $f.FullName).Hash.ToLowerInvariant() })
}
foreach ($f in Get-ChildItem -LiteralPath $root -File -Recurse) {
    $rel = [IO.Path]::GetRelativePath($root, $f.FullName).Replace('\','/')
    if ($rel -match '(^|/)(bin|obj|pre-fix-core-host)/' -or $f.Extension -in @('.dll','.exe','.pdb','.lib','.exp','.obj') -or $f.Name -eq 'archive-receipt.json') { continue }
    Add-File $f.FullName ('run/' + $rel)
}
$fixtureRoot = Join-Path $repo 'validation/zemax/2026-r1/wide-schmidt-repair-2026-10-21'
foreach ($f in Get-ChildItem -LiteralPath $fixtureRoot -File -Recurse) { Add-File $f.FullName ('fixtures/' + [IO.Path]::GetRelativePath($fixtureRoot, $f.FullName).Replace('\','/')) }
foreach ($name in @('WideSchmidtRepair.cs','WideSchmidtRays.cs','Program.cs','Objectives20261020.csproj','README.md','Prepare-WideSchmidtFixtures.ps1','Pack-WideSchmidtEvidence.ps1')) { Add-File (Join-Path $PSScriptRoot $name) ('diagnostic-source/' + $name) }
foreach ($file in @('src/OpticalSystemDesign.Core/Geometries/GeometryModels.cs','src/OpticalSystemDesign.Core/Geometries/StandardGeometry.VertexPlane.cs',
    'tests/OpticalSystemDesign.Tests/WideAngleAspherePrecisionTests.cs','tests/OpticalSystemDesign.ZemaxComparison.Tests/WideSchmidtNativeRepairTests.cs',
    'tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json')) { Add-File (Join-Path $repo $file) ('source/' + $file) }
if (@($files | Group-Object path | Where-Object Count -ne 1).Count) { throw 'Duplicate archive paths.' }
$stream = [IO.File]::Open($archive, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($f in $files) {
            $entry = $zip.CreateEntry($f.path, [IO.Compression.CompressionLevel]::Optimal)
            $target = $entry.Open(); $source = [IO.File]::OpenRead($f.fullPath)
            try { $source.CopyTo($target) } finally { $source.Dispose(); $target.Dispose() }
        }
        $writer = [IO.StreamWriter]::new($zip.CreateEntry('payload-manifest.json').Open(), [Text.UTF8Encoding]::new($false))
        try { $writer.Write((@($files | Select-Object path,bytes,sha256) | ConvertTo-Json -Depth 5)) } finally { $writer.Dispose() }
    } finally { $zip.Dispose() }
} finally { $stream.Dispose() }
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($f in $files) {
        $entry = $zip.GetEntry($f.path)
        if (!$entry -or $entry.Length -ne $f.bytes) { throw 'Missing/truncated evidence.' }
        $source = $entry.Open()
        try { $hash = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($source)) } finally { $source.Dispose() }
        if ($hash -cne $f.sha256) { throw 'Evidence readback mismatch.' }
    }
    if ($zip.Entries.Count -ne $files.Count + 1) { throw 'Unexpected archive entries.' }
} finally { $zip.Dispose() }
[ordered]@{ path=$ArchivePath; bytes=(Get-Item -LiteralPath $archive).Length; sha256=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant(); payloadFiles=$files.Count; entries=$files.Count+1; verifiedReadback=$true } |
    ConvertTo-Json | Set-Content (Join-Path $root 'archive-receipt.json') -Encoding utf8
Get-Content (Join-Path $root 'archive-receipt.json')
