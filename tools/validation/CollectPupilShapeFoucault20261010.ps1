param([Parameter(Mandatory)][string]$RepositoryRoot,
      [Parameter(Mandatory)][string]$EvidenceRoot)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$evidence = (Resolve-Path -LiteralPath $EvidenceRoot).Path
. (Join-Path $PSScriptRoot 'FrozenIntegrity.ps1')
function Hash([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Read-Trx([string]$path) {
    [xml]$xml = Get-Content -LiteralPath $path -Raw
    $c = $xml.TestRun.ResultSummary.Counters
    $rows = @($xml.TestRun.Results.UnitTestResult)
    if ($rows.Count -ne [int]$c.total -or [int]$c.total -le 0) { throw "Incomplete TRX: $path" }
    [pscustomobject]@{path=[IO.Path]::GetRelativePath($repository,$path).Replace('\','/');sha256=(Hash $path);
        total=[int]$c.total;passed=[int]$c.passed;failed=[int]$c.failed;
        skipped=([int]$c.total-[int]$c.passed-[int]$c.failed);start=$xml.TestRun.Times.start;finish=$xml.TestRun.Times.finish;rows=$rows}
}
function Summary($run) { $run | Select-Object path,sha256,total,passed,failed,skipped,start,finish }
function Inventory($run) {
    $map=@{}
    foreach ($row in $run.rows) { $key=$row.testId+'|'+$row.testName; if (!$map.ContainsKey($key)) {$map[$key]=0}; $map[$key]++ }
    return $map
}
$formal = Read-Trx (Join-Path $evidence 'formal-release-final/formal-release-final.trx')
$debug = Read-Trx (Join-Path $evidence 'targeted-debug-final/targeted-debug-final.trx')
$targeted = Read-Trx (Join-Path $evidence 'targeted-release-final/targeted-release-final.trx')
$render = Read-Trx (Join-Path $evidence 'render-release/render-release.trx')
$comparison = Read-Trx (Join-Path $evidence 'comparison-release-final/comparison-release-final.trx')
# The previous evidence directory name is fixed by the retained prior ledger, not guessed.
$previousLedger = Get-Content -LiteralPath (Join-Path $repository 'docs/validation/FFT_ENERGY_GRID_INTEGRITY_REPAIR_2026-10-10.json') -Raw | ConvertFrom-Json
if ((Hash (Join-Path $repository 'docs/validation/FFT_ENERGY_GRID_INTEGRITY_REPAIR_2026-10-10.json')) -cne
    '59a6fa3374a605aeb01dc9c30ca0b100bb6623d14880160129f8ea734bf53b9f' -or
    (Hash (Join-Path $repository 'docs/validation/PREPARED_PUPIL_INTEGRITY_REPAIR_2026-10-10.json')) -cne
    'ffa9f5503423e51d48e733835c009b2d7300bae378614f523fca1595d8a0dfa1') {throw 'Previous repair ledger bytes changed.'}
$previous = Read-Trx (Join-Path $repository $previousLedger.formalRelease.path)
$previousComparison = Read-Trx (Join-Path $repository $previousLedger.comparisonRelease.path)
$old = Inventory $previous; $current = Inventory $formal
$missing = @($old.Keys | Where-Object {!$current.ContainsKey($_) -or $current[$_] -ne $old[$_]})
$added = @($current.Keys | Where-Object {!$old.ContainsKey($_)} | Sort-Object)
if ($previous.total -ne 4577 -or $formal.total -ne 4608 -or $formal.passed -ne $formal.total -or
    $missing.Count -or $added.Count -ne 31 -or $targeted.total -ne 60 -or $targeted.passed -ne 60 -or
    $debug.passed -ne $debug.total -or $debug.total -lt 60 -or $render.passed -ne 1) { throw 'Formal regression inventory differs.' }
$oldComparison = Inventory $previousComparison; $newComparison = Inventory $comparison
if ($comparison.total -ne 180 -or $comparison.passed -ne 179 -or $comparison.failed -ne 1 -or
    $oldComparison.Count -ne $newComparison.Count -or
    @($oldComparison.Keys | Where-Object {!$newComparison.ContainsKey($_) -or $newComparison[$_] -ne $oldComparison[$_]}).Count) {
    throw 'Comparison identities differ.'
}
$failures = @($comparison.rows | Where-Object {$_.outcome -eq 'Failed'})
foreach ($failure in $failures) {
    $prior = @($previousComparison.rows | Where-Object {$_.testId -eq $failure.testId -and $_.testName -eq $failure.testName})
    if ($prior.Count -ne 1 -or $prior[0].outcome -ne 'Failed' -or
        $prior[0].Output.ErrorInfo.Message -cne $failure.Output.ErrorInfo.Message -or
        $prior[0].Output.StdOut -cne $failure.Output.StdOut) {throw 'Comparison failure details changed.'}
}
foreach ($run in @($formal,$debug,$targeted,$comparison,$render)) {if ($run.skipped) {throw 'Unexpected skipped tests.'}}
$n02 = @($comparison.rows | Where-Object {$_.testName -like '*Diffraction Encircled Energy*'}).Output.StdOut -join "`n"
if ($n02 -notmatch '0\.011173141548632002' -or $n02 -notmatch '0\.013875784968407625') {throw 'N02 residual changed or missing.'}
$builds = @(); $binaries = @()
foreach ($configuration in @('Release','Debug')) {
    $log = Join-Path $evidence ('build-'+$configuration.ToLowerInvariant()+'-final.log')
    $content = Get-Content -LiteralPath $log -Raw
    if ($content -notmatch '已成功生成。' -or $content -notmatch '(?m)^\s*0 个警告\s*$' -or
        $content -notmatch '(?m)^\s*0 个错误\s*$') {throw 'Default build did not pass cleanly.'}
    $builds += [ordered]@{configuration=$configuration;path=[IO.Path]::GetRelativePath($repository,$log).Replace('\','/');
        sha256=(Hash $log);warnings=0;errors=0;defaultOutput=$true}
    foreach ($directory in @('src/OptilandWorkbench.Core','src/OptilandWorkbench.App','tests/OptilandWorkbench.Tests',
        'tests/OptilandWorkbench.ZemaxComparison.Tests')) {
        $path="$directory/bin/$configuration/net10.0/OptilandWorkbench.Core.dll"
        $binaries += [ordered]@{configuration=$configuration;path=$path;sha256=(Hash (Join-Path $repository $path))}
    }
    if (@($binaries | Where-Object {$_.configuration -eq $configuration} | ForEach-Object {$_.sha256} | Sort-Object -Unique).Count -ne 1) {
        throw 'Default Core copies differ.'
    }
}
$frozen = Get-FrozenIntegrityReport $repository (Join-Path $repository 'artifacts/validation/n02-layer-diagnosis-20261009/frozen-after.json')
$historical = Join-Path $repository 'docs/validation/FFT_PUPIL_PHASE_REPAIR_2026-10-09.json'
if ((Hash $historical) -cne 'ca0918fddc86f519a78db0520b1bbe448cde849c1ebb3c65d4f3da280af2fb6e' -or
    $frozen.expectedAggregateSha256 -cne '3a5fde98e1c881700f00be1c62d6ab4ff6f4feacc90454046ed787e80bf28c10' -or
    $frozen.actualAggregateSha256 -cne '7c1fc6e327ac3a0d0da17adb04d1a88a90999af25522136110cd0041a92b3ce0' -or
    $frozen.matched -ne 2660 -or $frozen.count -ne 2668) {throw 'Historical evidence identity changed.'}
$rawCount=0; $rawBytes=0L
foreach ($name in @('fft-pupil-phase-2026-10-09','n02-energy-layer-controls-2026-10-09','huygens-method-controls-2026-10-09')) {
    $root = Join-Path $repository "validation/zemax/2026-r1/$name"
    $manifest = Get-Content -LiteralPath (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json
    foreach ($entry in $manifest.files) {
        $path = [IO.Path]::GetFullPath((Join-Path $root $entry.path))
        if (!$path.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
            (Get-Item -LiteralPath $path).Length -ne $entry.bytes -or (Hash $path) -cne $entry.sha256) {throw 'Native capture bytes differ.'}
        $rawCount++; $rawBytes += $entry.bytes
    }
}
if ($rawCount -ne 165) {throw 'Native capture inventory differs.'}
$sourcePaths=@('src/OptilandWorkbench.Core/Analysis/Wavefront/FoucaultEngine.cs',
    'src/OptilandWorkbench.Core/Analysis/Wavefront/FoucaultAnalysis.cs','src/OptilandWorkbench.Core/Analysis/Wavefront/WavefrontAnalyses.cs',
    'src/OptilandWorkbench.Core/Analysis/DiffractionEngine.cs','src/OptilandWorkbench.Core/Analysis/AnalysisModels.cs',
    'src/OptilandWorkbench.Application/Contracts/WorkspaceContracts.cs','src/OptilandWorkbench.Application/Services/Mapping/WorkbenchMapper.cs',
    'src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Analysis.Parameters.cs',
    'src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Localization.cs',
    'src/OptilandWorkbench.App/Controls/FoucaultPlotControl.cs','src/OptilandWorkbench.App/Controls/WavefrontSurfaceControl.cs',
    'src/OptilandWorkbench.App/Panels/Analysis/AnalysisPanel.Results.cs','src/OptilandWorkbench.App/Panels/Analysis/AnalysisPanel.Parameters.cs',
    'tests/OptilandWorkbench.Tests/PupilShapeAndFoucaultPhysicsTests.cs','tests/OptilandWorkbench.Tests/PupilShapeAndFoucaultPanelTests.cs',
    'tests/OptilandWorkbench.Tests/FoucaultAnalysisTests.cs','tests/OptilandWorkbench.Tests/CalculationPathRepairTests.cs',
    'tests/OptilandWorkbench.Tests/CalculationPathPanelTests.cs','tests/OptilandWorkbench.Tests/WavefrontAimingRepairTests.cs',
    'tests/OptilandWorkbench.Tests/AnalysisGuiContractTests.cs','tools/validation/CollectPupilShapeFoucault20261010.ps1')
$screenshot = Join-Path $evidence 'screenshots/pupil-shape-foucault.png'
$ledger=[ordered]@{
    date='2026-10-10';baseHead=(git -C $repository rev-parse HEAD);gitState='Uncommitted local implementation; not pushed'
    scope='Focused working-F-number exit-pupil display projection; complex-field focal knife FFT and inverse-FFT ideal pupil reimaging'
    builds=$builds;formalRelease=(Summary $formal);debugTargeted=(Summary $debug);
    releaseTargeted=(Summary $targeted);renderRelease=(Summary $render);comparisonRelease=(Summary $comparison)
    formalRetention=[ordered]@{previous=4577;current=$formal.total;identityOrOccurrenceDifference=$missing.Count;added=$added;
        existingContractChanges=@('Foucault no longer capped to unit intensity or restricted to circular observation mask',
            'Supported Jones input replaces permanent Foucault polarization rejection; unsupported media still rejected')}
    comparisonRetention=[ordered]@{identities=180;failureDetailsUnchanged=$true;failureNames=@($failures.testName)}
    frozenIntegrity=$frozen;historicalLedger=[ordered]@{sha256=(Hash $historical);unmodified=$true}
    previousRepairLedgers=@('PREPARED_PUPIL_INTEGRITY_REPAIR_2026-10-10.json','FFT_ENERGY_GRID_INTEGRITY_REPAIR_2026-10-10.json') |
        ForEach-Object { [ordered]@{path='docs/validation/'+$_;sha256=(Hash (Join-Path $repository ('docs/validation/'+$_)));
            scope='Previous source/binary revision; not current validation'} }
    defaultCoreCopies=$binaries
    retainedNativeCaptures=[ordered]@{files=$rawCount;bytes=$rawBytes;verified=$true;newCapture=$false}
    currentNativeEnvironment=[ordered]@{currentHostSupportedByUser=$false;captureHost='Another computer, confirmed by user';
        apiConnectedThisTurn=$false;installationOrLicenseProbeThisTurn=$false;newNativeCertification=$false}
    screenshot=[ordered]@{path=[IO.Path]::GetRelativePath($repository,$screenshot).Replace('\','/');sha256=(Hash $screenshot);
        scope='Actual controls with real Cooke data; Skia headless render, not manual desktop or native numerical certification'}
    sourceHashes=@($sourcePaths | ForEach-Object {[ordered]@{path=$_;sha256=(Hash (Join-Path $repository $_))}})
    f01=[ordered]@{focusedProjectionImplemented=$true;physicalDataUnchanged=$true;
        arbitrary3dPupilCertified=$false;afocalBehavior='Explicit normalized coordinates; focal projection not applicable'}
    f02=[ordered]@{physicalFftKnifeImplemented=$true;polarization='Formal supported Jones transport; unpolarized intensity average';
        boundaries=@('Ideal pupil reimaging, not general vector POP','No afocal focus without focusing optics',
            'No GRIN or absorbing-interface Jones fallback','Reference bitmap registration/difference unimplemented')}
    n01=[ordered]@{classification='Close';nrmse=0.0037256670590166724;propagationChanged=$false}
    n02=[ordered]@{classification='Difference';actualNrmse=0.011173141548632002;idealNrmse=0.013875784968407625;nativeResidualClosed=$false}
    notRun=@('Full Debug','Laboratory suites','Six-file 132-case matrix','Installer/package','Manual desktop acceptance','New native capture',
        'Independent Huygens planar diagnostic: retained previous revision, not rerun')
    earlierAttempts=@('Missing cancellation namespace and Dock.Top namespace collision: compilation errors, not numerical results',
        'First executable 48/2/50: raw Cooke Jones fixture exceeded absorbing-interface support; transparent declared fixture used, product restriction preserved',
        'Second 50/50: earlier phase/display revision, not final validation',
        'Fourth 59/1/60: logarithmic desktop flag was not connected; fixed by typed plot metadata')
}
$ledger | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $repository 'docs/validation/PUPIL_SHAPE_FOUCAULT_IMPLEMENTATION_2026-10-10.json') -Encoding utf8
[ordered]@{formalPassed=$formal.passed;added=$added.Count;debugPassed=$debug.passed;targetedReleasePassed=$targeted.passed;
    comparisonPassed=$comparison.passed;comparisonFailed=$comparison.failed;frozenMatched=$frozen.matched;frozenTotal=$frozen.count} | ConvertTo-Json
