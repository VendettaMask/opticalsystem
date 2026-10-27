$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
function Check($condition,$message){if(-not $condition){throw $message}}
$cal=Get-Content (Join-Path $PSScriptRoot 'calibration/calibration.json') -Raw|ConvertFrom-Json
Check ($cal.Rows.Count-eq24 -and $cal.ActualRays-le2000000) 'Incomplete primary convergence grid'
$convergence=@()
foreach($group in $cal.Rows|Group-Object Rings,Arms){
    $errors=@()
    foreach($row in $group.Group){$reference=$cal.Rows|Where-Object{$_.Case-eq$row.Case -and $_.Rings-eq10 -and $_.Arms-eq32};for($i=0;$i-lt5;$i++){$errors+=[math]::Abs($row.Fields[$i].TrainingRmsMillimeters-$reference.Fields[$i].TrainingRmsMillimeters)*1000}}
    $convergence+=[pscustomobject]@{Sampling=$group.Name;MaximumRmsErrorUm=($errors|Measure-Object -Maximum).Maximum;MaximumObjectiveRays=($group.Group.TracedRayCount|Measure-Object -Maximum).Maximum}
}
Check (($convergence|Where-Object Sampling -eq '4, 16').MaximumRmsErrorUm-lt.05) 'Chosen accuracy did not converge'
$hold=Get-Content (Join-Path $PSScriptRoot 'holdout/calibration.json') -Raw|ConvertFrom-Json
$incomplete=Get-Content (Join-Path $PSScriptRoot 'holdout/INCOMPLETE.json') -Raw|ConvertFrom-Json
Check ($hold.Rows.Count-eq11 -and $incomplete.CompletePairs-eq5 -and $incomplete.ConservativelyChargedUpperBound-eq900000) 'Holdout incompleteness was not retained'
$runs=@();$qualified=@();$protocol=$null
$paired=Get-Content (Join-Path $PSScriptRoot 'paired-runs/summary.json') -Raw|ConvertFrom-Json
Check ($paired.Runs.Count-eq8 -and $paired.CpuProcessors-eq2 -and $paired.Priority-eq'BelowNormal') 'Paired run policy mismatch'
$identity=$null
foreach($row in $paired.Runs){
    $state=(Get-Content (Join-Path $PSScriptRoot "paired-runs/$($row.Id)/run.autonomous.json") -Raw|ConvertFrom-Json).checkpoint
    $o=$state.plan.options
    Check ($o.maximumTracedRays-eq2000000 -and $o.maximumFunctionEvaluations-eq600 -and $o.maximumGradientEvaluations-eq100 -and $o.maximumTrials-eq6 -and $o.finalValidationRayReserve-eq800000) 'Changed paired budget'
    foreach($name in @('functionEvaluations','gradientEvaluations','validationEvaluations','tracedRays','geometryProbes','matrixEvaluations','differenceEvaluations','jacobianBuilds')){Check (($state.events.work.$name|Measure-Object -Sum).Sum-eq$state.consumed.$name) "Bad ledger $name"}
    Check ($state.consumed.tracedRays-le2000000 -and $state.consumed.gradientEvaluations-le100 -and $state.consumed.functionEvaluations-le600 -and ([timespan]$state.consumed.elapsed).TotalSeconds-le60) 'Paired cap exceeded'
    $normal=$state.plan|ConvertTo-Json -Depth 100|ConvertFrom-Json
    $normal.options.seed=42;$normal.options.expansion.enableGradualDeletion=$false
    foreach($stage in @($normal.options.local,$normal.options.full)){$stage.sampling.pupilRings=1;$stage.sampling.pupilArms=8}
    $value=$normal|ConvertTo-Json -Depth 100 -Compress
    if($null-eq$identity){$identity=$value}else{Check ($identity-ceq$value) 'Unexpected paired difference beyond sampling/seed/C-D strategy'}
    foreach($candidate in $state.candidates){Check ($candidate.assessment.completed -and $candidate.assessment.fields.Count-eq35) 'Unfinished paired assessment';foreach($field in $candidate.assessment.fields){Check ($field.attemptedRays-eq960 -and $field.checkAttemptedRays-eq99) 'Changed final sampling'}}
    $runs+=[pscustomobject]@{Batch='paired-runs';Id=$row.Id;Mode=$row.Mode;ActualRays=$row.Consumed.TracedRays;Gradients=$row.Consumed.GradientEvaluations;Final=$row.ReducedCandidates}
}
foreach($batch in @('repair-runs','axial-runs')){
    $summary=Get-Content (Join-Path $PSScriptRoot "$batch/summary.json") -Raw|ConvertFrom-Json
    Check ($summary.Runs.Count-eq$(if($batch-eq'repair-runs'){4}else{2}) -and $summary.CpuProcessors-eq2 -and $summary.Priority-eq'BelowNormal') 'Fixed repair run policy mismatch'
    foreach($row in $summary.Runs){
        Check ($row.Final.Completed -and $row.Initial.Completed -and $row.Final.Fields.Count-eq35) 'Incomplete fixed repair endpoint'
        Check ($row.ActualRays-le2000000 -and $row.GradientEvaluations-le100 -and $row.FunctionEvaluations-le600 -and $row.ActualSeconds-le60) 'Fixed repair cap exceeded'
        Check ($row.ActualRays-eq($row.InitialRays+$row.TrainingRays+$row.FinalRays) -and $row.TrainingRays-eq$row.Optimization.Work.TracedRays) 'Fixed repair ray ledger mismatch'
        Check ($row.TrainingRays-le(2000000-$row.InitialRays-800000)) 'Final physical reserve borrowed'
        Check ($row.Final.ProtocolFingerprint-eq$row.Initial.ProtocolFingerprint) 'Independent protocol changed'
        if($null-eq$protocol){$protocol=$row.Final.ProtocolFingerprint}else{Check ($protocol-eq$row.Final.ProtocolFingerprint) 'Physical gate differs across repairs'}
        foreach($field in $row.Final.Fields){Check ($field.AttemptedRays-eq960 -and $field.CheckAttemptedRays-eq99) 'Fixed final sampling changed'}
        $input=Get-Content (Join-Path $PSScriptRoot "$batch/$($row.Id)/input.json") -Raw|ConvertFrom-Json
        $topology=$row.Optimization.Topology
        Check (($topology.Stop|ConvertTo-Json -Compress)-ceq($input.Topology.Stop|ConvertTo-Json -Compress)) 'Stop changed'
        Check (($topology.Image|ConvertTo-Json -Compress)-ceq($input.Topology.Image|ConvertTo-Json -Compress)) 'Image moved'
        foreach($element in $topology.Elements){
            $old=$input.Topology.Elements|Where-Object Id -eq $element.Id
            Check ($element.Material-ceq$old.Material -and $element.Front.ClearSemiDiameterMillimeters-eq$old.Front.ClearSemiDiameterMillimeters -and $element.Back.ClearSemiDiameterMillimeters-eq$old.Back.ClearSemiDiameterMillimeters) 'Material/aperture changed'
            if($row.Mode-eq'accurate-axial' -and $element.Id-eq'element-002'){
                Check ([math]::Abs($element.FrontZMillimeters-$old.FrontZMillimeters)-le.2500000001 -and [math]::Abs($element.FrontZMillimeters+$element.ThicknessMillimeters-$old.FrontZMillimeters-$old.ThicknessMillimeters)-le.2500000001) 'Axial window exceeded'
            }else{Check ([math]::Abs($element.FrontZMillimeters-$old.FrontZMillimeters)-lt1e-10 -and [math]::Abs($element.ThicknessMillimeters-$old.ThicknessMillimeters)-lt1e-10) 'Unexpected axial freedom'}
        }
        if($row.MeetsTargets){Check ($topology.Elements.Count-eq3 -and $row.Final.Violations.Count-eq0) 'False qualified three-element result';foreach($field in $row.Final.Fields){Check ($field.ValidRays-eq960 -and $field.CheckValidRays-eq99) 'Qualified endpoint still loses rays'};$qualified+=$row}
        $runs+=[pscustomobject]@{Batch=$batch;Id=$row.Id;Mode=$row.Mode;ActualRays=$row.ActualRays;Gradients=$row.GradientEvaluations;CompletedGradients=$row.Optimization.Work.CompletedGradientEvaluations;Status=$row.Status;MeetsTargets=$row.MeetsTargets;Final=$row.Final}
    }
}
Check ($runs.Count-eq14 -and $qualified.Count-eq1) 'Missing actual comparison/qualification'
$audits=@(Get-Content (Join-Path $PSScriptRoot 'final-audit/native-and-precision-audits.json') -Raw|ConvertFrom-Json)
Check ($audits.Count-eq2 -and @($audits|Where-Object{$_.Assessment.MeetsTargets}).Count-eq1) 'Independent native audit did not reproduce qualification'
foreach($audit in $audits){Check ($audit.Assessment.Completed -and ($audit.Precision.IntegrationDifferenceUm|Measure-Object -Maximum).Maximum-lt.05) 'Final integration accuracy failed';Check ((Get-FileHash $audit.NativeFile -Algorithm SHA256).Hash.ToLowerInvariant()-eq$audit.NativeSha256) 'Native artifact hash changed'}
[xml]$trx=Get-Content (Join-Path $PSScriptRoot 'tests/sampling-precision-scoped.trx')
Check ($trx.TestRun.ResultSummary.Counters.passed-eq30 -and $trx.TestRun.ResultSummary.Counters.failed-eq0) 'Focused tests incomplete'
[ordered]@{Verified=$true;ExperimentCount=14;PreparedSourceCostsSeparate=$true;TotalSearchRays=($runs.ActualRays|Measure-Object -Sum).Sum;PrimaryCalibrationRays=$cal.ActualRays;HoldoutChargedUpperBound=900000;IncompleteHoldoutPairRetained=$true;AdditionalNativeAndPrecisionAuditRays=($audits.AdditionalAuditRays|Measure-Object -Sum).Sum;OriginalGatesAndPhysicalSamplingUnchanged=$true;ThirtyFocusedTestsPassed=$true;Convergence=$convergence;QualifiedThreeElement=$qualified[0];Runs=$runs}|
    ConvertTo-Json -Depth 50|Set-Content (Join-Path $PSScriptRoot 'verification.json') -Encoding utf8
'Verified 14 bounded comparisons, unchanged original gates, one qualified three-element repair, native replay and 30 focused tests.'
