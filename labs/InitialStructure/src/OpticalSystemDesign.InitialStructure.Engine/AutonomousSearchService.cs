using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Finite CPU search. Every dispatch is reserved durably before any optical work.</summary>
public static partial class AutonomousSearchService
{
    private static readonly ConcurrentDictionary<string, byte> Running = new(StringComparer.Ordinal);

    /// <summary>Historical transmission prototypes remain readable but must never dispatch optical work.</summary>
    public static bool IsHistoricalReadOnlyPlan(AutonomousSearchPlan plan)
        => new[] { plan.Options.Local, plan.Options.Full }.Any(stage =>
            (stage.TransmissionFirst == true || stage.ActiveParameterLimit is not null) && stage.TransmissionPolicyVersion is not (3 or 4));

    public static AutonomousSearchCheckpoint Create(string runId, AutonomousSearchPlan plan)
    {
        ValidatePlan(plan);
        var checkpoint = new AutonomousSearchCheckpoint
        {
            RunId = runId,
            Plan = plan,
            PlanFingerprint = ContentFingerprint.Compute(plan),
            RandomState = new DeterministicRandom(plan.Options.Seed).State
        };
        Validate(checkpoint);
        return checkpoint;
    }

    public static AutonomousSearchCheckpoint Run(AutonomousSearchCheckpoint checkpoint,
        Action<AutonomousSearchCheckpoint>? save = null, CancellationToken cancellationToken = default,
        Action<AutonomousSearchPending, FixedTopologyStep>? progress = null,
        Action<AutonomousSearchPending, ParaxialRecoveryStep>? recoveryProgress = null)
    {
        Validate(checkpoint);
        if (IsHistoricalReadOnlyPlan(checkpoint.Plan))
            throw new NotSupportedException("Unversioned transmission prototype records are read-only. Create a fresh plan with explicit policy version 3 or 4.");
        if (!Running.TryAdd(checkpoint.RunId, 0)) throw new InvalidOperationException("This autonomous run is already executing.");
        try { return Execute(checkpoint, save, cancellationToken, progress, recoveryProgress); }
        finally { Running.TryRemove(checkpoint.RunId, out _); }
    }

