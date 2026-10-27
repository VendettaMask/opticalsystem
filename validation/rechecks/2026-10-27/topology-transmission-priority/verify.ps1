$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
function Check($condition, $message) { if(-not $condition) { throw $message } }
$rows = @()
$identities = @{}
$anchor = $null
foreach($batch in @('paired-runs','active-set-runs','final-paired-runs')) {
    $summary = Get-Content (Join-Path $PSScriptRoot "$batch/summary.json") -Raw | ConvertFrom-Json
    $expected = if($batch -eq 'final-paired-runs'){8}else{16}
    Check ($summary.Runs.Count -eq $expected -and $summary.CpuProcessors -eq 2 -and $summary.Priority -eq 'BelowNormal') 'Background policy or run count mismatch'
    foreach($run in $summary.Runs) {
        $state = (Get-Content (Join-Path $PSScriptRoot "$batch/$($run.Id)/run.autonomous.json") -Raw | ConvertFrom-Json).checkpoint
        $o = $state.plan.options
        Check ($o.maximumTrials -eq 6 -and $o.maximumFunctionEvaluations -eq 600 -and $o.maximumGradientEvaluations -eq 100 -and $o.maximumTracedRays -eq 2000000 -and $o.finalValidationRayReserve -eq 800000 -and $o.maximumStageTracedRays -eq 400000) 'Changed cap'
        Check ($o.local.maximumUpdates -eq 4 -and $o.full.maximumUpdates -eq 8 -and $o.local.sampling.pupilRings -eq 1 -and $o.local.sampling.pupilArms -eq 8 -and $o.local.sampling.fieldsPerDirection -eq 3) 'Changed short training protocol'
        foreach($name in @('functionEvaluations','gradientEvaluations','validationEvaluations','tracedRays','geometryProbes','matrixEvaluations','differenceEvaluations','jacobianBuilds')) {
            Check (($state.events.work.$name | Measure-Object -Sum).Sum -eq $state.consumed.$name) "Ledger mismatch $name"
        }
        Check ($state.consumed.functionEvaluations -le 600 -and $state.consumed.gradientEvaluations -le 100 -and $state.consumed.tracedRays -le 2000000 -and ([timespan]$state.consumed.elapsed).TotalSeconds -le 60) 'Budget exceeded'
        Check ($state.status -eq 'budgetExhausted' -and $state.pending.phase -eq 'recovery' -and $null -eq $state.reservation) 'Missing safe stop'
        Check ($state.acceptedArchive.Count -eq 1 -and $state.acceptedArchive[0].topology.elements.Count -eq 4 -and $state.acceptedArchive[0].assessment.meetsTargets) 'Incorrect acceptance/archive'
        foreach($candidate in $state.candidates) {
            Check ($candidate.assessment.completed -and $candidate.assessment.fields.Count -eq 35) 'Incomplete physical endpoint'
            foreach($field in $candidate.assessment.fields) { Check ($field.attemptedRays -eq 960 -and $field.checkAttemptedRays -eq 99) 'Final sampling changed' }
            foreach($element in $candidate.topology.elements) {
                $original = $state.plan.seeds[0].elements | Where-Object id -eq $element.id
                Check ($null -ne $original -and $element.material -ceq $original.material -and $element.front.clearSemiDiameterMillimeters -eq $original.front.clearSemiDiameterMillimeters -and $element.back.clearSemiDiameterMillimeters -eq $original.back.clearSemiDiameterMillimeters) 'Material/aperture changed'
                Check ([math]::Abs($element.frontZMillimeters-$original.frontZMillimeters) -lt 1e-10 -and [math]::Abs($element.thicknessMillimeters-$original.thicknessMillimeters) -lt 1e-10) 'Axial permission changed'
            }
            Check (($candidate.topology.stop|ConvertTo-Json -Compress) -ceq ($state.plan.seeds[0].stop|ConvertTo-Json -Compress)) 'Stop changed'
            Check (($candidate.topology.image|ConvertTo-Json -Compress) -ceq ($state.plan.seeds[0].image|ConvertTo-Json -Compress)) 'Image changed'
        }
        $normal = $state.plan | ConvertTo-Json -Depth 100 | ConvertFrom-Json
        $normal.options.seed = 42; $normal.options.expansion.enableGradualDeletion = $false
        foreach($stage in @($normal.options.local,$normal.options.full)) {
            foreach($key in @('transmissionFirst','activeParameterLimit','transmissionPolicyVersion')) { $stage.PSObject.Properties.Remove($key) }
        }
        $identity = $normal | ConvertTo-Json -Depth 100 -Compress
        if($null -eq $anchor){$anchor=$identity}else{Check ($identity -ceq $anchor) 'Unexpected input/permission difference'}
        if($run.Mode -eq 'control') {
            $fingerprint = ($state.candidates | Where-Object {$_.topology.elements.Count -eq 3}).assessment.opticFingerprint
            if($identities.ContainsKey('control')){Check ($fingerprint -ceq $identities.control) 'Control optics changed across revisions'}else{$identities.control=$fingerprint}
        }
        $rows += [pscustomobject]@{Batch=$batch;Id=$run.Id;Mode=$run.Mode;Consumed=$state.consumed;Status=$state.status;Pending=$state.pending.phase;Reduced=$run.ReducedCandidates}
    }
}
$frozen = Get-Content (Join-Path $repo 'labs/InitialStructure/benchmarks/autonomous-topology-v1/transmission-priority-final-20261027.json') -Raw | ConvertFrom-Json
Check ((Get-FileHash (Join-Path $repo $frozen.Cases[0].Source.RepositoryPath) -Algorithm SHA256).Hash.ToLowerInvariant() -eq $frozen.Cases[0].OriginalSha256) 'Source hash mismatch'
$auditRays = 0; $diagnosticRays = (Get-Content (Join-Path $PSScriptRoot 'sensitivity/sensitivity.json') -Raw|ConvertFrom-Json).ActualRays
foreach($name in @('signed','backtracking')) {
    $audit = @(Get-Content (Join-Path $PSScriptRoot "native-audit-$name/native-checks.json") -Raw|ConvertFrom-Json)
    Check ($audit.Count -eq 2 -and $audit[0].Assessment.MeetsTargets -and -not $audit[1].Assessment.MeetsTargets) 'Native audit mismatch'
    $auditRays += ($audit.AdditionalAuditRays|Measure-Object -Sum).Sum
    $diagnosticRays += (Get-Content (Join-Path $PSScriptRoot "diagnostic-$name/ray-diagnostics.json") -Raw|ConvertFrom-Json).ActualRays
}
$current = (Get-Content (Join-Path $PSScriptRoot 'current-v3.topology-plan.json') -Raw|ConvertFrom-Json).plan
Check ($current.options.local.transmissionPolicyVersion -eq 3 -and $current.options.full.transmissionPolicyVersion -eq 3) 'Current plan lacks explicit identity'
[xml]$trx = Get-Content (Join-Path $PSScriptRoot 'tests/transmission-final-scoped.trx')
Check ($trx.TestRun.ResultSummary.Counters.passed -eq 70 -and $trx.TestRun.ResultSummary.Counters.failed -eq 0) 'Final focused tests incomplete'
[ordered]@{Verified=$true;RunCount=40;QualifiedReductions=0;MinimumQualifiedElements=4;PairedInputsAndPermissionsVerified=$true;FrozenSourceHashVerified=$true;LedgerAndCapsVerified=$true;FinalSamplingUnchanged=$true;NativeAuditsVerified=$true;CpuProcessors=2;Priority='BelowNormal';ActualSearchRays=($rows.Consumed.tracedRays|Measure-Object -Sum).Sum;AdditionalNativeAuditRays=$auditRays;AdditionalDiagnosticRays=$diagnosticRays;TestsPassed=70;Runs=$rows} |
    ConvertTo-Json -Depth 35 | Set-Content (Join-Path $PSScriptRoot 'verification.json') -Encoding utf8
'Verified 40 retained runs, unchanged physical task/gates, exact ledgers/caps, control parity, native audits and 70 focused tests.'
