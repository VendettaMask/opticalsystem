param([Parameter(Mandatory)][string]$RepositoryRoot,
      [Parameter(Mandatory)][string]$EvidenceRoot)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$evidence = (Resolve-Path -LiteralPath $EvidenceRoot).Path
$destination = Join-Path $repository 'docs/validation/PUPIL_FOUCAULT_EXTENSION_2026-10-12.json'
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
$formal=Read-Trx (Join-Path $evidence 'formal-release-boundary-fixed/formal-release-boundary-fixed.trx')
$comparison=Read-Trx (Join-Path $evidence 'comparison-release-boundary-fixed/comparison-release-boundary-fixed.trx')
$debug=Read-Trx (Join-Path $evidence 'debug-related-boundary-fixed/debug-related-boundary-fixed.trx')
$rendered=Read-Trx (Join-Path $evidence 'expanded-rendered-boundary-fixed/expanded-rendered-boundary-fixed.trx')
$firstCore=Read-Trx (Join-Path $evidence 'initial-core/initial-core.trx')
$firstDebug=Read-Trx (Join-Path $evidence 'debug-related-final/debug-related-final.trx')
$preBoundaryFormal=Read-Trx (Join-Path $evidence 'formal-release-final/formal-release-final.trx')
$preBoundaryComparison=Read-Trx (Join-Path $evidence 'comparison-release-final/comparison-release-final.trx')
$boundaryBefore=Read-Trx (Join-Path $evidence 'boundary-before-wide/boundary-before-wide.trx')
if ($preBoundaryFormal.total -ne 4731 -or $preBoundaryFormal.passed -ne 4731 -or $preBoundaryFormal.failed -or $preBoundaryFormal.skipped -or
    $preBoundaryComparison.total -ne 182 -or $preBoundaryComparison.passed -ne 182 -or $preBoundaryComparison.failed -or $preBoundaryComparison.skipped -or
    $boundaryBefore.total -ne 2 -or $boundaryBefore.passed -ne 1 -or $boundaryBefore.failed -ne 1 -or $boundaryBefore.skipped) {
    throw 'Retained pre-boundary evidence differs.'
}
$oldRoot=Join-Path $repository 'artifacts/validation/huygens-polarization-repair-20261012'
$oldFormal=Read-Trx (Join-Path $oldRoot 'formal-release-final/formal-release-final.trx')
$oldComparison=Read-Trx (Join-Path $oldRoot 'comparison-release-final/comparison-release-final.trx')
$oldDebug=Read-Trx (Join-Path $oldRoot 'debug-related-final/debug-related-final.trx')
$old=Inventory $oldFormal; $current=Inventory $formal
$missing=@($old.Keys|Where-Object {!$current.ContainsKey($_) -or $current[$_] -ne $old[$_]})
$added=@($current.Keys|Where-Object {!$old.ContainsKey($_)}|Sort-Object -CaseSensitive)
$oldTool=Inventory $oldComparison; $currentTool=Inventory $comparison
$toolChanges=@($oldTool.Keys|Where-Object {!$currentTool.ContainsKey($_) -or $currentTool[$_] -ne $oldTool[$_]})
$oldRelated=Inventory $oldDebug; $currentRelated=Inventory $debug
$relatedChanges=@($oldRelated.Keys|Where-Object {!$currentRelated.ContainsKey($_) -or $currentRelated[$_] -ne $oldRelated[$_]})
if ($oldFormal.total -ne 4697 -or $formal.total -ne 4733 -or $formal.passed -ne 4733 -or $missing.Count -or $added.Count -ne 36 -or
    $comparison.total -ne 182 -or $comparison.passed -ne 182 -or $toolChanges.Count -or $currentTool.Count -ne $oldTool.Count -or
    $debug.total -ne 554 -or $debug.passed -ne 554 -or $relatedChanges.Count -or $oldDebug.total -ne 440 -or
    $rendered.total -ne 36 -or $rendered.passed -ne 36) {throw 'Final counts or retained identities differ.'}
