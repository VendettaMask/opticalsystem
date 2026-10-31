using System.Collections.Immutable;
using System.Diagnostics;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

internal sealed record PreparedStructureCandidate(string Id, string Source, DesignTopology Topology,
    ImmutableArray<TopologyVariableRequest> AnchoredVariables, int PreviousFunctions);

/// <summary>Versioned successive halving of native thick seeds. Only original-spec validation certifies a result.</summary>
internal static partial class StructureCompetitionSearch
{
    private sealed class LimitReached : Exception;
    private sealed class Candidate(string id, StructureRayPathFamily family, int partition)
    {
        internal readonly string Id = id;
        internal readonly StructureRayPathFamily Family = family;
        internal readonly int Partition = partition;
        internal DesignTopology? Initial, Topology;
        internal ImmutableArray<TopologyVariableRequest> Variables;
        internal StructureCompetitionScreen? Screen;
        internal TopologyAssessment? Assessment;
        internal AutonomousSearchWork Work = new();
        internal int CompletedStages;
        internal string Outcome = "ConstructionRejected";
        internal int[]? RequestedSigns;
        internal StructureCandidateEvidence? Evidence;
        internal int PreviousFunctions;
    }

    internal static void Validate(ParaxialSeedSearchOptions options)
    {
        var p = options.Competition!;
        if (p.Version != 1 || options.Evolution is null && options.Generation is not { Method: StructureGenerationMethod.Delano, MaterializationVersion: 3 }
            || options.ImageScreening is not null || p.Families.IsDefaultOrEmpty || p.Families.Length > 4
            || p.Families.Distinct().Count() != p.Families.Length || p.Families.Any(f => !Enum.IsDefined(f))
            || p.Stages.IsDefaultOrEmpty || p.Stages.Length > 6 || p.UpdatesPerStage is < 0 or > 40
            || p.MaximumGradientEvaluations is < 0 or > 1000 || p.MaximumFinalists is < 1 or > 8
            || !double.IsFinite(p.SurvivalFraction) || p.SurvivalFraction is <= 0 or >= 1
            || p.ScreeningRaysPerCandidate is < 1 or > 1_000_000
            || p.MaximumStageRaysPerCandidate <= p.ScreeningRaysPerCandidate || p.MaximumStageRaysPerCandidate > 10_000_000
            || p.SearchStrategy is null && (p.FinalValidationRayReserve < 1 || p.FinalValidationRayReserve > options.MaximumTracedRays))
            throw new ArgumentException("Competition v1 requires native v3 generation and explicit bounded stage/final budgets; separate image screening cannot be combined.");
        if (p.MaximumStageTimePerCandidate <= TimeSpan.Zero || p.MaximumStageTimePerCandidate > TimeSpan.FromMinutes(2)
            || !double.IsFinite(p.FinalValidationTimeReserveFraction) || p.FinalValidationTimeReserveFraction is < .05 or > .5)
            throw new ArgumentException("Competition stages require a bounded CPU time quota and an independent final time reserve.");
        double field = 0, pupil = 0;
        foreach (var stage in p.Stages)
        {
            if (string.IsNullOrWhiteSpace(stage.Name) || !double.IsFinite(stage.FieldFraction) || !double.IsFinite(stage.PupilFraction)
                || stage.FieldFraction < field || stage.FieldFraction is <= 0 or > 1
                || stage.PupilFraction < pupil || stage.PupilFraction is <= 0 or > 1)
                throw new ArgumentException("Competition stages must monotonically restore field and pupil to the original specification.");
            field = stage.FieldFraction; pupil = stage.PupilFraction;
        }
        if (field != 1 || pupil != 1) throw new ArgumentException("The last competition stage must use the original field and pupil.");
        if (p.SearchStrategy is { } strategy)
        {
            if (strategy.Version != 1 || strategy.ChallengeCandidatesPerRound is < 0 or > 2
                || !double.IsFinite(strategy.FinalRayRecoveryFactor) || strategy.FinalRayRecoveryFactor is < 1 or > 20
                || strategy.FinalTimePerCandidate <= TimeSpan.Zero || strategy.FinalTimePerCandidate > TimeSpan.FromMinutes(2)
                || strategy.PowerSigns is { } signs && (signs.Version != 1 || signs.MaximumPatterns is < 1 or > 256)
                || strategy.QuickEvaluation is { } quick && (quick.Version != 1 || quick.MaximumProposalsPerStage is < 0 or > 16
                    || !double.IsFinite(quick.CoordinateStep) || quick.CoordinateStep is <= 0 or > .1))
                throw new ArgumentException("Unsupported or unbounded experimental structure search strategy.");
        }
    }

