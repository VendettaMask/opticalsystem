using System.Collections.Immutable;
using System.Diagnostics;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Bounded structural proposals, fresh recovery/compensation and common competition.
/// Parent qualification is retained separately; every new qualified design comes from an original-spec CPU validation.</summary>
public static partial class StructureEvolutionSearch
{
    private sealed class StopEvolution : Exception;
    public static void ValidateOptions(ParaxialSeedSearchOptions options)
    {
        var p = options.Evolution!;
        if (p.Version != 1 || !Enum.IsDefined(p.Mode) || options.Competition?.SearchStrategy is null
            || p.MaximumParents is < 1 or > 8 || p.MaximumBranches is < 1 or > 32
            || p.Operations.IsDefault || p.Operations.Length > 6 || p.Operations.Distinct().Count() != p.Operations.Length
            || p.Operations.Any(o => o is not (TopologyOperationKind.Insert or TopologyOperationKind.Delete or TopologyOperationKind.Split
                or TopologyOperationKind.Merge or TopologyOperationKind.Material or TopologyOperationKind.Profile))
            || p.RecoveryFunctionsPerBranch is < 2 or > 128 || p.CompensationUpdatesPerBranch is < 0 or > 40
            || p.CompensationRaysPerBranch <= options.Competition.ScreeningRaysPerCandidate || p.CompensationRaysPerBranch > 1_000_000
            || p.MaximumPhaseTime <= TimeSpan.Zero || p.MaximumPhaseTime > TimeSpan.FromMinutes(1)
            || !double.IsFinite(p.InsertedThicknessMillimeters) || p.InsertedThicknessMillimeters <= 0
            || p.PowerAllocation is { } a && (a.Version != 1 || a.MaximumAttempts is < 1 or > 8
                || a.ClosureFunctionsPerAttempt is < 2 or > 128 || !double.IsFinite(a.MaximumAbsoluteNormalizedPower)
                || a.MaximumAbsoluteNormalizedPower is <= 0 or > 10 || !double.IsFinite(a.PerturbationFraction) || a.PerturbationFraction is <= 0 or > 1))
            throw new ArgumentException("M2 requires explicit versioned competition and bounded structural/constructor budgets.");
    }