    private static AutonomousSearchCheckpoint Execute(AutonomousSearchCheckpoint state,
        Action<AutonomousSearchCheckpoint>? save, CancellationToken cancellationToken,
        Action<AutonomousSearchPending, FixedTopologyStep>? progress,
        Action<AutonomousSearchPending, ParaxialRecoveryStep>? recoveryProgress)
    {
        var policy = state.Plan.Options;
        // A persisted in-flight reservation has unknown execution extent. Charge its whole
        // allocation once and abandon the phase; never silently replay its operation cursor.
        if (state.Reservation is { } lost)
        {
            var pending = state.Pending!;
            state = state with { Consumed = Add(state.Consumed, lost.Maximum), Reservation = null, Pending = null };
            Log(pending, "InterruptedUnknownCharged", "Abandoned in-flight phase; full reservation conservatively charged.", lost.Maximum, pending.Template);
        }
        var elapsedBefore = state.Consumed.Elapsed;
        var timer = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(policy.TimeLimit > elapsedBefore ? policy.TimeLimit - elapsedBefore : TimeSpan.Zero);
        var token = deadline.Token;
        state = state with { Status = AutonomousSearchStatus.Running };
        while (true)
        {
            if (Stop()) return Finish(cancellationToken.IsCancellationRequested ? AutonomousSearchStatus.Cancelled : AutonomousSearchStatus.BudgetExhausted);
            if (state.Pending is null && !Schedule()) return Finish(AutonomousSearchStatus.Completed);
            if (state.Pending is null) continue; // A rejected edit is an audited, zero-optical-work decision.
            var pending = Prepare(state.Pending);
            state = state with { Pending = pending };
            if (pending.Phase == AutonomousSearchPhase.Feasibility
                && (policy.ProtectCompensationRayBudget == true && pending.AfterFeasibility == AutonomousSearchPhase.Full
                    || policy.ProtectPostRecoveryRayBudget == true && pending.AfterFeasibility == AutonomousSearchPhase.Local)
                && policy.MaximumTracedRays - state.Consumed.TracedRays - policy.FinalValidationRayReserve < policy.FinalValidationRayReserve)
            {
                // This optional check cannot reserve its configured full allowance. Preserve the
                // remaining training work and the independent final check, without accepting anything here.
                state = state with { Pending = Next(pending, pending.Template, pending.AfterFeasibility!.Value) };
                Log(pending, "StageValidationDeferred", "Full dense-check allowance unavailable; continue compensation and retain the independent final gate.", new());
                Save();
                continue;
            }
            var maximum = Reserve(pending);
            if (maximum is null) return Finish(AutonomousSearchStatus.BudgetExhausted);
            state = state with { Reservation = new($"{pending.TrialId}:{pending.Phase}:{state.Revision + 1}", pending.Phase, maximum) };
            Save(); // If saving fails, dispatch never starts.
            if (Stop())
            {
                state = state with { Reservation = null };
                Log(pending, "NotDispatched", "Known zero work; reservation refunded and pending phase retained.", new());
                return Finish(cancellationToken.IsCancellationRequested ? AutonomousSearchStatus.Cancelled : AutonomousSearchStatus.BudgetExhausted);
            }
            using var rays = SequentialTraceMeasurement.BeginCpuBudget(maximum.TracedRays);
            var phaseTimer = Stopwatch.StartNew();
            var work = new AutonomousSearchWork();
            var invoked = false;
            try
            {
                var constraints = PhaseConstraints(pending, state.Plan);
                var source = TopologyCompiler.Compile(pending.Template, constraints);
                switch (pending.Phase)
                {
                    case AutonomousSearchPhase.Recovery:
                        var parent = TopologyCompiler.Compile(pending.GradualDeletion?.Baseline ?? Find(pending.ParentId!).Topology, constraints);
                        invoked = true;
                        var recovery = ParaxialRestorer.Run(parent, source, pending.Variables, state.Plan.Targets,
                            policy.Recovery with { MaximumFunctionEvaluations = maximum.FunctionEvaluations, TimeLimit = maximum.Elapsed }, token,
                            step => recoveryProgress?.Invoke(pending, step));
                        work = new()
                        {
                            FunctionEvaluations = recovery.Work.FunctionEvaluations,
                            MatrixEvaluations = recovery.Work.MatrixEvaluations,
                            DifferenceEvaluations = recovery.Work.DifferenceEvaluations,
                            JacobianBuilds = recovery.Work.JacobianBuilds,
                            GeometryProbes = recovery.Work.GeometryProbes
                        };
                        state = state with
                        {
                            Pending = recovery.Termination == ParaxialRecoveryTermination.Recovered
                                ? AfterCompensation(pending, recovery.Topology, AutonomousSearchPhase.Local) : null
                        };
                        Log(pending, recovery.Termination.ToString(), recovery.Termination == ParaxialRecoveryTermination.Cancelled
                            ? "Recovery attempt abandoned at its safe endpoint; resume schedules a new decision, preserving its original charge." : null,
                            work, recovery.Topology, recovery: recovery);
                        if (pending.Operation == "delete" && policy.Expansion is { EnableGradualDeletion: true }
                            && recovery.Termination is ParaxialRecoveryTermination.RankDeficient or ParaxialRecoveryTermination.NoFreeVariables
                                or ParaxialRecoveryTermination.GeometryBlocked or ParaxialRecoveryTermination.NoParaxialRecovery or ParaxialRecoveryTermination.BudgetExhausted
                            && !token.IsCancellationRequested)
                        {
                            var start = BeginGradualDeletion(pending, parent.Topology, state.Plan);
                            state = state with { Pending = start.Pending };
                            var failure = state.Events[^1];
                            state = state with
                            {
                                Events = state.Events.SetItem(state.Events.Length - 1,
                                failure with { Detail = start.Detail })
                            };
                        }
                        break;
                    case AutonomousSearchPhase.Local:
                    case AutonomousSearchPhase.Full:
                        var binding = TopologyParameterBinder.Bind(source, pending.Variables);
                        invoked = true;
                        var optimized = FixedTopologyOptimizer.Run(source, binding,
                            state.Plan.Targets, pending.StageOptions, token, step => progress?.Invoke(pending, step), pending.Continuation);
                        var before = pending.Continuation?.Work;
                        work = new()
                        {
                            FunctionEvaluations = optimized.Work.FunctionEvaluations - (before?.FunctionEvaluations ?? 0),
                            GradientEvaluations = optimized.Work.GradientEvaluations - (before?.GradientEvaluations ?? 0),
                            ValidationEvaluations = optimized.Work.ValidationEvaluations - (before?.ValidationEvaluations ?? 0),
                            GeometryProbes = optimized.Work.GeometryProbes - (before?.GeometryProbes ?? 0)
                        };
                        var paused = optimized.Termination == FixedTopologyTermination.Cancelled && optimized.Continuation is not null;
                        state = state with
                        {
                            Pending = paused ? pending with
                            {
                                Continuation = optimized.Continuation,
                                PhaseConsumed = Add(pending.PhaseConsumed, work with { TracedRays = rays.RayCount, Elapsed = phaseTimer.Elapsed })
                            } : AfterCompensation(pending, optimized.Topology, pending.Phase == AutonomousSearchPhase.Local
                                && optimized.Termination is FixedTopologyTermination.Converged or FixedTopologyTermination.Stalled or FixedTopologyTermination.UpdateLimit
                                ? AutonomousSearchPhase.Full : pending.Template.HasTransitionElements
                                    ? AutonomousSearchPhase.TransitionValidation : AutonomousSearchPhase.Validation)
                        };
                        Log(pending, rays.LimitReached ? "RayBudgetExhausted" : optimized.Termination.ToString(),
                            paused ? "Same-template Adam coordinates, moments, learning rate and residuals retained." : null,
                            work, optimized.Topology, optimization: optimized);
                        break;
                    case AutonomousSearchPhase.Feasibility:
                    case AutonomousSearchPhase.Validation:
                    case AutonomousSearchPhase.TransitionValidation:
                        invoked = true;
                        var assessment = TopologyValidator.Validate(source, state.Plan.Targets, token);
                        work = new() { FunctionEvaluations = 1, ValidationEvaluations = 1, GeometryProbes = assessment.GeometryProbes };
                        state = state with { Pending = token.IsCancellationRequested ? pending : null };
                        var outcome = assessment.Completed ? assessment.MeetsTargets ? "Accepted" : "UnmetTargets" : "ValidationUnavailable";
                        var detail = assessment.Failure;
                        var intermediate = pending.Phase == AutonomousSearchPhase.Feasibility;
                        if (intermediate && !token.IsCancellationRequested)
                            state = state with { Pending = Next(pending, source.Topology, pending.AfterFeasibility!.Value) };
                        if (pending.Phase == AutonomousSearchPhase.TransitionValidation || intermediate && source.Topology.HasTransitionElements)
                        {
                            if (assessment.Completed && assessment.Violations.All(violation => violation.Code == "transition-element"))
                            {
                                var next = AdvanceGradualDeletion(pending, source.Topology, state.Plan);
                                state = state with { Pending = next.Pending };
                                outcome = next.Pending is null ? "TransitionRejected" : "TransitionFeasible";
                                detail = next.Detail;
                            }
                            else if (assessment.Completed) outcome = intermediate ? "UnmetTargets" : "TransitionRejected";
                        }
                        else if (assessment.Completed && !source.Topology.HasTransitionElements && (!intermediate || assessment.MeetsTargets))
                        {
                            var candidate = new AutonomousSearchCandidate(pending.TrialId, pending.ParentId, source.Topology, assessment, Score(assessment),
                                pending.Operation != "seed" && !assessment.MeetsTargets ? 1 : 0);
                            state = state with { Candidates = state.Candidates.Add(candidate), Pending = null };
                            if (assessment.MeetsTargets)
                                state = state with { AcceptedArchive = state.AcceptedArchive.Add(candidate) };
                            RebuildPool();
                        }
                        Log(pending, outcome, detail, work, source.Topology, assessment: assessment);
                        break;
                }
            }
            catch (Exception error) when (!invoked && TopologyEvaluationPlan.DomainError(error))
            {
                // Component protocols handle failures after requests start. These are preflight
                // binding/compiler errors, before a component has submitted counted work.
                state = state with { Pending = null };
                Log(pending, "PreflightRejected", error.Message, work, pending.Template);
            }
            work = work with { TracedRays = rays.RayCount, Elapsed = phaseTimer.Elapsed };
            var last = state.Events[^1];
            state = state with
            {
                Consumed = Add(state.Consumed, work),
                Reservation = null,
                Events = state.Events.SetItem(state.Events.Length - 1, last with { Work = work })
            };
            Save();
        }

        AutonomousSearchCandidate Find(string id) => state.Candidates.Single(candidate => candidate.Id == id);
        bool Stop() => token.IsCancellationRequested || elapsedBefore + timer.Elapsed >= policy.TimeLimit;
        void Save()
        {
            state = state with { Revision = state.Revision + 1, Consumed = state.Consumed with { Elapsed = elapsedBefore + timer.Elapsed } };
            save?.Invoke(state);
        }
        AutonomousSearchCheckpoint Finish(AutonomousSearchStatus status)
        {
            state = state with { Status = status };
            Save();
            return state;
        }
        void Log(AutonomousSearchPending pending, string outcome, string? detail, AutonomousSearchWork work,
            DesignTopology? endpoint = null, ParaxialRecoveryResult? recovery = null, FixedTopologyResult? optimization = null, TopologyAssessment? assessment = null)
            => state = state with
            {
                Events = state.Events.Add(new(state.Events.Length + 1, pending.TrialId, pending.ParentId,
                pending.Operation, pending.Phase, outcome, detail, work, endpoint, recovery, optimization)
                { Change = pending.Change, GradualDeletion = pending.GradualDeletion, Assessment = policy.CheckStageFeasibility == true ? assessment : null })
            };
        void RebuildPool() => state = state with
        {
            Pool = state.Candidates.GroupBy(candidate => candidate.Topology.Elements.Length)
                .SelectMany(group => group.OrderBy(candidate => candidate.Score).ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
                    .Take(policy.CandidatesPerElementCount)).Select(candidate => candidate.Id).ToImmutableArray()
        };
        bool Schedule()
        {
            if (state.SeedCursor < state.Plan.Seeds.Length)
            {
                var index = state.SeedCursor;
                state = state with
                {
                    SeedCursor = index + 1,
                    Pending = new() { TrialId = $"seed-{index}", Operation = "seed", Template = state.Plan.Seeds[index], Phase = AutonomousSearchPhase.Validation }
                };
                return true;
            }
            if (state.TrialCursor >= policy.MaximumTrials) return false;
            var parents = state.Pool.Select(Find).Where(candidate => candidate.OperationCursor
                < policy.BranchesPerParent + (candidate.Assessment.MeetsTargets ? 0 : 1))
                .OrderBy(candidate => candidate.OperationCursor).ThenBy(candidate => candidate.Score).ThenBy(candidate => candidate.Id, StringComparer.Ordinal).ToArray();
            if (parents.Length == 0) return false;
            var parent = parents[0];
            var cursor = parent.OperationCursor;
            var id = $"trial-{state.TrialCursor}";
            var random = DeterministicRandom.Restore(state.RandomState);
            var position = random.NextUnitDouble();
            state = state with
            {
                TrialCursor = state.TrialCursor + 1,
                RandomState = random.State,
                Candidates = state.Candidates.SetItem(state.Candidates.IndexOf(parent), parent with { OperationCursor = cursor + 1 })
            };
            if (policy.FixedTopologyOnly == true || cursor == 0 && !parent.Assessment.MeetsTargets)
            {
                state = state with { Pending = new() { TrialId = id, ParentId = parent.Id, Operation = "optimize", Template = parent.Topology, Phase = AutonomousSearchPhase.Full } };
                return true;
            }
            var branch = parent.Assessment.MeetsTargets ? cursor : cursor - 1;
            var selected = policy.Expansion is null ? (Operation: parent.Assessment.MeetsTargets ? "delete" : "insert", Edit: Edit(parent, branch, id, position))
                : ExpandedEdit(parent, branch, id, state.Plan, variant => Edit(parent, variant, id, position));
            var pending = new AutonomousSearchPending { TrialId = id, ParentId = parent.Id, Operation = selected.Operation, Template = parent.Topology, Phase = AutonomousSearchPhase.Recovery };
            var edit = selected.Edit;
            if (!edit.Succeeded) Log(pending, "EditRejected", edit.Failure?.Code + ": " + edit.Failure?.Message, new());
            else
            {
                var edited = pending with { Template = edit.Topology!, Change = policy.Expansion is null ? null : edit.Change };
                if (policy.SkipParaxialRecovery == true)
                {
                    Log(edited, "RecoverySkipped", "First-order recovery is disabled by the original comparison policy.", new(), edited.Template);
                    edited = edited with { Phase = AutonomousSearchPhase.Local };
                }
                state = state with { Pending = edited };
            }
            return true;
        }
        TopologyEditResult Edit(AutonomousSearchCandidate parent, int branch, string id, double position)
        {
            if (parent.Assessment.MeetsTargets)
            {
                var removable = parent.Topology.Elements.Where(element => !element.Protected).ToArray();
                if (branch >= removable.Length) return Rejected("No untried unprotected element remains.");
                return TopologyOperators.Delete(parent.Topology, removable[branch].Id, state.Plan.Constraints);
            }
            var intervals = TopologyValidation.AirIntervals(parent.Topology, state.Plan.Constraints);
            if (intervals.Count == 0) return Rejected("No legal insertion interval.");
            var interval = intervals[branch % intervals.Count];
            var material = policy.InsertionMaterials[(branch / intervals.Count) % policy.InsertionMaterials.Length];
            var radius = parent.Topology.Elements.Max(element => element.ClearSemiDiameterMillimeters);
            var curvature = branch % 2 == 0 ? .003 : -.003;
            var lens = new LensElement
            {
                Id = id + "-lens",
                FrontSurfaceId = id + "-front",
                BackSurfaceId = id + "-back",
                Material = material,
                ThicknessMillimeters = Math.Max(1, state.Plan.Constraints.MinimumCenterThicknessMillimeters),
                Front = new() { ClearSemiDiameterMillimeters = radius, MechanicalSemiDiameterMillimeters = radius, Profile = new() { CurvaturePerMillimeter = curvature } },
                Back = new() { ClearSemiDiameterMillimeters = radius, MechanicalSemiDiameterMillimeters = radius, Profile = new() { CurvaturePerMillimeter = -curvature } }
            };
            return TopologyOperators.Insert(parent.Topology, interval.LeftSurfaceId, interval.RightSurfaceId, lens, .25 + .5 * position, state.Plan.Constraints);
        }
        TopologyEditResult Rejected(string message) => new(null, null, new(TopologyFailureCode.ElementLimit, message));
        AutonomousSearchPending Next(AutonomousSearchPending pending, DesignTopology topology, AutonomousSearchPhase phase)
            => pending with { Template = topology, Phase = phase, AfterFeasibility = null, StageOptions = null, Continuation = null, Variables = [], PhaseConsumed = new() };
        AutonomousSearchPending AfterCompensation(AutonomousSearchPending pending, DesignTopology topology, AutonomousSearchPhase phase)
            => policy.CheckStageFeasibility == true && phase is AutonomousSearchPhase.Local or AutonomousSearchPhase.Full
                ? Next(pending, topology, AutonomousSearchPhase.Feasibility) with { AfterFeasibility = phase } : Next(pending, topology, phase);
        AutonomousSearchPending Prepare(AutonomousSearchPending pending)
        {
            if (pending.Phase is AutonomousSearchPhase.Validation or AutonomousSearchPhase.TransitionValidation or AutonomousSearchPhase.Feasibility || pending.StageOptions is not null) return pending;
            pending = AnchorAxialCompensation(pending, policy);
            var requests = ExpansionVariables(pending, policy);
            if (pending.Phase == AutonomousSearchPhase.Recovery)
            {
                var parentIds = Find(pending.ParentId!).Topology.Elements.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
                var surfaces = pending.Template.Elements.Where(element => parentIds.Contains(element.Id))
                    .SelectMany(element => new[] { element.FrontSurfaceId, element.BackSurfaceId }).ToHashSet(StringComparer.Ordinal);
                return pending with
                {
                    Variables = requests.Where(request => surfaces.Contains(request.SurfaceId)
                    && (request.Kind == OpticalDesignParameterKind.Curvature
                        || policy.UseAxialVariablesInRecovery == true && request.Kind == OpticalDesignParameterKind.AxialPosition)).ToImmutableArray()
                };
            }
            var stage = pending.Phase == AutonomousSearchPhase.Local ? policy.Local : policy.Full;
            var functions = Math.Max(1, Math.Min(stage.MaximumFunctionEvaluations, policy.MaximumFunctionEvaluations - state.Consumed.FunctionEvaluations - 1));
            var gradients = Math.Max(0, Math.Min(stage.MaximumGradientEvaluations, policy.MaximumGradientEvaluations - state.Consumed.GradientEvaluations));
            return pending with
            {
                Variables = pending.Phase == AutonomousSearchPhase.Local && stage.ActiveParameterLimit is null ? requests.Take(2).ToImmutableArray() : requests,
                StageOptions = stage with { MaximumFunctionEvaluations = functions, MaximumGradientEvaluations = gradients }
            };
        }
        AutonomousSearchWork? Reserve(AutonomousSearchPending pending)
        {
            var final = pending.Phase == AutonomousSearchPhase.Validation;
            var validation = final || pending.Phase is AutonomousSearchPhase.TransitionValidation or AutonomousSearchPhase.Feasibility;
            var functions = policy.MaximumFunctionEvaluations - state.Consumed.FunctionEvaluations - (final ? 0 : 1);
            var gradients = policy.MaximumGradientEvaluations - state.Consumed.GradientEvaluations;
            var availableRays = policy.MaximumTracedRays - state.Consumed.TracedRays - (final ? 0 : policy.FinalValidationRayReserve);
            var availableTime = policy.TimeLimit - elapsedBefore - timer.Elapsed;
            if (functions < 1 || availableRays < 0 || availableTime <= TimeSpan.Zero) return null;
            var prior = pending.Continuation?.Work;
            var recoveryFunctions = pending.Operation == "delete" && policy.Expansion?.DirectDeletionRecoveryMaximumFunctionEvaluations is { } directLimit
                ? Math.Min(directLimit, policy.Recovery.MaximumFunctionEvaluations) : policy.Recovery.MaximumFunctionEvaluations;
            var phaseFunctions = validation ? 1 : pending.Phase == AutonomousSearchPhase.Recovery ? recoveryFunctions : pending.StageOptions!.MaximumFunctionEvaluations - (prior?.FunctionEvaluations ?? 0);
            var phaseGradients = validation || pending.Phase == AutonomousSearchPhase.Recovery ? 0 : pending.StageOptions!.MaximumGradientEvaluations - (prior?.GradientEvaluations ?? 0);
            var phaseTime = validation ? availableTime : pending.Phase == AutonomousSearchPhase.Recovery ? policy.Recovery.TimeLimit : pending.StageOptions!.TimeLimit - (prior?.Elapsed ?? TimeSpan.Zero);
            if (phaseTime <= TimeSpan.Zero) return null;
            return new()
            {
                FunctionEvaluations = Math.Min(functions, Math.Max(0, phaseFunctions)),
                GradientEvaluations = Math.Min(gradients, Math.Max(0, phaseGradients)),
                TracedRays = pending.Phase == AutonomousSearchPhase.Recovery ? 0
                    : Math.Min(availableRays, Math.Max(0, (validation ? policy.FinalValidationRayReserve : policy.MaximumStageTracedRays) - pending.PhaseConsumed.TracedRays)),
                Elapsed = availableTime < phaseTime ? availableTime : phaseTime
            };
        }
        double Score(TopologyAssessment assessment)
        {
            var rms = assessment.Fields.Select(field => field.RmsRadiusMillimeters ?? 1000).DefaultIfEmpty(1000).Max();
            return assessment.Violations.Length + rms / state.Plan.Targets.MaximumRmsRadiusMillimeters;
        }
    }