foreach ($run in @($formal,$comparison,$debug,$rendered)) {
    if ($run.failed -or $run.skipped -or @($run.rows|Where-Object outcome -eq 'Passed').Count -ne $run.passed) {throw 'Final acceptance was incomplete.'}
}
if ($firstCore.total -ne 55 -or $firstCore.passed -ne 44 -or $firstCore.failed -ne 11 -or $firstCore.skipped -or
    $firstDebug.total -ne 552 -or $firstDebug.passed -ne 551 -or $firstDebug.failed -ne 1 -or $firstDebug.skipped) {
    throw 'Retained first-attempt inventory differs.'
}
$builds=@(); $binaries=@()
foreach ($configuration in @('Debug','Release')) {
    $log=Join-Path $evidence ('build-'+$configuration.ToLowerInvariant()+'-boundary-fixed.log')
    $content=Get-Content -LiteralPath $log -Raw
    if ($content -notmatch '已成功生成。' -or $content -notmatch '(?m)^\s*0 个警告\s*$' -or $content -notmatch '(?m)^\s*0 个错误\s*$') {throw 'Default build failed.'}
    $builds += [ordered]@{configuration=$configuration;path=[IO.Path]::GetRelativePath($repository,$log).Replace('\','/');sha256=(Hash $log);warnings=0;errors=0}
    foreach ($directory in @('src/OpticalSystemDesign.Core','src/OpticalSystemDesign.App','tests/OpticalSystemDesign.Tests','tests/OpticalSystemDesign.ZemaxComparison.Tests')) {
        $path="$directory/bin/$configuration/net10.0/OpticalSystemDesign.Core.dll"
        $binaries += [ordered]@{configuration=$configuration;path=$path;sha256=(Hash (Join-Path $repository $path))}
    }
    if (@($binaries|Where-Object configuration -eq $configuration|ForEach-Object sha256|Sort-Object -Unique).Count -ne 1) {throw 'Default Core copies differ.'}
}
$nativeRoot=Join-Path $repository 'validation/zemax/2026-r1/huygens-extensions-2026-10-11'
$manifestPath=Join-Path $nativeRoot 'capture-file-hashes.json'
if ((Hash $manifestPath) -cne '9b80b110f9580e660eac8014b6a5f151e2a91ae1c27c54a91867fcbc8a35958f') {throw 'Native capture manifest changed.'}
$manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
if ($manifest.Count -ne 306) {throw 'Native inventory changed.'}
foreach ($entry in $manifest) {
    $path=[IO.Path]::GetFullPath((Join-Path $nativeRoot $entry.path))
    if (!$path.StartsWith($nativeRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
        (Hash $path) -cne $entry.sha256) {throw 'Native capture bytes changed.'}
}
$frozenArguments=@('-NoProfile','-File',(Join-Path $PSScriptRoot 'GitFrozenIntegrity.ps1'),'-RepositoryRoot',$repository,
    '-ManifestPath',(Join-Path $repository 'artifacts/validation/n02-layer-diagnosis-20261009/frozen-after.json'),
    '-ExpectedAggregateSha256','3a5fde98e1c881700f00be1c62d6ab4ff6f4feacc90454046ed787e80bf28c10')
$frozenJson=& pwsh @frozenArguments
if ($LASTEXITCODE -ne 0) {throw 'Frozen Git or working bytes failed.'}
$frozen=$frozenJson -join [Environment]::NewLine|ConvertFrom-Json
$previous='docs/validation/HUYGENS_POLARIZATION_REPAIR_2026-10-12.json'
if ((Hash (Join-Path $repository $previous)) -cne 'e0b4c014848479c422822508b3cb0d7a9740be1582a2c276a95380dc42e89dd4') {throw 'Previous phase ledger changed.'}
$metrics=@($formal.rows|Where-Object {$_.testName -like '*CapturedUnpolarizedHuygensIntensityMatchesTheOriginalNativeGrid*'}|ForEach-Object {
    if ($_.Output.StdOut -notmatch '(polarized-field[13]-linear): relativeL2=([^;]+); maximumAbsolute=([^\s]+)') {throw 'Missing retained native metrics.'}
    $relative=[double]::Parse($Matches[2],[Globalization.CultureInfo]::InvariantCulture)
    $maximum=[double]::Parse($Matches[3],[Globalization.CultureInfo]::InvariantCulture)
    if (!([double]::IsFinite($relative)) -or !([double]::IsFinite($maximum)) -or $relative -lt 0 -or $maximum -lt 0 -or $relative -ge 5e-5 -or $maximum -ge 1e-5) {throw 'Retained native tolerance failed.'}
    [ordered]@{name=$Matches[1];relativeL2=$relative;maximumAbsolute=$maximum;nativePoints=1024}
})
if ($metrics.Count -ne 2) {throw 'Retained N04 native cases missing.'}
$sourcePaths=@(git -C $repository diff --name-only -- src tests)+@(git -C $repository ls-files --others --exclude-standard -- src tests)
$sourcePaths=@($sourcePaths|Where-Object {$_ -like '*.cs'}|Sort-Object -Unique)
$sourcePaths += 'tools/validation/CollectPupilFoucaultExtension20261012.ps1'
$screenshot='artifacts/validation/pupil-foucault-extension-20261012/rendered-boundary-fixed/pupil-foucault-extension.png'
$ledger=[ordered]@{
    date='2026-10-12';baseHead=(git -C $repository rev-parse HEAD);gitState='Uncommitted local extension; not pushed'
    scope='Traced 3-D least-squares geometric stop image; afocal physical-plane scalar and homogeneous nonmagnetic Maxwell vector angular spectrum, with ideal thin amplitude screen'
    builds=$builds;formalRelease=(Summary $formal);comparisonRelease=(Summary $comparison);debugRelated=(Summary $debug);expandedRendered=(Summary $rendered)
    formalRetention=[ordered]@{previous=4697;current=4733;identityOrOccurrenceDifference=$missing.Count;added=$added;
        existingUiContractUpdate='Same connector test identity, exact ordered parameter lists extended by ExitPupilGeometry and four Foucault controls; no tolerance/assertion weakening'}
    comparisonRetention=[ordered]@{previous=182;current=182;identityOrOccurrenceDifference=$toolChanges.Count;referencesOrTolerancesChanged=$false}
    debugRetention=[ordered]@{previous=440;current=554;identityOrOccurrenceDifference=$relatedChanges.Count;additionalExistingSelected=78;newFormalCases=36}
    retainedN04=$metrics
    defaultCoreCopies=$binaries;sourceHashes=@($sourcePaths|ForEach-Object {[ordered]@{path=$_;sha256=(Hash (Join-Path $repository $_))}})
    screenshot=[ordered]@{path=$screenshot;sha256=(Hash (Join-Path $repository $screenshot));producer='Final 36-case Debug run with actual Skia rendering';reviewed=$true;nativeCertification=$false}
    previousPhase=[ordered]@{path=$previous;sha256=(Hash (Join-Path $repository $previous));unmodified=$true}
    retainedNative=[ordered]@{files=306;manifestSha256=(Hash $manifestPath);newCapture=$false}
    frozenIntegrity=$frozen
    boundaryRepair=[ordered]@{before=(Summary $boundaryBefore);preBoundaryFormal=(Summary $preBoundaryFormal);preBoundaryComparison=(Summary $preBoundaryComparison);
        sourceRecord=[ordered]@{path='artifacts/validation/pupil-foucault-extension-20261012/pre-boundary-source.json';sha256=(Hash (Join-Path $evidence 'pre-boundary-source.json'))};
        cause='Fixed diagonal omitted supported three-corner triangles; default grid did not expose it, 1.3-mm observation half-width broke mirror symmetry';
        fix='Use cyclic valid-corner triangle for three accepted vertices, in scalar/vector source reconstruction and projected planar display';
        oldCompletePassIsNotFinal=$true}
    firstAttempts=[ordered]@{core=(Summary $firstCore);coreFailure='New custom-material fixture was initialized before its legacy surface registry was bound';
        debug=(Summary $firstDebug);debugFailures=@($firstDebug.rows|Where-Object outcome -eq 'Failed'|Select-Object testId,testName);
        sourceRecord=[ordered]@{path='artifacts/validation/pupil-foucault-extension-20261012/first-debug-source.json';sha256=(Hash (Join-Path $evidence 'first-debug-source.json'))};
        acceptedAsFinal=$false}
    boundaries=@('Geometric stop image is a local five-ray least-squares conjugate, not a unique exact wave-optical pupil',
        'OPD and original normalized coordinates remain untouched; typed 3-D geometry is display-only',
        'Spatial Foucault supports parallel chief-normal planes and a regular band-limited single-branch source reconstruction',
        'Actual source slopes beyond Nyquist, folded/overlapping/degenerate cells and backward plane distances are explicit errors',
        'Maxwell projection and an ideal amplitude screen do not solve finite-conductivity knife-edge scattering or full-system POP',
        'General propagation kernel allows passive homogeneous complex index; formal absorbing-interface and GRIN Jones fields still unsupported',
        'Electric norm intensity and normal Poynting flux remain distinct; finite FFT windows do not establish infinite-space convergence')
    currentNativeEnvironment=[ordered]@{apiConnected=$false;installationOrLicenseProbe=$false;newNativeCertification=$false}
    excludedFollowup=@('Independent Cooke residual/certification','Independent Tessar residual/certification')
    notRun=@('Full Debug','Laboratory suites','Installer/package','Manual desktop acceptance','Six-file 132-case matrix','New native capture')
}
$ledger|ConvertTo-Json -Depth 15|Set-Content -LiteralPath $destination -Encoding utf8
[ordered]@{formalPassed=$formal.passed;comparisonPassed=$comparison.passed;debugPassed=$debug.passed;added=$added.Count;nativeCases=$metrics.Count;frozenMatched=$frozen.matched}|ConvertTo-Json