    internal static (DesignTopology Topology, TopologyTargets Targets) StageSpecification(
        DesignTopology original, TopologyTargets targets, StructureCompetitionStage stage) =>
        (original with
        {
            Fields = original.Fields.Select(f => f with
            {
                XAngleDegrees = f.XAngleDegrees * stage.FieldFraction,
                YAngleDegrees = f.YAngleDegrees * stage.FieldFraction
            }).ToImmutableArray(),
            Stop = original.Stop with { ClearSemiDiameterMillimeters = original.Stop.ClearSemiDiameterMillimeters * stage.PupilFraction }
        }, targets with { FNumber = targets.FNumber / stage.PupilFraction });

    internal static double[] InitialShape(StructureRayPathFamily family, int elements) =>
        Enumerable.Range(0, 2 * elements - 1).Select(i =>
        {
            var t = (double)i / Math.Max(1, 2 * elements - 2);
            return family switch
            {
                StructureRayPathFamily.FrontBending => .1 + .8 * Math.Sqrt(t),
                StructureRayPathFamily.RearBending => .1 + .8 * t * t,
                StructureRayPathFamily.AlternatingBending => .1 + .8 * t + (i % 2 == 0 ? .05 : -.05),
                _ => .1 + .8 * t
            };
        }).ToArray();

