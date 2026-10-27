using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class TopologyEdgeLossTests
{
    [Fact]
    public async Task RecordedLossIsRealMiddleRearApertureClippingMissedByInteriorTraining()
    {
        var (state, candidate) = await Recorded();
        var source = TopologyCompiler.Compile(candidate.Topology, state.Plan.Constraints);
        var optic = source.CreateOptic();
        var boundary = SampledApertureDiagnostics.Evaluate(optic, 0, 1, Checks());
        var interior = SampledApertureDiagnostics.Evaluate(optic, 0, 1, ApertureSampler.GenerateGaussianQuadrature(1, 8).ToArray());
        Assert.True(boundary.IsComplete); Assert.True(interior.IsComplete);
        Assert.All(interior.Rays, ray => Assert.True(ray.MinimumClearanceMillimeters > .4));
        var lost = boundary.Rays.Where(ray => ray.MinimumClearanceMillimeters < -1e-8).ToArray();
        Assert.Equal(15, lost.Length);
        Assert.All(lost, ray => Assert.Equal("element-002:back", source.SurfaceIds[ray.LimitingSurfaceIndex!.Value]));
        Assert.All(lost.GroupBy(ray => ray.WavelengthNanometers), wave => Assert.Equal(5, wave.Count()));
        Assert.InRange(lost.Min(ray => ray.MinimumClearanceMillimeters)!.Value, -.08, -.05);
    }

    [Fact]
    public async Task BoundaryRowsKeepGaussianStatisticsAndEveryParameterGradientMatchesIndependentDifferences()
    {
        var (state, candidate) = await Recorded();
        var source = TopologyCompiler.Compile(candidate.Topology, state.Plan.Constraints);
        var binding = Bind(source);
        var sampling = state.Plan.Options.Full.Sampling;
        var old = new FixedTopologyObjective(source, binding, state.Plan.Targets, sampling);
        var guard = new FixedTopologyObjective(source, binding, state.Plan.Targets, sampling with { IncludeBoundaryClearance = true });
        var initial = old.Evaluate(binding.InitialCoordinates, includeJacobian: true);
        var actual = guard.Evaluate(binding.InitialCoordinates, includeJacobian: true);
        Assert.True(initial.Available, initial.Failure); Assert.True(actual.Available, actual.Failure);
        Assert.Equal(old.ResidualIds, guard.ResidualIds.Take(old.ResidualIds.Length));
        Assert.Equal(initial.Residuals, actual.Residuals.Take(initial.Residuals.Length));
        Assert.Contains(actual.Residuals.Skip(initial.Residuals.Length), value => value > 0);
        Assert.True(actual.TracedRayCount > initial.TracedRayCount);
        Assert.NotEqual(old.Fingerprint, guard.Fingerprint);
        var errors = new List<object>();
        for (var parameter = 0; parameter < binding.Slots.Length; parameter++)
        {
            const double step = 1e-6;
            var plus = binding.InitialCoordinates.ToArray(); var minus = plus.ToArray();
            plus[parameter] += step; minus[parameter] -= step;
            var a = guard.Evaluate(plus); var b = guard.Evaluate(minus);
            Assert.True(a.Available, a.Failure); Assert.True(b.Available, b.Failure);
            var numerical = (a.Objective!.Value - b.Objective!.Value) / (2 * step);
            Near(numerical, actual.Gradient[parameter]);
            for (var row = 0; row < actual.Residuals.Length; row++)
                Near((a.Residuals[row] - b.Residuals[row]) / (2 * step), actual.Jacobian[row][parameter]);
            errors.Add(new { binding.Slots[parameter].Request, Numerical = numerical, Automatic = actual.Gradient[parameter] });
        }
        Evidence("gradients", new { GaussianRows = initial.Residuals.Length, BoundaryRows = actual.Residuals.Length - initial.Residuals.Length, Parameters = errors });
    }

    [Fact]
    public async Task BoundaryPolicyResumesExactlyAndRejectsChangedLayoutBeforeTracing()
    {
        var (state, candidate) = await Recorded();
        var source = TopologyCompiler.Compile(candidate.Topology, state.Plan.Constraints); var binding = Bind(source);
        var options = state.Plan.Options.Full with
        {
            Sampling = state.Plan.Options.Full.Sampling with { IncludeBoundaryClearance = true },
            MaximumUpdates = 3,
            MaximumFunctionEvaluations = 4,
            MaximumGradientEvaluations = 4
        };
        var full = FixedTopologyOptimizer.Run(source, binding, state.Plan.Targets, options);
        using var stop = new CancellationTokenSource();
        var part = FixedTopologyOptimizer.Run(source, binding, state.Plan.Targets, options, stop.Token, _ => stop.Cancel());
        Assert.Equal(FixedTopologyTermination.Cancelled, part.Termination);
        var saved = JsonSerializer.Deserialize<FixedTopologyContinuation>(JsonSerializer.Serialize(part.Continuation))!;
        var resumed = FixedTopologyOptimizer.Run(source, binding, state.Plan.Targets, options, continuation: saved);
        Assert.Equal(ContentFingerprint.Compute(full.Topology), ContentFingerprint.Compute(resumed.Topology));
        Assert.Equal(full.Continuation!.Adam.Coordinates, resumed.Continuation!.Adam.Coordinates);
        Assert.Equal(full.Continuation.Adam.FirstMoment, resumed.Continuation.Adam.FirstMoment);
        Assert.Equal(full.Work.FunctionEvaluations, resumed.Work.FunctionEvaluations);
        Assert.Equal(full.Work.TracedRays, resumed.Work.TracedRays);
        using var measurement = SequentialTraceMeasurement.Begin();
        Assert.Throws<TopologyValidationException>(() => FixedTopologyOptimizer.Run(source, binding, state.Plan.Targets,
            options with { Sampling = options.Sampling with { IncludeBoundaryClearance = null } }, continuation: saved));
        Assert.Equal(0, measurement.RayCount);
    }

    [Fact]
    public async Task ExtraBoundaryWorkRespectsHardRayQuotaAndCannotProduceAvailablePartialObjective()
    {
        var (state, candidate) = await Recorded();
        var source = TopologyCompiler.Compile(candidate.Topology, state.Plan.Constraints);
        var binding = Bind(source);
        var baseline = new FixedTopologyObjective(source, binding, state.Plan.Targets, state.Plan.Options.Full.Sampling).Evaluate(binding.InitialCoordinates);
        Assert.True(baseline.Available, baseline.Failure);
        var objective = new FixedTopologyObjective(source, binding, state.Plan.Targets,
            state.Plan.Options.Full.Sampling with { IncludeBoundaryClearance = true });
        using var quota = SequentialTraceMeasurement.BeginCpuBudget(baseline.TracedRayCount + 100);
        var result = objective.Evaluate(binding.InitialCoordinates);
        Assert.False(result.Available);
        Assert.Equal(objective.ResidualIds.Length, result.Residuals.Length);
        Assert.Equal(quota.RayCount, result.TracedRayCount);
        Assert.InRange(quota.RayCount, baseline.TracedRayCount, baseline.TracedRayCount + 100);
    }

    [Fact]
    public async Task OptionalDenseCheckDefersWithoutChargingOrPublishingAndResumesBeforeFullCompensation()
    {
        var (old, _) = await Recorded();
        var options = old.Plan.Options;
        var plan = old.Plan with
        {
            Options = options with
            {
                ProtectCompensationRayBudget = true,
                Local = options.Local with { Sampling = options.Local.Sampling with { IncludeBoundaryClearance = true } },
                Full = options.Full with { Sampling = options.Full.Sampling with { IncludeBoundaryClearance = true } }
            }
        };
        var initial = AutonomousSearchService.Create("edge-budget-resume", plan);
        var full = AutonomousSearchService.Run(initial);
        using var stop = new CancellationTokenSource();
        var part = AutonomousSearchService.Run(initial, state =>
        {
            if (state.Events.Any(item => item.Outcome == "StageValidationDeferred")) stop.Cancel();
        }, stop.Token);
        Assert.Equal(AutonomousSearchStatus.Cancelled, part.Status);
        Assert.Equal(AutonomousSearchPhase.Full, part.Pending!.Phase);
        Assert.Null(part.Reservation);
        var skipped = Assert.Single(part.Events.Where(item => item.Outcome == "StageValidationDeferred"));
        Assert.Equal(new AutonomousSearchWork(), skipped.Work);
        Assert.Null(skipped.Assessment);
        Assert.DoesNotContain(part.AcceptedArchive, candidate => candidate.Topology.Elements.Length < 4);
        var resumed = AutonomousSearchService.Run(part);
        Assert.Equal(ContentFingerprint.Compute(full.AcceptedArchive), ContentFingerprint.Compute(resumed.AcceptedArchive));
        Assert.Equal(ContentFingerprint.Compute(full.Candidates), ContentFingerprint.Compute(resumed.Candidates));
        Assert.Equal(full.Consumed.FunctionEvaluations, resumed.Consumed.FunctionEvaluations);
        Assert.Equal(full.Consumed.TracedRays, resumed.Consumed.TracedRays);
        Assert.Contains(resumed.Events, item => item.Phase == AutonomousSearchPhase.Full && item.Work.GradientEvaluations > 1);
        Assert.Equal(resumed.Events.Sum(item => item.Work.TracedRays), resumed.Consumed.TracedRays);
        Assert.InRange(resumed.Consumed.TracedRays, 0, plan.Options.MaximumTracedRays);
        Assert.Equal(800_000, resumed.Plan.Options.FinalValidationRayReserve);
    }

    [Fact]
    public async Task OriginalAcceptanceAndOldCheckpointIdentityAreUnchanged()
    {
        var (state, candidate) = await Recorded();
        Assert.Null(state.Plan.Options.Full.Sampling.IncludeBoundaryClearance);
        Assert.Null(state.Plan.Options.ProtectCompensationRayBudget);
        Assert.DoesNotContain("IncludeBoundaryClearance", JsonSerializer.Serialize(new TopologyTrainingSampling()));
        Assert.True(AutonomousDesignExamples.ThreeElement().Options.Full.Sampling.IncludeBoundaryClearance);
        using var quota = SequentialTraceMeasurement.BeginCpuBudget(800_000);
        var actual = TopologyValidator.Validate(TopologyCompiler.Compile(candidate.Topology, state.Plan.Constraints), state.Plan.Targets);
        Assert.True(actual.Completed); Assert.False(actual.MeetsTargets);
        Assert.Equal(candidate.Assessment.ProtocolFingerprint, actual.ProtocolFingerprint);
        Assert.Equal(candidate.Assessment.Targets, actual.Targets);
        var failed = Assert.Single(actual.Violations);
        Assert.Equal("field:34:check-lost", failed.Code); Assert.Equal(15, failed.Actual); Assert.Equal(0, failed.Limit);
        Assert.Equal(35, actual.Fields.Length);
        Assert.All(actual.Fields, field => { Assert.Equal(960, field.AttemptedRays); Assert.Equal(99, field.CheckAttemptedRays); });
    }

    private static BoundTopologyParameters Bind(CompiledTopology source) => TopologyParameterBinder.Bind(source,
        source.Topology.Elements.SelectMany(element => new[] { (element.FrontSurfaceId, element.Front), (element.BackSurfaceId, element.Back) })
            .Where(face => face.Item2.Profile.Kind != TopologyProfileKind.Plane)
            .Select(face => new TopologyVariableRequest(face.Item1, OpticalDesignParameterKind.Curvature, -.3, .3, .1)));
    private static PupilSample[] Checks() => [new(0, 0, 1), .. Enumerable.Range(0, 32)
        .Select(index => new PupilSample(Math.Cos(2 * Math.PI * index / 32), Math.Sin(2 * Math.PI * index / 32), 1))];
    private static void Near(double expected, double actual) => Assert.True(Math.Abs(expected - actual) <= 1e-5 + 3e-4 * Math.Abs(expected), $"FD {expected:R}, AD {actual:R}");
    private static async Task<(AutonomousSearchCheckpoint State, AutonomousSearchCandidate Candidate)> Recorded()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        var state = await new AutonomousSearchCheckpointStore().LoadAsync(Path.Combine(root!.FullName,
            "validation/rechecks/2026-10-27/representative-reduction/qualified-control-verified/repr-qualified4-wide21-42-C-fixed/run.autonomous.json"));
        return (state, state.Candidates.Where(item => item.Topology.Elements.Length == 3 && item.Assessment.Completed).OrderBy(item => item.Score).First());
    }
    private static void Evidence(string name, object value)
    {
        if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_EDGE_LOSS_EVIDENCE") is not { } directory) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }
}
