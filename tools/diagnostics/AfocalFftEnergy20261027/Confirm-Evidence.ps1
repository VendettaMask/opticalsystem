param([string]$Root='artifacts/validation/afocal-fft-energy-20261027')
$ErrorActionPreference='Stop'
function ReadJson([string]$p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function Hash([string]$p){(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
function Require($condition,[string]$message){if(!$condition){throw $message}}
$core=Hash "$Root/final-runner-v2/OpticalSystemDesign.Core.dll"
$policy=Hash 'tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json'
Require ($policy -ceq '30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5') 'Policy changed'
foreach($p in @("$Root/final-objectives-v2/OpticalSystemDesign.Core.dll",'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
    'tests/OpticalSystemDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.Core.dll','tests/OpticalSystemDesign.ZemaxComparison.Tests/bin/Release/net10.0/OpticalSystemDesign.Core.dll')){Require ((Hash $p) -ceq $core) 'Mixed final Core binaries'}
$focal=@(ReadJson "$Root/focal-guards-v2/summary.json")
Require ($focal.Count -eq 1159 -and @($focal|Where-Object conclusion -ne 'Pass').Count -eq 0) 'Focal regression'
foreach($r in $focal){
    Require ($r.requestExactlyEqual -and $r.referenceUnchanged -and !$r.error) 'Focal request/reference mismatch'
    Require ((Hash $r.referencePath) -ceq $r.referenceHash) 'Focal reference changed'
    Require ((Hash ('src/OpticalSystemDesign.App/Assets/SequentialExamples/'+$r.key.Split('|')[0])) -ceq $r.sourceSha256) 'Focal source changed'
}
$suppA=@(ReadJson "$Root/focal-supplemental-analyses-v2/summary.json");$suppP=@(ReadJson "$Root/focal-supplemental-points-v2/summary.json")
Require ($suppA.Count -eq 22 -and $suppP.Count -eq 30 -and @(@($suppA+$suppP)|Where-Object conclusion -ne 'Pass').Count -eq 0) 'Supplemental regression'
foreach($name in 'focal-guards-v2','focal-supplemental-analyses-v2','focal-supplemental-points-v2'){
    Require ((ReadJson "$Root/$name/identity.json").coreSha256 -ceq $core) 'Final guard identity mismatch'
}
foreach($r in (ReadJson 'validation/zemax/2026-r1/objectives-completion-2026-10-23/comparison-ledger.json')){
    Require ((Hash ('src/OpticalSystemDesign.App/Assets/SequentialExamples/'+$r.originalKey.Split('|')[0])) -ceq $r.sourceSha256) 'Supplemental source changed'
    Require ((Hash $r.requestPath) -ceq $r.requestSha256) 'Supplemental request changed'
    Require ((Hash $r.referencePath) -ceq $r.referenceSha256) 'Supplemental reference changed'
}
$matrix=@(ReadJson "$Root/final-150-v2/summary.json")
Require ($matrix.Count -eq 150 -and @($matrix.id|Sort-Object -Unique).Count -eq 150) 'AF coverage changed'
$counts=[ordered]@{};foreach($g in $matrix|Group-Object conclusion){$counts[$g.Name]=$g.Count}
Require ($counts.Pass -eq 134 -and @($matrix|Where-Object conclusion -eq Close).Count -eq 0 -and $counts.Difference -eq 3 -and $counts.Incomparable -eq 13) 'Unexpected final AF results'
$index=@{};foreach($r in $matrix){$index[$r.id]=$r}
$previous=@(ReadJson 'artifacts/validation/afocal-sampling-20261027/final-150/summary.json')
Require (@($previous|Where-Object {$_.conclusion -eq 'Pass' -and $index[$_.id].conclusion -ne 'Pass'}).Count -eq 0) 'Previous AF Pass regression'
$closure=@(foreach($r in $previous|Where-Object conclusion -eq Close){Require ($index[$r.id].conclusion -eq 'Pass') 'Original Close not closed';$index[$r.id]})
Require ($closure.Count -eq 3) 'Original target set changed'
$oldItems=@{};foreach($r in (ReadJson 'artifacts/validation/afocal-sampling-20261027/final-150/items.json')){$oldItems[$r.id]=$r}
$referenceHashes=@(foreach($r in (ReadJson "$Root/final-150-v2/items.json")){
    Require (($r.request|ConvertTo-Json -Depth 50 -Compress) -ceq ($oldItems[$r.id].request|ConvertTo-Json -Depth 50 -Compress)) 'AF request changed'
    foreach($file in Get-ChildItem -LiteralPath $r.native -File){@{id=$r.id;path=$file.FullName;sha256=(Hash $file.FullName)}}
})
$referenceHashes|ConvertTo-Json -Depth 5|Set-Content "$Root/reference-hashes.json" -Encoding utf8
$jobs=@(ReadJson "$Root/final-150-v2/workbench-results.json")
Require ($jobs.Count -eq 150 -and @($jobs|Where-Object {$_.exitCode -ne 0 -or $_.timedOut}).Count -eq 2 -and @($jobs|Where-Object timedOut).Count -eq 0) 'Unexpected availability'
Require ((ReadJson "$Root/final-150-v2/identity.json").coreSha256 -ceq $core) 'AF binary mismatch'
$fixed=@(ReadJson "$Root/final-fixed-v2/summary.json")
Require ($fixed.Count -eq 20 -and @($fixed|Where-Object conclusion -ne 'Pass').Count -eq 0) 'Fixed-grid regression'
$tests=@(foreach($p in 'core-fft-energy-final.trx','afocal-fft-energy-final.trx'){
    [xml]$trx=Get-Content -LiteralPath "$Root/tests/$p" -Raw;$c=$trx.TestRun.ResultSummary.Counters
    Require ([int]$c.failed -eq 0 -and [int]$c.passed -eq [int]$c.total) 'Incomplete final tests'
    @{path=$p;passed=[int]$c.passed;total=[int]$c.total;failed=[int]$c.failed}
})
Require (($tests.passed|Measure-Object -Sum).Sum -eq 237) 'Focused test count changed'
$nativeJobs=@()
foreach($name in 'native-energy-grid-v2','native-binocular'){
    foreach($j in (ReadJson "$Root/$name/native-jobs.json")){
        Require ((ReadJson "$($j.output)/batch-result.json").exitCode -eq 0) 'Native capture failed'
        $r=ReadJson $j.request;$s=ReadJson "$($j.output)/captured-settings.json"
        Require ($s.selectors.Wavelength -eq $r.wavelength -and $s.selectors.Field -eq $r.field) 'Native selected scope differs'
        $nativeJobs+=@{id=$j.id;requestSha256=(Hash $j.request);dataSha256=(Hash "$($j.output)/data.json")}
    }
}
Require ($nativeJobs.Count -eq 14) 'Native control coverage changed'
$binocular=@(ReadJson "$Root/binocular-audit/summary.json")
Require ($binocular.Count -eq 2 -and @($binocular|Where-Object {$_.freshProfileMaxDifference -ne 0 -or $_.nativeCount -ne 699 -or $_.coreCount -ne 699}).Count -eq 0) 'Fresh binocular control differs'
$fresh=ReadJson "$Root/native-energy-grid-v2/rifle-DEE-Centroid/data.json"
$old=ReadJson 'artifacts/validation/afocal-recompare-20261026/expanded/0014-001-afocal-riflescope-c1-primary-f1-w2-diffraction-encircled-energy/native/data.json'
Require (($fresh.dataSeries|ConvertTo-Json -Depth 40 -Compress) -ceq ($old.dataSeries|ConvertTo-Json -Depth 40 -Compress)) 'Fresh energy reference changed'
$frozen=ReadJson "$Root/frozen-integrity.json"
Require ($frozen.unchanged -and $frozen.workingTree.unchanged -and $frozen.matched -eq 2668) 'Frozen evidence changed'
$ledger=[ordered]@{date='2026-10-27';sourceCommit=(git rev-parse HEAD).Trim();coreSha256=$core;globalPolicySha256=$policy;
    changes=@('Automatically generated scalar AF FFT pupils retain physical exit-pupil aspect','Negative image delta retains its explicit unstretched pupil','AF cumulative energy forms its angular Fourier interval directly');
    afocal=@{total=150;counts=$counts;previousPassRetained=131;originalCloseNowPass=$closure;originalRequestsUnchanged=$true;coreUnavailable=2;timedOut=0};fixedAngularGrid=@{total=20;passed=20};
    focal=@{originalRequests=1159;originalPass=1159;supplementalAnalyses=22;supplementalPoints=30;supplementalPass=52;regressions=0;sourcesRequestsReferencesUnchanged=$true};
    tests=$tests;differentFocusedTests=237;defaultReleaseAppCoreMatches=$true;nativeJobs=$nativeJobs;binocularControls=$binocular;freshRiflescopeEnergyReferenceEqual=$true;
    failedStages=@('First aspect version changed negative-delta MTF: two tests failed and matrix 132/2/3/13; rejected','First-version focal replay interrupted after 756 completed requests, not final acceptance','First diagnostic energy Linear PSF accidentally selected all wavelengths; excluded and recaptured with explicit selector readback');
    frozen=@{files=2668;headAndWorkingTreeUnchanged=$true;aggregateSha256=$frozen.expectedAggregateSha256};
    limitations=@('Three built-in native centroid plot Differences and 13 Incomparable retained','Binocular configuration2 still has two Core-only and two native-only pupil nodes; original curve gates pass, no bitwise identity claim','New aspect convention is limited to automatically generated scalar nonnegative-delta FFT pupils; prepared and polarized input behavior retained','No all-95-file certification, foreground desktop use, installation, publishing or Git push')}
$ledger|ConvertTo-Json -Depth 50|Set-Content "$Root/final-ledger.json" -Encoding utf8
Copy-Item "$Root/final-ledger.json" 'docs/validation/AFOCAL_FFT_ENERGY_REPAIR_2026-10-27.json'
Write-Output 'Confirmed: AF134 Pass/0 Close/3 Difference/13 Incomparable; fixed20 Pass; focal1159+52 Pass; focused237 Pass; frozen2668 unchanged.'
