param([Parameter(Mandatory)][string]$RepositoryRoot)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $RepositoryRoot).Path
$artifacts=Join-Path $root 'artifacts/validation';$destination=Join-Path $root 'validation/rechecks/2026-10-31/afocal-huygens-checked'
function ReadJson([string]$p){Get-Content -Raw -LiteralPath $p|ConvertFrom-Json}
function Require([bool]$value,[string]$message){if(-not $value){throw $message}}
function Hash([string]$p){(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant()}
function Relative([string]$p){[IO.Path]::GetRelativePath($root,$p).Replace('\','/')}
$archivePath=Join-Path $destination 'evidence.zip'
Require (-not(Test-Path -LiteralPath $archivePath)) 'Never overwrite sealed evidence.'
$dependencies=@(
    @{path='validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip';sha256='235f113b4633fce7341e837da03850ac3c8cff14a48c32f4b4731e7250d7f4b1'},
    @{path='validation/rechecks/2026-10-30/fft-seam-repair/evidence.zip';sha256='8a9a21f077515b016dd90c7cff890abccc7c038058cf0094ace38970523a2bd1'},
    @{path='validation/rechecks/2026-10-30/afocal-auto-grid/evidence.zip';sha256='b7340ea55499260feef6080a9e265c68318c37f500cec66df5a2e83b8a39586f'},
    @{path='validation/rechecks/2026-10-30/riflescope-pupil/evidence.zip';sha256='f4e6fef82110d8b6bde01a6d85ea72dbf7613e480991751e79b1f5a423bace4a'},
    @{path='validation/rechecks/2026-10-30/centroid-energy/evidence.zip';sha256='a7c6cded1d3d98546a1aad0aa2bc42290d3fcd5f0c3688a8382f107e6d82d997'},
    @{path='validation/rechecks/2026-10-30/afocal-ri/evidence.zip';sha256='89aeb7953c4d03e2948a3db5b48e090fa9a1ee190e395c12ff029200524f471c'},
    @{path='validation/rechecks/2026-10-30/afocal-huygens/evidence.zip';sha256='f30d3d52b991a258c1a40630ebc13ac004601e136680b982593b5cf415998b47'})
foreach($d in $dependencies){Require ((Hash (Join-Path $root $d.path)) -ceq $d.sha256) 'Frozen dependency changed.'}
$tests=@();$measurements=[Collections.Generic.List[object]]::new();$distinct=[Collections.Generic.HashSet[string]]::new()
foreach($p in 'afocal-huygens-check-20261031-tests-comparison-final-02/comparison.trx','afocal-huygens-check-20261031-tests-core-final-02/core.trx'){
    [xml]$doc=Get-Content -Raw -LiteralPath (Join-Path $artifacts $p);$c=$doc.TestRun.ResultSummary.Counters
    Require ($c.failed -eq 0 -and $c.notExecuted -eq 0 -and $c.total -eq $c.passed) 'Final tests failed or skipped.'
    $definitions=@{};foreach($t in $doc.TestRun.TestDefinitions.UnitTest){$definitions[[string]$t.id]=[string]$t.TestMethod.className}
    foreach($t in $doc.TestRun.Results.UnitTestResult){
        Require ($t.outcome -ceq 'Passed' -and $distinct.Add($definitions[[string]$t.testId]+':'+$t.testName)) 'Duplicate or nonpassing test identity.'
        foreach($line in ([string]$t.Output.StdOut -split '\r?\n')){
            if($line.Trim().StartsWith('{"kind":"checked-')){$measurements.Add(($line|ConvertFrom-Json))}
        }
    }
    $tests+=[pscustomobject]@{path=('artifacts/validation/'+$p);sha256=(Hash (Join-Path $artifacts $p));passed=[int]$c.passed;failed=[int]$c.failed;skipped=[int]$c.notExecuted}
}
Require ($distinct.Count -eq 201 -and $tests[0].passed -eq 130 -and $tests[1].passed -eq 71) 'Wrong distinct focused scope.'
$wide=@($measurements|Where-Object kind -CEQ 'checked-wide-window')
Require ($wide.Count -eq 2) 'Missing original wide-window controls.'
foreach($r in $wide){
    Require ($r.maximum -le 1e-10 -and $r.check.Status -ceq 'SamplingStable' -and $r.check.FinalNodes -gt $r.initial*$r.initial) 'Wide-window precision or disclosure failed.'
    Require ($r.check.Tolerance -eq 1e-10 -and -not $r.check.IsContinuumErrorBound -and $r.check.Scope -ceq 'PsfQuadratureOnFixedReportedWindow') 'Sampling agreement misrepresented as an error bound.'
    Require ($r.check.CumulativeOperations -le 100000000 -and $r.check.MaximumIntensityDisagreement -le 1e-10 -and $r.check.MaximumReferenceAmplitudeDisagreement -le 1e-10) 'Original work/tolerance limit failed.'
}
$phase=@($measurements|Where-Object kind -CEQ 'checked-radial-phase');Require ($phase.Count -eq 3) 'Missing phase controls.'
foreach($r in $phase){Require ([Math]::Abs($r.actual-$r.expected) -le 1e-10) 'Analytic phase control failed.'}
$alias=@($measurements|Where-Object kind -CEQ 'checked-angular-alias');Require ($alias.Count -eq 1 -and [Math]::Abs($alias[0].HuygensReferenceAmplitude-.7) -le 1e-12 -and @($alias[0].check.Trials|Where-Object ReferenceAmplitude -GT .9).Count -gt 0) 'Hidden angular alias was not detected.'
$clipped=@($measurements|Where-Object kind -CEQ 'checked-clipping-rejected');Require ($clipped.Count -eq 3) 'Missing application failure boundaries.'
foreach($r in $clipped){Require ($r.checks[-1].Status -cne 'SamplingStable') 'Unresolved clipping was accepted.'}
$budget=@($measurements|Where-Object {$_.kind -in @('checked-spectral-budget','checked-field-budget')})
Require ($budget.Count -eq 3) 'Missing aggregate budget controls.'
foreach($r in $budget){Require ($r.checks.Count -ge 2 -and $r.checks[-1].Status -ceq 'WorkLimitReached' -and ($r.checks|Measure-Object CumulativeOperations -Sum).Sum -le 100000000) 'Aggregate work budget exceeded.'}
Add-Type -AssemblyName System.IO.Compression
$prior=[IO.Compression.ZipFile]::OpenRead((Join-Path $root $dependencies[-1].path))
try{
    $reader=[IO.StreamReader]::new($prior.GetEntry('entry-map.json').Open());try{$map=$reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}
    $logical='artifacts/validation/afocal-huygens-20261030-final/summary.json';$mapped=@($map|Where-Object path -CEQ $logical)
    Require ($mapped.Count -eq 1) 'Missing immutable fixed-mode replay.'
    $reader=[IO.StreamReader]::new($prior.GetEntry($mapped[0].member).Open());try{$before=@($reader.ReadToEnd()|ConvertFrom-Json)}finally{$reader.Dispose()}
}finally{$prior.Dispose()}
$after=@(ReadJson (Join-Path $artifacts 'afocal-huygens-check-20261031-fixed-regression/summary.json'))
Require ($before.Count -eq 3 -and $after.Count -eq 3) 'Wrong fixed-mode control scope.'
for($i=0;$i -lt 3;$i++){
    foreach($key in 'n','nativeAiryMaximumAbsolute','coreAiryMaximumAbsolute','nativeMaximumAbsoluteDifference','gaussianAiryMaximumAbsolute','PupilQuadratureNodeCount'){
        Require ($before[$i].$key -eq $after[$i].$key) ('Fixed-mode numeric summary changed: '+$key)
    }
}
$copies=@();foreach($p in 'src/OpticalSystemDesign.Core/bin/Release/net10.0','src/OpticalSystemDesign.App/bin/Release/net10.0','tests/OpticalSystemDesign.Tests/bin/Release/net10.0','tests/OpticalSystemDesign.ZemaxComparison.Tests/bin/Release/net10.0','tools/OpticalSystemDesign.ZemaxComparison/bin/Release/net10.0','tools/diagnostics/AfocalHuygens20261030/bin/Release/net10.0'){
    $relativePath=$p+'/OpticalSystemDesign.Core.dll';$copies+=[pscustomobject]@{path=$relativePath;sha256=(Hash (Join-Path $root $relativePath))}
}
Require (@($copies.sha256|Sort-Object -Unique).Count -eq 1) 'Final Core copies differ.'
$replayIdentity=ReadJson (Join-Path $artifacts 'afocal-huygens-check-20261031-fixed-regression/identity.json')
Require ($replayIdentity.coreSha256 -ceq $copies[0].sha256) 'Fixed-mode replay uses another Core.'
Require ($replayIdentity.sourceSha256 -ceq '67d33177298a0d23398a882de2e68c7f4ddaa681185b6025698aaeb1c708ffe3') 'Independent disk source changed.'
foreach($p in 'afocal-huygens-check-20261031-app-build-final.log','afocal-huygens-check-20261031-tool-build-final.log'){
    $log=Get-Content -Raw -LiteralPath (Join-Path $artifacts $p);Require ($log -match '0 个警告' -and $log -match '0 个错误') 'Default build failed or warned.'
}
git -C $root -c core.fsmonitor=false diff --quiet -- tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json
Require ($LASTEXITCODE -eq 0) 'Comparison policy changed.'
$sources=@();$paths=@(git -C $root -c core.fsmonitor=false diff --name-only -- src/OpticalSystemDesign.Core)+@(git -C $root -c core.fsmonitor=false ls-files --others --exclude-standard -- src/OpticalSystemDesign.Core)+@('src/OpticalSystemDesign.Application/Runtime/WorkbenchRuntime.Analysis.cs','src/OpticalSystemDesign.Application/Runtime/WorkbenchRuntime.Analysis.Parameters.cs')
foreach($p in $paths|Sort-Object -Unique){$sources+=[pscustomobject]@{path=$p;sha256=(Hash (Join-Path $root $p))}}
$assessment=[ordered]@{date='2026-10-31';status='checked_afocal_huygens_sampling_with_bounded_refinement';samplingAgreementTolerance=1e-10;samplingAgreementIsRigorousErrorBound=$false;defaultNativeProtocolChanged=$false;newNativeCaptures=0;newFormalNativePasses=0;allFiniteSamplingIssuesClosed=$false;originalInputsReferencesGatesAndHistoricalLedgerChanged=$false;dependencies=$dependencies;sourceBaseCommit=(git -C $root -c core.fsmonitor=false rev-parse HEAD);measurements=@($measurements);fixedModesBefore=$before;fixedModesAfter=$after;coreCopies=$copies;workingTreeSources=$sources;testRuns=$tests;latestDistinctTestsPassed=$distinct.Count;newCheckedSamplingTestsPassed=16;earlierRunsSupersededNotAdded=$true;foregroundDesktopUsed=$false;full170Or95CorpusRerun=$false;committedOrPushed=$false}
$assessmentPath=Join-Path $destination 'assessment.json';$assessment|ConvertTo-Json -Depth 50|Set-Content -LiteralPath $assessmentPath -Encoding utf8
$files=[Collections.Generic.List[IO.FileInfo]]::new();$extensions=@('.json','.txt','.log','.cfg','.config','.cs','.patch','.md','.ps1','.csproj','.trx','.csv')
foreach($item in Get-ChildItem -LiteralPath $artifacts -Filter 'afocal-huygens-check-20261031*'){
    $candidates=if($item.PSIsContainer){@(Get-ChildItem -LiteralPath $item.FullName -Recurse -File)}else{@($item)}
    foreach($f in $candidates){if($f.Extension.ToLowerInvariant() -in $extensions){$files.Add($f)}}
}
foreach($f in Get-ChildItem -LiteralPath (Join-Path $root 'tools/diagnostics/AfocalHuygens20261030') -File){if($f.Extension.ToLowerInvariant() -in $extensions){$files.Add($f)}}
foreach($p in @('docs/AFOCAL_HUYGENS_SAMPLING_CHECKS_2026-10-31.md','tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalHuygensSamplingChecksTests.cs','tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json')+$sources.path){$files.Add((Get-Item -LiteralPath (Join-Path $root $p)))}
$files.Add((Get-Item -LiteralPath $assessmentPath))
$entries=[Collections.Generic.List[object]]::new();$canonical=@{}
foreach($f in $files|Sort-Object FullName -Unique){$logical=Relative $f.FullName;Require (-not $logical.StartsWith('../')) 'Evidence escaped repository.';$sha=Hash $f.FullName;if(-not $canonical.ContainsKey($sha)){$canonical[$sha]=$logical};$entries.Add([pscustomobject]@{path=$logical;member=$canonical[$sha];length=$f.Length;sha256=$sha})}
$stream=[IO.File]::Open($archivePath,[IO.FileMode]::CreateNew);$zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
try{
    foreach($e in $entries|Where-Object {$_.path -ceq $_.member}){
        $entry=$zip.CreateEntry($e.member,[IO.Compression.CompressionLevel]::Optimal);$inputStream=[IO.File]::OpenRead((Join-Path $root $e.path));$outputStream=$entry.Open();try{$inputStream.CopyTo($outputStream)}finally{$outputStream.Dispose();$inputStream.Dispose()}
    }
    $entry=$zip.CreateEntry('entry-map.json');$writer=[IO.StreamWriter]::new($entry.Open(),[Text.UTF8Encoding]::new($false));try{$writer.Write((ConvertTo-Json -InputObject @($entries) -Depth 10))}finally{$writer.Dispose()}
}finally{$zip.Dispose();$stream.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($archivePath)
try{foreach($e in $entries){$member=$zip.GetEntry($e.member);$inputStream=$member.Open();try{$sha=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($inputStream))}finally{$inputStream.Dispose()};Require ($sha -ceq $e.sha256 -and $member.Length -eq $e.length) ('Evidence mismatch: '+$e.path)}}finally{$zip.Dispose()}
$seal=[ordered]@{date='2026-10-31';status=$assessment.status;latestDistinctTestsPassed=$distinct.Count;newCheckedSamplingTestsPassed=16;samplingAgreementTolerance=1e-10;samplingAgreementIsRigorousErrorBound=$false;defaultNativeProtocolChanged=$false;newFormalNativePasses=0;allFiniteSamplingIssuesClosed=$false;coreAssemblySha256=$copies[0].sha256;coreCopiesIdentical=6;archiveSha256=(Hash $archivePath);archiveBytes=(Get-Item -LiteralPath $archivePath).Length;logicalEntries=$entries.Count;uniqueMembers=$canonical.Count;allEntryHashesVerified=$true;proprietaryAssembliesIncluded=$false;foregroundDesktopUsed=$false}
$seal|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $destination 'verification.json') -Encoding utf8
$seal|ConvertTo-Json -Depth 12
