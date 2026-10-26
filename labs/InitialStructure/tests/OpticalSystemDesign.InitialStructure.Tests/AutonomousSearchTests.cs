using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;
using Xunit.Abstractions;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class AutonomousSearchTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    [Fact]
    public async Task CpuQuotaReservesParallelSubmissionsAndAllowsCacheHitsWithoutOvershoot()
    {
        var source = TopologyCompiler.Compile(Source());
        using var quota = SequentialTraceMeasurement.BeginCpuBudget(3);
        var counts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            using var inner = SequentialTraceMeasurement.Begin();
            try { source.CreateOptic().SequentialRayTracer.TraceGenericFinalSample(0, 0, 0, 0, .5876); }
            catch (SequentialTraceBudgetExceededException) { }
            return inner.RayCount;
        })));
        Assert.Equal(3, counts.Sum()); Assert.Equal(3, quota.RayCount); Assert.True(quota.LimitReached);
        Assert.Throws<NotSupportedException>(() => new SequentialGpuSession(DesignComputeMode.Automatic));
    }

    [Fact]
    public void CacheHitNeedsNoSecondReservationAndFailedPhysicalRayStillCostsWork()
    {
        var optic = TopologyCompiler.Compile(Source()).CreateOptic();
        optic.ConfigureRayTraceCache(new RayTraceCache(maximumEntries: 4, maximumSamples: 1000), 1);
        var bundle = new RealRayBundle([new(new(100, 100, -1), new(0, 0, 1), 587.6)]);
        using var quota = SequentialTraceMeasurement.BeginCpuBudget(1);
        using var first = optic.SequentialRayTracer.Trace(bundle, TraceRequest.FinalOnly(false));
        Assert.True(first.GetSurfaceSamples(optic.SurfaceGroup.Items.Count - 1)[0] is null or { Vignetted: true });
        using var cached = optic.SequentialRayTracer.Trace(bundle, TraceRequest.FinalOnly(false));
        Assert.Equal(1, quota.RayCount);
        Assert.False(quota.LimitReached);
        Assert.Throws<SequentialTraceBudgetExceededException>(() => optic.SequentialRayTracer.TraceGenericFinalSample(0, 0, 0, 0, .5876));
        Assert.Equal(1, quota.RayCount);
    }

    [Fact]
    public void AdamCancellationContinuesTheSameMomentsAndRejectsChangedPhaseBeforeTracing()
    {
        var source = TopologyCompiler.Compile(Source());
        var variables = Curvatures(source.Topology);
        var binding = TopologyParameterBinder.Bind(source, variables);
        var options = Plan().Options.Full with { MaximumUpdates = 3, MaximumFunctionEvaluations = 5, MaximumGradientEvaluations = 5 };
        var full = FixedTopologyOptimizer.Run(source, binding, new(), options);
        using var stop = new CancellationTokenSource();
        var part = FixedTopologyOptimizer.Run(source, binding, new(), options, stop.Token, progress: _ => stop.Cancel());
        Assert.Equal(FixedTopologyTermination.Cancelled, part.Termination);
        Assert.Single(part.Steps); Assert.Equal(1, part.Continuation!.Adam.MomentIteration);
        var restored = JsonSerializer.Deserialize<FixedTopologyContinuation>(JsonSerializer.Serialize(part.Continuation, Json), Json)!;
        var resumed = FixedTopologyOptimizer.Run(source, binding, new(), options, continuation: restored);
        Assert.Equal(full.Continuation!.Adam.Coordinates.ToArray(), resumed.Continuation!.Adam.Coordinates.ToArray());
        Assert.Equal(full.Continuation.Adam.FirstMoment.ToArray(), resumed.Continuation.Adam.FirstMoment.ToArray());
        Assert.Equal(full.Continuation.Adam.SecondMoment.ToArray(), resumed.Continuation.Adam.SecondMoment.ToArray());
        Assert.Equal(full.Work.FunctionEvaluations, resumed.Work.FunctionEvaluations);
        Assert.Equal(full.AcceptedUpdates, resumed.AcceptedUpdates);
        using var measurement = SequentialTraceMeasurement.Begin();
        Assert.Throws<TopologyValidationException>(() => FixedTopologyOptimizer.Run(source, binding, new(), options with { LearningRate = .03 }, continuation: restored));
        Assert.Equal(0, measurement.RayCount);
        Evidence("adam-resume", new { full, part, resumed });
    }

    [Fact]
    public void FiniteLoopAutomaticallyOptimizesInsertsRecoversAndIndependentlyAssesses()
    {
        var result = AutonomousSearchService.Run(AutonomousSearchService.Create("m4-finite", Plan()));
        Evidence("finite-search", result);
        Assert.Equal(AutonomousSearchStatus.Completed, result.Status);
        Assert.Equal(4, result.TrialCursor);
        Assert.Contains(result.Events, item => item.Operation == "optimize" && item.Phase == AutonomousSearchPhase.Full);
        var recovery = Assert.Single(result.Events.Where(item => item.Operation == "insert" && item.Recovery is not null));
        Assert.Contains(result.Events, item => item.Operation == "insert" && item.Outcome == "EditRejected");
        Assert.Equal(4, recovery.Endpoint!.Elements.Length);
        Assert.Equal("Recovered", recovery.Outcome);
        Assert.Contains(result.Events, item => item.Operation == "insert" && item.Phase == AutonomousSearchPhase.Local);
        Assert.Contains(result.Events, item => item.Operation == "insert" && item.Phase == AutonomousSearchPhase.Full);
        Assert.Contains(result.Events, item => item.Operation == "insert" && item.Phase == AutonomousSearchPhase.Validation);
        Assert.Empty(result.AcceptedArchive); // Deliberately strict RMS fixture, no claim of original-task success.
        Assert.All(result.Pool.GroupBy(id => result.Candidates.Single(candidate => candidate.Id == id).Topology.Elements.Length),
            group => Assert.InRange(group.Count(), 1, result.Plan.Options.CandidatesPerElementCount));
        AssertLedger(result);
        Evidence("finite-search", result);
        output.WriteLine($"finite search: trials={result.TrialCursor}, functions={result.Consumed.FunctionEvaluations}, gradients={result.Consumed.GradientEvaluations}, rays={result.Consumed.TracedRays}, accepted={result.AcceptedArchive.Length}");
    }

    [Fact]
    public async Task SearchPersistsMidPhaseAdamAndResumesWithoutRepeatingItsInitialEvaluation()
    {
        var plan = Plan();
        plan = plan with
        {
            Options = plan.Options with
            {
                MaximumTrials = 1,
                Full = plan.Options.Full with { MaximumUpdates = 3, MaximumFunctionEvaluations = 5, MaximumGradientEvaluations = 5 }
            }
        };
        var initial = AutonomousSearchService.Create("m4-mid-phase", plan);
        var full = AutonomousSearchService.Run(initial);
        var path = Path.Combine(Path.GetTempPath(), "m4-mid-phase-" + Guid.NewGuid().ToString("N"), "checkpoint.json");
        var store = new AutonomousSearchCheckpointStore();
        using var stop = new CancellationTokenSource();
        var part = AutonomousSearchService.Run(initial,
            state => store.SaveAsync(state, path, state.Revision - 1).AsTask().GetAwaiter().GetResult(), stop.Token,
            (_, step) => { if (step.Accepted) stop.Cancel(); });
        Assert.Equal(AutonomousSearchStatus.Cancelled, part.Status);
        Assert.NotNull(part.Pending!.Continuation);
        Assert.Equal(1, part.Pending.Continuation.Adam.MomentIteration);
        var loaded = await store.LoadAsync(path);
        var resumed = AutonomousSearchService.Run(loaded);
        Assert.Equal(ContentFingerprint.Compute(full.Candidates[^1].Topology), ContentFingerprint.Compute(resumed.Candidates[^1].Topology));
        Assert.Equal(full.Consumed.FunctionEvaluations, resumed.Consumed.FunctionEvaluations);
        Assert.Equal(full.Consumed.GradientEvaluations, resumed.Consumed.GradientEvaluations);
        Assert.Equal(full.Consumed.TracedRays, resumed.Consumed.TracedRays);
        AssertLedger(resumed);
        Evidence("search-mid-phase-resume", new { full, part, resumed });
    }

    [Fact]
    public async Task DurableCancelResumePreservesAcceptedArchiveAndRejectsStaleOrCorruptWrites()
    {
        var (cancelled, path) = await AcceptedCheckpoint("m4-archive");
        var store = new AutonomousSearchCheckpointStore();
        var loaded = await store.LoadAsync(path);
        Assert.Equal(ContentFingerprint.Compute(cancelled), ContentFingerprint.Compute(loaded));
        Assert.Equal(AutonomousSearchStatus.Cancelled, loaded.Status);
        var accepted = Assert.Single(loaded.AcceptedArchive);
        var result = AutonomousSearchService.Run(loaded, state => store.SaveAsync(state, path, state.Revision - 1).AsTask().GetAwaiter().GetResult());
        Assert.Equal(AutonomousSearchStatus.Completed, result.Status);
        Assert.Contains(result.Events, item => item.Operation == "delete" && item.Phase == AutonomousSearchPhase.Recovery);
        Assert.Equal(ContentFingerprint.Compute(accepted), ContentFingerprint.Compute(result.AcceptedArchive[0]));
        AssertLedger(result);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await store.SaveAsync(loaded with { Revision = loaded.Revision + 1 }, path, loaded.Revision));
        var rolledBack = loaded with { Revision = result.Revision + 1, Consumed = loaded.Consumed with { Elapsed = result.Consumed.Elapsed } };
        await Assert.ThrowsAsync<InvalidDataException>(async () => await store.SaveAsync(rolledBack, path, result.Revision));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await store.SaveAsync(result with { Revision = result.Revision + 1, AcceptedArchive = [] }, path, result.Revision));
        var bytes = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path + ".corrupt", bytes.Replace("m4-archive", "m4-tampered", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync(path + ".corrupt"));
        Evidence("archive-resume", result);
    }

    [Fact]
    public async Task UnknownReservationIsChargedOnceAndKnownUndispatchedCancellationRefundsZeroWork()
    {
        var (initial, path) = await AcceptedCheckpoint("m4-interruption");
        var store = new AutonomousSearchCheckpointStore();
        Assert.Throws<SimulatedCrash>(() => AutonomousSearchService.Run(initial, state =>
        {
            store.SaveAsync(state, path, state.Revision - 1).AsTask().GetAwaiter().GetResult();
            if (state.Reservation is not null) throw new SimulatedCrash();
        }));
        var inFlight = await store.LoadAsync(path);
        var reserved = inFlight.Reservation!.Maximum;
        var recovered = AutonomousSearchService.Run(inFlight);
        Assert.Single(recovered.Events.Where(item => item.Outcome == "InterruptedUnknownCharged"));
        Assert.Equal(initial.Consumed.FunctionEvaluations + reserved.FunctionEvaluations, recovered.Consumed.FunctionEvaluations);
        Assert.Equal(initial.Consumed.TracedRays + reserved.TracedRays, recovered.Consumed.TracedRays);
        Assert.Equal(ContentFingerprint.Compute(initial.AcceptedArchive), ContentFingerprint.Compute(recovered.AcceptedArchive));
        var again = AutonomousSearchService.Run(recovered);
        Assert.Equal(recovered.Consumed.FunctionEvaluations, again.Consumed.FunctionEvaluations);
        using var stop = new CancellationTokenSource();
        var refunded = AutonomousSearchService.Run(initial, state => { if (state.Reservation is not null) stop.Cancel(); }, stop.Token);
        Assert.Equal(initial.Consumed.FunctionEvaluations, refunded.Consumed.FunctionEvaluations);
        Assert.Equal(initial.Consumed.TracedRays, refunded.Consumed.TracedRays);
        Assert.Null(refunded.Reservation); Assert.NotNull(refunded.Pending);
        Assert.Contains(refunded.Events, item => item.Outcome == "NotDispatched");
        AssertLedger(recovered); AssertLedger(refunded);
        Evidence("interrupted-work", new { inFlight, recovered, refunded });
    }

    [Fact]
    public void TinyRayBudgetCannotOverrunOrPublishPartialValidationAndNonCpuPlanIsExplicitlyRejected()
    {
        var plan = Plan();
        plan = plan with { Options = plan.Options with { MaximumTracedRays = 20, FinalValidationRayReserve = 20, MaximumStageTracedRays = 10 } };
        var result = AutonomousSearchService.Run(AutonomousSearchService.Create("m4-small-budget", plan));
        Assert.InRange(result.Consumed.TracedRays, 1, 20);
        Assert.Empty(result.AcceptedArchive);
        Assert.Contains(result.Events, item => item.Outcome == "ValidationUnavailable");
        AssertLedger(result);
        Assert.Throws<ArgumentException>(() => AutonomousSearchService.Create("m4-gpu", plan with
        { Options = plan.Options with { Full = plan.Options.Full with { ComputeMode = DesignComputeMode.Automatic } } }));
        Assert.Throws<InvalidDataException>(() => AutonomousSearchService.Run(result with { Plan = plan with { Targets = plan.Targets with { FNumber = 10 } } }));
        Evidence("hard-budget", result);
    }

    private static async Task<(AutonomousSearchCheckpoint, string)> AcceptedCheckpoint(string id)
    {
        var plan = Plan() with
        {
            Targets = new()
            {
                FocalLengthRelativeTolerance = 1,
                FNumberRelativeTolerance = 1,
                MaximumRmsRadiusMillimeters = 5,
                MaximumSpotRadiusMillimeters = 10,
                MaximumDistortionFraction = 1,
                MinimumValidRayFraction = .1
            }
        };
        plan = plan with { Options = plan.Options with { MaximumTrials = 1 } };
        var directory = Path.Combine(Path.GetTempPath(), id + "-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "checkpoint.json");
        var store = new AutonomousSearchCheckpointStore();
        using var stop = new CancellationTokenSource();
        var result = AutonomousSearchService.Run(AutonomousSearchService.Create(id, plan), state =>
        {
            store.SaveAsync(state, path, state.Revision - 1).AsTask().GetAwaiter().GetResult();
            if (state.AcceptedArchive.Length > 0 && state.Reservation is null) stop.Cancel();
        }, stop.Token);
        Assert.Single(result.AcceptedArchive);
        return (result, path);
    }

    private static AutonomousSearchPlan Plan()
    {
        var stage = new FixedTopologyOptions
        {
            ComputeMode = DesignComputeMode.Cpu,
            PerformFinalAssessment = false,
            MaximumUpdates = 1,
            MaximumFunctionEvaluations = 2,
            MaximumGradientEvaluations = 2,
            Sampling = new() { PupilRings = 1, PupilArms = 8, ProfileRadialSamples = 8 }
        };
        return new()
        {
            Seeds = [Source()],
            Targets = new() { MaximumRmsRadiusMillimeters = 1e-9 },
            Options = new()
            {
                MaximumTrials = 4,
                CandidatesPerElementCount = 2,
                MaximumFunctionEvaluations = 600,
                MaximumGradientEvaluations = 100,
                MaximumTracedRays = 1_000_000,
                MaximumStageTracedRays = 100_000,
                FinalValidationRayReserve = 50_000,
                TimeLimit = TimeSpan.FromMinutes(3),
                Local = stage,
                Full = stage,
                Recovery = new() { ProfileRadialSamples = 8, MaximumFunctionEvaluations = 150 }
            }
        };
    }
    private static DesignTopology Source()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return JsonSerializer.Deserialize<DesignTopology>(File.ReadAllText(Path.Combine(directory!.FullName,
            "labs/InitialStructure/benchmarks/autonomous-topology-v1/foundation-3.json")), Json)! with
        { Fields = [new("Axis", 0, 0)] };
    }
    private static ImmutableArray<TopologyVariableRequest> Curvatures(DesignTopology topology) => topology.Elements
        .SelectMany(element => new[] { element.FrontSurfaceId, element.BackSurfaceId })
        .Select(id => new TopologyVariableRequest(id, OpticalDesignParameterKind.Curvature, -.08, .08, .01)).ToImmutableArray();
    private static void AssertLedger(AutonomousSearchCheckpoint result)
    {
        Assert.Equal(result.Consumed.FunctionEvaluations, result.Events.Sum(item => item.Work.FunctionEvaluations));
        Assert.Equal(result.Consumed.GradientEvaluations, result.Events.Sum(item => item.Work.GradientEvaluations));
        Assert.Equal(result.Consumed.TracedRays, result.Events.Sum(item => item.Work.TracedRays));
        Assert.InRange(result.Consumed.FunctionEvaluations, 0, result.Plan.Options.MaximumFunctionEvaluations);
        Assert.InRange(result.Consumed.GradientEvaluations, 0, result.Plan.Options.MaximumGradientEvaluations);
        Assert.InRange(result.Consumed.TracedRays, 0, result.Plan.Options.MaximumTracedRays);
        AutonomousSearchService.Validate(result);
    }
    private static void Evidence(string name, object value)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_TOPOLOGY_M4_EVIDENCE");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(value, Json));
    }
    private sealed class SimulatedCrash : Exception;
}
