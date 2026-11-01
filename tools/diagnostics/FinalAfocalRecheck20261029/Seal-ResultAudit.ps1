param([Parameter(Mandatory)][string]$RepositoryRoot)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $RepositoryRoot).Path
$artifacts=Join-Path $root 'artifacts/validation'
$run=Join-Path $artifacts 'afocal-result-audit-20261031-final'
$destination=Join-Path $root 'validation/rechecks/2026-10-31/afocal-results'
function Require([bool]$v,[string]$m){if(-not $v){throw $m}}
function Hash([string]$p){(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant()}
function ReadJson([string]$p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function Relative([string]$p){[IO.Path]::GetRelativePath($root,$p).Replace('\','/')}
$archive=Join-Path $destination 'evidence.zip'
Require (-not(Test-Path -LiteralPath $archive)) 'Never overwrite sealed evidence'
$rows=@(ReadJson (Join-Path $run 'comparison.json'))
$items=@(ReadJson (Join-Path $run 'items.json'))
$workers=@(ReadJson (Join-Path $run 'worker-results.json'))
Require ($rows.Count -eq 170 -and $items.Count -eq 170 -and $workers.Count -eq 170) 'Incomplete matrix'
Require (@($workers|Where-Object {$_.ExitCode -ne 0 -or $_.TimedOut -or $_.Cancelled}).Count -eq 0) 'Incomplete computation'
Require (@($rows|Where-Object {$_.after.run.conclusion -eq 'Pass'}).Count -eq 163) 'Unexpected result scope'
Require (@($rows|Where-Object {$_.after.run.conclusion -eq 'Incomparable'}).Count -eq 7) 'Unexpected availability scope'
Require (($rows.after.points|Measure-Object violations -Sum).Sum -eq 0) 'Remaining pointwise differences'
Require (($rows.after.points|Measure-Object count -Sum).Sum -eq 314478 -and $rows.after.points.Count -eq 495) 'Changed comparison scope'
$initial=@(ReadJson (Join-Path $artifacts 'afocal-result-audit-20261031/comparison.json'))
foreach($r in $rows){
    $previous=@($initial|Where-Object Id -CEQ $r.id);Require ($previous.Count -eq 1) 'Duplicate case identity'
    Require ($r.requestFingerprint -ceq $previous[0].requestFingerprint -and $r.after.run.conclusion -ceq $previous[0].after.run.conclusion) 'Request or original result regressed'
    Require ((ConvertTo-Json -InputObject $r.after.points -Depth 15 -Compress) -ceq (ConvertTo-Json -InputObject $previous[0].after.points -Depth 15 -Compress)) 'Original output metrics changed'
}
$identity=ReadJson (Join-Path $run 'input-identity.json')
foreach($f in $identity.files){Require ((Hash (Join-Path $identity.inputReuseRoot $f.path)) -ceq $f.sha256) 'Frozen input bytes changed'}
$runtime=ReadJson (Join-Path $run 'runtime-identity.json')
$copies=@();foreach($p in 'src/OpticalSystemDesign.Core/bin/Release/net10.0','src/OpticalSystemDesign.App/bin/Release/net10.0','tests/OpticalSystemDesign.Tests/bin/Release/net10.0','tests/OpticalSystemDesign.ZemaxComparison.Tests/bin/Release/net10.0','tools/OpticalSystemDesign.ZemaxComparison/bin/Release/net10.0','artifacts/validation/afocal-result-audit-20261031-final/runner'){
    $dll=$p+'/OpticalSystemDesign.Core.dll';$copies+=[pscustomobject]@{path=$dll;sha256=(Hash (Join-Path $root $dll))}
}
Require (@($copies.sha256|Sort-Object -Unique).Count -eq 1 -and $copies[0].sha256 -ceq $runtime.coreSha256) 'Mixed Core cohort'
$binaryManifest=@(Get-ChildItem -LiteralPath (Join-Path $run 'runner') -File -Filter '*.dll'|ForEach-Object {[pscustomobject]@{file=$_.Name;sha256=(Hash $_.FullName)}})
$binaryManifest|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $run 'runtime-binaries.json') -Encoding utf8
$tests=@();$measurements=@();$distinct=[Collections.Generic.HashSet[string]]::new()
foreach($p in 'afocal-result-audit-20261031-tests-comparison-final/comparison.trx','afocal-result-audit-20261031-tests-core-final/core.trx'){
    [xml]$t=Get-Content -LiteralPath (Join-Path $artifacts $p) -Raw;$c=$t.TestRun.ResultSummary.Counters
    Require ($c.failed -eq 0 -and $c.total -eq $c.passed -and $c.notExecuted -eq 0) 'Tests failed or skipped'
    foreach($r in $t.TestRun.Results.UnitTestResult){
        Require ($distinct.Add([string]$r.testName)) 'Duplicate test identity'
        foreach($line in ([string]$r.Output.StdOut -split '\r?\n')){if($line.StartsWith('{"kind":"native-afocal-aperture"')){$measurements+=($line|ConvertFrom-Json)}}
    }
    $tests+=[pscustomobject]@{path=('artifacts/validation/'+$p);sha256=(Hash (Join-Path $artifacts $p));passed=[int]$c.passed}
}
Require ($distinct.Count -eq 230 -and $tests[0].passed -eq 96 -and $tests[1].passed -eq 134) 'Changed focused test scope'
Require ($measurements.Count -eq 5 -and ($measurements|Measure-Object count -Sum).Sum -eq 4696 -and ($measurements|Measure-Object violations -Sum).Sum -eq 0) 'New native result control failed'
$fixture=Join-Path $root 'validation/zemax/2026-r1/afocal-aperture-state-2026-10-31'
$manifest=ReadJson (Join-Path $fixture 'manifest.json')
foreach($f in $manifest.files){Require ((Hash (Join-Path $fixture $f.path)) -ceq $f.sha256) 'Native fixture changed'}
$receipts=@($manifest.nativeApertureReceipts|ForEach-Object {ReadJson (Join-Path $root $_)})
Require ($receipts[0].actual.CurrentType -ceq 'None' -and $receipts[1].actual.CurrentType -ceq 'CircularAperture') 'Native aperture activation unproven'
$build=Get-Content -LiteralPath (Join-Path $artifacts 'afocal-result-audit-20261031-app-build-final.log') -Raw
Require ($build -match '0 个警告' -and $build -match '0 个错误') 'Default App build failed or warned'
git -C $root -c core.fsmonitor=false diff --quiet -- tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json
Require ($LASTEXITCODE -eq 0) 'Comparison policy changed'
$sources=@();$paths=@(git -C $root -c core.fsmonitor=false diff --name-only -- src/OpticalSystemDesign.Core)+@(git -C $root -c core.fsmonitor=false ls-files --others --exclude-standard -- src/OpticalSystemDesign.Core)
foreach($p in $paths|Sort-Object -Unique){$sources+=[pscustomobject]@{path=$p;sha256=(Hash (Join-Path $root $p))}}
$assessment=[ordered]@{date='2026-10-31';scope='Afocal result-level audit and explicit aperture correction';originalRequests=170;originalNumericPasses=163;notApplicableOrIncomparable=7;actualDifferences=0;pointwiseComponents=495;pointwiseComparisons=314478;pointwiseViolations=0;newNativeControls=$measurements;focusedTests=$tests;latestDistinctTestsPassed=230;coreCopies=$copies;runtimeIdentity=$runtime;finalRuntimeBinaryManifest='artifacts/validation/afocal-result-audit-20261031-final/runtime-binaries.json';initialStagingBinariesInInputIdentityAreHistorical=$true;originalInputsReferencesAndGatesChanged=$false;allAfocalProductAnalysesCertified=$false;analysisInventory=(ReadJson (Join-Path $run 'analysis-inventory.json'));workingTreeCoreSources=$sources;foregroundDesktopUsed=$false;committedOrPushed=$false}
$assessmentPath=Join-Path $destination 'assessment.json';$assessment|ConvertTo-Json -Depth 35|Set-Content -LiteralPath $assessmentPath -Encoding utf8
$files=[Collections.Generic.List[IO.FileInfo]]::new()
foreach($p in 'items.json','input-identity.json','runtime-identity.json','runtime-binaries.json','worker-results.json','comparison.json','analysis-inventory.json','compute.log','audit.log'){$files.Add((Get-Item -LiteralPath (Join-Path $run $p)))}
Get-ChildItem -LiteralPath (Join-Path $run 'workbench') -Recurse -File|Where-Object {$_.Name -in @('data.json','provenance.json','execution.json','worker-result.json','process.log')}|ForEach-Object {$files.Add($_)}
foreach($p in @('docs/AFOCAL_ANALYSIS_RESULTS_2026-10-31.md','tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalApertureResultTests.cs','tests/OpticalSystemDesign.Tests/ZemaxImportTests.cs','tests/OpticalSystemDesign.Tests/ZemaxLayoutApertureTests.cs','tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost/NativeSurfaceApertureControls.cs','tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost/Host.cs')+$sources.path+$tests.path+$manifest.nativeApertureReceipts){$files.Add((Get-Item -LiteralPath (Join-Path $root $p)))}
Get-ChildItem -LiteralPath $fixture -Recurse -File|ForEach-Object {$files.Add($_)}
Get-ChildItem -LiteralPath $PSScriptRoot -File|Where-Object Extension -in '.cs','.ps1','.csproj'|ForEach-Object {$files.Add($_)}
foreach($p in 'afocal-aperture-probe-20261031-before','afocal-aperture-probe-20261031-after','afocal-active-aperture-probe-20261031-after','afocal-aperture-results-20261031-tests'){
    Get-ChildItem -LiteralPath (Join-Path $artifacts $p) -Recurse -File|Where-Object Extension -in '.json','.trx'|ForEach-Object {$files.Add($_)}
}
$files.Add((Get-Item -LiteralPath $assessmentPath));$files.Add((Get-Item -LiteralPath (Join-Path $artifacts 'afocal-result-audit-20261031-app-build-final.log')))
$entries=@();$canonical=@{}
foreach($f in $files|Sort-Object FullName -Unique){$path=Relative $f.FullName;$sha=Hash $f.FullName;if(-not $canonical.ContainsKey($sha)){$canonical[$sha]=$path};$entries+=[pscustomobject]@{path=$path;member=$canonical[$sha];sha256=$sha;length=$f.Length}}
Add-Type -AssemblyName System.IO.Compression
$stream=[IO.File]::Open($archive,[IO.FileMode]::CreateNew);$zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
try{
    foreach($e in $entries|Where-Object {$_.path -ceq $_.member}){$entry=$zip.CreateEntry($e.member,[IO.Compression.CompressionLevel]::Optimal);$inputStream=[IO.File]::OpenRead((Join-Path $root $e.path));$outputStream=$entry.Open();try{$inputStream.CopyTo($outputStream)}finally{$inputStream.Dispose();$outputStream.Dispose()}}
    $entry=$zip.CreateEntry('entry-map.json');$writer=[IO.StreamWriter]::new($entry.Open());try{$writer.Write((ConvertTo-Json -InputObject $entries -Depth 8))}finally{$writer.Dispose()}
}finally{$zip.Dispose();$stream.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try{foreach($e in $entries){$entry=$zip.GetEntry($e.member);$s=$entry.Open();try{$sha=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($s))}finally{$s.Dispose()};Require ($sha -ceq $e.sha256 -and $entry.Length -eq $e.length) ('Archive mismatch: '+$e.path)}}finally{$zip.Dispose()}
$verification=[ordered]@{date='2026-10-31';requests=170;numericPasses=163;notApplicableOrIncomparable=7;differences=0;pointwiseViolations=0;newNativeControlsPassed=5;newNativeComparedValues=4696;latestDistinctTestsPassed=230;coreSha256=$runtime.coreSha256;coreCopiesIdentical=6;archiveSha256=(Hash $archive);archiveBytes=(Get-Item -LiteralPath $archive).Length;logicalEntries=$entries.Count;uniqueMembers=$canonical.Count;allEntryHashesVerified=$true;allAfocalProductAnalysesCertified=$false;foregroundDesktopUsed=$false}
$verification|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $destination 'verification.json') -Encoding utf8
$verification|ConvertTo-Json -Depth 8