    public static ParaxialSeedSearchResult Run(AutonomousSearchPlan plan, ParaxialSeedSearchOptions options,
        CancellationToken cancellationToken = default, Action<int, int>? progress = null)
    {
        ParaxialSeedSearch.ValidateOptions(options);
        _ = AutonomousSearchService.Create("structure-evolution-preparation", plan);
        if (AutonomousSearchService.IsHistoricalReadOnlyPlan(plan) || plan.Seeds.Any(s => s.HasTransitionElements))
            throw new NotSupportedException("M2 requires executable ordinary physical input seeds.");
        var policy = options.Evolution!; var protocol = options.Competition!;
        var identity = ContentFingerprint.Compute(new
        {
            Identity = "initial-structure-evolution-v1",
            plan,
            options,
            Recovery = "fresh-Core-AD-parent-A-C-pupil-and-geometry",
            Compensation = "anchored-before-recovery",
            Final = "unchanged-original-TopologyValidator",
            Archive = "immutable-qualified-parents-and-Pareto"
        });
        var watch = Stopwatch.StartNew(); using var deadline = new CancellationTokenSource(options.TimeLimit);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var token = linked.Token; using var cpu = new SequentialGpuSession(DesignComputeMode.Cpu, token);
        using var measured = SequentialTraceMeasurement.BeginCpuBudget(options.MaximumTracedRays);
        var parents = new List<StructureEvolutionParent>(); var trials = new List<StructureEvolutionTrial>();
        var costs = new List<StructurePhaseCost>(); var archive = new List<QualifiedEvolutionDesign>();
        var total = new AutonomousSearchWork(); var termination = ParaxialSeedTermination.Completed; var reason = "Completed";
        ParaxialSeedSearchResult? competition = null;
        TimeSpan? firstNewQualified = null;
        var finalCount = Math.Min(protocol.MaximumFinalists, options.RetainedSeeds);
        var estimate = plan.Seeds.Take(policy.MaximumParents).Max(s => StructureSearchDiagnostics.EstimateFinalRays(
            TopologyCompiler.Compile(s, plan.Constraints), protocol.SearchStrategy!.FinalRayRecoveryFactor));
        var reserveRays = estimate * finalCount; var reserveTime = protocol.SearchStrategy!.FinalTimePerCandidate * finalCount;
        var random = new DeterministicRandom(plan.Options.Seed);
        try
        {
            token.ThrowIfCancellationRequested();
            var parentCount = Math.Min(policy.MaximumParents, plan.Seeds.Length);
            if (options.MaximumTracedRays < reserveRays + estimate * parentCount || options.MaximumFunctionEvaluations < finalCount + parentCount + 1
                || options.TimeLimit <= reserveTime)
            { reason = "InsufficientParentAndOriginalFinalValidationReserve"; termination = ParaxialSeedTermination.BudgetExhausted; throw new StopEvolution(); }
            foreach (var topology in plan.Seeds.Take(policy.MaximumParents))
            {
                token.ThrowIfCancellationRequested();
                var id = $"parent-{parents.Count + 1:D3}"; var before = measured.RayCount; var timer = Stopwatch.StartNew();
                using var budget = SequentialTraceMeasurement.BeginCpuBudget(Math.Max(1, options.MaximumTracedRays - measured.RayCount - reserveRays));
                var assessment = TopologyValidator.Validate(TopologyCompiler.Compile(topology, plan.Constraints), plan.Targets, token);
                var work = new AutonomousSearchWork
                {
                    FunctionEvaluations = 1,
                    ValidationEvaluations = 1,
                    TracedRays = measured.RayCount - before,
                    GeometryProbes = assessment.GeometryProbes,
                    Elapsed = timer.Elapsed
                };
                parents.Add(new(id, topology, assessment, work)); total = Add(total, work);
                costs.Add(Cost(id, -1, "OriginalParentValidation", work)); AddQualified(id, null, topology, assessment);
                progress?.Invoke(0, total.FunctionEvaluations);
                if (!assessment.Completed) { token.ThrowIfCancellationRequested(); reason = "ParentValidationIncomplete"; termination = ParaxialSeedTermination.BudgetExhausted; throw new StopEvolution(); }
            }
            var tasks = new List<(StructureEvolutionParent Parent, TopologyOperationKind? Operation, int Variant, bool Power)>();
            var powerSlots = Math.Min(policy.PowerAllocation?.MaximumAttempts ?? 0, policy.MaximumBranches);
            if (policy.OptimizeParents)
                tasks.AddRange(parents.Take(policy.MaximumBranches - powerSlots).Select(p => (p, (TopologyOperationKind?)null, 0, false)));
            for (var cycle = 0; tasks.Count < policy.MaximumBranches - powerSlots && !policy.Operations.IsEmpty; cycle++)
                foreach (var operation in policy.Operations)
                    foreach (var parent in parents)
                    {
                        if (tasks.Count >= policy.MaximumBranches - powerSlots) break;
                        tasks.Add((parent, operation, cycle + (int)((ulong)plan.Options.Seed % (ulong)parent.Topology.Elements.Length), false));
                    }
            for (var i = 0; i < powerSlots; i++) tasks.Add((parents[i % parents.Count], null, i, true));
            foreach (var task in tasks)
            {
                token.ThrowIfCancellationRequested();
                var availableFunctions = options.MaximumFunctionEvaluations - total.FunctionEvaluations - finalCount;
                var candidateQuota = Math.Min(options.MaximumEvaluationsPerStart - 2 * protocol.Stages.Length - 1,
                    availableFunctions / Math.Max(1, tasks.Count - trials.Count));
                if (candidateQuota < 3 || options.MaximumTracedRays - measured.RayCount <= reserveRays + protocol.ScreeningRaysPerCandidate
                    || options.TimeLimit - watch.Elapsed <= reserveTime || total.GradientEvaluations >= protocol.MaximumGradientEvaluations)
                { reason = "InsufficientCompleteEvolutionTrialBudget"; termination = ParaxialSeedTermination.BudgetExhausted; break; }
                var id = $"evolution-{trials.Count + 1:D3}"; var beforeRays = measured.RayCount; var timer = Stopwatch.StartNew();
                var trial = new StructureEvolutionTrial
                {
                    Id = id,
                    ParentId = task.Parent.Id,
                    Operation = task.Power ? "power-allocation" : task.Operation?.ToString().ToLowerInvariant() ?? "optimize",
                    Outcome = "NotDispatched"
                };
                var work = new AutonomousSearchWork();
                try
                {
                    DesignTopology child;
                    if (task.Power)
                    {
                        if (plan.Options.QualifiedReductionOnly == true) { trial = trial with { Outcome = "OriginalReductionOnlyPermission" }; continue; }
                        var allocated = PowerAllocationSeedGenerator.Run(task.Parent.Id, task.Parent.Topology, plan, options,
                            policy.PowerAllocation!, candidateQuota - 2, policy.MaximumPhaseTime, ref random, token);
                        work = Add(work, allocated.Evidence.Work); costs.Add(Cost(id, 0, "PowerAllocationAndTargetClosure", allocated.Evidence.Work));
                        trial = trial with { PowerAllocation = allocated.Evidence, Outcome = allocated.Evidence.Outcome };
                        if (allocated.Topology is null) continue;
                        child = allocated.Topology;
                    }
                    else if (task.Operation is { } operation)
                    {
                        if (plan.Options.QualifiedReductionOnly == true && (operation != TopologyOperationKind.Delete || !task.Parent.Assessment.MeetsTargets))
                        { trial = trial with { Outcome = "OriginalReductionOnlyPermission" }; continue; }
                        var edit = Propose(task.Parent.Topology, operation, task.Variant, id, plan, policy);
                        trial = trial with { Change = edit.Change, Detail = edit.Failure?.Message };
                        if (!edit.Succeeded) { trial = trial with { Outcome = "EditRejected:" + edit.Failure?.Code }; continue; }
                        child = edit.Topology!;
                    }
                    else child = task.Parent.Topology;
                    var source = TopologyCompiler.Compile(child, plan.Constraints);
                    var variables = EvolutionVariables(child, plan.Options);
                    _ = TopologyParameterBinder.Bind(source, variables);
                    trial = trial with { EditedTopology = child, Endpoint = child, AnchoredVariables = variables, EditBindingFingerprint = source.BindingFingerprint };
                    var geometry = Geometry(source, plan.Targets, token);
                    work = Add(work, geometry.Work); costs.Add(Cost(id, 0, "EditedGeometry", geometry.Work));
                    if (!geometry.Passed) { trial = trial with { Outcome = token.IsCancellationRequested ? "EditedGeometryCancelled" : "EditedGeometryRejected" }; continue; }
                    if (policy.Mode != StructureEvolutionMode.DirectCompensation && task.Operation is not null)
                    {
                        var recoveryLimit = Math.Min(policy.RecoveryFunctionsPerBranch, candidateQuota - work.FunctionEvaluations - 2);
                        if (recoveryLimit < 2) { trial = trial with { Outcome = "InsufficientRecoveryBudget" }; continue; }
                        var recovered = ParaxialRestorer.Run(TopologyCompiler.Compile(task.Parent.Topology, plan.Constraints), source,
                            variables.Where(v => v.Kind == OpticalDesignParameterKind.Curvature
                                || v.Kind == OpticalDesignParameterKind.AxialPosition && plan.Options.UseAxialVariablesInRecovery == true), plan.Targets,
                            plan.Options.Recovery with
                            {
                                UseAutomaticMatrixDerivatives = true,
                                RestorePupilMagnification = true,
                                UseGeometryConstraintTangents = true,
                                MaximumFunctionEvaluations = recoveryLimit,
                                TimeLimit = policy.MaximumPhaseTime
                            }, token);
                        var recoveryWork = new AutonomousSearchWork
                        {
                            FunctionEvaluations = recovered.Work.FunctionEvaluations,
                            MatrixEvaluations = recovered.Work.MatrixEvaluations,
                            DifferenceEvaluations = recovered.Work.DifferenceEvaluations,
                            JacobianBuilds = recovered.Work.JacobianBuilds,
                            GeometryProbes = recovered.Work.GeometryProbes,
                            Elapsed = recovered.Work.Elapsed
                        };
                        work = Add(work, recoveryWork); costs.Add(Cost(id, 1, "FreshParaxialRecovery", recoveryWork));
                        child = recovered.Topology; trial = trial with { Recovery = recovered, Endpoint = child };
                        if (recovered.Termination != ParaxialRecoveryTermination.Recovered)
                        { trial = trial with { Outcome = "Recovery:" + recovered.Termination }; continue; }
                    }
                    source = TopologyCompiler.Compile(child, plan.Constraints);
                    var binding = TopologyParameterBinder.Bind(source, variables); // New template, same original physical bounds; no optimizer continuation.
                    trial = trial with { CompensationBindingFingerprint = source.BindingFingerprint };
                    var functions = Math.Min(policy.CompensationUpdatesPerBranch + 1, candidateQuota - work.FunctionEvaluations - 1);
                    var gradients = Math.Min(functions, protocol.MaximumGradientEvaluations - total.GradientEvaluations);
                    if (functions < 1 || gradients < 1) { trial = trial with { Outcome = "InsufficientCompensationBudget" }; continue; }
                    var rayQuota = Math.Min(policy.CompensationRaysPerBranch, options.MaximumTracedRays - measured.RayCount - reserveRays);
                    if (rayQuota <= protocol.ScreeningRaysPerCandidate) { trial = trial with { Outcome = "InsufficientCompensationRayBudget" }; continue; }
                    using var phaseTime = CancellationTokenSource.CreateLinkedTokenSource(token); phaseTime.CancelAfter(policy.MaximumPhaseTime);
                    using var phaseRays = SequentialTraceMeasurement.BeginCpuBudget(rayQuota);
                    var phaseWatch = Stopwatch.StartNew(); FixedTopologyResult optimized;
                    using (var optimizationBudget = SequentialTraceMeasurement.BeginCpuBudget(rayQuota - protocol.ScreeningRaysPerCandidate))
                        optimized = FixedTopologyOptimizer.Run(source, binding, plan.Targets, plan.Options.Local with
                        {
                            MaximumUpdates = Math.Max(0, functions - 1),
                            MaximumFunctionEvaluations = functions,
                            MaximumGradientEvaluations = gradients,
                            TimeLimit = policy.MaximumPhaseTime * .8,
                            ComputeMode = DesignComputeMode.Cpu,
                            PerformFinalAssessment = false,
                            Sampling = new()
                            {
                                FieldsPerDirection = 3,
                                PupilRings = 1,
                                PupilArms = 4,
                                ProfileRadialSamples = 64,
                                IncludeBoundaryClearance = true,
                                IncludeBoundaryMaximum = true
                            }
                        }, phaseTime.Token);
                    child = optimized.Topology;
                    var screen = StructureCompetitionSearch.Screen(TopologyCompiler.Compile(child, plan.Constraints), plan.Targets,
                        protocol.ScreeningRaysPerCandidate, phaseTime.Token);
                    if (phaseTime.IsCancellationRequested && !token.IsCancellationRequested && !screen.Completed)
                        screen = screen with { Outcome = "CompensationPhaseTimeLimit" };
                    var phaseWork = new AutonomousSearchWork
                    {
                        FunctionEvaluations = optimized.Work.FunctionEvaluations + 1,
                        GradientEvaluations = optimized.Work.GradientEvaluations,
                        GeometryProbes = optimized.Work.GeometryProbes + screen.GeometryProbes,
                        TracedRays = phaseRays.RayCount,
                        Elapsed = phaseWatch.Elapsed
                    };
                    work = Add(work, phaseWork); costs.Add(Cost(id, 2, "ShortRealRayCompensationAndScreen", phaseWork));
                    trial = trial with
                    {
                        Endpoint = child,
                        Screen = screen,
                        Outcome = screen.Outcome,
                        Compensation = new(optimized.Algorithm, optimized.ObjectiveFingerprint, optimized.Termination, optimized.AcceptedUpdates,
                            optimized.Work, optimized.InitialEvaluation?.Objective, optimized.EndpointEvaluation?.Objective,
                            optimized.EndpointEvaluation?.Available == true, optimized.Topology, optimized.Targets, optimized.Variables, optimized.Steps, optimized.Diagnostics)
                    };
                }
                catch (Exception error) when (TopologyEvaluationPlan.DomainError(error) && !SequentialTraceBudgetExceededException.IsBudgetException(error))
                { trial = trial with { Outcome = "TrialDomainRejected", Detail = error.Message }; }
                finally
                {
                    work = work with { TracedRays = measured.RayCount - beforeRays, Elapsed = timer.Elapsed };
                    trial = trial with { Work = work }; trials.Add(trial); total = Add(total, work);
                    progress?.Invoke(trials.Count, total.FunctionEvaluations);
                }
            }
            token.ThrowIfCancellationRequested();
            var prepared = Rank(trials.Where(t => t.Endpoint is not null && t.Screen is { Completed: true, GeometryPassed: true }))
                .Take(options.MaximumStarts).ToArray();
            if (prepared.Length == 0) { if (reason == "Completed") reason = "NoCompleteEvolvedCandidate"; throw new StopEvolution(); }
            if (policy.Mode == StructureEvolutionMode.RecoveryAndCompetition)
            {
                var remainingFunctions = options.MaximumFunctionEvaluations - total.FunctionEvaluations;
                var remainingRays = options.MaximumTracedRays - measured.RayCount;
                var remainingTime = options.TimeLimit - watch.Elapsed;
                if (remainingFunctions < 2 || remainingRays < estimate || remainingTime <= TimeSpan.Zero)
                { reason = "InsufficientCompetitionAndFinalBudget"; termination = ParaxialSeedTermination.BudgetExhausted; throw new StopEvolution(); }
                var competitionOptions = options with
                {
                    Evolution = null,
                    MaximumStarts = Math.Min(options.MaximumStarts, prepared.Length),
                    MaximumFunctionEvaluations = remainingFunctions,
                    MaximumTracedRays = remainingRays,
                    TimeLimit = remainingTime,
                    Competition = protocol with { MaximumGradientEvaluations = Math.Max(0, protocol.MaximumGradientEvaluations - total.GradientEvaluations) }
                };
                competition = StructureCompetitionSearch.Run(plan, competitionOptions, token, progress,
                    prepared.Select(t => new PreparedStructureCandidate(t.Id, "M2:" + t.Operation, t.Endpoint!, t.AnchoredVariables, t.Work.FunctionEvaluations)).ToArray());
                total = Add(total, competition.Work);
                foreach (var candidate in competition.Competition!.Candidates)
                {
                    var index = trials.FindIndex(t => t.Id == candidate.Id); var trial = trials[index];
                    trials[index] = trial with
                    {
                        Endpoint = candidate.Topology,
                        OriginalAssessment = candidate.OriginalAssessment,
                        Outcome = candidate.Outcome,
                        Work = Add(trial.Work, candidate.Work)
                    };
                    if (candidate.OriginalAssessment is { } a) AddQualified(candidate.Id, trial.ParentId, candidate.Topology!, a);
                }
                if (competition.Termination != ParaxialSeedTermination.Completed) { termination = competition.Termination; reason = "Competition:" + competition.Competition.StopReason; }
                costs.AddRange(competition.Competition.SearchEvidence?.Costs ?? []);
            }
            else
            {
                var finalists = prepared.Take(finalCount).ToArray();
                for (var i = 0; i < finalists.Length; i++)
                {
                    token.ThrowIfCancellationRequested(); var trial = finalists[i];
                    if (total.FunctionEvaluations >= options.MaximumFunctionEvaluations || trial.Work.FunctionEvaluations >= options.MaximumEvaluationsPerStart)
                    { termination = ParaxialSeedTermination.BudgetExhausted; reason = "InsufficientOriginalFinalFunctionBudget"; break; }
                    var before = measured.RayCount; var timer = Stopwatch.StartNew();
                    using var budget = SequentialTraceMeasurement.BeginCpuBudget(Math.Max(1, (options.MaximumTracedRays - measured.RayCount) / (finalists.Length - i)));
                    var a = TopologyValidator.Validate(TopologyCompiler.Compile(trial.Endpoint!, plan.Constraints), plan.Targets, token);
                    var work = new AutonomousSearchWork
                    {
                        FunctionEvaluations = 1,
                        ValidationEvaluations = 1,
                        TracedRays = measured.RayCount - before,
                        GeometryProbes = a.GeometryProbes,
                        Elapsed = timer.Elapsed
                    };
                    total = Add(total, work); costs.Add(Cost(trial.Id, 3, "OriginalFinalValidation", work));
                    trials[trials.FindIndex(t => t.Id == trial.Id)] = trial with
                    {
                        OriginalAssessment = a,
                        Outcome = a.MeetsTargets ? "OriginalQualified" : a.Completed ? "OriginalUnqualified" : "OriginalValidationIncomplete",
                        Work = Add(trial.Work, work)
                    };
                    AddQualified(trial.Id, trial.ParentId, trial.Endpoint!, a);
                    if (!a.Completed) { termination = ParaxialSeedTermination.BudgetExhausted; reason = "OriginalValidationIncomplete"; }
                }
            }
        }
        catch (StopEvolution) { }
        catch (OperationCanceledException)
        { termination = cancellationToken.IsCancellationRequested ? ParaxialSeedTermination.Cancelled : ParaxialSeedTermination.TimeLimit; reason = termination.ToString(); }
        catch (Exception error) when (SequentialTraceBudgetExceededException.IsBudgetException(error))
        { termination = ParaxialSeedTermination.BudgetExhausted; reason = "GlobalRayBudgetExhausted"; }
        var qualified = archive.ToImmutableArray(); var pareto = Pareto(qualified);
        var selected = qualified.OrderBy(q => q.Complexity.Elements).ThenBy(q => q.Complexity.AsphericSurfaces)
            .ThenBy(q => q.Complexity.PolynomialTerms).ThenBy(q => q.WorstRmsMillimeters).Select(q => q.Topology)
            .Concat(Rank(trials.Where(t => IsWorkingSeedAssessment(t.OriginalAssessment))).Select(t => t.Endpoint!))
            .DistinctBy(TopologyDiversitySelection.PrescriptionIdentity).Take(options.RetainedSeeds).ToImmutableArray();
        return new()
        {
            Algorithm = "initial-structure-evolution-v1",
            ProtocolFingerprint = identity,
            SourcePlan = plan,
            Options = options,
            Termination = termination,
            Work = total with { TracedRays = measured.RayCount, Elapsed = watch.Elapsed },
            MatrixRequests = total.MatrixEvaluations,
            Seeds = selected,
            Competition = competition?.Competition,
            Evolution = new("initial-structure-evolution-v1", identity, reason, parents.ToImmutableArray(), trials.ToImmutableArray(), qualified, pareto, costs.ToImmutableArray())
            { FirstNewQualifiedElapsed = firstNewQualified }
        };

        void AddQualified(string id, string? parentId, DesignTopology topology, TopologyAssessment assessment)
        {
            if (!assessment.Completed || !assessment.MeetsTargets) return;
            if (parentId is not null && archive.Any(q => TopologyDiversitySelection.PrescriptionIdentity(q.Topology) == TopologyDiversitySelection.PrescriptionIdentity(topology))) return;
            archive.Add(new(id, parentId, topology, assessment, Complexity(topology),
                assessment.Fields.Max(f => f.RmsRadiusMillimeters!.Value), assessment.Fields.Max(f => f.MaximumRadiusMillimeters!.Value)));
            if (parentId is not null) firstNewQualified ??= watch.Elapsed;
        }
    }

