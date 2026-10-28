using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class QualifiedReductionTests
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    [Fact]
    public void FailedSeedCannotStartReductionOrRepairCycle()
    {
        using var quota = SequentialTraceMeasurement.BeginCpuBudget(300_000);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var plan = Plan() with { Targets = new() { MaximumRmsRadiusMillimeters = 1e-9 } };
        var result = AutonomousSearchService.Run(AutonomousSearchService.Create("bounded-reduction-failed-seed", plan), cancellationToken: deadline.Token);
        Write("failed-seed", new { result, Rays = quota.RayCount, RayCap = 300_000 });
        Assert.Equal(AutonomousSearchStatus.Completed, result.Status);
        Assert.Equal(0, result.TrialCursor);
        Assert.Empty(result.AcceptedArchive);
        Assert.Single(result.Events);
        Assert.Equal("seed", result.Events[0].Operation);
        var before = quota.RayCount;
        var resumed = AutonomousSearchService.Run(result, cancellationToken: deadline.Token);
        Assert.Equal(before, quota.RayCount);
        Assert.Equal(ContentFingerprint.Compute(result.Events), ContentFingerprint.Compute(resumed.Events));
        Assert.Equal(AutonomousReductionStopReason.NoQualifiedSource, AutonomousSearchService.ReductionSummary(resumed).Reason);
        Assert.Null(AutonomousSearchService.ReductionSummary(resumed).MinimumVerifiedElementCount);
        Assert.Throws<InvalidDataException>(() => AutonomousSearchService.Run(result with { Pool = [result.Candidates[0].Id] }));
        Assert.Equal(before, quota.RayCount);
    }

    [Fact]
    public void FailedChildIsRetainedButCannotStartAnInsertionOrFurtherDeletion()
    {
        using var quota = SequentialTraceMeasurement.BeginCpuBudget(300_000);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var plan = QualifiedPlan(deadline.Token);
        var result = AutonomousSearchService.Run(AutonomousSearchService.Create("bounded-reduction-child", plan), cancellationToken: deadline.Token);
        var summary = AutonomousSearchService.ReductionSummary(result);
        Write("failed-child", new { result, summary, Rays = quota.RayCount, RayCap = 300_000 });
        Assert.Equal(AutonomousSearchStatus.Completed, result.Status);
        Assert.Equal(1, result.TrialCursor);
        Assert.Equal(3, Assert.Single(result.AcceptedArchive).Topology.Elements.Length);
        Assert.Equal(2, Assert.Single(result.Candidates.Where(c => c.ParentId is not null)).Topology.Elements.Length);
        Assert.Equal(1, summary.RetainedUnqualifiedChildren);
        Assert.Equal(3, summary.MinimumVerifiedElementCount);
        Assert.Equal(AutonomousReductionStopReason.ConfiguredBranchesExhausted, summary.Reason);
        Assert.All(result.Events, item => Assert.Contains(item.Operation, new[] { "seed", "delete" }));
        Assert.Contains(result.Events, item => item.Operation == "delete" && item.Outcome == "UnmetTargets");
        Assert.All(result.Pool, id => Assert.True(result.Candidates.Single(c => c.Id == id).Assessment.MeetsTargets));
        var saved = JsonSerializer.Deserialize<AutonomousSearchCheckpoint>(JsonSerializer.Serialize(result, Json), Json)!;
        var before = quota.RayCount;
        var resumed = AutonomousSearchService.Run(saved, cancellationToken: deadline.Token);
        Assert.Equal(before, quota.RayCount);
        Assert.Equal(ContentFingerprint.Compute(result.AcceptedArchive), ContentFingerprint.Compute(resumed.AcceptedArchive));
        Assert.Equal(ContentFingerprint.Compute(result.Events), ContentFingerprint.Compute(resumed.Events));
        Assert.Equal(result.Events.Sum(e => e.Work.TracedRays), result.Consumed.TracedRays);
    }

    [Fact]
    public void BudgetStopKeepsQualifiedParentAndPendingWorkWithoutClaimingBranchExhaustion()
    {
        using var quota = SequentialTraceMeasurement.BeginCpuBudget(300_000);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var plan = QualifiedPlan(deadline.Token);
        plan = plan with { Options = plan.Options with { MaximumFunctionEvaluations = 2 } };
        var result = AutonomousSearchService.Run(AutonomousSearchService.Create("bounded-reduction-budget", plan), cancellationToken: deadline.Token);
        var summary = AutonomousSearchService.ReductionSummary(result);
        Write("budget-stop", new { result, summary, Rays = quota.RayCount, RayCap = 300_000 });
        Assert.Equal(AutonomousReductionStopReason.BudgetExhausted, summary.Reason);
        Assert.Equal(3, summary.MinimumVerifiedElementCount);
        Assert.True(summary.HasPendingWork);
        var saved = JsonSerializer.Deserialize<AutonomousSearchCheckpoint>(JsonSerializer.Serialize(result, Json), Json)!;
        var before = quota.RayCount;
        var resumed = AutonomousSearchService.Run(saved, cancellationToken: deadline.Token);
        Assert.Equal(before, quota.RayCount);
        Assert.Equal(ContentFingerprint.Compute(result.Events), ContentFingerprint.Compute(resumed.Events));
        Assert.Equal(result.Consumed.FunctionEvaluations, resumed.Consumed.FunctionEvaluations);
        Assert.Equal(AutonomousSearchStatus.BudgetExhausted, resumed.Status);
    }

    [Fact]
    public void ConfiguredMinimumStopsWithoutDeletionAndCancellationRemainsResumable()
    {
        using var quota = SequentialTraceMeasurement.BeginCpuBudget(300_000);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var plan = QualifiedPlan(deadline.Token);
        plan = plan with { Constraints = plan.Constraints with { MinimumElementCount = 3 } };
        var result = AutonomousSearchService.Run(AutonomousSearchService.Create("bounded-reduction-minimum", plan), cancellationToken: deadline.Token);
        Assert.Equal(0, result.TrialCursor);
        Assert.Equal(AutonomousReductionStopReason.MinimumAllowedCountReached, AutonomousSearchService.ReductionSummary(result).Reason);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var part = AutonomousSearchService.Run(AutonomousSearchService.Create("bounded-reduction-cancel", plan), s =>
        {
            if (!s.AcceptedArchive.IsEmpty && s.Reservation is null) stop.Cancel();
        }, stop.Token);
        Assert.Equal(AutonomousReductionStopReason.Cancelled, AutonomousSearchService.ReductionSummary(part).Reason);
        var before = quota.RayCount;
        var resumed = AutonomousSearchService.Run(part, cancellationToken: deadline.Token);
        Assert.Equal(before, quota.RayCount);
        Assert.Equal(AutonomousSearchStatus.Completed, resumed.Status);
        Write("minimum-and-cancel", new { result, part, resumed, Rays = quota.RayCount, RayCap = 300_000 });
    }

    [Fact]
    public void PolicyIsExplicitAndIncompatibleOperationsFailBeforeOptics()
    {
        var plan = Plan();
        using var quota = SequentialTraceMeasurement.Begin();
        Assert.Throws<ArgumentException>(() => AutonomousSearchService.Create("bad-reduction-fixed", plan with
        { Options = plan.Options with { FixedTopologyOnly = true } }));
        Assert.Throws<ArgumentException>(() => AutonomousSearchService.Create("bad-reduction-expanded", plan with
        { Options = plan.Options with { Expansion = new() } }));
        var legacy = plan with { Options = plan.Options with { QualifiedReductionOnly = null } };
        Assert.DoesNotContain("QualifiedReductionOnly", JsonSerializer.Serialize(legacy, Json));
        var state = AutonomousSearchService.Create("legacy-reduction", legacy);
        Assert.Throws<ArgumentException>(() => AutonomousSearchService.ReductionSummary(state));
        Assert.Equal(0, quota.RayCount);
    }

    private static AutonomousSearchPlan QualifiedPlan(CancellationToken token)
    {
        var plan = Plan();
        var targets = new TopologyTargets { FocalLengthRelativeTolerance = 1, FNumberRelativeTolerance = 1,
            MaximumRmsRadiusMillimeters = 5, MaximumSpotRadiusMillimeters = 10, MaximumDistortionFraction = 1, MinimumValidRayFraction = .1 };
        var assessment = TopologyValidator.Validate(TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints), targets, token);
        Assert.True(assessment.Completed);
        return plan with { Targets = targets with { EffectiveFocalLengthMillimeters = assessment.EffectiveFocalLengthMillimeters!.Value,
            FNumber = assessment.FNumber!.Value, FocalLengthRelativeTolerance = 1e-8, FNumberRelativeTolerance = 1e-8 } };
    }

    private static AutonomousSearchPlan Plan()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        var source = JsonSerializer.Deserialize<DesignTopology>(File.ReadAllText(Path.Combine(root!.FullName,
            "labs/InitialStructure/benchmarks/autonomous-topology-v1/foundation-3.json")), Json)! with { Fields = [new("Axis", 0, 0)] };
        var stage = new FixedTopologyOptions { ComputeMode = DesignComputeMode.Cpu, PerformFinalAssessment = false, MaximumUpdates = 0,
            MaximumFunctionEvaluations = 1, MaximumGradientEvaluations = 0 };
        return new() { Seeds = [source], Options = new() { QualifiedReductionOnly = true, SkipParaxialRecovery = true,
            MaximumTrials = 4, BranchesPerParent = 1, MaximumFunctionEvaluations = 20, MaximumGradientEvaluations = 4,
            MaximumTracedRays = 300_000, MaximumStageTracedRays = 10_000, FinalValidationRayReserve = 80_000,
            TimeLimit = TimeSpan.FromSeconds(30), Local = stage, Full = stage } };
    }

    private static void Write(string name, object value)
    {
        if (Environment.GetEnvironmentVariable("AUTOLENS_REDUCTION_STOP_EVIDENCE") is not { } folder) return;
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name + ".json"), JsonSerializer.Serialize(value, Json));
    }
}