    private static ImmutableArray<TopologyVariableRequest> Variables(DesignTopology topology, double bound)
        => topology.Elements.Where(element => !element.Protected).SelectMany(element => new[] { (element.FrontSurfaceId, element.Front), (element.BackSurfaceId, element.Back) })
            .Where(pair => pair.Item2.Profile.Kind != TopologyProfileKind.Plane)
            .Select(pair => new TopologyVariableRequest(pair.Item1, OpticalDesignParameterKind.Curvature, -bound, bound, .01)).ToImmutableArray();

    private static AutonomousSearchWork Add(AutonomousSearchWork left, AutonomousSearchWork right) => new()
    {
        FunctionEvaluations = checked(left.FunctionEvaluations + right.FunctionEvaluations),
        GradientEvaluations = checked(left.GradientEvaluations + right.GradientEvaluations),
        ValidationEvaluations = checked(left.ValidationEvaluations + right.ValidationEvaluations),
        MatrixEvaluations = checked(left.MatrixEvaluations + right.MatrixEvaluations),
        DifferenceEvaluations = checked(left.DifferenceEvaluations + right.DifferenceEvaluations),
        JacobianBuilds = checked(left.JacobianBuilds + right.JacobianBuilds),
        TracedRays = checked(left.TracedRays + right.TracedRays),
        GeometryProbes = checked(left.GeometryProbes + right.GeometryProbes),
        Elapsed = left.Elapsed + right.Elapsed
    };