    private static (bool Passed, AutonomousSearchWork Work) Geometry(CompiledTopology source, TopologyTargets targets, CancellationToken token)
    {
        var watch = Stopwatch.StartNew(); long probes = 0; var valid = true; var optic = source.CreateOptic();
        try
        {
            foreach (var row in TopologyEvaluationPlan.Geometry(source, targets, 64))
            {
                token.ThrowIfCancellationRequested(); probes++; var value = TopologyEvaluationPlan.FormalMeasure(optic, row);
                valid &= double.IsFinite(value) && (row.Upper ? value <= row.Limit : value >= row.Limit);
            }
        }
        catch (OperationCanceledException) { valid = false; }
        catch (Exception error) when (TopologyEvaluationPlan.DomainError(error)) { valid = false; }
        return (valid, new() { FunctionEvaluations = 1, GeometryProbes = probes, Elapsed = watch.Elapsed });
    }

    private static IOrderedEnumerable<StructureEvolutionTrial> Rank(IEnumerable<StructureEvolutionTrial> pool) =>
        pool.OrderByDescending(t => t.Screen?.Completed == true && t.Screen.GeometryPassed)
            .ThenByDescending(t => t.Screen?.MinimumValidFraction ?? -1).ThenByDescending(t => t.Screen?.BoundaryChecksPassed == true)
            .ThenBy(t => t.Screen?.QualityScore ?? double.PositiveInfinity).ThenBy(t => t.Endpoint!.Elements.Length).ThenBy(t => t.Id, StringComparer.Ordinal);

