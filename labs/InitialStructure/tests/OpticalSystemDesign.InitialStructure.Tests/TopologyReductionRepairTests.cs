using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class TopologyReductionRepairTests
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    [Fact]
    public async Task DescentRejectsFiniteWorseProposalsAndSamePhaseResumeKeepsExactState()
    {
        var old = await Recorded();
        var recovered = old.Events.First(item => item.TrialId == "trial-0" && item.Recovery is not null).Endpoint!;
        var source = TopologyCompiler.Compile(recovered, old.Plan.Constraints);
        var requests = old.Events.First(item => item.TrialId == "trial-0" && item.Phase == AutonomousSearchPhase.Full).Optimization!.Variables;
        var binding = TopologyParameterBinder.Bind(source, requests);
        var options = old.Plan.Options.Full with { RequireDescent = true, MaximumUpdates = 20, MaximumFunctionEvaluations = 21, MaximumGradientEvaluations = 21 };
        var full = FixedTopologyOptimizer.Run(source, binding, old.Plan.Targets, options);
        var previous = full.InitialEvaluation!.Objective!.Value;
        Assert.Contains(full.Steps, step => !step.Accepted && step.Failure!.StartsWith("Non-descending", StringComparison.Ordinal));
        foreach (var step in full.Steps.Where(step => step.Accepted)) { Assert.True(step.Objective <= previous); previous = step.Objective!.Value; }
        Assert.Equal(previous, full.EndpointEvaluation!.Objective);
        Assert.Equal(full.Steps.Length + 1, full.Work.FunctionEvaluations);
        Assert.Equal(full.Work.FunctionEvaluations, full.Work.GradientEvaluations);
        using var stop = new CancellationTokenSource();
        var part = FixedTopologyOptimizer.Run(source, binding, old.Plan.Targets, options, stop.Token, step => { if (step.Attempt == 3) stop.Cancel(); });
        var continuation = JsonSerializer.Deserialize<FixedTopologyContinuation>(JsonSerializer.Serialize(part.Continuation, Json), Json)!;
        var resumed = FixedTopologyOptimizer.Run(source, binding, old.Plan.Targets, options, continuation: continuation);
        Assert.Equal(ContentFingerprint.Compute(full.Topology), ContentFingerprint.Compute(resumed.Topology));
        Assert.Equal(full.Continuation!.Adam.Coordinates, resumed.Continuation!.Adam.Coordinates);
        Assert.Equal(full.Continuation.Adam.FirstMoment, resumed.Continuation.Adam.FirstMoment);
        Assert.Equal(full.Work.FunctionEvaluations, resumed.Work.FunctionEvaluations);
        using var measurement = SequentialTraceMeasurement.Begin();
        Assert.Throws<TopologyValidationException>(() => FixedTopologyOptimizer.Run(source, binding, old.Plan.Targets, options with { RequireDescent = null }, continuation: continuation));
        Assert.Equal(0, measurement.RayCount);
        Evidence("descent", new { full, part, resumed });
    }

    [Theory]
    [InlineData(42)]
    [InlineData(101)]
    public async Task DenseStageValidationRetainsGenuinelyQualifiedTwoElementBeforeFurtherOptimization(int seed)
    {
        var old = await Recorded(seed);
        var plan = NewPlan(old.Plan);
        var result = AutonomousSearchService.Run(AutonomousSearchService.Create("repair-two-" + seed, plan));
        var candidate = Assert.Single(result.AcceptedArchive.Where(item => item.Topology.Elements.Length == 2));
        Assert.True(candidate.Assessment.MeetsTargets); Assert.Empty(candidate.Assessment.Violations);
        Assert.Equal(plan.Targets, candidate.Assessment.Targets);
        Assert.Equal(21, candidate.Assessment.Fields.Length);
        Assert.All(candidate.Assessment.Fields, field => Assert.Equal(960, field.AttemptedRays));
        Assert.InRange(candidate.Assessment.Fields.Max(field => field.RmsRadiusMillimeters)!.Value, 0, .05);
        var check = Assert.Single(result.Events.Where(item => item.Phase == AutonomousSearchPhase.Feasibility));
        Assert.True(check.Assessment!.MeetsTargets); Assert.Equal("Accepted", check.Outcome);
        Assert.Equal(1, check.Work.FunctionEvaluations); Assert.Equal(1, check.Work.ValidationEvaluations); Assert.True(check.Work.TracedRays > 0);
        Assert.DoesNotContain(result.Events, item => item.Optimization is not null);
        AssertLedger(result); Evidence("two-" + seed, result);
    }

    [Fact]
    public async Task IndependentStageCheckPersistsAndResumesAndUnknownWorkCannotPublishUnvalidatedCandidate()
    {
        var plan = NewPlan((await Recorded()).Plan);
        var initial = AutonomousSearchService.Create("repair-stage-resume", plan);
        var full = AutonomousSearchService.Run(initial);
        var directory = Path.Combine(Path.GetTempPath(), "reduction-repair-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "run.autonomous.json"); var store = new AutonomousSearchCheckpointStore();
        try
        {
            using var stop = new CancellationTokenSource();
            var part = AutonomousSearchService.Run(initial, state =>
            {
                store.SaveAsync(state, path, state.Revision - 1).AsTask().GetAwaiter().GetResult();
                if (state.Pending?.Phase == AutonomousSearchPhase.Feasibility && state.Reservation is null) stop.Cancel();
            }, stop.Token);
            var loaded = await store.LoadAsync(path);
            Assert.Equal(AutonomousSearchPhase.Local, loaded.Pending!.AfterFeasibility);
            Assert.DoesNotContain(loaded.AcceptedArchive, item => item.Topology.Elements.Length == 2);
            var resumed = AutonomousSearchService.Run(loaded);
            Assert.Equal(ContentFingerprint.Compute(full.AcceptedArchive), ContentFingerprint.Compute(resumed.AcceptedArchive));
            Assert.Equal(full.Consumed.FunctionEvaluations, resumed.Consumed.FunctionEvaluations); Assert.Equal(full.Consumed.TracedRays, resumed.Consumed.TracedRays);
            Assert.Throws<SimulatedCrash>(() => AutonomousSearchService.Run(loaded, state =>
            {
                store.SaveAsync(state, path, state.Revision - 1).AsTask().GetAwaiter().GetResult();
                if (state.Reservation?.Phase == AutonomousSearchPhase.Feasibility) throw new SimulatedCrash();
            }));
            var lost = await store.LoadAsync(path);
            var unknown = AutonomousSearchService.Run(lost);
            Assert.Single(unknown.Events.Where(item => item.Outcome == "InterruptedUnknownCharged"));
            Assert.Equal(lost.Consumed.TracedRays + lost.Reservation!.Maximum.TracedRays, unknown.Consumed.TracedRays);
            Assert.DoesNotContain(unknown.AcceptedArchive, item => item.Topology.Elements.Length == 2);
            var forged = loaded with { Pending = loaded.Pending with { AfterFeasibility = AutonomousSearchPhase.Validation } };
            using var trace = SequentialTraceMeasurement.Begin();
            Assert.Throws<InvalidDataException>(() => AutonomousSearchService.Run(forged)); Assert.Equal(0, trace.RayCount);
            AssertLedger(resumed); AssertLedger(unknown); Evidence("stage-resume", new { part, resumed, lost, unknown });
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task StageCheckDoesNotBorrowFinalReserveAndStrictTargetStillFails()
    {
        var old = await Recorded(); var plan = NewPlan(old.Plan);
        var seedRays = old.Events[0].Work.TracedRays;
        plan = plan with { Options = plan.Options with { MaximumTracedRays = seedRays + plan.Options.FinalValidationRayReserve } };
        var limited = AutonomousSearchService.Run(AutonomousSearchService.Create("repair-reserve", plan));
        Assert.Contains(limited.Events, item => item.Phase == AutonomousSearchPhase.Feasibility && item.Assessment?.Completed == false && item.Work.TracedRays == 0);
        Assert.DoesNotContain(limited.Events, item => item.Phase == AutonomousSearchPhase.Feasibility && item.Outcome == "Accepted");
        AssertLedger(limited);
        var strict = NewPlan(old.Plan) with
        {
            Targets = old.Plan.Targets with { MaximumRmsRadiusMillimeters = 1e-9 },
            Options = NewPlan(old.Plan).Options with { MaximumTrials = 6 }
        };
        var failed = AutonomousSearchService.Run(AutonomousSearchService.Create("repair-strict", strict));
        Assert.Empty(failed.AcceptedArchive);
        Assert.Contains(failed.Events, item => item.Phase == AutonomousSearchPhase.Feasibility && item.Outcome == "UnmetTargets");
        Assert.Contains(failed.Events, item => item.Phase == AutonomousSearchPhase.Full);
        AssertLedger(failed); Evidence("bounded-failure", new { limited, failed });
    }

    [Fact]
    public async Task TransitionStageChecksMayAdvanceOnlyWithOriginalGatesAndNeverArchiveTransitions()
    {
        var plan = NewPlan((await Recorded()).Plan);
        plan = plan with { Options = plan.Options with { Expansion = plan.Options.Expansion! with { EnableGradualDeletion = true, DirectDeletionRecoveryMaximumFunctionEvaluations = 2 } } };
        var result = AutonomousSearchService.Run(AutonomousSearchService.Create("repair-transition", plan));
        Assert.Contains(result.Events, item => item.Phase == AutonomousSearchPhase.Feasibility && item.Outcome == "TransitionFeasible");
        Assert.All(result.Events.Where(item => item.Outcome == "TransitionFeasible"), item =>
        { Assert.True(item.Assessment!.Completed); Assert.All(item.Assessment.Violations, violation => Assert.Equal("transition-element", violation.Code)); });
        Assert.DoesNotContain(result.Candidates, item => item.Topology.HasTransitionElements);
        Assert.DoesNotContain(result.AcceptedArchive, item => item.Topology.HasTransitionElements);
        AssertLedger(result); Evidence("transition", result);
    }

    [Fact]
    public async Task NewExampleUsesGuardsWhileFrozenM6PlansRemainUnchangedAndLoadWithoutTracing()
    {
        var example = AutonomousDesignExamples.ThreeElement();
        Assert.True(example.Options.CheckStageFeasibility); Assert.True(example.Options.Full.RequireDescent);
        using var measurement = SequentialTraceMeasurement.Begin();
        foreach (var seed in new[] { 42, 101 })
        {
            var old = await Recorded(seed); Assert.Null(old.Plan.Options.CheckStageFeasibility); Assert.Null(old.Plan.Options.Full.RequireDescent);
            Assert.DoesNotContain(old.Events, item => item.Assessment is not null);
        }
        Assert.Equal(0, measurement.RayCount);
    }

    private static AutonomousSearchPlan NewPlan(AutonomousSearchPlan old) => old with
    {
        Options = old.Options with
        { MaximumTrials = 1, CheckStageFeasibility = true, Local = old.Options.Local with { RequireDescent = true }, Full = old.Options.Full with { RequireDescent = true } }
    };
    private static Task<AutonomousSearchCheckpoint> Recorded(int seed = 42) => new AutonomousSearchCheckpointStore().LoadAsync(Path.Combine(Root(),
        $"validation/rechecks/2026-10-27/autonomous-comparison-m6/m6-m2-endpoint-{seed}-C/run.autonomous.json"));
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory!.FullName;
    }
    private static void AssertLedger(AutonomousSearchCheckpoint state)
    {
        Assert.Equal(state.Events.Sum(item => item.Work.FunctionEvaluations), state.Consumed.FunctionEvaluations);
        Assert.Equal(state.Events.Sum(item => item.Work.GradientEvaluations), state.Consumed.GradientEvaluations);
        Assert.Equal(state.Events.Sum(item => item.Work.TracedRays), state.Consumed.TracedRays);
        Assert.InRange(state.Consumed.TracedRays, 0, state.Plan.Options.MaximumTracedRays);
        Assert.InRange(state.Consumed.FunctionEvaluations, 0, state.Plan.Options.MaximumFunctionEvaluations);
    }
    private static void Evidence(string name, object value)
    {
        var path = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_REDUCTION_REPAIR_EVIDENCE");
        if (path is null) return;
        Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, name + ".json"), JsonSerializer.Serialize(value, Json));
    }
    private sealed class SimulatedCrash : Exception;
}