    public static void Validate(AutonomousSearchCheckpoint state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidatePlan(state.Plan);
        if (state.SchemaVersion != 1 || state.Algorithm != AutonomousTopologyIdentity.Name || state.PlanFingerprint != ContentFingerprint.Compute(state.Plan)
            || !Identifier(state.RunId) || state.Revision < 0 || !Enum.IsDefined(state.Status)
            || state.SeedCursor < 0 || state.SeedCursor > state.Plan.Seeds.Length || state.TrialCursor < 0 || state.TrialCursor > state.Plan.Options.MaximumTrials
            || state.Candidates.Length > state.Plan.Seeds.Length + state.Plan.Options.MaximumTrials || state.Events.Length > 4096
            || state.Candidates.Select(candidate => candidate.Id).Distinct(StringComparer.Ordinal).Count() != state.Candidates.Length
            || state.Pool.Distinct(StringComparer.Ordinal).Count() != state.Pool.Length
            || state.AcceptedArchive.Select(candidate => candidate.Id).Distinct(StringComparer.Ordinal).Count() != state.AcceptedArchive.Length)
            throw new InvalidDataException("Invalid autonomous checkpoint identity or bounded collections.");
        var work = state.Reservation is null ? state.Consumed : Add(state.Consumed, state.Reservation.Maximum);
        CheckWork(state.Consumed);
        if (state.Reservation is { } reservation) CheckWork(reservation.Maximum);
        var recorded = new AutonomousSearchWork();
        foreach (var item in state.Events)
        {
            CheckWork(item.Work);
            recorded = Add(recorded, item.Work);
        }
        if (recorded with { Elapsed = TimeSpan.Zero } != state.Consumed with { Elapsed = TimeSpan.Zero }
            || !state.Events.Select(item => item.Sequence).SequenceEqual(Enumerable.Range(1, state.Events.Length)))
            throw new InvalidDataException("Consumed work does not agree with the append-only event ledger.");
        if (work.FunctionEvaluations < 0 || work.FunctionEvaluations > state.Plan.Options.MaximumFunctionEvaluations
            || work.GradientEvaluations < 0 || work.GradientEvaluations > state.Plan.Options.MaximumGradientEvaluations
            || work.TracedRays < 0 || work.TracedRays > state.Plan.Options.MaximumTracedRays || work.Elapsed < TimeSpan.Zero
            || state.Reservation is not null && (state.Pending is null || state.Reservation.Phase != state.Pending.Phase))
            throw new InvalidDataException("Invalid autonomous consumed/reserved work.");
        foreach (var candidate in state.Candidates.Concat(state.AcceptedArchive))
        {
            var compiled = TopologyCompiler.Compile(candidate.Topology, state.Plan.Constraints);
            if (!double.IsFinite(candidate.Score) || candidate.OperationCursor < 0 || candidate.OperationCursor > state.Plan.Options.BranchesPerParent + 1
                || !candidate.Assessment.Completed || candidate.Assessment.OpticFingerprint != compiled.OpticFingerprint
                || candidate.Assessment.ProtocolFingerprint != TopologyValidator.GetProtocolFingerprint(compiled, state.Plan.Targets)
                || candidate.Assessment.Targets != state.Plan.Targets || ContentFingerprint.Compute(candidate.Assessment.StructuralConstraints) != ContentFingerprint.Compute(state.Plan.Constraints)
                || TaskIdentity(candidate.Topology) != TaskIdentity(state.Plan.Seeds[0])
                || candidate.Assessment.MeetsTargets && (candidate.Assessment.Violations.Length != 0 || candidate.Topology.HasTransitionElements))
                throw new InvalidDataException("Candidate physical assessment does not match the original specification.");
        }
        foreach (var group in state.Pool.Select(id => state.Candidates.Single(candidate => candidate.Id == id)).GroupBy(candidate => candidate.Topology.Elements.Length))
            if (group.Count() > state.Plan.Options.CandidatesPerElementCount) throw new InvalidDataException("Candidate pool exceeds its per-count bound.");
        foreach (var accepted in state.AcceptedArchive)
            if (!accepted.Assessment.MeetsTargets || !state.Candidates.Any(candidate => candidate.Id == accepted.Id
                && ContentFingerprint.Compute(candidate.Topology) == ContentFingerprint.Compute(accepted.Topology)))
                throw new InvalidDataException("Invalid accepted archive.");
        if (state.Pending is { } pending && (!Enum.IsDefined(pending.Phase) || pending.ParentId is not null && !state.Candidates.Any(candidate => candidate.Id == pending.ParentId)))
            throw new InvalidDataException("Invalid pending lineage or phase.");
        foreach (var candidate in state.Candidates)
            if (candidate.ParentId is not null && !state.Candidates.TakeWhile(item => item.Id != candidate.Id).Any(item => item.Id == candidate.ParentId))
                throw new InvalidDataException("Candidate lineage must reference an earlier immutable prescription.");
        if (state.Pending is { } active)
        {
            CheckWork(active.PhaseConsumed);
            if (active.Phase == AutonomousSearchPhase.Feasibility
                ? state.Plan.Options.CheckStageFeasibility != true || active.AfterFeasibility is not (AutonomousSearchPhase.Local or AutonomousSearchPhase.Full)
                    || active.Continuation is not null || active.StageOptions is not null || !active.Variables.IsEmpty
                : active.AfterFeasibility is not null)
                throw new InvalidDataException("Invalid independent stage feasibility continuation.");
            if (TaskIdentity(active.Template) != TaskIdentity(state.Plan.Seeds[0])) throw new InvalidDataException("Pending task metadata changed.");
        }
        ValidateExpansionCheckpoint(state);
        ValidateAxialCheckpoint(state);
    }