    internal static QualifiedStructureComplexity Complexity(DesignTopology topology)
    {
        var faces = topology.Elements.SelectMany(e => new[] { e.Front, e.Back }).ToArray();
        return new(topology.Elements.Length, faces.Count(f => f.Profile.Kind == TopologyProfileKind.EvenAsphere || f.Profile.Conic != 0), faces.Sum(f => f.Profile.Terms.Length));
    }
    internal static bool IsWorkingSeedAssessment(TopologyAssessment? assessment) => assessment is { Completed: true }
        && !assessment.Violations.Any(v => v.Code.StartsWith("geometry:", StringComparison.Ordinal)
            || v.Code is "focal-length" or "f-number" or "transition-element");
    internal static ImmutableArray<string> Pareto(ImmutableArray<QualifiedEvolutionDesign> pool) => pool.Where(a => !pool.Any(b => b.Id != a.Id
        && b.Complexity.Elements <= a.Complexity.Elements && b.Complexity.AsphericSurfaces <= a.Complexity.AsphericSurfaces
        && b.Complexity.PolynomialTerms <= a.Complexity.PolynomialTerms && b.WorstRmsMillimeters <= a.WorstRmsMillimeters
        && b.WorstMaximumMillimeters <= a.WorstMaximumMillimeters
        && (b.Complexity != a.Complexity || b.WorstRmsMillimeters < a.WorstRmsMillimeters || b.WorstMaximumMillimeters < a.WorstMaximumMillimeters)))
        .Select(q => q.Id).ToImmutableArray();
    private static StructurePhaseCost Cost(string id, int stage, string name, AutonomousSearchWork work) =>
        new(id, stage, name, work.Elapsed, work.FunctionEvaluations, work.GradientEvaluations, work.TracedRays, work.GeometryProbes, work.MatrixEvaluations);
    private static AutonomousSearchWork Add(AutonomousSearchWork a, AutonomousSearchWork b) => new()
    {
        FunctionEvaluations = a.FunctionEvaluations + b.FunctionEvaluations,
        GradientEvaluations = a.GradientEvaluations + b.GradientEvaluations,
        ValidationEvaluations = a.ValidationEvaluations + b.ValidationEvaluations,
        MatrixEvaluations = a.MatrixEvaluations + b.MatrixEvaluations,
        DifferenceEvaluations = a.DifferenceEvaluations + b.DifferenceEvaluations,
        JacobianBuilds = a.JacobianBuilds + b.JacobianBuilds,
        GeometryProbes = a.GeometryProbes + b.GeometryProbes,
        TracedRays = a.TracedRays + b.TracedRays,
        Elapsed = a.Elapsed + b.Elapsed
    };
}
