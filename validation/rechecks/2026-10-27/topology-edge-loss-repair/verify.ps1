$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
function Check($condition, $message) { if (-not $condition) { throw $message } }
$runs = @()
$paired = @{}
foreach ($batch in @('paired-runs', 'budget-control')) {
    $summary = Get-Content (Join-Path $PSScriptRoot "$batch/summary.json") -Raw | ConvertFrom-Json
    Check ($summary.Runs.Count -eq 8 -and $summary.CpuProcessors -eq 2 -and $summary.Priority -eq 'BelowNormal') 'Incomplete/background policy mismatch'
    foreach ($row in $summary.Runs) {
        $state = (Get-Content (Join-Path $PSScriptRoot "$batch/$($row.Id)/run.autonomous.json") -Raw | ConvertFrom-Json).checkpoint
        $options = $state.plan.options
        Check ($options.maximumTracedRays -eq 2000000 -and $options.maximumFunctionEvaluations -eq 600 -and $options.maximumGradientEvaluations -eq 100 -and $options.maximumTrials -eq 6 -and $options.finalValidationRayReserve -eq 800000) 'Changed caps'
        Check ($options.local.maximumUpdates -eq 4 -and $options.full.maximumUpdates -eq 8) 'Changed update budget'
        Check ($state.consumed.tracedRays -le 2000000 -and $state.consumed.functionEvaluations -le 600 -and $state.consumed.gradientEvaluations -le 100 -and ([timespan]$state.consumed.elapsed).TotalSeconds -le 60) 'Exceeded caps'
        foreach ($name in @('functionEvaluations','gradientEvaluations','validationEvaluations','tracedRays','geometryProbes','matrixEvaluations','differenceEvaluations','jacobianBuilds')) {
            $sum = ($state.events.work.$name | Measure-Object -Sum).Sum
            Check ($sum -eq $state.consumed.$name) "Ledger mismatch $name"
        }
        Check ($state.candidates[0].assessment.meetsTargets -and $state.acceptedArchive.Count -eq 1 -and $state.acceptedArchive[0].topology.elements.Count -eq 4) 'Incorrect accepted source/archive'
        foreach ($candidate in $state.candidates) {
            Check ($candidate.assessment.completed -and $candidate.assessment.fields.Count -eq 35) 'Incomplete endpoint'
            foreach ($field in $candidate.assessment.fields) {
                Check ($field.attemptedRays -eq 960 -and $field.checkAttemptedRays -eq 99) 'Changed final sampling'
            }
        }
        $normal = $state.plan | ConvertTo-Json -Depth 100 | ConvertFrom-Json
        $normal.options.seed = 42
        $normal.options.expansion.enableGradualDeletion = $false
        $normal.options.local.sampling.PSObject.Properties.Remove('includeBoundaryClearance')
        $normal.options.full.sampling.PSObject.Properties.Remove('includeBoundaryClearance')
        $identity = $normal | ConvertTo-Json -Depth 100 -Compress
        if ($paired.ContainsKey($batch)) { Check ($paired[$batch] -ceq $identity) 'Unexpected paired input/permission difference' } else { $paired[$batch] = $identity }
        $full = @($state.events | Where-Object { $_.phase -eq 'full' })
        if ($batch -eq 'budget-control') {
            Check ($full.Count -eq 1 -and $full[0].work.gradientEvaluations -eq 9) 'Full compensation not executed'
            $deferred = @($state.events | Where-Object outcome -eq 'StageValidationDeferred')
            Check ($deferred.Count -eq 1 -and $deferred[0].work.tracedRays -eq 0 -and $deferred[0].work.functionEvaluations -eq 0) 'Bad deferral charge'
        }
        $runs += [pscustomobject]@{Batch=$batch;Id=$row.Id;Status=$state.status;Consumed=$state.consumed;Pending=$state.pending.phase;Reduced=$row.ReducedCandidates;FinalProtocol=$state.candidates[0].assessment.protocolFingerprint}
    }
}
$protocol = Get-Content (Join-Path $repo 'labs/InitialStructure/benchmarks/autonomous-topology-v1/edge-loss-budget-control-20261027.json') -Raw | ConvertFrom-Json
$source = $protocol.Cases[0]
Check ((Get-FileHash (Join-Path $repo $source.Source.RepositoryPath) -Algorithm SHA256).Hash.ToLowerInvariant() -eq $source.OriginalSha256) 'Frozen source bytes changed'
$audits = @()
foreach ($mode in @('budget','boundary-budget')) {
    $audit = @(Get-Content (Join-Path $PSScriptRoot "native-audit-$mode/native-checks.json") -Raw | ConvertFrom-Json)
    Check ($audit.Count -eq 2 -and $audit[0].Assessment.MeetsTargets -and -not $audit[1].Assessment.MeetsTargets) 'Native audit mismatch'
    $audits += $audit
}
$phase = Get-Content (Join-Path $PSScriptRoot 'phase-diagnosis/direct-deletion.json') -Raw | ConvertFrom-Json
$diagnosticRays = $phase.ActualRays + (Get-Content (Join-Path $PSScriptRoot 'direct-deletion/direct-deletion.json') -Raw | ConvertFrom-Json).ActualRays
foreach($name in @('diagnostic','diagnostic-budget','diagnostic-boundary-budget')) { $diagnosticRays += (Get-Content (Join-Path $PSScriptRoot "$name/ray-diagnostics.json") -Raw | ConvertFrom-Json).ActualRays }
[xml]$trx = Get-Content (Join-Path $PSScriptRoot 'tests/edge-loss-scoped.trx')
Check ($trx.TestRun.ResultSummary.Counters.passed -eq 60 -and $trx.TestRun.ResultSummary.Counters.failed -eq 0) 'Focused tests did not pass'
$result = [ordered]@{Verified=$true;RunCount=$runs.Count;BackgroundOnly=$true;CpuProcessors=2;PairedInputsVerified=$true;FrozenSourceHashVerified=$true;FinalTargetsAndSamplingUnchanged=$true;LedgerAndCapsVerified=$true;SuccessfulReductions=0;RunRayTotal=($runs.Consumed.tracedRays|Measure-Object -Sum).Sum;AdditionalNativeAuditRays=($audits.AdditionalAuditRays|Measure-Object -Sum).Sum;AdditionalDiagnosticRays=$diagnosticRays;TestsPassed=60;Runs=$runs}
$result | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $PSScriptRoot 'verification.json') -Encoding utf8
Write-Output "Verified $($runs.Count) paired runs, unchanged gates, actual ledgers/caps, native audits and 60 focused tests."