    private static void ValidatePlan(AutonomousSearchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var options = plan.Options;
        if (options.FixedTopologyOnly == true && options.SkipParaxialRecovery == true
            || options.SkipParaxialRecovery == true && options.Expansion?.EnableGradualDeletion == true)
            throw new ArgumentException("Recovery-free comparison cannot enable progressive recovery or the fixed-topology policy.");
        if (plan.Seeds.Length is < 1 or > 8 || options.MaximumTrials is < 1 or > 128
            || options.CandidatesPerElementCount is < 1 or > 8 || options.BranchesPerParent is < 1 or > 12
            || options.MaximumFunctionEvaluations is < 2 or > 100_000 || options.MaximumGradientEvaluations is < 0 or > 100_000
            || options.MaximumTracedRays is < 1 or > 1_000_000_000 || options.MaximumStageTracedRays is < 1 or > 100_000_000
            || options.FinalValidationRayReserve < 1 || options.FinalValidationRayReserve > options.MaximumTracedRays
            || options.TimeLimit <= TimeSpan.Zero || options.TimeLimit > TimeSpan.FromHours(4)
            || !double.IsFinite(options.CurvatureBoundPerMillimeter) || options.CurvatureBoundPerMillimeter is <= 0 or > 1
            || options.InsertionMaterials.Length is < 1 or > 8 || options.InsertionMaterials.Any(material => !Identifier(material)))
            throw new ArgumentException("Invalid finite autonomous search plan.");
        foreach (var stage in new[] { options.Local, options.Full })
        {
            TopologyEvaluationPlan.Validate(plan.Targets, stage.Sampling);
            if (stage.ComputeMode != DesignComputeMode.Cpu || stage.PerformFinalAssessment || stage.MaximumUpdates is < 0 or > 400
                || stage.MaximumFunctionEvaluations is < 1 or > 10000 || stage.MaximumGradientEvaluations is < 0 or > 10000
                || stage.TimeLimit <= TimeSpan.Zero || stage.TimeLimit > TimeSpan.FromHours(1)
                || stage.ActiveParameterLimit is < 1 or > 512
                || (stage.TransmissionFirst == true || stage.ActiveParameterLimit is not null) && stage.Sampling.IncludeBoundaryClearance != true
                || !double.IsFinite(stage.LearningRate) || stage.LearningRate is <= 0 or > 1)
                throw new ArgumentException("Autonomous hard-quota stages require bounded explicit CPU training and separate final assessment.");
        }
        ParaxialRestorer.ValidateOptions(options.Recovery);
        if (options.Expansion is { } expansion) ValidateExpansion(expansion, plan);
        if (options.AxialCompensation is { } axial) ValidateAxialPolicy(axial, plan);
        foreach (var seed in plan.Seeds)
        {
            TopologyValidation.Validate(seed, plan.Constraints);
            if (TaskIdentity(seed) != TaskIdentity(plan.Seeds[0])) throw new ArgumentException("All seeds must preserve the original field, spectrum, frame, stop and image specification.");
        }
    }
    private static string TaskIdentity(DesignTopology topology) => ContentFingerprint.Compute(topology with { Name = "task", Elements = [] });
    private static void CheckWork(AutonomousSearchWork work)
    {
        if (work.FunctionEvaluations < 0 || work.GradientEvaluations < 0 || work.ValidationEvaluations < 0 || work.MatrixEvaluations < 0
            || work.DifferenceEvaluations < 0 || work.JacobianBuilds < 0 || work.TracedRays < 0 || work.GeometryProbes < 0 || work.Elapsed < TimeSpan.Zero)
            throw new InvalidDataException("Work counters cannot be negative.");
    }
    private static bool Identifier(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && !value.Any(char.IsControl);
}
