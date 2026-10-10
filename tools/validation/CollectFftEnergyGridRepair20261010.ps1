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
    if ($rows.Count -ne [int]$c.total) { throw "Incomplete TRX: $path" }
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
$targeted = Read-Trx (Join-Path $evidence 'targeted-release/targeted-release.trx')
$comparison = Read-Trx (Join-Path $evidence 'comparison-release-final/comparison-release-final.trx')
$before = Read-Trx (Join-Path $evidence 'before/before.trx')
$previousRoot = Join-Path $repository 'artifacts/validation/prepared-pupil-integrity-repair-20261010'
$previous = Read-Trx (Join-Path $previousRoot 'formal-release-final/formal-release-final.trx')
$previousComparison = Read-Trx (Join-Path $previousRoot 'comparison-release-final/comparison-release-final.trx')
$old = Inventory $previous; $current = Inventory $formal
$missing = @($old.Keys | Where-Object {!$current.ContainsKey($_) -or $current[$_] -ne $old[$_]})
$added = @($current.Keys | Where-Object {!$old.ContainsKey($_)} | Sort-Object)
if ($previous.total -ne 4567 -or $formal.total -ne 4577 -or $formal.passed -ne 4577 -or $missing.Count -or
    $added.Count -ne 10 -or $debug.total -ne 177 -or $debug.passed -ne 177 -or $targeted.passed -ne 177 -or
    $before.total -ne 10 -or $before.failed -ne 10) { throw 'Formal regression inventory differs.' }
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
foreach ($run in @($formal,$debug,$targeted,$comparison)) {if ($run.skipped) {throw 'Unexpected skipped tests.'}}
$n02 = @($comparison.rows | Where-Object {$_.testName -like '*Diffraction Encircled Energy*'}).Output.StdOut -join "`n"
if ($n02 -notmatch '0\.011173141548632002' -or $n02 -notmatch '0\.013875784968407625') {throw 'N02 residual changed or missing.'}
$builds = @(); $binaries = @()
foreach ($configuration in @('Release','Debug')) {
    $log = Join-Path $evidence ('build-'+$configuration.ToLowerInvariant()+'-final.log')
    $content = Get-Content -LiteralPath $log -Raw
    if ($content -notmatch '已成功生成。' -or $content -notmatch '(?m)^\s*0 个警告\s*$' -or
        $content -notmatch '(?m)^\s*0 个错误\s*$') {throw 'Default build did not pass cleanly.'}
    $builds += [ordered]@{configuration=$configuration;sha256=(Hash $log);warnings=0;errors=0;defaultOutput=$true}
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
$referenceDirectory = Join-Path $repository 'artifacts/validation/huygens-reference-20261010/final-current-core'
$referenceRun = Read-Trx (Join-Path $referenceDirectory 'reference-final.trx')
if ($referenceRun.total -ne 5 -or $referenceRun.passed -ne 5 -or $referenceRun.skipped) {throw 'Reference measurement run incomplete.'}
$referenceCoreHash = Hash (Join-Path $repository 'tools/diagnostics/HuygensReference20261010/bin/Release/net10.0/OptilandWorkbench.Core.dll')
if ($referenceCoreHash -cne (Hash (Join-Path $repository 'src/OptilandWorkbench.Core/bin/Release/net10.0/OptilandWorkbench.Core.dll'))) {
    throw 'Reference measurement used an older Core.'
}
$referenceRows = @('endpoints','centers','zemax','halfopen','closed32intervals') | ForEach-Object {
    $path=Join-Path $referenceDirectory ($_.ToString()+'.json')
    $r=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    [ordered]@{nodes=$r.nodes;pupilCount=$r.pupilCount;illuminated=$r.illuminated;relativeL2=$r.relativeL2;
        maximumAbsolute=$r.maximumAbsolute;sha256=(Hash $path);precisionGatePassed=$false}
}
$prepared = @(Get-Content (Join-Path $evidence 'prepared-native-final.json') -Raw | ConvertFrom-Json)
if ($prepared.Count -ne 4 -or @($prepared | Where-Object {$_.physicalRelativeL2 -ge 1e-6}).Count) {throw 'Prepared native controls regressed.'}
$sourcePaths=@('src/OptilandWorkbench.Core/Analysis/Radiometry/EncircledEnergyVariants.cs',
    'tests/OptilandWorkbench.Tests/FftEnergyGridIntegrityTests.cs','tools/diagnostics/HuygensReference20261010/PlanarReferenceTests.cs',
    'tools/diagnostics/HuygensReference20261010/HuygensReference20261010.csproj',
    'tools/diagnostics/HuygensReference20261010/packages.lock.json','tools/validation/CollectFftEnergyGridRepair20261010.ps1')
$installationPaths=@('D:\Program Files\ANSYS Inc\v261\Zemax OpticStudio','C:\Program Files\ANSYS Inc\v261\Zemax OpticStudio',
    'C:\Program Files\Zemax OpticStudio')
$installedProducts=@(Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
    Where-Object {$_.DisplayName -match 'Zemax|OpticStudio|Ansys'} | Select-Object DisplayName,DisplayVersion,InstallLocation)
$registeredDataRoot=(Get-ItemProperty -LiteralPath 'HKCU:\Software\Zemax' -ErrorAction SilentlyContinue).ZemaxRoot
$ledger=[ordered]@{
    date='2026-10-10';baseHead=(git -C $repository rev-parse HEAD);gitState='Uncommitted local repair; not pushed'
    scope='FFT energy plot retains zero-pixel geometry and rejects invalid intensity; independent Huygens planar reference measured but not adopted'
    builds=$builds;before=(Summary $before);formalRelease=(Summary $formal);debugTargeted=(Summary $debug);
    releaseTargeted=(Summary $targeted);comparisonRelease=(Summary $comparison)
    formalRetention=[ordered]@{previous=4567;current=4577;identityOrOccurrenceDifference=$missing.Count;added=$added}
    comparisonRetention=[ordered]@{identities=180;failureDetailsUnchanged=$true;failureNames=@($failures.testName)}
    frozenIntegrity=$frozen;historicalLedger=[ordered]@{sha256=(Hash $historical);unmodified=$true}
    previousRepairLedger=[ordered]@{path='docs/validation/PREPARED_PUPIL_INTEGRITY_REPAIR_2026-10-10.json';
        sha256=(Hash (Join-Path $repository 'docs/validation/PREPARED_PUPIL_INTEGRITY_REPAIR_2026-10-10.json'));scope='Previous source/binary revision; not current validation'}
    defaultCoreCopies=$binaries;preparedNativeControls=$prepared
    retainedNativeCaptures=[ordered]@{files=$rawCount;bytes=$rawBytes;verified=$true;newCapture=$false}
    currentNativeEnvironment=[ordered]@{checkedPaths=@($installationPaths | ForEach-Object {[ordered]@{path=$_;exists=(Test-Path -LiteralPath $_)}});
        registeredDataRoot=[ordered]@{path=$registeredDataRoot;exists=([bool]$registeredDataRoot -and (Test-Path -LiteralPath $registeredDataRoot));
            semantics='Registered data directory, not proof of program/API installation.'};
        matchingInstalledProducts=$installedProducts;apiConnectedThisTurn=$false;licenseCheckedThisTurn=$false;
        currentHostSupportedByUser=$false;captureHost='Another computer, confirmed by user on 2026-10-10';
        semantics='User confirmed current host is unsupported and native Zemax capture ran on another computer; no local API or license certification this turn.'}
    huygensReference=[ordered]@{measurementRun=(Summary $referenceRun);coreSha256=$referenceCoreHash;precisionGatePassed=$false;controls=$referenceRows;productModelChanged=$false}
    sourceHashes=@($sourcePaths | ForEach-Object {[ordered]@{path=$_;sha256=(Hash (Join-Path $repository $_))}})
    n01=[ordered]@{classification='Close';nrmse=0.0037256670590166724;propagationChanged=$false}
    n02=[ordered]@{classification='Difference';actualNrmse=0.011173141548632002;idealNrmse=0.013875784968407625;
        zeroPixelGeometryFixed=$true;nativeResidualClosed=$false}
    notRun=@('Full Debug','Laboratory suites','Six-file 132-case matrix','Installer/package','Manual desktop acceptance','New native capture')
    excludedAttempts=@('Initial Huygens reference compilation error; not a numerical failure')
}
$ledger | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $repository 'docs/validation/FFT_ENERGY_GRID_INTEGRITY_REPAIR_2026-10-10.json') -Encoding utf8
[ordered]@{formalPassed=$formal.passed;debugPassed=$debug.passed;comparisonPassed=$comparison.passed;comparisonFailed=$comparison.failed;
    frozenMatched=$frozen.matched;frozenTotal=$frozen.count;referenceMeasurements=$referenceRun.passed;huygensPrecisionPassed=$false} | ConvertTo-Json