    internal static ParaxialSeedSearchResult Run(AutonomousSearchPlan plan, ParaxialSeedSearchOptions options,
        CancellationToken cancellationToken, Action<int, int>? progress, IReadOnlyList<PreparedStructureCandidate>? prepared = null)
    {
        _ = AutonomousSearchService.Create("competition-preparation-check", plan);
        if (AutonomousSearchService.IsHistoricalReadOnlyPlan(plan))
            throw new NotSupportedException("Historical optimizer records cannot execute a new competition protocol.");
        var protocol = options.Competition!; var descriptor = options.Generation!;
        var strategy = protocol.SearchStrategy;
        var algorithm = prepared is not null ? "initial-structure-evolved-competition-v1"
            : strategy is null ? "initial-structure-competition-v1" : "initial-structure-competition-m1-v1";
        var fingerprint = ContentFingerprint.Compute(new
        {
            Identity = algorithm,
            plan,
            options,
            Training = plan.Options.Local.GateCompensationVersion switch
            {
                null => "Core/CPU/fields3/Gaussian1x4/boundary32/geometry64",
                2 => "Core/CPU/original-fields-pupil/Gaussian2x8-then10x32/boundary32/geometry64",
                _ => "Core/CPU/original-fields-pupil/Gaussian2x8-then4x16/boundary32/geometry64"
            },
            Screen = "Core/fields3/Gaussian1x4/boundary8/geometry64",
            Final = "unchanged-TopologyValidator-original-specification",
            Variables = "anchored-at-generated-candidate"
        });
        if (prepared is not null)
            fingerprint = ContentFingerprint.Compute(new
            {
                Identity = algorithm,
                plan,
                options,
                prepared,
                Final = "unchanged-TopologyValidator-original-specification",
                Variables = "anchored-before-recovery"
            });
        var timer = Stopwatch.StartNew();
        using var deadline = new CancellationTokenSource(options.TimeLimit);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var token = linked.Token;
        using var measured = SequentialTraceMeasurement.BeginCpuBudget(options.MaximumTracedRays);
        var random = new DeterministicRandom(plan.Options.Seed);
        var candidates = new List<Candidate>(); var rounds = new List<StructureCompetitionRound>();
        var attempts = new List<ParaxialSeedAttempt>();
        var total = new AutonomousSearchWork();
        var termination = ParaxialSeedTermination.Completed; var reason = "Completed";
        var finalCount = Math.Min(Math.Min(protocol.MaximumFinalists, options.RetainedSeeds), prepared?.Count ?? options.MaximumStarts);
        var costs = new List<StructurePhaseCost>(); var quickEvidence = new List<StructureQuickEvidence>();
        var challenges = new List<StructureChallengeEvidence>();
        var prechecks = strategy is null || prepared is not null ? [] : StructureSearchDiagnostics.Precheck(plan, descriptor);
        var finalEstimate = strategy is null ? 0 : StructureSearchDiagnostics.EstimateFinalRays(
            TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints), strategy.FinalRayRecoveryFactor);
        if (strategy is not null)
            finalCount = Math.Min(finalCount, (int)Math.Min(int.MaxValue, options.MaximumTracedRays / Math.Max(1, finalEstimate)));
        var finalReserve = strategy is null ? protocol.FinalValidationRayReserve : finalEstimate * finalCount;
        var reservedTime = strategy is null ? options.TimeLimit * protocol.FinalValidationTimeReserveFraction
            : strategy.FinalTimePerCandidate * finalCount;
        var startCount = Math.Min(options.MaximumStarts, prepared?.Count ?? options.MaximumStarts);
        if (strategy is not null)
        {
            startCount = Math.Min(startCount, options.MaximumFunctionEvaluations / (3 + protocol.Stages.Length * 2));
            startCount = Math.Min(startCount, (int)Math.Min(int.MaxValue,
                Math.Max(0, options.MaximumTracedRays - finalReserve) / ((protocol.ScreeningRaysPerCandidate + 1) * protocol.Stages.Length)));
        }
        try
        {
            using var cpu = new SequentialGpuSession(DesignComputeMode.Cpu, token);
            if (strategy is not null && (finalCount == 0 || startCount == 0 || reservedTime >= options.TimeLimit))
            { termination = ParaxialSeedTermination.BudgetExhausted; reason = "InsufficientOriginalValidationReserve"; throw new StrategyStop(); }
            if (prechecks.Any(p => p.ProvenInfeasible))
            { reason = "ProvenInfeasible:MinimumAxialSpace"; throw new StrategyStop(); }
            if (prepared is not null)
            {
                foreach (var seed in prepared.Take(startCount))
                {
                    token.ThrowIfCancellationRequested();
                    var source = TopologyCompiler.Compile(seed.Topology, plan.Constraints);
                    _ = TopologyParameterBinder.Bind(source, seed.AnchoredVariables, exactMechanicalFrame: plan.Options.Local.GateCompensationVersion == 2);
                    candidates.Add(new(seed.Id, protocol.Families[candidates.Count % protocol.Families.Length],
                        seed.Topology.Elements.Count(e => e.BackZMillimeters < seed.Topology.Stop.ZMillimeters))
                    {
                        Initial = seed.Topology,
                        Topology = seed.Topology,
                        Variables = seed.AnchoredVariables,
                        PreviousFunctions = seed.PreviousFunctions,
                        Outcome = "PreparedForCompetition",
                        Evidence = new(seed.Source, [], [], null)
                    });
                }
            }
            else
            {
                var partitions = descriptor.FeasibleLayouts!.StopAfterElementCandidates.IsEmpty
                    ? [descriptor.StopAfterElement] : descriptor.FeasibleLayouts.StopAfterElementCandidates.ToArray();
                // Eliminate only partitions with insufficient axial space even at the minimum thickness.
                var envelope = plan.Seeds[0]; var margin = Math.Max(.05, plan.Constraints.MinimumAirGapMillimeters);
                var back = Math.Max(descriptor.MinimumBackFocusMillimeters, plan.Constraints.MinimumBackFocusMillimeters);
                partitions = partitions.Where(n => (n == 0 || Math.Min(envelope.Stop.ZMillimeters - margin, envelope.Image.ZMillimeters - back)
                        - envelope.Reference.ZMillimeters > n * (descriptor.MinimumThicknessMillimeters + margin))
                    && (n == descriptor.Elements.Length || envelope.Image.ZMillimeters - back - envelope.Stop.ZMillimeters - margin
                        > (descriptor.Elements.Length - n) * (descriptor.MinimumThicknessMillimeters + margin))).ToArray();
                if (partitions.Length == 0) reason = "NoPermittedPartitionFits";
                var generationQuota = Math.Min(options.MaximumEvaluationsPerStart - 1, Math.Max(2,
                    (options.MaximumFunctionEvaluations - finalCount - startCount * protocol.Stages.Length * 2) / Math.Max(1, startCount)));
                for (var index = 0; index < startCount && partitions.Length > 0; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var candidate = new Candidate($"candidate-{index + 1:D3}", protocol.Families[index % protocol.Families.Length],
                        partitions[(index % protocol.Families.Length + index / protocol.Families.Length) % partitions.Length]);
                    candidates.Add(candidate);
                    if (strategy?.PowerSigns is { } signs && (!signs.RetainNativeProposals || index % 2 == 1))
                    {
                        var patterns = PowerSignPatterns(descriptor.Elements.Length, signs.MaximumPatterns);
                        candidate.RequestedSigns = patterns[(signs.RetainNativeProposals ? index / 2 : index) % patterns.Length].ToArray();
                    }
                    var construction = new StructureGenerationWork(); var calls = 0;
                    var constructionStarted = Stopwatch.GetTimestamp();
                    try
                    {
                        candidate.Topology = candidate.Initial = ThickStructureSeedGenerator.Create(plan,
                            descriptor with
                            {
                                StopAfterElement = candidate.Partition,
                                AnnealingProposals = Math.Min(descriptor.AnnealingProposals, Math.Max(0, generationQuota - 2))
                            }, ref random, () =>
                            {
                                token.ThrowIfCancellationRequested();
                                if (calls >= generationQuota || total.FunctionEvaluations + calls >= options.MaximumFunctionEvaluations - finalCount)
                                    throw new LimitReached();
                                calls++;
                            }, construction, token, requireFeasibleGeometry: true,
                            candidate.RequestedSigns is null ? InitialShape(candidate.Family, descriptor.Elements.Length)
                                : InitialShape(candidate.Family, descriptor.Elements.Length).Zip(FixedStopDelano.ProposePowerSignShape(candidate.RequestedSigns),
                                    (native, signed) => .5 * native + .5 * signed).ToArray(), candidate.RequestedSigns, strategy is not null);
                        if (strategy is not null)
                        {
                            candidate.Evidence = SourceEvidence(TopologyCompiler.Compile(candidate.Topology, plan.Constraints), candidate.RequestedSigns);
                            construction.MatrixRequests += candidate.Topology.Elements.Length;
                        }
                        candidate.Variables = ParaxialSeedSearch.Variables(candidate.Initial, plan.Options, true);
                        candidate.Outcome = "Constructed";
                    }
                    catch (Exception error) when (TopologyEvaluationPlan.DomainError(error) && !SequentialTraceBudgetExceededException.IsBudgetException(error))
                    { candidate.Outcome = (candidate.RequestedSigns is null ? "ConstructionRejected:" : "PowerSignConstructionRejected:") + error.GetType().Name; }
                    catch (LimitReached) { candidate.Outcome = "ConstructionBudgetExhausted"; }
                    finally
                    {
                        if (strategy is not null)
                        {
                            candidate.Evidence ??= new(candidate.RequestedSigns is null ? "NativeThickDelano" : "PowerSignThickDelano",
                                candidate.RequestedSigns?.ToImmutableArray() ?? [], [], candidate.RequestedSigns is null ? null : false);
                            costs.Add(new(candidate.Id, -1, "Construction", TimeSpan.FromTicks(Math.Max(0,
                                Stopwatch.GetElapsedTime(constructionStarted).Ticks - construction.MatrixTime.Ticks - construction.GeometryTime.Ticks)), calls, 0, 0, 0, 0));
                            costs.Add(new(candidate.Id, -1, "Paraxial", construction.MatrixTime, 0, 0, 0, 0, construction.MatrixRequests));
                            costs.Add(new(candidate.Id, -1, "Geometry", construction.GeometryTime, 0, 0, 0, construction.GeometryProbes, 0));
                        }
                        Add(candidate, new()
                        {
                            FunctionEvaluations = calls,
                            MatrixEvaluations = construction.MatrixRequests,
                            GeometryProbes = construction.GeometryProbes,
                            Elapsed = strategy is null ? TimeSpan.Zero : Stopwatch.GetElapsedTime(constructionStarted)
                        });
                        attempts.Add(new(index + 1, ContentFingerprint.Compute(plan.Seeds[0]), candidate.Outcome, calls, null, candidate.Topology)
                        {
                            Construction = new(1, candidate.Topology is null ? 0 : 1, construction.RejectedGeometryProposals,
                            candidate.Topology is null ? null : candidate.Partition, construction.MinimumClearanceMarginMillimeters)
                        });
                        progress?.Invoke(index + 1, total.FunctionEvaluations);
                    }
                    if (total.FunctionEvaluations >= options.MaximumFunctionEvaluations - finalCount)
                    { termination = ParaxialSeedTermination.BudgetExhausted; reason = "ConstructionFunctionBudget"; break; }
                }
            }
            var active = candidates.Where(c => c.Topology is not null).ToList();
            if (active.Count == 0 && reason == "Completed") reason = "NoFeasibleCandidate";
            for (var stageIndex = 0; stageIndex < protocol.Stages.Length && active.Count > 0; stageIndex++)
            {
                token.ThrowIfCancellationRequested();
                var remainingStages = protocol.Stages.Length - stageIndex;
                var functionQuota = Math.Min(2 + protocol.UpdatesPerStage,
                    (options.MaximumFunctionEvaluations - total.FunctionEvaluations - finalCount) / (active.Count * remainingStages));
                functionQuota = Math.Min(functionQuota, active.Min(c => (options.MaximumEvaluationsPerStart - c.PreviousFunctions - c.Work.FunctionEvaluations - 1) / remainingStages));
                var rayQuota = Math.Min(protocol.MaximumStageRaysPerCandidate,
                    Math.Max(0, options.MaximumTracedRays - measured.RayCount - finalReserve) / (active.Count * remainingStages));
                var gradientQuota = Math.Min(protocol.UpdatesPerStage + 1,
                    (protocol.MaximumGradientEvaluations - total.GradientEvaluations) / (active.Count * remainingStages));
                var timeQuota = TimeSpan.FromSeconds(Math.Min(protocol.MaximumStageTimePerCandidate.TotalSeconds,
                    Math.Max(0, options.TimeLimit.TotalSeconds - reservedTime.TotalSeconds - timer.Elapsed.TotalSeconds)
                        / (active.Count * remainingStages)));
                if (functionQuota < 2 || rayQuota <= protocol.ScreeningRaysPerCandidate || gradientQuota < 1 || timeQuota < TimeSpan.FromMilliseconds(10))
                { termination = ParaxialSeedTermination.BudgetExhausted; reason = "InsufficientCompleteRoundBudget"; break; }
                var stage = plan.Options.Local.GateCompensationVersion is null ? protocol.Stages[stageIndex]
                    : protocol.Stages[stageIndex] with { FieldFraction = 1, PupilFraction = 1 };
                var roundStart = rounds.Count;
                var allocation = new StructureCompetitionAllocation(functionQuota, gradientQuota, rayQuota,
                    protocol.ScreeningRaysPerCandidate, TimeSpan.FromTicks(Math.Max(1, (long)(timeQuota.Ticks * .8))))
                { MaximumStageTime = strategy is null ? null : timeQuota };
                foreach (var candidate in active)
                {
                    token.ThrowIfCancellationRequested();
                    var beforeRays = measured.RayCount;
                    var staged = StageSpecification(candidate.Topology!, plan.Targets, stage);
                    FixedTopologyResult? optimized = null;
                    var stageWork = new AutonomousSearchWork();
                    var optimizeStarted = Stopwatch.GetTimestamp();
                    using var stageDeadline = strategy is null ? null : CancellationTokenSource.CreateLinkedTokenSource(token);
                    stageDeadline?.CancelAfter(timeQuota);
                    var stageToken = stageDeadline?.Token ?? token;
                    try
                    {
                        if (plan.Options.Local.GateCompensationVersion is null && strategy?.QuickEvaluation is { } quick && stageIndex < protocol.Stages.Length - 1)
                        {
                            var fast = QuickOptimize(candidate, plan, stage, stageIndex, quick, functionQuota - 1,
                                rayQuota - protocol.ScreeningRaysPerCandidate, allocation.MaximumOptimizationTime, ref random, stageToken);
                            candidate.Topology = fast.Topology; stageWork = fast.Work; quickEvidence.Add(fast.Evidence);
                            if (!fast.Evidence.Outcome.StartsWith("Completed", StringComparison.Ordinal)
                                && fast.Evidence.InitialScore is null)
                                candidate.Outcome = "QuickUnsupportedRoutesToRealRays";
                        }
                        else
                        {
                            using var budget = SequentialTraceMeasurement.BeginCpuBudget(rayQuota - protocol.ScreeningRaysPerCandidate);
                            var source = TopologyCompiler.Compile(staged.Topology, plan.Constraints);
                            // Bounds stay anchored to the generated structure, never re-centered after each phase.
                            var binding = TopologyParameterBinder.Bind(source, candidate.Variables, exactMechanicalFrame: plan.Options.Local.GateCompensationVersion == 2);
                            var policy = stageIndex == protocol.Stages.Length - 1 ? plan.Options.Full : plan.Options.Local;
                            optimized = FixedTopologyOptimizer.Run(source, binding, staged.Targets, policy with
                            {
                                MaximumUpdates = Math.Min(protocol.UpdatesPerStage, functionQuota - 2),
                                MaximumFunctionEvaluations = functionQuota - 1,
                                MaximumGradientEvaluations = gradientQuota,
                                TimeLimit = allocation.MaximumOptimizationTime,
                                ComputeMode = DesignComputeMode.Cpu,
                                PerformFinalAssessment = false,
                                Sampling = new()
                                {
                                    FieldsPerDirection = 3,
                                    PupilRings = policy.GateCompensationVersion is null ? 1 : stageIndex == protocol.Stages.Length - 1 ? policy.GateCompensationVersion == 2 ? 10 : 4 : 2,
                                    PupilArms = policy.GateCompensationVersion is null ? 4 : stageIndex == protocol.Stages.Length - 1 ? policy.GateCompensationVersion == 2 ? 32 : 16 : 8,
                                    ProfileRadialSamples = 64,
                                    IncludeBoundaryClearance = true,
                                    IncludeBoundaryMaximum = true
                                }
                            }, stageToken);
                            candidate.Topology = optimized.Topology with { Fields = candidate.Initial!.Fields, Stop = candidate.Initial.Stop };
                            stageWork = new()
                            {
                                FunctionEvaluations = optimized.Work.FunctionEvaluations,
                                GradientEvaluations = optimized.Work.GradientEvaluations,
                                GeometryProbes = optimized.Work.GeometryProbes
                            };
                        }
                    }
                    catch (Exception error) when (TopologyEvaluationPlan.DomainError(error) && !SequentialTraceBudgetExceededException.IsBudgetException(error))
                    { candidate.Outcome = "StageRejected:" + error.GetType().Name; }
                    if (strategy is not null) costs.Add(new(candidate.Id, stageIndex,
                        optimized is null ? "QuickAberration" : "RealRayGradientOptimization", Stopwatch.GetElapsedTime(optimizeStarted),
                        stageWork.FunctionEvaluations, stageWork.GradientEvaluations, measured.RayCount - beforeRays, stageWork.GeometryProbes, stageWork.MatrixEvaluations));
                    // Always screen the accepted endpoint, including stalled or zero-throughput endpoints.
                    var screenStarted = Stopwatch.GetTimestamp(); var screenBeforeRays = measured.RayCount;
                    var endpoint = StageSpecification(candidate.Topology!, plan.Targets, stage);
                    using var screenDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                    screenDeadline.CancelAfter(TimeSpan.FromSeconds(Math.Max(0, Math.Min(timeQuota.TotalSeconds,
                        (options.TimeLimit - timer.Elapsed - reservedTime).TotalSeconds))));
                    candidate.Screen = Screen(TopologyCompiler.Compile(endpoint.Topology, plan.Constraints), endpoint.Targets,
                        protocol.ScreeningRaysPerCandidate, plan.Options.Local.GateCompensationVersion == 2 ? screenDeadline.Token : stageToken);
                    if (stageDeadline?.IsCancellationRequested == true && !token.IsCancellationRequested && !candidate.Screen.Completed)
                        candidate.Screen = candidate.Screen with { Outcome = "StageTimeLimit" };
                    if (strategy is not null) costs.Add(new(candidate.Id, stageIndex, "SparseRealRays", Stopwatch.GetElapsedTime(screenStarted),
                        1, 0, measured.RayCount - screenBeforeRays, candidate.Screen.GeometryProbes, 0));
                    stageWork = stageWork with
                    {
                        FunctionEvaluations = stageWork.FunctionEvaluations + 1,
                        GeometryProbes = stageWork.GeometryProbes + candidate.Screen.GeometryProbes,
                        TracedRays = measured.RayCount - beforeRays,
                        Elapsed = strategy is null ? TimeSpan.Zero : Stopwatch.GetElapsedTime(optimizeStarted)
                    };
                    Add(candidate, stageWork);
                    if (candidate.Screen.Completed) candidate.CompletedStages++;
                    candidate.Outcome = candidate.Screen.Outcome;
                    var evidence = optimized is null ? null : new StructureCompetitionOptimization(optimized.Algorithm,
                        optimized.ObjectiveFingerprint, optimized.Termination, optimized.AcceptedUpdates, optimized.Work,
                        optimized.InitialEvaluation?.Objective, optimized.EndpointEvaluation?.Objective,
                        optimized.EndpointEvaluation?.Available == true, optimized.Topology, optimized.Targets,
                        optimized.Variables, optimized.Steps, optimized.Diagnostics);
                    rounds.Add(new(stageIndex, stage, candidate.Id, candidate.Screen, false, evidence, stageWork, allocation));
                    progress?.Invoke(candidates.Count, total.FunctionEvaluations);
                    token.ThrowIfCancellationRequested();
                }
                var keep = stageIndex == protocol.Stages.Length - 1 ? finalCount
                    : Math.Max(finalCount, Math.Min(active.Count - 1, (int)Math.Ceiling(active.Count * protocol.SurvivalFraction)));
                var survivors = Select(active, keep, plan.Targets);
                if (strategy is not null)
                {
                    var challengers = active.Except(survivors).Where(c => c.Screen is { Completed: true, GeometryPassed: true })
                        .OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
                    var challengeCount = Math.Min(strategy.ChallengeCandidatesPerRound, Math.Min(challengers.Length, survivors.Count - 1));
                    for (var challenge = 0; challenge < challengeCount; challenge++)
                    {
                        var challenger = challengers[(stageIndex + challenge) % challengers.Length];
                        var replaceAt = survivors.Count - 1 - challenge;
                        var replaced = survivors[replaceAt]; survivors[replaceAt] = challenger;
                        challenges.Add(new(stageIndex, challenger.Id, replaced.Id, challenger.Screen!.QualityScore,
                            stageIndex == protocol.Stages.Length - 1 ? "OriginalValidationFalseNegativeAudit" : "NextStagePotentialAudit"));
                    }
                }
                foreach (var eliminated in active.Except(survivors)) eliminated.Outcome = "EliminatedAfterStage";
                var ids = survivors.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
                for (var i = roundStart; i < rounds.Count; i++) rounds[i] = rounds[i] with { Selected = ids.Contains(rounds[i].CandidateId) };
                active = survivors;
            }
            var finalists = Select(active, finalCount, plan.Targets);
            for (var i = 0; i < finalists.Count; i++)
            {
                token.ThrowIfCancellationRequested(); var candidate = finalists[i];
                if (total.FunctionEvaluations >= options.MaximumFunctionEvaluations || candidate.PreviousFunctions + candidate.Work.FunctionEvaluations >= options.MaximumEvaluationsPerStart)
                { termination = ParaxialSeedTermination.BudgetExhausted; reason = "InsufficientFinalFunctionBudget"; break; }
                var allowance = (options.MaximumTracedRays - measured.RayCount) / (finalists.Count - i);
                if (allowance < 1) { termination = ParaxialSeedTermination.BudgetExhausted; reason = "InsufficientFinalRayBudget"; break; }
                using var finalBudget = SequentialTraceMeasurement.BeginCpuBudget(allowance);
                var finalStarted = Stopwatch.GetTimestamp();
                using var finalTime = CancellationTokenSource.CreateLinkedTokenSource(token);
                finalTime.CancelAfter(TimeSpan.FromTicks(Math.Max(1, (options.TimeLimit - timer.Elapsed).Ticks / (finalists.Count - i))));
                candidate.Assessment = TopologyValidator.Validate(TopologyCompiler.Compile(candidate.Topology!, plan.Constraints), plan.Targets, finalTime.Token);
                if (candidate.Assessment is { Completed: true, MeetsTargets: true }) StructureCompetitionObservation.RecordQualified(timer.Elapsed);
                if (strategy is not null) costs.Add(new(candidate.Id, protocol.Stages.Length, "OriginalValidation", Stopwatch.GetElapsedTime(finalStarted),
                    1, 0, finalBudget.RayCount, candidate.Assessment.GeometryProbes, 0));
                Add(candidate, new()
                {
                    FunctionEvaluations = 1,
                    ValidationEvaluations = 1,
                    TracedRays = finalBudget.RayCount,
                    GeometryProbes = candidate.Assessment.GeometryProbes,
                    Elapsed = strategy is null ? TimeSpan.Zero : Stopwatch.GetElapsedTime(finalStarted)
                });
                candidate.Outcome = candidate.Assessment.MeetsTargets ? "OriginalQualified"
                    : candidate.Assessment.Completed ? "OriginalUnqualified" : "OriginalValidationIncomplete";
                if (!candidate.Assessment.Completed && !token.IsCancellationRequested)
                {
                    termination = finalTime.IsCancellationRequested ? ParaxialSeedTermination.TimeLimit : ParaxialSeedTermination.BudgetExhausted;
                    reason = finalTime.IsCancellationRequested ? "OriginalValidationTimeLimit" : "OriginalValidationIncomplete";
                }
            }
            token.ThrowIfCancellationRequested();
        }
        catch (StrategyStop) { }
        catch (OperationCanceledException)
        {
            termination = cancellationToken.IsCancellationRequested ? ParaxialSeedTermination.Cancelled : ParaxialSeedTermination.TimeLimit;
            reason = termination.ToString();
        }
        catch (LimitReached) { termination = ParaxialSeedTermination.BudgetExhausted; reason = "ConstructionFunctionBudget"; }
        catch (Exception error) when (SequentialTraceBudgetExceededException.IsBudgetException(error))
        { termination = ParaxialSeedTermination.BudgetExhausted; reason = "RayBudgetExhausted"; }
        var selected = candidates.Where(c => c.Assessment is { Completed: true } a
                && !a.Violations.Any(v => v.Code.StartsWith("geometry:", StringComparison.Ordinal) || v.Code is "focal-length" or "f-number" or "transition-element"))
            .OrderByDescending(c => c.Assessment!.MeetsTargets).ThenBy(c => c.Screen?.QualityScore ?? double.PositiveInfinity)
            .Take(options.RetainedSeeds).ToArray();
        return new()
        {
            Algorithm = algorithm,
            ProtocolFingerprint = fingerprint,
            SourcePlan = plan,
            Options = options,
            Termination = termination,
            Work = total with { TracedRays = measured.RayCount, Elapsed = timer.Elapsed },
            MatrixRequests = total.MatrixEvaluations,
            DiagramEvaluations = total.FunctionEvaluations - total.ValidationEvaluations - rounds.Sum(r => r.Work.FunctionEvaluations),
            Attempts = attempts.ToImmutableArray(),
            Seeds = selected.Select(c => c.Topology!).ToImmutableArray(),
            Competition = new()
            {
                Algorithm = algorithm,
                ProtocolFingerprint = fingerprint,
                StopReason = reason,
                Candidates = candidates.Select(c => new StructureCompetitionCandidate(c.Id, c.Family, c.Partition, c.Outcome,
                    c.Topology, c.CompletedStages, c.Assessment, c.Work)
                { SearchEvidence = c.Evidence }).ToImmutableArray(),
                Rounds = rounds.ToImmutableArray(),
                SelectedIds = selected.Select(c => c.Id).ToImmutableArray(),
                QualifiedIds = candidates.Where(c => c.Assessment is { Completed: true, MeetsTargets: true }).Select(c => c.Id).ToImmutableArray(),
                SearchEvidence = strategy is null ? null : new(prechecks, costs.ToImmutableArray(), quickEvidence.ToImmutableArray(), challenges.ToImmutableArray(),
                    finalEstimate, finalReserve, reservedTime, "Original fields21/Gaussian10x32/checks32/positive wavelengths; configurable aiming recovery factor; actual shared CPU charges authoritative")
            }
        };
        void Add(Candidate candidate, AutonomousSearchWork work)
        { candidate.Work = Sum(candidate.Work, work); total = Sum(total, work); }
    }
    private sealed class StrategyStop : Exception;

    private static AutonomousSearchWork Sum(AutonomousSearchWork a, AutonomousSearchWork b) => new()
    {
        FunctionEvaluations = a.FunctionEvaluations + b.FunctionEvaluations,
        GradientEvaluations = a.GradientEvaluations + b.GradientEvaluations,
        ValidationEvaluations = a.ValidationEvaluations + b.ValidationEvaluations,
        MatrixEvaluations = a.MatrixEvaluations + b.MatrixEvaluations,
        GeometryProbes = a.GeometryProbes + b.GeometryProbes,
        TracedRays = a.TracedRays + b.TracedRays,
        Elapsed = a.Elapsed + b.Elapsed
    };

    private static List<Candidate> Select(List<Candidate> pool, int count, TopologyTargets targets)
    {
        var remaining = pool.OrderByDescending(c => c.Screen?.Completed == true && c.Screen.GeometryPassed)
            .ThenByDescending(c => c.Screen?.MinimumValidFraction ?? -1).ThenByDescending(c => c.Screen?.BoundaryChecksPassed == true)
            .ThenBy(c => c.Screen?.QualityScore ?? double.PositiveInfinity).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        var selected = new List<Candidate>();
        while (remaining.Count > 0 && selected.Count < count)
        {
            var best = remaining[0]; var score = best.Screen?.QualityScore;
            var near = remaining.Where(c => c.Screen?.Completed == best.Screen?.Completed && c.Screen?.GeometryPassed == best.Screen?.GeometryPassed
                && (c.Screen?.MinimumValidFraction ?? -1) >= (best.Screen?.MinimumValidFraction ?? -1) - .001
                && c.Screen?.BoundaryChecksPassed == best.Screen?.BoundaryChecksPassed
                && (c.Screen?.QualityScore ?? double.PositiveInfinity) <= (score is { } q ? q + Math.Max(1e-12, .25 * q) : double.PositiveInfinity));
            var chosen = selected.Count == 0 ? best : near.OrderByDescending(c => selected.All(s => s.Family != c.Family))
                .ThenByDescending(c => selected.Min(s => TopologyDiversitySelection.Distance(c.Topology!, s.Topology!, targets)))
                .ThenBy(c => c.Id, StringComparer.Ordinal).First();
            selected.Add(chosen); remaining.Remove(chosen);
        }
        return selected;
    }
}
