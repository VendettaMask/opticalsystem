param([Parameter(Mandatory)][string]$RepositoryRoot,
      [Parameter(Mandatory)][string]$EvidenceRoot)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$evidence = (Resolve-Path -LiteralPath $EvidenceRoot).Path
$destination = Join-Path $repository 'docs/validation/HUYGENS_POLARIZATION_REPAIR_2026-10-12.json'
if (Test-Path -LiteralPath $destination) { throw 'Preserve the existing phase ledger; use a new phase for later verification.' }
function Hash([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Read-Trx([string]$path) {
    if ($path.EndsWith('.gz')) {
        $compressedStream = [IO.File]::OpenRead($path)
        $gzip = [IO.Compression.GZipStream]::new($compressedStream,[IO.Compression.CompressionMode]::Decompress)
        $reader = [IO.StreamReader]::new($gzip)
        try { [xml]$xml = $reader.ReadToEnd() } finally { $reader.Dispose(); $gzip.Dispose(); $compressedStream.Dispose() }
    } else { [xml]$xml = Get-Content -LiteralPath $path -Raw }
    $c = $xml.TestRun.ResultSummary.Counters
    $rows = @($xml.TestRun.Results.UnitTestResult)
    if ($rows.Count -ne [int]$c.total -or [int]$c.total -le 0) { throw "Incomplete TRX: $path" }
    [pscustomobject]@{path=[IO.Path]::GetRelativePath($repository,$path).Replace('\','/');sha256=(Hash $path);
        total=[int]$c.total;passed=[int]$c.passed;failed=[int]$c.failed;
        skipped=([int]$c.total-[int]$c.passed-[int]$c.failed);start=$xml.TestRun.Times.start;finish=$xml.TestRun.Times.finish;rows=$rows}
}
function Summary($run) { $run | Select-Object path,sha256,total,passed,failed,skipped,start,finish }
function Inventory($run) {
    $map=[Collections.Generic.Dictionary[string,int]]::new([StringComparer]::Ordinal)
    foreach ($row in $run.rows) { $key=$row.testId+'|'+$row.testName; if (!$map.ContainsKey($key)) {$map[$key]=0}; $map[$key]++ }
    return ,$map
}
$formal=Read-Trx (Join-Path $evidence 'formal-release-final/formal-release-final.trx')
$comparison=Read-Trx (Join-Path $evidence 'comparison-release-final/comparison-release-final.trx')
$debug=Read-Trx (Join-Path $evidence 'debug-related-final/debug-related-final.trx')
$before=Read-Trx (Join-Path $evidence 'before/before.trx')
$isolated=Read-Trx (Join-Path $evidence 'isolated-contracts/isolated-contracts.trx')
$firstFormal=Read-Trx (Join-Path $evidence 'formal-release-complete/formal-release-complete.trx')
$firstComparison=Read-Trx (Join-Path $evidence 'comparison-release-complete/comparison-release-complete.trx')
$firstDebug=Read-Trx (Join-Path $evidence 'debug-related-complete/debug-related-complete.trx')
$oldFormal=Read-Trx (Join-Path $repository 'artifacts/validation/huygens-spherical-repair-20261011/product-release-complete.trx.gz')
$oldComparison=Read-Trx (Join-Path $repository 'artifacts/validation/huygens-spherical-repair-20261011/comparison-release-complete.trx.gz')
$old=Inventory $oldFormal; $current=Inventory $formal
$missing=@($old.Keys|Where-Object {!$current.ContainsKey($_) -or $current[$_] -ne $old[$_]})
$added=@($current.Keys|Where-Object {!$old.ContainsKey($_)}|Sort-Object -CaseSensitive)
$oldTool=Inventory $oldComparison; $currentTool=Inventory $comparison
$toolChanges=@($oldTool.Keys|Where-Object {!$currentTool.ContainsKey($_) -or $currentTool[$_] -ne $oldTool[$_]})
if ($oldFormal.total -ne 4667 -or $formal.total -ne 4697 -or $formal.passed -ne 4697 -or $missing.Count -or $added.Count -ne 30 -or
    $oldTool.Count -ne $currentTool.Count -or $toolChanges.Count -or $comparison.passed -ne 182 -or $comparison.total -ne 182 -or
    $debug.passed -ne $debug.total -or $debug.total -ne 440 -or $before.total -ne 2 -or $before.failed -ne 2 -or
    $isolated.total -ne 80 -or $isolated.passed -ne 80 -or $isolated.skipped) {
    throw 'Final test counts or preserved identities differ.'
}
if ($firstFormal.total -ne 4697 -or $firstFormal.passed -ne 4694 -or $firstFormal.failed -ne 3 -or $firstFormal.skipped -or
    $firstComparison.total -ne 182 -or $firstComparison.passed -ne 182 -or $firstComparison.skipped -or
    $firstDebug.total -ne 440 -or $firstDebug.passed -ne 440 -or $firstDebug.skipped -or $before.skipped) {
    throw 'Retained earlier-run inventory differs.'
}
foreach ($run in @($formal,$comparison,$debug)) { if ($run.failed -or $run.skipped) { throw 'Final verification did not pass completely.' } }
$nativeRows=@($formal.rows|Where-Object {$_.testName -like '*CapturedUnpolarizedHuygensIntensityMatchesTheOriginalNativeGrid*'})
if ($nativeRows.Count -ne 2) {throw 'Missing native polarization cases.'}
$metrics=@(foreach($row in $nativeRows) {
    if ($row.Output.StdOut -notmatch '(polarized-field[13]-linear): relativeL2=([^;]+); maximumAbsolute=([^\s]+)') {throw 'Missing native metrics.'}
    $relative=[double]::Parse($Matches[2],[Globalization.CultureInfo]::InvariantCulture)
    $maximum=[double]::Parse($Matches[3],[Globalization.CultureInfo]::InvariantCulture)
    if ($relative -ge 5e-5 -or $maximum -ge 1e-5) {throw 'Native polarization tolerance failed.'}
    [ordered]@{name=$Matches[1];relativeL2=$relative;maximumAbsolute=$maximum;nativePoints=1024;passed=$true}
})
$builds=@(); $binaries=@()
foreach ($configuration in @('Debug','Release')) {
    $log=Join-Path $evidence ('build-'+$configuration.ToLowerInvariant()+'-final-compatible.log')
    $content=Get-Content -LiteralPath $log -Raw
    if ($content -notmatch '已成功生成。' -or $content -notmatch '(?m)^\s*0 个警告\s*$' -or $content -notmatch '(?m)^\s*0 个错误\s*$') {throw 'Default build did not pass cleanly.'}
    $builds += [ordered]@{configuration=$configuration;path=[IO.Path]::GetRelativePath($repository,$log).Replace('\','/');sha256=(Hash $log);warnings=0;errors=0;defaultOutput=$true}
    foreach ($directory in @('src/OptilandWorkbench.Core','src/OptilandWorkbench.App','tests/OptilandWorkbench.Tests','tests/OptilandWorkbench.ZemaxComparison.Tests')) {
        $path="$directory/bin/$configuration/net10.0/OptilandWorkbench.Core.dll"
        $binaries += [ordered]@{configuration=$configuration;path=$path;sha256=(Hash (Join-Path $repository $path))}
    }
    if (@($binaries|Where-Object {$_.configuration -eq $configuration}|ForEach-Object {$_.sha256}|Sort-Object -Unique).Count -ne 1) {throw 'Default Core copies differ.'}
}
$nativeRoot=Join-Path $repository 'validation/zemax/2026-r1/huygens-extensions-2026-10-11'
$manifestPath=Join-Path $nativeRoot 'capture-file-hashes.json'
if ((Hash $manifestPath) -cne '9b80b110f9580e660eac8014b6a5f151e2a91ae1c27c54a91867fcbc8a35958f' -or
    (Hash (Join-Path $nativeRoot 'source.ZMX')) -cne '8bcc937c2c2e02ba175f38875fd0def40db547f7eedab509cbfd1fed4353e0e8') {throw 'Native manifest or source identity differs.'}
$manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
if ($manifest.Count -ne 306) {throw 'Native capture inventory differs.'}
foreach ($entry in $manifest) {
    $path=[IO.Path]::GetFullPath((Join-Path $nativeRoot $entry.path))
    if (!$path.StartsWith($nativeRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
        (Hash $path) -cne $entry.sha256) {throw 'Native capture bytes differ.'}
}
$frozenArguments=@('-NoProfile','-File',(Join-Path $PSScriptRoot 'GitFrozenIntegrity.ps1'),'-RepositoryRoot',$repository,
    '-ManifestPath',(Join-Path $repository 'artifacts/validation/n02-layer-diagnosis-20261009/frozen-after.json'),
    '-ExpectedAggregateSha256','3a5fde98e1c881700f00be1c62d6ab4ff6f4feacc90454046ed787e80bf28c10')
$frozenJson=& pwsh @frozenArguments
if ($LASTEXITCODE -ne 0) {throw 'Frozen Git or working bytes failed.'}
$frozen=$frozenJson -join "`n"|ConvertFrom-Json
$historical='docs/HUYGENS_SPHERICAL_REPAIR_2026-10-11.json'
if ((Hash (Join-Path $repository $historical)) -cne '2980d73fde660765c744302597eceea4caa34d9853305737aaa79cf0e7f9dcb2') {throw 'Historical ledger bytes changed.'}
$sourcePaths=@('src/OptilandWorkbench.Core/Analysis/DiffractionEngine.cs','src/OptilandWorkbench.Core/Coatings/FresnelPower.cs',
    'src/OptilandWorkbench.Core/Raytrace/SequentialRayTracer.Polarization.cs','src/OptilandWorkbench.Core/Raytrace/SequentialRayTracer.ApertureDiagnostics.cs',
    'src/OptilandWorkbench.Core/Analysis/Diffraction/HuygensPsfSynthesis.cs','src/OptilandWorkbench.Core/Analysis/Diffraction/HuygensDiffractionAnalyses.cs',
    'src/OptilandWorkbench.Core/Analysis/Diffraction/PsfProfileAnalyses.cs','src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Analysis.Parameters.cs',
    'src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Localization.cs','tests/OptilandWorkbench.Tests/HuygensPolarizationRepairTests.cs',
    'tests/OptilandWorkbench.Tests/PolarizationPsfLabelTests.cs','tools/validation/CollectHuygensPolarizationRepair20261012.ps1')
$ledger=[ordered]@{
    date='2026-10-12';baseHead=(git -C $repository rev-parse HEAD);gitState='Uncommitted local repair; not pushed'
    scope='Original MS-L7 Unpolarized=true Huygens intensity residual closed; limited transparent coherent XYZ path implemented without new native phase certification'
    builds=$builds;formalRelease=(Summary $formal);comparisonRelease=(Summary $comparison);debugRelated=(Summary $debug);isolatedContracts=(Summary $isolated)
    before=(Summary $before);nativeMetrics=$metrics;nativeCriteria=[ordered]@{maximumAbsolute=1e-5;relativeL2=5e-5;pointsPerCase=1024;fittedAlignmentOrScale=$false}
    formalRetention=[ordered]@{previous=4667;current=4697;identityOrOccurrenceDifference=$missing.Count;added=$added;
        existingContractUpdate='Existing Huygens label test retains its identity and now asserts the executed mean-power mode; FFT experimental contract unchanged'}
    comparisonRetention=[ordered]@{previous=182;current=182;identityOrOccurrenceDifference=$toolChanges.Count;referencesOrTolerancesChanged=$false}
    defaultCoreCopies=$binaries;sourceHashes=@($sourcePaths|ForEach-Object {[ordered]@{path=$_;sha256=(Hash (Join-Path $repository $_))}})
    retainedNative=[ordered]@{manifestSha256=(Hash $manifestPath);files=306;sourceSha256=(Hash (Join-Path $nativeRoot 'source.ZMX'));newCapture=$false}
    historicalLedger=[ordered]@{path=$historical;sha256=(Hash (Join-Path $repository $historical));unmodified=$true}
    frozenIntegrity=$frozen
    firstCompleteAttempt=[ordered]@{formal=(Summary $firstFormal);comparison=(Summary $firstComparison);debug=(Summary $firstDebug);
        failures=@($firstFormal.rows|Where-Object {$_.outcome -eq 'Failed'}|Select-Object testId,testName);
        sourceRecord=[ordered]@{path='artifacts/validation/huygens-polarization-repair-20261012/first-complete-attempt-source.json';sha256=(Hash (Join-Path $evidence 'first-complete-attempt-source.json'))};
        scope='Concurrent validation before restoring the historical exception-message prefix; not final acceptance'}
    earlierAttempts=@('Before: original numerical residuals reproduced in both 1024-point cases',
        'First absorbing-interface attempt selected backward exit flux; rejected rather than accepting negative power',
        'Forward-power attempt passed axial only; off-axis same-field normalization still missing',
        'First related run: exception wording lost complex-index marker; no numerical defect in that failure',
        'First complete run: historical error-prefix mismatch and timed waits recorded; final validation isolated below')
    boundaries=@('Unpolarized mean transmission is not the incoherent average of two diffracted vector fields',
        'Absorbing interface power is an explicit homogeneous-wave bare-interface contract, not arbitrary inhomogeneous XYZ transport',
        'Existing full TracePolarized and default diagnostics still reject absorbing-interface electric fields',
        'Transparent coherent XYZ has independent mathematical regression but no new native Jones/phase certificate',
        'Coherent afocal and GRIN remain explicitly unsupported; legacy afocal unpolarized approximation remains labeled')
    currentNativeEnvironment=[ordered]@{installationOrLicenseProbe=$false;apiConnected=$false;newNativeCertification=$false}
    excludedFollowup=@('Independent Cooke residual/certification','Independent Tessar residual/certification')
    notRun=@('Full Debug','Laboratory suites','Installer/package','Manual desktop acceptance','Six-file 132-case matrix','New native capture')
}
$ledger|ConvertTo-Json -Depth 15|Set-Content -LiteralPath $destination -Encoding utf8
[ordered]@{formalPassed=$formal.passed;comparisonPassed=$comparison.passed;debugPassed=$debug.passed;added=$added.Count;nativeCases=$metrics.Count;frozenMatched=$frozen.matched}|ConvertTo-Json
