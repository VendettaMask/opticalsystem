param([string]$Root='artifacts/validation/afocal-sampling-20261027')
$ErrorActionPreference='Stop'
function ReadJson([string]$p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function Hash([string]$p){(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
function Require($condition,[string]$message){if(!$condition){throw $message}}
$core=Hash "$Root/final-runner/OpticalSystemDesign.Core.dll"
$policy=Hash 'tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json'
Require ($policy -ceq '30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5') 'Policy changed'
foreach($p in @("$Root/final-objectives/OpticalSystemDesign.Core.dll",'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
    'tests/OpticalSystemDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
    'tests/OpticalSystemDesign.ZemaxComparison.Tests/bin/Release/net10.0/OpticalSystemDesign.Core.dll')){Require ((Hash $p) -ceq $core) 'Mixed Core binaries'}
$focal=@(ReadJson "$Root/focal-guards/summary.json")
Require ($focal.Count -eq 1159 -and @($focal|Where-Object conclusion -ne 'Pass').Count -eq 0) 'Focal regression'
foreach($r in $focal){
    Require ($r.requestExactlyEqual -and $r.referenceUnchanged -and !$r.error) 'Focal request/reference mismatch'
    Require ((Hash $r.referencePath) -ceq $r.referenceHash) 'Focal reference changed'
    Require ((Hash ('src/OpticalSystemDesign.App/Assets/SequentialExamples/'+$r.key.Split('|')[0])) -ceq $r.sourceSha256) 'Focal source changed'
}
$suppA=@(ReadJson "$Root/focal-supplemental-analyses/summary.json")
$suppP=@(ReadJson "$Root/focal-supplemental-points/summary.json")
Require ($suppA.Count -eq 22 -and $suppP.Count -eq 30 -and @(@($suppA+$suppP)|Where-Object conclusion -ne 'Pass').Count -eq 0) 'Supplemental regression'
foreach($name in 'focal-guards','focal-supplemental-analyses','focal-supplemental-points'){
    Require ((ReadJson "$Root/$name/identity.json").coreSha256 -ceq $core) 'Guard identity mismatch'
}
foreach($r in (ReadJson 'validation/zemax/2026-r1/objectives-completion-2026-10-23/comparison-ledger.json')){
    Require ((Hash ('src/OpticalSystemDesign.App/Assets/SequentialExamples/'+$r.originalKey.Split('|')[0])) -ceq $r.sourceSha256) 'Supplemental source changed'
    Require ((Hash $r.requestPath) -ceq $r.requestSha256) 'Supplemental request changed'
    Require ((Hash $r.referencePath) -ceq $r.referenceSha256) 'Supplemental reference changed'
}
$matrix=@(ReadJson "$Root/final-150/summary.json")
Require ($matrix.Count -eq 150 -and @($matrix.id|Sort-Object -Unique).Count -eq 150) 'AF coverage changed'
$counts=[ordered]@{};foreach($g in $matrix|Group-Object conclusion){$counts[$g.Name]=$g.Count}
Require ($counts.Pass -eq 131 -and $counts.Close -eq 3 -and $counts.Difference -eq 3 -and $counts.Incomparable -eq 13) 'Unexpected AF results'
$index=@{};foreach($r in $matrix){$index[$r.id]=$r}
$previous=@(ReadJson 'artifacts/validation/afocal-reference-focus-20261027/final-150/summary.json')
Require (@($previous|Where-Object {$_.conclusion -eq 'Pass' -and $index[$_.id].conclusion -ne 'Pass'}).Count -eq 0) 'AF Pass regression'
$oldItems=@{};foreach($r in (ReadJson 'artifacts/validation/afocal-reference-focus-20261027/final-150/items.json')){$oldItems[$r.id]=$r}
$referenceHashes=@(foreach($r in (ReadJson "$Root/final-150/items.json")){
    Require (($r.request|ConvertTo-Json -Depth 50 -Compress) -ceq ($oldItems[$r.id].request|ConvertTo-Json -Depth 50 -Compress)) 'AF request changed'
    foreach($file in Get-ChildItem -LiteralPath $r.native -File){@{id=$r.id;path=$file.FullName;sha256=(Hash $file.FullName)}}
})
$referenceHashes|ConvertTo-Json -Depth 5|Set-Content "$Root/reference-hashes.json" -Encoding utf8
$jobs=@(ReadJson "$Root/final-150/workbench-results.json")
Require ($jobs.Count -eq 150 -and @($jobs|Where-Object {$_.exitCode -ne 0 -or $_.timedOut}).Count -eq 2 -and @($jobs|Where-Object timedOut).Count -eq 0) 'Unexpected worker availability'
Require ((ReadJson "$Root/final-150/identity.json").coreSha256 -ceq $core) 'AF identity mismatch'
$fixed=@(ReadJson "$Root/final-fixed/summary.json")
Require ($fixed.Count -eq 20 -and @($fixed|Where-Object conclusion -ne 'Pass').Count -eq 0) 'Fixed-grid regression'
$tests=@(foreach($p in 'core-sampling.trx','afocal-sampling-final.trx'){
    [xml]$trx=Get-Content -LiteralPath "$Root/tests/$p" -Raw;$c=$trx.TestRun.ResultSummary.Counters
    Require ([int]$c.failed -eq 0 -and [int]$c.passed -eq [int]$c.total) 'Incomplete tests'
    @{path=$p;passed=[int]$c.passed;total=[int]$c.total;failed=[int]$c.failed}
})
Require (($tests.passed|Measure-Object -Sum).Sum -eq 135) 'Focused count changed'
foreach($r in (ReadJson "$Root/native-complex-pupil-v2/items.json")){
    foreach($kind in 'Real','Imaginary'){
        Require ((ReadJson "$Root/native-complex-pupil-v2/$($r.id)-$kind/captured-settings.json").properties.Type -ceq $kind) 'Complex setting did not apply'
        Require ((ReadJson "$Root/native-complex-pupil-v2/$($r.id)-$kind/batch-result.json").exitCode -eq 0) 'Native complex request failed'
    }
}
$gridRays=@(ReadJson "$Root/native-grid-rays/summary.json")
Require ($gridRays.Count -eq 2 -and @($gridRays|Where-Object {$_.nativeRays -ne 709 -or $_.errors -ne 0 -or $_.vignetted -ne 0}).Count -eq 0) 'Native grid-ray control incomplete'
foreach($r in $gridRays){
    $directory="$Root/native-grid-rays/$($r.id)"
    $request=ReadJson "$directory/request.json";$rays=@(ReadJson "$directory/image-geometry.json")
    Require ($request.pointInputs.Count -eq 709 -and $rays.Count -eq 709 -and @($rays|Where-Object {$_.error -ne 0 -or $_.vignette -ne 0 -or $_.intensity -le 0}).Count -eq 0) 'Native grid-ray raw data differs'
    Require ((Hash $request.input) -ceq $request.inputSha256) 'Native grid-ray source changed'
    Require ((ReadJson "$directory/batch-result.json").exitCode -eq 0) 'Native grid-ray request failed'
}
$frozen=ReadJson "$Root/frozen-integrity.json"
Require ($frozen.unchanged -and $frozen.workingTree.unchanged -and $frozen.matched -eq 2668) 'Frozen evidence changed'
$ledger=[ordered]@{date='2026-10-27';sourceCommit=(git rev-parse HEAD).Trim();coreSha256=$core;globalPolicySha256=$policy;
    change='Noncoaxial sequential afocal diffraction uses traced tangential pupil scale; focal and compound paths retained';
    afocal=@{total=150;counts=$counts;previousPassRetained=129;newAutomaticGridPass=2;originalRequestsUnchanged=$true;coreUnavailable=2;timedOut=0};
    fixedAngularGrid=@{total=20;passed=20};
    focal=@{originalRequests=1159;originalPass=1159;supplementalAnalyses=22;supplementalPoints=30;supplementalPass=52;regressions=0;sourcesRequestsReferencesUnchanged=$true};
    tests=$tests;differentFocusedTests=135;defaultReleaseAppCoreMatches=$true;
    native=@{validComplexRequests=8;validExplicitRayRequests=2;excludedLegacyLinearRequests=8;gridRayControls=$gridRays;inverse='pupil-inverse-v7; native complex conjugate convention and cyclic pupil center restored'};
    frozen=@{files=2668;headAndWorkingTreeUnchanged=$true;aggregateSha256=$frozen.expectedAggregateSha256};
    limitations=@('Three Close and three native built-in centroid-plot Differences retained','Six automatic grids still Incomparable, plus five native illumination failures and two first-order N/A','No general pupil-mask mapping or all-95-file certification','No desktop interaction, installation, publishing or Git push')}
$ledger|ConvertTo-Json -Depth 40|Set-Content "$Root/final-ledger.json" -Encoding utf8
Copy-Item -LiteralPath "$Root/final-ledger.json" -Destination 'docs/validation/AFOCAL_PUPIL_SAMPLING_REPAIR_2026-10-27.json'
Write-Output 'Confirmed: AF 131/3/3/13; fixed20 Pass; focal1159+52 Pass; focused135 Pass; frozen2668 unchanged.'
