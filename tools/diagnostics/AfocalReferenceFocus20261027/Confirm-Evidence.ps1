param([string]$Root='artifacts/validation/afocal-reference-focus-20261027')
$ErrorActionPreference='Stop'
function ReadJson([string]$p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function Hash([string]$p){(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
function Require($condition,[string]$message){if(!$condition){throw $message}}
function Counts($rows){$result=[ordered]@{};foreach($g in $rows|Group-Object conclusion){$result[$g.Name]=$g.Count};return $result}
$core=Hash "$Root/final-runner/OpticalSystemDesign.Core.dll"
$policy=Hash 'tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json'
Require ($policy -ceq '30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5') 'Policy changed'
foreach($p in @("$Root/final-objectives/OpticalSystemDesign.Core.dll",'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
    'tests/OpticalSystemDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
    'tests/OpticalSystemDesign.ZemaxComparison.Tests/bin/Release/net10.0/OpticalSystemDesign.Core.dll')){Require ((Hash $p) -ceq $core) 'Mixed optical binaries'}
$focal=@(ReadJson "$Root/focal-guards/summary.json")
Require ($focal.Count -eq 1159 -and @($focal|Where-Object conclusion -ne 'Pass').Count -eq 0) 'Focal regression'
foreach($r in $focal){
    Require ($r.requestExactlyEqual -and $r.referenceUnchanged -and !$r.error) 'Focal request/reference mismatch'
    Require ((Hash $r.referencePath) -ceq $r.referenceHash) 'Focal reference changed after replay'
    Require ((Hash ('src/OpticalSystemDesign.App/Assets/SequentialExamples/'+$r.key.Split('|')[0])) -ceq $r.sourceSha256) 'Focal input changed'
}
$suppA=@(ReadJson "$Root/focal-supplemental-analyses/summary.json")
$suppP=@(ReadJson "$Root/focal-supplemental-points/summary.json")
Require ($suppA.Count -eq 22 -and $suppP.Count -eq 30 -and @(@($suppA+$suppP)|Where-Object conclusion -ne 'Pass').Count -eq 0) 'Supplemental regression'
foreach($name in 'focal-guards','focal-supplemental-analyses','focal-supplemental-points'){
    Require ((ReadJson "$Root/$name/identity.json").coreSha256 -ceq $core) 'Guard binary identity mismatch'
}
$previous=@(ReadJson 'validation/zemax/2026-r1/objectives-completion-2026-10-23/comparison-ledger.json')
Require ($previous.Count -eq 52) 'Supplemental original ledger changed'
foreach($r in $previous){
    Require ((Hash ('src/OpticalSystemDesign.App/Assets/SequentialExamples/'+$r.originalKey.Split('|')[0])) -ceq $r.sourceSha256) 'Supplemental source changed'
    Require ((Hash $r.requestPath) -ceq $r.requestSha256) 'Supplemental request changed'
    Require ((Hash $r.referencePath) -ceq $r.referenceSha256) 'Supplemental reference changed'
}
$matrix=@(ReadJson "$Root/final-150/summary.json")
Require ($matrix.Count -eq 150 -and @($matrix.id|Sort-Object -Unique).Count -eq 150) 'AF coverage changed'
$counts=Counts $matrix
Require ($counts.Pass -eq 129 -and $counts.Close -eq 3 -and $counts.Difference -eq 3 -and $counts.Incomparable -eq 15) 'Unexpected AF results'
$jobs=@(ReadJson "$Root/final-150/workbench-results.json")
$unavailable=@($jobs|Where-Object {$_.exitCode -ne 0 -or $_.timedOut})
Require ($jobs.Count -eq 150 -and $unavailable.Count -eq 2) 'Unexpected AF worker availability'
foreach($job in $unavailable){
    $r=$matrix|Where-Object id -eq $job.id
    Require ($job.exitCode -eq 3 -and !$job.timedOut -and $r.key -eq 'Relative Illumination' -and $r.conclusion -eq 'Incomparable') 'Unexpected AF worker failure'
}
Require ((ReadJson "$Root/final-150/identity.json").coreSha256 -ceq $core) 'AF binary identity mismatch'
$fixed=@(ReadJson "$Root/final-fixed/summary.json")
Require ($fixed.Count -eq 20 -and @($fixed|Where-Object conclusion -ne 'Pass').Count -eq 0) 'Fixed angular grid regression'
$focus=@($fixed|Where-Object key -eq 'Huygens Through Focus MTF')
$tests=@(foreach($p in @('afocal-reference-focus.trx','core-reference-focus.trx','native-angular-centroid.trx')){
    [xml]$trx=Get-Content -LiteralPath "$Root/tests/$p" -Raw
    $c=$trx.TestRun.ResultSummary.Counters
    Require ([int]$c.failed -eq 0 -and [int]$c.passed -eq [int]$c.total) 'Incomplete or failed tests'
    @{path=$p;passed=[int]$c.passed;failed=[int]$c.failed;total=[int]$c.total}
})
Require (($tests.passed|Measure-Object -Sum).Sum -eq 477) 'Focused test count changed'
$referenceControls=@()
foreach($row in (ReadJson "$Root/chief-controls-v2/items.json")){
    $aPath="$Root/native-reference-controls/Centroid-$($row.id)/data.json"
    $bPath="$Root/native-reference-controls/ChiefRay-$($row.id)/data.json"
    $a=ReadJson $aPath;$b=ReadJson $bPath
    Require (($a.dataSeries|ConvertTo-Json -Depth 30 -Compress) -ceq ($b.dataSeries|ConvertTo-Json -Depth 30 -Compress)) 'Native reference control changed'
    $sa=ReadJson "$Root/native-reference-controls/Centroid-$($row.id)/captured-settings.json"
    $sb=ReadJson "$Root/native-reference-controls/ChiefRay-$($row.id)/captured-settings.json"
    Require ($sa.properties.ReferTo -eq 'Centroid' -and $sb.properties.ReferTo -eq 'ChiefRay') 'Native requested reference was not applied'
    $referenceControls+=@{lens=$row.lens;configuration=$row.configuration;curvesEqual=$true;centroidDataSha256=(Hash $aPath);chiefDataSha256=(Hash $bPath)}
}
$positiveA=ReadJson "$Root/native-reference-controls/focal-DoubleGauss-Centroid/data.json"
$positiveB=ReadJson "$Root/native-reference-controls/focal-DoubleGauss-ChiefRay/data.json"
Require (($positiveA.dataSeries|ConvertTo-Json -Depth 30 -Compress) -cne ($positiveB.dataSeries|ConvertTo-Json -Depth 30 -Compress)) 'Focal reference positive control did not change'
$frozen=ReadJson "$Root/frozen-integrity.json"
Require ($frozen.unchanged -and $frozen.workingTree.unchanged -and $frozen.matched -eq 2668) 'Frozen evidence changed'
$ledger=[ordered]@{date='2026-10-27';sourceCommit=(git rev-parse HEAD).Trim();coreSha256=$core;globalPolicySha256=$policy;
    implemented=@('Energy reference ray respects system aiming','Afocal Huygens uses physical reference-plane pupil positions','Afocal Huygens retains finite pupil-cell footprint and physical diopter phase');
    afocal=@{total=150;counts=$counts;rawCentroidPlotDifferencesPreserved=3;singleRayTracePass=20;coreCalculationsAvailable=148;coreRelativeIlluminationUnavailable=2;timedOutWorkers=0};
    fixedAngularGrid=@{total=20;passed=20;throughFocus=@($focus|ForEach-Object {@{lens=$_.lens;configuration=$_.configuration;conclusion=$_.conclusion;maximumNrmse=($_.metrics.nrmse|Measure-Object -Maximum).Maximum}})};
    focal=@{originalRequests=1159;originalPass=1159;supplementalAnalyses=22;supplementalPoints=30;supplementalPass=52;regressions=0;originalSourcesRequestsReferencesUnchanged=$true};
    tests=$tests;differentFocusedTests=477;defaultReleaseAppCoreMatches=$true;
    nativeReferenceControls=$referenceControls;nativeReferencePositiveControl='Focal Double Gauss curves differ; maximum absolute energy difference 0.14930991217063994';
    nativeRayCentroid=@{groups=3;fieldCurves=13;inputRays=10361;sampling='32 intervals; 797 unit-weight pupil points per field';passedGroups=3;nativeChartReplaced=$false};
    frozen=@{files=2668;headAndWorkingTreeUnchanged=$true;aggregateSha256=$frozen.expectedAggregateSha256};
    limitations=@('Three built-in native AF centroid plots remain numeric Difference; their reference selector is inactive in these captured conditions','Three Close and 15 Incomparable retained','No all-95-file or universal sampling/polarization certification','No foreground app interaction, installation, package publishing or Git push in this task')}
$ledger|ConvertTo-Json -Depth 40|Set-Content -LiteralPath "$Root/final-ledger.json" -Encoding utf8
Write-Output 'Confirmed: 1159 + 52 focal Pass; 20 fixed-grid Pass; 477 focused tests; original AF 129/3/3/15; 2668 frozen files unchanged.'
