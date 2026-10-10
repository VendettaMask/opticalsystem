param([Parameter(Mandatory)][string]$RepositoryRoot,
      [Parameter(Mandatory)][string]$EvidenceRoot)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$evidence = (Resolve-Path -LiteralPath $EvidenceRoot).Path
. (Join-Path $PSScriptRoot 'FrozenIntegrity.ps1')
function Hash([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Read-Trx([string]$relative) {
    $path = Join-Path $repository $relative
    [xml]$xml = Get-Content -LiteralPath $path -Raw
    $c = $xml.TestRun.ResultSummary.Counters
    $rows = @($xml.TestRun.Results.UnitTestResult)
    if ($rows.Count -ne [int]$c.total) { throw "Incomplete TRX: $relative" }
    [pscustomobject]@{path=$relative;sha256=(Hash $path);total=[int]$c.total;passed=[int]$c.passed;
        failed=[int]$c.failed;skipped=([int]$c.total-[int]$c.passed-[int]$c.failed);
        start=$xml.TestRun.Times.start;finish=$xml.TestRun.Times.finish;rows=$rows}
}
function Summary($run) { $run | Select-Object path,sha256,total,passed,failed,skipped,start,finish }
function Inventory($run) {
    $map=@{}
    foreach ($row in $run.rows) { $key=$row.testId+'|'+$row.testName; if (!$map.ContainsKey($key)) {$map[$key]=0}; $map[$key]++ }
    return $map
}
function Build-Summary([string]$configuration) {
    $path=Join-Path $evidence "build-$configuration-final.log"
    $content=Get-Content -LiteralPath $path -Raw
    if ($content -notmatch '已成功生成。' -or $content -notmatch '(?m)^\s*0 个警告\s*$' -or
        $content -notmatch '(?m)^\s*0 个错误\s*$') {throw 'Default build did not pass cleanly.'}
    [ordered]@{configuration=$configuration;sha256=(Hash $path);warnings=0;errors=0;defaultOutput=$true}
}
$prefix=[IO.Path]::GetRelativePath($repository,$evidence).Replace('\','/')
$formal=Read-Trx "$prefix/formal-release-final/formal-release-final.trx"
$debug=Read-Trx "$prefix/targeted-debug-final/targeted-debug-final.trx"
$comparison=Read-Trx "$prefix/comparison-release-final/comparison-release-final.trx"
$before=Read-Trx "$prefix/before/before.trx"
$nested=Read-Trx "$prefix/before-nested-propagation/before-nested-propagation.trx"
$previous=Read-Trx 'artifacts/validation/n02-layer-diagnosis-20261009/formal-release/formal-release.trx'
$previousComparison=Read-Trx 'artifacts/validation/n02-layer-diagnosis-20261009/comparison-release/comparison-release.trx'
$old=Inventory $previous; $current=Inventory $formal
$missing=@($old.Keys | Where-Object {!$current.ContainsKey($_) -or $current[$_] -ne $old[$_]})
$added=@($current.Keys | Where-Object {!$old.ContainsKey($_)} | Sort-Object)
if ($previous.total -ne 4546 -or $formal.total -ne 4567 -or $formal.passed -ne 4567 -or
    $missing.Count -ne 0 -or $added.Count -ne 21 -or $debug.total -ne 103 -or $debug.passed -ne 103) {throw 'Formal regression inventory differs.'}
$oldComparison=Inventory $previousComparison; $currentComparison=Inventory $comparison
if ($comparison.total -ne 180 -or $comparison.passed -ne 179 -or $comparison.failed -ne 1 -or
    $oldComparison.Count -ne $currentComparison.Count -or
    @($oldComparison.Keys | Where-Object {!$currentComparison.ContainsKey($_) -or $currentComparison[$_] -ne $oldComparison[$_]}).Count) {throw 'Comparison regression inventory differs.'}
$failures=@($comparison.rows | Where-Object {$_.outcome -eq 'Failed'})
$failureDetailsUnchanged=$true
foreach ($failure in $failures) {
    $prior=@($previousComparison.rows | Where-Object {$_.testId -eq $failure.testId -and $_.testName -eq $failure.testName})
    if ($prior.Count -ne 1 -or $prior[0].outcome -ne 'Failed' -or
        $prior[0].Output.ErrorInfo.Message -cne $failure.Output.ErrorInfo.Message -or
        $prior[0].Output.StdOut -cne $failure.Output.StdOut) {$failureDetailsUnchanged=$false}
}
if (!$failureDetailsUnchanged -or $formal.skipped -ne 0 -or $debug.skipped -ne 0 -or $comparison.skipped -ne 0) {throw 'Comparison changed or tests skipped.'}
$corePaths=@('src/OptilandWorkbench.Core','src/OptilandWorkbench.App','tests/OptilandWorkbench.Tests',
    'tests/OptilandWorkbench.ZemaxComparison.Tests','tools/diagnostics/N02Layers20261009')
$binaries=@()
foreach ($configuration in @('Release','Debug')) {
    foreach ($directory in $corePaths) {
        if ($configuration -eq 'Debug' -and $directory -eq 'tools/diagnostics/N02Layers20261009') {continue}
        $path="$directory/bin/$configuration/net10.0/OptilandWorkbench.Core.dll"
        $binaries += [ordered]@{configuration=$configuration;path=$path;sha256=(Hash (Join-Path $repository $path))}
    }
    if (@($binaries | Where-Object {$_.configuration -eq $configuration} | ForEach-Object {$_.sha256} | Sort-Object -Unique).Count -ne 1) {throw 'Default Core copies differ.'}
}
$frozen=Get-FrozenIntegrityReport $repository (Join-Path $repository 'artifacts/validation/n02-layer-diagnosis-20261009/frozen-after.json')
function TextHash([string]$value) {
    $provider=[Security.Cryptography.SHA256]::Create()
    try {[BitConverter]::ToString($provider.ComputeHash([Text.Encoding]::UTF8.GetBytes($value))).Replace('-','').ToLowerInvariant()}
    finally {$provider.Dispose()}
}
foreach ($row in $frozen.mismatches) {
    $content=[Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes((Join-Path $repository $row.path)))
    $lf=$content.Replace("`r`n","`n")
    $newlineEquivalent=(TextHash $lf) -ceq $row.expectedSha256 -or (TextHash $lf.Replace("`n","`r`n")) -ceq $row.expectedSha256
    $baseBlob=(git -C $repository rev-parse "12a8531:$($row.path)")
    $headBlob=(git -C $repository rev-parse "HEAD:$($row.path)")
    if ($LASTEXITCODE -ne 0) {throw 'Git identity cannot be read.'}
    $row | Add-Member -NotePropertyName newlineEquivalent -NotePropertyValue $newlineEquivalent
    $row | Add-Member -NotePropertyName baseGitBlob -NotePropertyValue $baseBlob
    $row | Add-Member -NotePropertyName currentGitBlob -NotePropertyValue $headBlob
    $row | Add-Member -NotePropertyName gitBlobUnchangedSinceSync -NotePropertyValue ($baseBlob -ceq $headBlob)
}
$historical=Join-Path $repository 'docs/validation/FFT_PUPIL_PHASE_REPAIR_2026-10-09.json'
if ((Hash $historical) -cne 'ca0918fddc86f519a78db0520b1bbe448cde849c1ebb3c65d4f3da280af2fb6e') {throw 'Historical ledger was modified.'}
$historicalLedger=Get-Content -LiteralPath $historical -Raw | ConvertFrom-Json
if ($frozen.expectedAggregateSha256 -cne $historicalLedger.frozenIntegrity.afterSha256) {throw 'Frozen manifest baseline identity was changed.'}
$scriptTests=@(Get-Content (Join-Path $evidence 'integrity-script-tests/results.json') -Raw | ConvertFrom-Json)
if ($scriptTests.Count -ne 8 -or @($scriptTests | Where-Object {!$_.passed}).Count) {throw 'Integrity script regressions differ.'}
$prepared=Get-Content (Join-Path $evidence 'prepared-native-final.json') -Raw | ConvertFrom-Json
if (@($prepared).Count -ne 4 -or @($prepared | Where-Object {$_.physicalRelativeL2 -ge 1e-6}).Count) {throw 'Prepared native controls regressed.'}
$nativeManifests=@(); $rawCount=0; $rawBytes=0L
foreach ($name in @('fft-pupil-phase-2026-10-09','n02-energy-layer-controls-2026-10-09','huygens-method-controls-2026-10-09')) {
    $root=Join-Path $repository "validation/zemax/2026-r1/$name"
    $path=Join-Path $root 'manifest.json'
    $manifest=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    foreach ($entry in $manifest.files) {
        $raw=[IO.Path]::GetFullPath((Join-Path $root $entry.path))
        if (!$raw.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {throw 'Native manifest escapes capture.'}
        if ((Get-Item -LiteralPath $raw).Length -ne $entry.bytes -or (Hash $raw) -cne $entry.sha256) {throw 'Native capture bytes differ.'}
        $rawCount++; $rawBytes+=$entry.bytes
    }
    $nativeManifests += [ordered]@{path=[IO.Path]::GetRelativePath($repository,$path).Replace('\','/');sha256=(Hash $path);files=@($manifest.files).Count}
}
if ($rawCount -ne 165) {throw 'Native capture inventory differs.'}
$n02Rows=@($comparison.rows | Where-Object {$_.testName -like '*Diffraction Encircled Energy*'})
$n02Output=$n02Rows.Output.StdOut -join "`n"
if ($n02Output -notmatch '0\.011173141548632002' -or $n02Output -notmatch '0\.013875784968407625') {throw 'N02 measured residual changed or was not recorded.'}
$sourcePaths=@('src/OptilandWorkbench.Core/Analysis/PupilSourceIdentity.cs','src/OptilandWorkbench.Core/Analysis/DiffractionEngine.cs',
    'src/OptilandWorkbench.Core/Analysis/WavefrontEngine.cs','src/OptilandWorkbench.Core/Analysis/JonesPupilEngine.cs',
    'tests/OptilandWorkbench.Tests/PreparedPupilProvenanceTests.cs','tools/validation/FrozenIntegrity.ps1',
    'tools/validation/FrozenIntegrity.Tests.ps1','tools/diagnostics/N02Layers20261009/CollectVerification.ps1',
    'tools/validation/CollectPreparedPupilRepair20261010.ps1')
$ledger=[ordered]@{
    date='2026-10-10';baseHead=(git -C $repository rev-parse HEAD);gitState='Uncommitted local repair; not pushed'
    scope='Prepared FFT input provenance and strict frozen-byte verification; no Huygens propagation or DEE integration changes'
    builds=@((Build-Summary 'Release'),(Build-Summary 'Debug'))
    before=(Summary $before);beforeNestedPropagation=(Summary $nested)
    formalRelease=(Summary $formal);debugTargeted=(Summary $debug);comparisonRelease=(Summary $comparison)
    formalRetention=[ordered]@{previous=4546;current=4567;identityOrOccurrenceDifference=$missing.Count;added=$added}
    comparisonRetention=[ordered]@{identities=180;failureDetailsUnchanged=$failureDetailsUnchanged;failureNames=@($failures.testName)}
    integrityScriptTests=$scriptTests;frozenIntegrity=$frozen
    historicalLedger=[ordered]@{sha256=(Hash $historical);unmodified=$true}
    defaultCoreCopies=$binaries;preparedNativeControls=$prepared
    retainedNativeCaptures=[ordered]@{files=$rawCount;bytes=$rawBytes;verified=$true;manifests=$nativeManifests}
    sourceHashes=@($sourcePaths | ForEach-Object {[ordered]@{path=$_;sha256=(Hash (Join-Path $repository $_))}})
    n01=[ordered]@{classification='Close';nrmse=0.0037256670590166724;propagationChanged=$false}
    n02=[ordered]@{classification='Difference';actualNrmse=0.011173141548632002;idealNrmse=0.013875784968407625;integrationChanged=$false}
    notRun=@('Full Debug','Laboratory suites','Six-file 132-case matrix','Installer/package','Manual desktop acceptance')
    excludedAttempts=@('Early interrupted formal Release','Unquoted initial TRX logger command','Early 101-case runs before nested propagation guard')
}
$ledger | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $repository 'docs/validation/PREPARED_PUPIL_INTEGRITY_REPAIR_2026-10-10.json') -Encoding utf8
[ordered]@{formalPassed=$formal.passed;debugPassed=$debug.passed;comparisonPassed=$comparison.passed;comparisonFailed=$comparison.failed;
    frozenMatched=$frozen.matched;frozenTotal=$frozen.count;frozenUnchanged=$frozen.unchanged;scriptTests=$scriptTests.Count} | ConvertTo-Json
