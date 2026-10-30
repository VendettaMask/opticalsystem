#requires -Version 7.0
param([Parameter(Mandatory)][string]$EvidenceRoot)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($EvidenceRoot)
$protocol = Get-Content -Raw -LiteralPath (Join-Path $root 'experiment-protocol.json') | ConvertFrom-Json
$rows = [Collections.Generic.List[object]]::new()
$finals = [Collections.Generic.List[object]]::new()
$identities = [Collections.Generic.List[object]]::new()
$roundCosts = [Collections.Generic.List[object]]::new()
$phaseCosts = [Collections.Generic.List[object]]::new()
$inputs = @{}
function Canonical($value) { ConvertTo-Json -InputObject $value -Depth 100 -Compress }
function Median([double[]]$values) {
    if ($values.Length -eq 0) { return $null }
    $ordered = @($values | Sort-Object)
    if ($ordered.Length % 2) { return $ordered[[int][Math]::Floor($ordered.Length / 2)] }
    return ($ordered[$ordered.Length / 2 - 1] + $ordered[$ordered.Length / 2]) / 2
}
foreach ($seed in $protocol.seeds) {
    foreach ($arm in $protocol.arms) {
        $directory = Join-Path $root ('formal/' + $arm + '-seed' + $seed)
        foreach ($case in $protocol.cases) {
            $path = Join-Path $directory ($case + '.json')
            $r = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
            $timing = Get-Content -Raw -LiteralPath (Join-Path $directory ($case + '.timing.json')) | ConvertFrom-Json
            $c = $r.competition
            foreach ($phase in $c.searchEvidence.costs) {
                $phaseCosts.Add([pscustomobject]@{ Case=$case; Arm=$arm; Seed=$seed; Candidate=$phase.candidateId;
                    Stage=$phase.stageIndex; Phase=$phase.phase; Seconds=[TimeSpan]::Parse($phase.elapsed).TotalSeconds;
                    Functions=$phase.functions; Gradients=$phase.gradients; Rays=$phase.rays;
                    GeometryProbes=$phase.geometryProbes; MatrixRequests=$phase.matrixRequests })
            }
            foreach ($round in @($c.rounds | Group-Object stageIndex)) {
                $members = @($round.Group)
                $roundCosts.Add([pscustomobject]@{ Case=$case; Arm=$arm; Seed=$seed; Stage=[int]$round.Name;
                    Candidates=$members.Count; CompleteScreens=@($members | Where-Object { $_.screen.completed }).Count;
                    AllScreensComplete=(@($members | Where-Object { -not $_.screen.completed }).Count -eq 0);
                    Functions=($members.work.functionEvaluations | Measure-Object -Sum).Sum;
                    Gradients=($members.work.gradientEvaluations | Measure-Object -Sum).Sum;
                    Rays=($members.work.tracedRays | Measure-Object -Sum).Sum;
                    CandidateSeconds=($members.work.elapsed | ForEach-Object { [TimeSpan]::Parse($_).TotalSeconds } | Measure-Object -Sum).Sum;
                    Allocation=$members[0].allocation })
            }
            $inputIdentity = Canonical ([ordered]@{ Seeds=$r.sourcePlan.seeds; Targets=$r.sourcePlan.targets; Constraints=$r.sourcePlan.constraints })
            if ($inputs.ContainsKey($case)) { if ($inputs[$case] -ne $inputIdentity) { throw "Input drift: $arm/$seed/$case" } }
            else { $inputs[$case] = $inputIdentity }
            foreach ($measure in @('functionEvaluations','gradientEvaluations','tracedRays','geometryProbes')) {
                $sum = ($c.candidates | ForEach-Object { $_.work.$measure } | Measure-Object -Sum).Sum
                if ($sum -ne $r.work.$measure) { throw "Unsettled work: $arm/$seed/$case/$measure" }
            }
            if ($r.work.functionEvaluations -gt 200 -or $r.work.gradientEvaluations -gt 200 -or $r.work.tracedRays -gt 500000) { throw "Budget overrun: $arm/$seed/$case" }
            if (@($c.candidates | Where-Object { $_.work.functionEvaluations -gt 50 }).Count) { throw 'Candidate function overrun' }
            $assessed = @($c.candidates | Where-Object { $null -ne $_.originalAssessment })
            foreach ($candidate in $assessed) {
                $a = $candidate.originalAssessment
                if ((Canonical $a.targets) -ne (Canonical $r.sourcePlan.targets) -or (Canonical $a.structuralConstraints) -ne (Canonical $r.sourcePlan.constraints)) { throw 'Original gate drift' }
                $qualified = [bool]$a.completed -and [bool]$a.meetsTargets
                if ($qualified -ne ($candidate.id -in $c.qualifiedIds)) { throw 'False qualification' }
                $metrics = @($a.fields | Where-Object { $null -ne $_.rmsRadiusMillimeters -and $null -ne $_.maximumRadiusMillimeters -and
                    [double]::IsFinite([double]$_.rmsRadiusMillimeters) -and [double]::IsFinite([double]$_.maximumRadiusMillimeters) })
                $available = $a.completed -and $metrics.Count -gt 0 -and $metrics.Count -eq $a.fields.Count
                $worstRms = if ($available) { ($metrics.rmsRadiusMillimeters | Measure-Object -Maximum).Maximum } else { $null }
                $worstMaximum = if ($available) { ($metrics.maximumRadiusMillimeters | Measure-Object -Maximum).Maximum } else { $null }
                $finals.Add([pscustomobject]@{ Case=$case; Arm=$arm; Seed=$seed; Id=$candidate.id; Completed=$a.completed;
                    Qualified=$qualified; Rays=$a.tracedRayCount; Fields=$a.fields.Count; AllFieldMetricsAvailable=$available;
                    WorstRmsMillimeters=$worstRms; WorstMaximumMillimeters=$worstMaximum; Violations=$a.violations;
                    ProtocolFingerprint=$a.protocolFingerprint; OpticFingerprint=$a.opticFingerprint; Failure=$a.failure })
            }
            $expectedQualified = @($assessed | Where-Object { $_.originalAssessment.completed -and $_.originalAssessment.meetsTargets })
            if ($expectedQualified.Count -ne $c.qualifiedIds.Count) { throw 'Qualified archive differs from final assessments' }
            $signs = @($c.candidates | Where-Object { $_.searchEvidence.requestedPowerSigns.Count -gt 0 })
            foreach ($candidate in $signs) {
                if ($null -ne $candidate.topology -and -not $candidate.searchEvidence.signsVerified) { throw 'Unverified signed seed escaped construction' }
            }
            $auditFinals = @($assessed | Where-Object { $_.id -in $c.searchEvidence.challenges.candidateId })
            $first = if ($null -ne $timing.firstOriginalQualifiedElapsed) { [TimeSpan]::Parse($timing.firstOriginalQualifiedElapsed).TotalSeconds } else { $null }
            $rows.Add([pscustomobject]@{ Case=$case; Arm=$arm; Seed=$seed; Qualified=$c.qualifiedIds.Count; Found=$timing.found;
                FirstQualifiedSeconds=$first; TotalSeconds=[TimeSpan]::Parse($r.work.elapsed).TotalSeconds;
                Functions=$r.work.functionEvaluations; Gradients=$r.work.gradientEvaluations; Rays=$r.work.tracedRays;
                Candidates=$c.candidates.Count; Constructed=@($c.candidates | Where-Object topology).Count;
                Finalists=$assessed.Count; CompletedFinalists=@($assessed | Where-Object { $_.originalAssessment.completed }).Count;
                IncompleteFinalists=@($assessed | Where-Object { -not $_.originalAssessment.completed }).Count;
                RequestedSignBranches=$signs.Count; VerifiedSignBranches=@($signs | Where-Object { $_.searchEvidence.signsVerified }).Count;
                Challenges=$c.searchEvidence.challenges.Count; ChallengeFinalists=$auditFinals.Count;
                QualifiedChallengers=@($auditFinals | Where-Object { $_.originalAssessment.completed -and $_.originalAssessment.meetsTargets }).Count;
                Termination=$r.termination; StopReason=$c.stopReason; Bytes=(Get-Item -LiteralPath $path).Length })
            if ($arm -eq 'off' -and $case -ne 'original-half21') {
                $baseline = Get-Content -Raw -LiteralPath (Join-Path $root ("baseline-seed$seed/$case.json")) | ConvertFrom-Json
                $same = $baseline.protocolFingerprint -eq $r.protocolFingerprint -and $baseline.competition.protocolFingerprint -eq $c.protocolFingerprint
                $identities.Add([pscustomobject]@{ Case=$case; Seed=$seed; LegacyFingerprintPreserved=$same })
                if (-not $same) { throw 'Legacy identity changed' }
            }
        }
    }
}
if ($rows.Count -ne 80) { throw 'Incomplete formal run set' }
$summary = foreach ($case in $protocol.cases) {
    foreach ($arm in $protocol.arms) {
        $group = @($rows | Where-Object { $_.Case -eq $case -and $_.Arm -eq $arm })
        $groupFinals = @($finals | Where-Object { $_.Case -eq $case -and $_.Arm -eq $arm })
        $qualifiedFinals = @($groupFinals | Where-Object Qualified)
        [pscustomobject]@{ Case=$case; Arm=$arm; Runs=$group.Count; SuccessfulRuns=@($group | Where-Object Found).Count;
            Qualified=($group.Qualified | Measure-Object -Sum).Sum;
            FirstQualifiedMedianSeconds=Median @($group | Where-Object Found | ForEach-Object FirstQualifiedSeconds);
            TotalMedianSeconds=Median $group.TotalSeconds; RaysMedian=Median $group.Rays;
            CompletedFinalists=($group.CompletedFinalists | Measure-Object -Sum).Sum; IncompleteFinalists=($group.IncompleteFinalists | Measure-Object -Sum).Sum;
            BestQualifiedRmsMillimeters=if ($qualifiedFinals.Count) { ($qualifiedFinals.WorstRmsMillimeters | Measure-Object -Minimum).Minimum } else { $null };
            MedianQualifiedRmsMillimeters=if ($qualifiedFinals.Count) { Median @($qualifiedFinals.WorstRmsMillimeters) } else { $null };
            RequestedSigns=($group.RequestedSignBranches | Measure-Object -Sum).Sum; VerifiedSigns=($group.VerifiedSignBranches | Measure-Object -Sum).Sum;
            Challenges=($group.Challenges | Measure-Object -Sum).Sum; ChallengeFinalists=($group.ChallengeFinalists | Measure-Object -Sum).Sum;
            QualifiedChallengers=($group.QualifiedChallengers | Measure-Object -Sum).Sum }
    }
}
$rows | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $root 'formal-runs.json') -Encoding utf8
$finals | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $root 'formal-finalists.json') -Encoding utf8
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $root 'formal-summary.json') -Encoding utf8
$identities | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'legacy-identity-check.json') -Encoding utf8
$roundCosts | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $root 'formal-round-costs.json') -Encoding utf8
$phaseCosts | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'formal-phase-costs.json') -Encoding utf8
$summary | Format-Table Case,Arm,SuccessfulRuns,Qualified,FirstQualifiedMedianSeconds,TotalMedianSeconds,RaysMedian,CompletedFinalists,IncompleteFinalists -AutoSize
Write-Output 'All 80 runs: shared inputs/gates, cumulative work, budget limits, sign qualification and 15 legacy identities verified.'
