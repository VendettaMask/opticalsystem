using System.Collections.Immutable;
using System.Text.Json;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class FieldConflictCompensationTests
{
    [Fact]
    public void ACompliantAnchorMustNotDriveAnUphillGateRestorationFallback()
    {
        string[] ids = ["target:power", "target:inverse-f-number", "field:0:maximum", "field:1:maximum"];
        double[][] jacobian = [[-10], [0], [1], [0]];
        var before = At(0);
        var binding = new BoundTopologyParameters("synthetic", [new(new("q", OpticalDesignParameterKind.Curvature, -1, 1, 1), new(1, OpticalDesignParameterKind.Curvature, 1), 0, -1, 1)]);
        var old = new TopologyTransmissionCompensation(ids, 4); var repaired = new TopologyTransmissionCompensation(ids, 5);
        var a = old.Propose(before, [0], binding, [0], 1); var b = repaired.Propose(before, [0], binding, [0], 1);
        Assert.True(a.Coordinates[0] > 0); Assert.False(old.Accept(before, At(a.Coordinates[0])));
        Assert.InRange(b.Coordinates[0], -.005000001, -.000001); Assert.True(repaired.Accept(before, At(b.Coordinates[0])));
        Assert.InRange(At(b.Coordinates[0]).Residuals[0], -.95, .95000001);
        TopologyObjectiveEvaluation At(double q)
        {
            double[] raw = [.9 - 10 * q, 0, 1 + q, .1];
            return new()
            {
                Available = true,
                LayoutFingerprint = "test",
                OpticFingerprint = "test",
                Residuals = raw.ToImmutableArray(),
                Gradient = [0],
                Jacobian = jacobian.Select(row => row.ToImmutableArray()).ToImmutableArray(),
                ConstraintTangents = raw.Select((v, row) => new TopologyConstraintTangent(row, v, jacobian[row].ToImmutableArray())).ToImmutableArray()
            };
        }
    }

    [Fact]
    public async Task EveryFrozenMeritDerivativeMatchesCorePerturbationsWithoutRecenteringTheActiveBound()
    {
        var f = await Fixture(); var q = f.Binding.InitialCoordinates.ToArray();
        using var quota = SequentialTraceMeasurement.BeginCpuBudget(600_000);
        var before = f.Objective.Evaluate(q, includeJacobian: true, includeConstraintTangents: true);
        Assert.True(before.Available, before.Failure);
        var policy = new TopologyTransmissionCompensation(f.Objective.ResidualIds, 5);
        var gradient = policy.MeritGradient(before); var errors = new List<double>();
        for (var column = 0; column < q.Length; column++)
        {
            const double h = 1e-6;
            var up = q.ToArray(); up[column] += h;
            var a = f.Objective.Evaluate(up, includeConstraintTangents: true); Assert.True(a.Available, a.Failure);
            double measured;
            if (q[column] - h < f.Binding.Slots[column].LowerCoordinate)
                measured = (policy.Merit(a).Other - policy.Merit(before).Other) / h;
            else
            {
                var down = q.ToArray(); down[column] -= h;
                var b = f.Objective.Evaluate(down, includeConstraintTangents: true); Assert.True(b.Available, b.Failure);
                measured = (policy.Merit(a).Other - policy.Merit(b).Other) / (2 * h);
            }
            var error = Math.Abs(measured - gradient[column]); errors.Add(error);
            Assert.InRange(error, 0, 2e-5);
        }
        var old = new TopologyTransmissionCompensation(f.Objective.ResidualIds, 4);
        var a4 = old.Propose(before, q, f.Binding, Enumerable.Range(0, q.Length).ToArray(), 1);
        var a5 = policy.Propose(before, q, f.Binding, Enumerable.Range(0, q.Length).ToArray(), 1);
        var v4 = f.Objective.Evaluate(a4.Coordinates, includeConstraintTangents: true);
        var v5 = f.Objective.Evaluate(a5.Coordinates, includeConstraintTangents: true);
        Assert.False(old.Accept(before, v4)); Assert.True(policy.Accept(before, v5));
        Assert.Equal(0, f.Binding.Slots.Single(slot => slot.Request.SurfaceId == "element-001:front" && slot.Request.Kind == OpticalDesignParameterKind.AxialPosition).LowerCoordinate);
        Evidence("merit-gradients", new { Errors = errors, Rays = quota.RayCount, BeforeOther = policy.Merit(before).Other, AfterOther = policy.Merit(v5).Other });
    }

    [Fact]
    public async Task VersionFiveResumeIsExactAndCannotConsumeVersionFourState()
    {
        var f = await Fixture();
        var options = f.State.Plan.Options.Full with { TransmissionPolicyVersion = 5, MaximumUpdates = 2, MaximumFunctionEvaluations = 3, MaximumGradientEvaluations = 3 };
        Assert.False(AutonomousSearchService.IsHistoricalReadOnlyPlan(f.State.Plan with
        { Options = f.State.Plan.Options with { Local = options, Full = options } }));
        var full = FixedTopologyOptimizer.Run(f.Source, f.Binding, f.State.Plan.Targets, options);
        using var stop = new CancellationTokenSource();
        var part = FixedTopologyOptimizer.Run(f.Source, f.Binding, f.State.Plan.Targets, options, stop.Token, step => { if (step.Attempt == 1) stop.Cancel(); });
        Assert.Equal(FixedTopologyTermination.Cancelled, part.Termination); Assert.NotNull(part.Continuation);
        var resumed = FixedTopologyOptimizer.Run(f.Source, f.Binding, f.State.Plan.Targets, options, continuation: part.Continuation);
        Assert.Equal(ContentFingerprint.Compute(full.Topology), ContentFingerprint.Compute(resumed.Topology));
        Assert.Equal(full.Work.TracedRays, resumed.Work.TracedRays); Assert.True(full.AcceptedUpdates > 0);
        using var noRays = SequentialTraceMeasurement.Begin();
        Assert.Throws<TopologyValidationException>(() => FixedTopologyOptimizer.Run(f.Source, f.Binding, f.State.Plan.Targets, options with { TransmissionPolicyVersion = 4 }, continuation: part.Continuation));
        Assert.Equal(0, noRays.RayCount);
        Evidence("resume", new { full, part, resumed });
    }

    [Fact]
    public void BoundClippingCanReverseTheFallbackButProtectedDescentStillExists()
    {
        string[] ids = ["target:power", "target:inverse-f-number", "field:0:maximum", "field:1:maximum", "field:2:maximum"];
        double[][] j = [[0, 0, 0], [0, 0, 0], [10, 1, 0], [1, -1, -1], [0, 0, 0]];
        var before = Value([0, 0, 1, .9, .1]);
        var slots = Enumerable.Range(0, 3).Select(i => new TopologyParameterSlot(new("q" + i, OpticalDesignParameterKind.Curvature, i == 0 ? 0 : -1, 1, 1),
            new(i + 1, OpticalDesignParameterKind.Curvature, 1), 0, i == 0 ? 0 : -1, 1)).ToImmutableArray();
        var binding = new BoundTopologyParameters("synthetic", slots);
        var old = new TopologyTransmissionCompensation(ids, 4); var repaired = new TopologyTransmissionCompensation(ids, 5);
        var a = old.Propose(before, [0, 0, 0], binding, [0, 1, 2], 1);
        var b = repaired.Propose(before, [0, 0, 0], binding, [0, 1, 2], 1);
        Assert.True(old.Merit(At(a.Coordinates)).Other > old.Merit(before).Other);
        Assert.True(repaired.Accept(before, At(b.Coordinates)));
        Assert.InRange(b.Coordinates[0], 0, 1);
        Assert.InRange(b.Coordinates[1], -1, 1); Assert.InRange(b.Coordinates[2], -1, 1);
        Assert.Equal(.1, At(b.Coordinates).Residuals[4]); // Uncontrollable failed row does not prohibit progress elsewhere.
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => repaired.Propose(before, [0, 0, 0], binding, [0, 1, 2], 1, cancelled.Token));
        TopologyObjectiveEvaluation At(double[] q) => Value([0, 0, 1 + 10 * q[0] + q[1], .9 + q[0] - q[1] - q[2], .1]);
        TopologyObjectiveEvaluation Value(double[] raw) => new()
        {
            Available = true,
            LayoutFingerprint = "fixture",
            OpticFingerprint = "fixture",
            Residuals = raw.ToImmutableArray(),
            Gradient = [0, 0, 0],
            Jacobian = j.Select(row => row.ToImmutableArray()).ToImmutableArray(),
            ConstraintTangents = raw.Select((v, row) => new TopologyConstraintTangent(row, v, j[row].ToImmutableArray())).ToImmutableArray()
        };
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(11)]
    [InlineData(12)]
    public void SameNonlinearGateRejectsCrossFieldRegressionsAndNewlyFailedGates(int version)
    {
        string[] ids = ["target:power", "target:inverse-f-number", "field:0:maximum", "field:1:maximum", "field:0:boundary:0:clearance"];
        var policy = new TopologyTransmissionCompensation(ids, version);
        var before = Value([0, 0, 1, 0, 0]);
        Assert.False(policy.Accept(before, Value([0, 0, .1, .001, 0])));
        Assert.False(policy.Accept(before, Value([0, 0, 1.01, 0, 0])));
        Assert.False(policy.Accept(before, Value([.951, 0, .1, 0, 0])));
        Assert.False(policy.Accept(before, Value([0, 0, .1, 0, .001])));
        Assert.True(policy.Accept(before, Value([0, 0, .9, 0, 0])));
        static TopologyObjectiveEvaluation Value(double[] r) => new() { Available = true, LayoutFingerprint = "test", OpticFingerprint = "test", Residuals = r.ToImmutableArray() };
    }

    [Theory]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(11)]
    public void BoundAxialCoordinatesPreserveProtectedPlanesAndSerializedContinuation(int version)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        json.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var root = Path.Combine(directory!.FullName, "validation/rechecks/2026-10-28/two-element-field-conflict/paired-runs/v5");
        var endpoint = JsonSerializer.Deserialize<FixedTopologyResult>(File.ReadAllText(Path.Combine(root, "full.json")), json)!;
        using var input = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "input.json")));
        var options = JsonSerializer.Deserialize<FixedTopologyOptions>(input.RootElement.GetProperty("Full").GetRawText(), json)! with
        { TransmissionPolicyVersion = version, MaximumUpdates = 2, MaximumFunctionEvaluations = 3, MaximumGradientEvaluations = 3, PerformFinalAssessment = false };
        var source = TopologyCompiler.Compile(endpoint.Topology, endpoint.Constraints);
        var binding = TopologyParameterBinder.Bind(source, endpoint.Variables);
        // Exact fixed identities survive every legal axial update, including old one-ULP failures.
        for (var sample = 1; sample <= 20; sample++)
        {
            var q = binding.InitialCoordinates.ToArray(); q[5] += sample * .001;
            var applied = TopologyParameterBinder.Apply(source, binding, q);
            Assert.Equal(source.Topology.Stop, applied.Topology.Stop);
            Assert.Equal(source.Topology.Reference, applied.Topology.Reference);
            Assert.Equal(source.Topology.Image, applied.Topology.Image);
            Assert.Equal(q[4] * binding.Slots[4].Request.Scale, applied.Topology.Elements[0].FrontZMillimeters);
            Assert.InRange(Math.Abs(applied.Topology.Elements[0].BackZMillimeters - q[5] * binding.Slots[5].Request.Scale), 0, 1e-14);
        }
        var tampered = source.CreateOptic();
        tampered.SurfaceGroup.Items[source.SurfaceIndices[source.Topology.Stop.Id] - 1].Thickness += 1e-6;
        tampered.SurfaceGroup.Renumber();
        Assert.Throws<TopologyValidationException>(() => TopologyCompiler.Update(source, tampered.ToSnapshot()));
        var full = FixedTopologyOptimizer.Run(source, binding, endpoint.Targets, options);
        using var stop = new CancellationTokenSource();
        var part = FixedTopologyOptimizer.Run(source, binding, endpoint.Targets, options, stop.Token, _ => stop.Cancel());
        Assert.Equal(FixedTopologyTermination.Cancelled, part.Termination);
        Assert.False(part.Steps[0].Accepted);
        Assert.NotNull(part.Continuation);
        var saved = JsonSerializer.Deserialize<FixedTopologyContinuation>(JsonSerializer.Serialize(part.Continuation, json), json)!;
        var resumed = FixedTopologyOptimizer.Run(source, binding, endpoint.Targets, options, continuation: saved);
        Assert.Equal(ContentFingerprint.Compute(full.Topology), ContentFingerprint.Compute(resumed.Topology));
        Assert.Equal(ContentFingerprint.Compute(full.Steps), ContentFingerprint.Compute(resumed.Steps));
        Assert.Equal(full.Work.TracedRays, resumed.Work.TracedRays);
        Assert.Equal(full.Work.FunctionEvaluations, resumed.Work.FunctionEvaluations);
        using var quota = SequentialTraceMeasurement.Begin();
        Assert.Throws<TopologyValidationException>(() => FixedTopologyOptimizer.Run(source, binding, endpoint.Targets,
            options with { TransmissionPolicyVersion = 4 }, continuation: saved));
        Assert.Equal(0, quota.RayCount);
    }

    private static async Task<(AutonomousSearchCheckpoint State, CompiledTopology Source, BoundTopologyParameters Binding, FixedTopologyObjective Objective)> Fixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        var state = await new AutonomousSearchCheckpointStore().LoadAsync(Path.Combine(directory!.FullName,
            "validation/rechecks/2026-10-28/three-to-two-continuation/selected-runs/reduce-3-2-42-C-geometry/run.autonomous.json"));
        var candidate = state.Candidates.Single(c => c.Topology.Elements.Length == 2);
        var source = TopologyCompiler.Compile(candidate.Topology, state.Plan.Constraints);
        var binding = TopologyParameterBinder.Bind(source, state.Events.Last(e => e.Optimization is not null).Optimization!.Variables);
        return (state, source, binding, new(source, binding, state.Plan.Targets, state.Plan.Options.Full.Sampling));
    }
    private static void Evidence(string name, object value)
    {
        if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_FIELD_CONFLICT_EVIDENCE") is not { } directory) return;
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }
}
