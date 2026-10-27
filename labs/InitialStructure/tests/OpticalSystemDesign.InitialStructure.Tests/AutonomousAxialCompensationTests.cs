using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class AutonomousAxialCompensationTests
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    [Fact]
    public async Task ActualAutonomousRunRepairsNineLostRaysAndResumeAndNativeRoundTripAgree()
    {
        var old = await Recorded();
        var seed = old.Candidates.Where(item => item.Topology.Elements.Length == 3).OrderBy(item => item.Score).First().Topology;
        var plan = Plan(old) with { Seeds = [seed] };
        var full = AutonomousSearchService.Run(AutonomousSearchService.Create("axial-integration", plan));
        var accepted = Assert.Single(full.AcceptedArchive);
        Assert.True(accepted.Assessment.MeetsTargets); Assert.Empty(accepted.Assessment.Violations);
        Assert.Equal(3, accepted.Topology.Elements.Length); Assert.Equal(35, accepted.Assessment.Fields.Length);
        Assert.All(accepted.Assessment.Fields, field => { Assert.Equal(960, field.ValidRays); Assert.Equal(99, field.CheckValidRays); });
        Assert.InRange(accepted.Assessment.Fields.Max(field => field.RmsRadiusMillimeters)!.Value * 1000, 48.9, 49.1);
        Assert.Equal(seed.Stop, accepted.Topology.Stop); Assert.Equal(seed.Image, accepted.Topology.Image); Assert.Equal(seed.Reference, accepted.Topology.Reference);
        Assert.All(seed.Elements, element =>
        {
            var endpoint = accepted.Topology.Elements.Single(item => item.Id == element.Id);
            Assert.Equal(element.Front.ClearSemiDiameterMillimeters, endpoint.Front.ClearSemiDiameterMillimeters);
            Assert.Equal(element.Back.ClearSemiDiameterMillimeters, endpoint.Back.ClearSemiDiameterMillimeters);
            Assert.InRange(Math.Abs(element.FrontZMillimeters - endpoint.FrontZMillimeters), 0, element.Id == "element-002" ? .25 : 1e-12);
            Assert.InRange(Math.Abs(element.BackZMillimeters - endpoint.BackZMillimeters), 0, element.Id == "element-002" ? .25 : 1e-12);
        });
        Assert.Equal(7, full.Events.Single(item => item.Optimization is not null).Optimization!.Variables.Length);
        AssertLedger(full);
        using var stop = new CancellationTokenSource();
        var part = AutonomousSearchService.Run(AutonomousSearchService.Create("axial-resume", plan), cancellationToken: stop.Token,
            progress: (_, step) => { if (step.Attempt == 3) stop.Cancel(); });
        Assert.Equal(AutonomousSearchStatus.Cancelled, part.Status); Assert.NotNull(part.Pending!.Continuation); Assert.NotNull(part.Pending.AxialAnchors);
        var loaded = JsonSerializer.Deserialize<AutonomousSearchCheckpoint>(JsonSerializer.Serialize(part, Json), Json)!;
        var resumed = AutonomousSearchService.Run(loaded);
        Assert.Equal(ContentFingerprint.Compute(accepted.Topology), ContentFingerprint.Compute(Assert.Single(resumed.AcceptedArchive).Topology));
        Assert.Equal(full.Consumed.TracedRays, resumed.Consumed.TracedRays); AssertLedger(resumed);
        var directory = EvidenceDirectory() ?? Path.Combine(Path.GetTempPath(), "axial-native-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var native = await AutonomousDesignFiles.ExportAsync(accepted, plan.Constraints, Path.Combine(directory, "integrated-qualified.staropt"));
        var imported = await AutonomousDesignFiles.ImportSeedAsync(native, plan.Constraints);
        using var audit = SequentialTraceMeasurement.BeginCpuBudget(800_000);
        var assessment = TopologyValidator.Validate(TopologyCompiler.Compile(imported, plan.Constraints), plan.Targets);
        Assert.True(assessment.MeetsTargets); Assert.Empty(assessment.Violations);
        Evidence("autonomous-control", new { full, part, resumed, NativeAssessment = assessment, AdditionalAuditRays = audit.RayCount });
        if (EvidenceDirectory() is null) { File.Delete(native); Directory.Delete(directory); }
    }

    [Fact]
    public async Task PerTrialBoundsSurviveRecoveryLocalFullAndProtectedFacesNeverBecomeVariables()
    {
        var old = await Recorded(); var plan = Plan(old) with { Seeds = [old.Plan.Seeds[0]] };
        plan = plan with { Options = plan.Options with { FixedTopologyOnly = null, MaximumTrials = 2, CheckStageFeasibility = null, Local = plan.Options.Local with { MaximumUpdates = 4, MaximumFunctionEvaluations = 5, MaximumGradientEvaluations = 5 }, Full = plan.Options.Full with { MaximumUpdates = 8, MaximumFunctionEvaluations = 9, MaximumGradientEvaluations = 9 } } };
        var windows = new List<ImmutableArray<TopologyAxialAnchor>>();
        using var stop = new CancellationTokenSource();
        var state = AutonomousSearchService.Run(AutonomousSearchService.Create("axial-stage-bounds", plan), saved =>
        {
            if (saved.Reservation is not null && saved.Pending?.AxialAnchors is { } anchors)
            {
                windows.Add(anchors);
                if (saved.Pending.Phase == AutonomousSearchPhase.Recovery) Assert.All(saved.Pending.Variables, variable => Assert.Equal(OpticalDesignParameterKind.Curvature, variable.Kind));
                if (saved.Pending.Phase == AutonomousSearchPhase.Full) stop.Cancel();
            }
        }, stop.Token);
        Evidence("stage-window-attempt", state);
        Assert.Equal(AutonomousSearchPhase.Full, state.Pending!.Phase); Assert.True(windows.Count >= 3);
        Assert.All(windows, window => Assert.Equal(windows[0].ToArray(), window.ToArray()));
        var axes = state.Pending.Variables.Where(variable => variable.Kind == OpticalDesignParameterKind.AxialPosition).ToArray();
        Assert.Equal(2, axes.Length);
        foreach (var request in axes)
        {
            var anchor = windows[0].Single(item => item.SurfaceId == request.SurfaceId);
            Assert.Equal(anchor.ZMillimeters - .25, request.LowerBound); Assert.Equal(anchor.ZMillimeters + .25, request.UpperBound);
        }
        var bad = state with { Pending = state.Pending with { AxialAnchors = windows[0].SetItem(0, windows[0][0] with { ZMillimeters = 1000 }) } };
        using var measurement = SequentialTraceMeasurement.Begin();
        Assert.Throws<InvalidDataException>(() => AutonomousSearchService.Run(bad)); Assert.Equal(0, measurement.RayCount);
        var stripped = state with { Pending = state.Pending with { Variables = state.Pending.Variables.Where(variable => variable.Kind != OpticalDesignParameterKind.AxialPosition).ToImmutableArray() } };
        Assert.Throws<InvalidDataException>(() => AutonomousSearchService.Run(stripped)); Assert.Equal(0, measurement.RayCount);

        var source = AutonomousDesignExamples.ThreeElement();
        source = source with { Seeds = [source.Seeds[0] with { Elements = source.Seeds[0].Elements.SetItem(0, source.Seeds[0].Elements[0] with { Protected = true }) }] };
        using var pause = new CancellationTokenSource();
        var guardedInitial = AutonomousSearchService.Create("axial-protected", source) with
        {
            SeedCursor = 1,
            Pending = new()
            { TrialId = "trial-0", Operation = "optimize", Template = source.Seeds[0], Phase = AutonomousSearchPhase.Full }
        };
        var guarded = AutonomousSearchService.Run(guardedInitial, saved =>
        { if (saved.Pending?.StageOptions is not null) pause.Cancel(); }, pause.Token);
        Assert.DoesNotContain(guarded.Pending!.Variables, variable => variable.SurfaceId.StartsWith("lens-1", StringComparison.Ordinal)
            || variable.SurfaceId == source.Seeds[0].Stop.Id || variable.SurfaceId == source.Seeds[0].Image.Id || variable.SurfaceId == source.Seeds[0].Reference.Id);
        AssertLedger(state); Evidence("stage-windows", new { state, guarded });
    }

    [Fact]
    public async Task LegacyNullIdentityRemainsAndInvalidAxialPoliciesFailBeforeTracing()
    {
        var old = await Recorded(); Assert.Null(old.Plan.Options.AxialCompensation);
        var text = JsonSerializer.Serialize(old, Json); Assert.DoesNotContain("AxialAnchors", text); Assert.DoesNotContain("AxialCompensation", text);
        Assert.Equal(old.PlanFingerprint, ContentFingerprint.Compute(JsonSerializer.Deserialize<AutonomousSearchCheckpoint>(text, Json)!.Plan));
        var plan = Plan(old);
        using var measurement = SequentialTraceMeasurement.Begin();
        foreach (var policy in new[] { new TopologyAxialCompensationOptions { MaximumDisplacementMillimeters = 0 }, new() { CoordinateScaleMillimeters = double.NaN }, new() { ElementIds = ["unknown"] }, new() { ElementIds = ["element-002", "element-002"] } })
            Assert.Throws<ArgumentException>(() => AutonomousSearchService.Create("invalid-axial", plan with { Options = plan.Options with { AxialCompensation = policy } }));
        Assert.Equal(0, measurement.RayCount);
        var example = AutonomousDesignExamples.ThreeElement(); Assert.NotNull(example.Options.AxialCompensation);
        Assert.Equal(4, example.Options.Full.Sampling.PupilRings); Assert.Equal(16, example.Options.Full.Sampling.PupilArms); Assert.Equal(4, example.Options.Full.TransmissionPolicyVersion);
        Assert.True(example.Options.Full.Sampling.IncludeBoundaryMaximum);
    }

    private static AutonomousSearchPlan Plan(AutonomousSearchCheckpoint old)
    {
        var stage = old.Plan.Options.Full with { TransmissionFirst = true, TransmissionPolicyVersion = 3, RequireDescent = true, Sampling = old.Plan.Options.Full.Sampling with { PupilRings = 4, PupilArms = 16 }, MaximumUpdates = 99, MaximumFunctionEvaluations = 100, MaximumGradientEvaluations = 100 };
        return old.Plan with { Options = old.Plan.Options with { FixedTopologyOnly = true, MaximumTrials = 1, AxialCompensation = new() { ElementIds = ["element-002"] }, Local = stage with { ActiveParameterLimit = 2 }, Full = stage } };
    }
    private static async Task<AutonomousSearchCheckpoint> Recorded() => await new AutonomousSearchCheckpointStore().LoadAsync(Path.Combine(Root(), "validation/rechecks/2026-10-27/topology-transmission-priority/active-set-runs/repr-qualified4-wide21-42-C-both/run.autonomous.json"));
    private static string Root() { var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent; return directory!.FullName; }
    private static string? EvidenceDirectory() => Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_AXIAL_INTEGRATION_EVIDENCE");
    private static void Evidence(string name, object value) { if (EvidenceDirectory() is not { } directory) return; Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(value, Json)); }
    private static void AssertLedger(AutonomousSearchCheckpoint state)
    {
        Assert.Equal(state.Events.Sum(item => item.Work.TracedRays), state.Consumed.TracedRays);
        Assert.Equal(state.Events.Sum(item => item.Work.FunctionEvaluations), state.Consumed.FunctionEvaluations);
        Assert.InRange(state.Consumed.TracedRays, 0, state.Plan.Options.MaximumTracedRays);
        Assert.InRange(state.Consumed.GradientEvaluations, 0, state.Plan.Options.MaximumGradientEvaluations);
    }
}
