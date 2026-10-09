param([Parameter(Mandatory)][string]$RepositoryRoot, [Parameter(Mandatory)][string]$EvidenceRoot)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$evidence = (Resolve-Path -LiteralPath $EvidenceRoot).Path
function Hash([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Read-Trx([string]$path) {
    [xml]$xml = Get-Content -LiteralPath $path -Raw
    $counter = $xml.TestRun.ResultSummary.Counters
    $results = @($xml.TestRun.Results.UnitTestResult)
    if ($results.Count -ne [int]$counter.total) { throw "TRX result count differs: $path" }
    [ordered]@{
        path=[IO.Path]::GetRelativePath($repository,$path).Replace('\','/'); sha256=(Hash $path)
        total=[int]$counter.total; passed=[int]$counter.passed; failed=[int]$counter.failed
        skipped=([int]$counter.total-[int]$counter.passed-[int]$counter.failed)
        start=$xml.TestRun.Times.start; finish=$xml.TestRun.Times.finish
        results=$results
    }
}
function Summary($run) {
    [ordered]@{path=$run.path;sha256=$run.sha256;total=$run.total;passed=$run.passed;failed=$run.failed;skipped=$run.skipped;start=$run.start;finish=$run.finish}
}
function Inventory($run) {
    $map=@{}
    foreach($result in $run.results) {
        $identity=$result.testId+'|'+$result.testName
        if(!$map.ContainsKey($identity)){$map[$identity]=0};$map[$identity]++
    }
    return $map
}
function Build-Summary([string]$name) {
    $path=Join-Path $evidence ('build-'+$name+'-final.log')
    $content=Get-Content -LiteralPath $path -Raw
    $warnings=[regex]::Match($content,'(?m)^\s*(\d+) 个警告\s*$')
    $errors=[regex]::Match($content,'(?m)^\s*(\d+) 个错误\s*$')
    if(!$warnings.Success -or !$errors.Success -or $content -notmatch '已成功生成。'){throw 'Build log incomplete'}
    [ordered]@{path=[IO.Path]::GetRelativePath($repository,$path).Replace('\','/');sha256=(Hash $path);warnings=[int]$warnings.Groups[1].Value;errors=[int]$errors.Groups[1].Value;defaultOutput=$true}
}
$formal=Read-Trx (Join-Path $evidence 'formal-release/formal-release.trx')
$debug=Read-Trx (Join-Path $evidence 'targeted-debug-final/targeted-debug-final.trx')
$comparison=Read-Trx (Join-Path $evidence 'comparison-release/comparison-release.trx')
$previous=Read-Trx (Join-Path $repository 'artifacts/validation/fft-sampling-reference-repair-20261009/formal-release-final-v3/formal-release-final.trx')
$previousComparison=Read-Trx (Join-Path $repository 'artifacts/validation/fft-sampling-reference-repair-20261009/comparison-release-final/comparison-release-final.trx')
$old=Inventory $previous; $current=Inventory $formal
$missing=@($old.Keys | Where-Object { !$current.ContainsKey($_) -or $current[$_] -ne $old[$_] })
$added=@($current.Keys | Where-Object { !$old.ContainsKey($_) } | Sort-Object)
if($previous.total -ne 4539 -or $formal.total -ne 4546 -or $missing.Count -ne 0 -or $added.Count -ne 7){throw 'Formal test retention differs'}
$oldComparison=Inventory $previousComparison; $currentComparison=Inventory $comparison
$comparisonDifference=@($oldComparison.Keys | Where-Object { !$currentComparison.ContainsKey($_) -or $currentComparison[$_] -ne $oldComparison[$_] })
if($comparison.total -ne 180 -or $comparisonDifference.Count -ne 0 -or $currentComparison.Count -ne $oldComparison.Count){throw 'Comparison identity differs'}
$corePaths=@(
    'src/OptilandWorkbench.Core/bin/Release/net10.0/OptilandWorkbench.Core.dll',
    'tests/OptilandWorkbench.Tests/bin/Release/net10.0/OptilandWorkbench.Core.dll',
    'tests/OptilandWorkbench.ZemaxComparison.Tests/bin/Release/net10.0/OptilandWorkbench.Core.dll',
    'src/OptilandWorkbench.App/bin/Release/net10.0/OptilandWorkbench.Core.dll',
    'tools/diagnostics/N02Layers20261009/bin/Release/net10.0/OptilandWorkbench.Core.dll',
    'src/OptilandWorkbench.Core/bin/Debug/net10.0/OptilandWorkbench.Core.dll',
    'tests/OptilandWorkbench.Tests/bin/Debug/net10.0/OptilandWorkbench.Core.dll',
    'src/OptilandWorkbench.App/bin/Debug/net10.0/OptilandWorkbench.Core.dll')
$binaries=@($corePaths | ForEach-Object { [ordered]@{path=$_;sha256=(Hash (Join-Path $repository $_))} })
if(@($binaries | Where-Object {$_.path -like '*/Release/*'} | ForEach-Object {$_.sha256} | Sort-Object -Unique).Count -ne 1){throw 'Release Core copies differ'}
if(@($binaries | Where-Object {$_.path -like '*/Debug/*'} | ForEach-Object {$_.sha256} | Sort-Object -Unique).Count -ne 1){throw 'Debug Core copies differ'}
$sourcePaths=@('src/OptilandWorkbench.Core/Analysis/DiffractionEngine.cs','src/OptilandWorkbench.Core/Analysis/Diffraction/PsfAndMtfAnalyses.cs','tests/OptilandWorkbench.Tests/FftPupilPhaseParityTests.cs','tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj')
$sourceHashes=@($sourcePaths | ForEach-Object {[ordered]@{path=$_;sha256=(Hash (Join-Path $repository $_))}})
$rawCount=0;$rawBytes=0L;$manifests=@()
foreach($name in @('fft-pupil-phase-2026-10-09','n02-energy-layer-controls-2026-10-09','huygens-method-controls-2026-10-09')) {
    $root=Join-Path $repository ('validation/zemax/2026-r1/'+$name)
    $manifest=Get-Content -LiteralPath (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json
    foreach($file in $manifest.files) {
        $path=[IO.Path]::GetFullPath((Join-Path $root $file.path))
        if(!$path.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Manifest path escapes capture'}
        if((Get-Item -LiteralPath $path).Length -ne $file.bytes -or (Hash $path) -ne $file.sha256){throw ('Raw file differs: '+$path)}
        $rawCount++;$rawBytes+=$file.bytes
    }
    $manifests += [ordered]@{path=[IO.Path]::GetRelativePath($repository,(Join-Path $root 'manifest.json')).Replace('\','/');sha256=(Hash (Join-Path $root 'manifest.json'));rawFiles=$manifest.files.Count}
}
$frozen=Get-Content -LiteralPath (Join-Path $evidence 'frozen-after.json') -Raw | ConvertFrom-Json
if(!$frozen.unchanged -or $frozen.count -ne 2668){throw 'Preexisting frozen integrity differs'}
$layers=Get-Content -LiteralPath (Join-Path $evidence 'layers-final/layer-summary.json') -Raw | ConvertFrom-Json
if($layers.coreSha256 -ne $binaries[0].sha256){throw 'Diagnostic Core differs'}
$prepared=Get-Content -LiteralPath (Join-Path $evidence 'prepared-after-final-binary.json') -Raw | ConvertFrom-Json
$methodGrid=Get-Content -LiteralPath (Join-Path $evidence 'n01-method-grid-controls/method-comparison.json') -Raw | ConvertFrom-Json
$methodSection=Get-Content -LiteralPath (Join-Path $evidence 'n01-method-controls/method-comparison.json') -Raw | ConvertFrom-Json
$failures=@($comparison.results | Where-Object {$_.outcome -eq 'Failed'} | ForEach-Object {
    [ordered]@{testId=$_.testId;name=$_.testName;error=$_.Output.ErrorInfo.Message;stdout=$_.Output.StdOut}
})
$failureDetailsUnchanged=$true
foreach($failure in $failures) {
    $prior=@($previousComparison.results | Where-Object {$_.testId -eq $failure.testId -and $_.testName -eq $failure.name})
    if($prior.Count -ne 1 -or $prior[0].outcome -ne 'Failed' -or
        $prior[0].Output.ErrorInfo.Message -ne $failure.error -or $prior[0].Output.StdOut -ne $failure.stdout){$failureDetailsUnchanged=$false}
}
$releaseSubset=@($formal.results | Where-Object {$_.testName -match 'FftPupilPhaseParityTests|FftSamplingReferenceRepairTests|DiffractionEnergyOperandTests|ZemaxEncircledEnergyParityTests|ZemaxHuygensPsfParityTests|ZemaxHuygensPsfCrossSectionParityTests|PsfAndMtf'})
$ledger=[ordered]@{
    date='2026-10-09';baseHead=(git -C $repository rev-parse HEAD);gitState='Working tree changes; not committed or pushed'
    scope='Confirmed scalar FFT phase construction and Nyquist roundoff repair; N01/N02 numerical closure remains open'
    builds=[ordered]@{release=(Build-Summary 'release');debug=(Build-Summary 'debug')}
    beforeNativeInput=(Summary (Read-Trx (Join-Path $evidence 'before/before.trx')))
    beforeNyquistBoundary=(Summary (Read-Trx (Join-Path $evidence 'before-boundary/before-boundary.trx')))
    formalRelease=(Summary $formal);debugTargeted=(Summary $debug);comparisonRelease=(Summary $comparison)
    formalRetention=[ordered]@{previousTotal=$previous.total;currentTotal=$formal.total;identityOrOccurrenceDifference=$missing.Count;added=$added}
    releaseRelatedSubset=[ordered]@{count=$releaseSubset.Count;passed=@($releaseSubset | Where-Object {$_.outcome -eq 'Passed'}).Count;source='Subset of final full Release; not an additional run'}
    comparisonRetention=[ordered]@{total=$comparison.total;identityDifference=$comparisonDifference.Count;previousFailures=$previousComparison.failed;currentFailures=$comparison.failed;remainingFailureDetailsUnchanged=$failureDetailsUnchanged;failures=$failures}
    frozenIntegrity=[ordered]@{count=$frozen.count;beforeSha256=$frozen.beforeSha256;afterSha256=$frozen.sha256;unchanged=$frozen.unchanged;definition=$frozen.definition}
    newRawCaptureIntegrity=[ordered]@{count=$rawCount;bytes=$rawBytes;verified=$true;manifests=$manifests}
    sourceHashes=$sourceHashes;defaultCoreCopies=$binaries;preparedNativeInputControls=$prepared
    n02=[ordered]@{actualNrmseBefore=0.011467280942848471;actualNrmseAfter=0.011173141548632002;idealNrmse=0.013875784968407625;classification='Difference';layerSummarySha256=(Hash (Join-Path $evidence 'layers-final/layer-summary.json'))}
    n01=[ordered]@{nrmse=0.0037256670590166724;classification='Close';advancedSource='Auto';gridControl=$methodGrid;textControl=$methodSection;productHuygensChanged=$false}
    notRun=@('Full Debug','Laboratory suites','Six-file 132-case matrix','Installer/package checks','Manual desktop acceptance')
    excludedAttempts=@('Initial DENF guessed column order','Interrupted independent capture','Empty Huygens API-array comparison','Diagnostic compile failure','Historical earlier layer-coordinate metric')
}
$target=Join-Path $repository 'docs/validation/FFT_PUPIL_PHASE_REPAIR_2026-10-09.json'
$ledger | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $target -Encoding utf8
[ordered]@{formal=$formal.total;formalPassed=$formal.passed;debug=$debug.total;comparisonPassed=$comparison.passed;comparisonFailed=$comparison.failed;retained=$previous.total;added=$added.Count;releaseSubset=$releaseSubset.Count;rawFiles=$rawCount;frozenUnchanged=$frozen.unchanged;releaseCore=$binaries[0].sha256} | ConvertTo-Json
