using System.Collections.Immutable;
using System.Text.Json;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class WideFieldGateRepairTests
{
    [Fact]
    public async Task CommonCheckMaximumExposesTheRetainedMissWithoutExtraRaysOrRmsChanges()
    {
        var f = await Fixture(); var old = new FixedTopologyObjective(f.Source, f.Binding, f.State.Plan.Targets, f.State.Plan.Options.Full.Sampling);
        var corrected = new FixedTopologyObjective(f.Source, f.Binding, f.State.Plan.Targets, f.State.Plan.Options.Full.Sampling with { IncludeBoundaryMaximum = true });
        var a = old.Evaluate(f.Binding.InitialCoordinates, includeJacobian: true, includeConstraintTangents: true);
        var b = corrected.Evaluate(f.Binding.InitialCoordinates, includeJacobian: true, includeConstraintTangents: true);
        Assert.True(a.Available, a.Failure); Assert.True(b.Available, b.Failure);
        Assert.Equal(a.TracedRayCount, b.TracedRayCount); Assert.NotEqual(a.LayoutFingerprint, b.LayoutFingerprint);
        Assert.Equal(old.ResidualIds.ToArray(), corrected.ResidualIds.ToArray());
        var edge = old.Fields.IndexOf(new(0, 1)); var row = old.ResidualIds.IndexOf($"field:{edge}:maximum");
        Assert.Equal(0, a.Residuals[row]); Assert.True(b.Residuals[row] > .06);
        foreach (var index in Enumerable.Range(0, old.ResidualIds.Length).Where(index => !old.ResidualIds[index].EndsWith(":maximum", StringComparison.Ordinal)))
            Assert.Equal(a.Residuals[index], b.Residuals[index]);
        Assert.Throws<TopologyValidationException>(() => new FixedTopologyObjective(f.Source, f.Binding, f.State.Plan.Targets,
            f.State.Plan.Options.Full.Sampling with { IncludeBoundaryMaximum = true, IncludeBoundaryClearance = null }));
    }

    [Fact]
    public async Task AllSixteenCurvatureAndAxialDerivativesOfTheBoundaryMaximumMatchDifferences()
    {
        var f = await Fixture(); var objective = new FixedTopologyObjective(f.Source, f.Binding, f.State.Plan.Targets,
            f.State.Plan.Options.Full.Sampling with { IncludeBoundaryMaximum = true });
        var actual = objective.Evaluate(f.Binding.InitialCoordinates, includeJacobian: true, includeConstraintTangents: true);
        Assert.True(actual.Available, actual.Failure); Assert.Equal(16, f.Binding.Slots.Length);
        var edge = objective.Fields.IndexOf(new(0, 1)); var row = objective.ResidualIds.IndexOf($"field:{edge}:maximum");
        var errors = new List<object>();
        for (var parameter = 0; parameter < f.Binding.Slots.Length; parameter++)
        {
            const double h = 1e-6;
            var plus = f.Binding.InitialCoordinates.ToArray(); var minus = plus.ToArray(); plus[parameter] += h; minus[parameter] -= h;
            var a = objective.Evaluate(plus); var b = objective.Evaluate(minus); Assert.True(a.Available, a.Failure); Assert.True(b.Available, b.Failure);
            var difference = (a.Residuals[row] - b.Residuals[row]) / (2 * h); var derivative = actual.Jacobian[row][parameter];
            Assert.True(Math.Abs(difference - derivative) < 1e-5 + 3e-4 * Math.Abs(difference), $"parameter{parameter}: FD={difference:R}, AD={derivative:R}");
            errors.Add(new { Parameter = f.Binding.Slots[parameter].Request, Difference = difference, Derivative = derivative });
        }
        Evidence("check-maximum-gradient", errors);
    }

    [Fact]
    public void JointRestorationCanReduceCoupledViolationsInsideTheSameTrustBox()
    {
        string[] ids = ["target:power", "target:inverse-f-number", "geometry:edge", "field:0:boundary:0:clearance"];
        var signed = new[] { new TopologyConstraintTangent(0, 0, [0, 0]), new(1, 0, [0, 0]), new(2, 2, [-4, 0]), new(3, .2, [10, 1]) };
        var value = new TopologyObjectiveEvaluation
        {
            Available = true,
            LayoutFingerprint = "test",
            OpticFingerprint = "test",
            Residuals = [0, 0, 2, .2],
            Gradient = [0, 0],
            Jacobian = signed.Select(row => row.Derivatives).ToImmutableArray(),
            ConstraintTangents = signed.ToImmutableArray()
        };
        var slots = new[] { "a", "b" }.Select((id, index) => new TopologyParameterSlot(new(id, OpticalDesignParameterKind.Curvature, -1, 1, 1), new(index + 1, OpticalDesignParameterKind.Curvature, 1), 0, -1, 1)).ToImmutableArray();
        var binding = new BoundTopologyParameters("synthetic", slots);
        var policy = new TopologyTransmissionCompensation(ids, 4);
        var proposal = policy.Propose(value, [0.0, 0.0], binding, [0, 1], 1);
        var after = value with { Residuals = [0, 0, 2 - 4 * proposal.Coordinates[0], .2 + 10 * proposal.Coordinates[0] + proposal.Coordinates[1]] };
        Evidence("coupled-model", new { proposal, Before = value.Residuals, After = after.Residuals, Accepted = policy.Accept(value, after) });
        Assert.True(policy.Accept(value, after)); Assert.InRange(proposal.Coordinates[0], .049999, .050001); Assert.InRange(proposal.Coordinates[1], -.520001, -.519999);
        // Aggregate improvement cannot conceal a newly failed formerly safe gate in version4.
        string[] guardIds = ["target:power", "target:inverse-f-number", "geometry:edge", "field:0:maximum", "field:0:boundary:0:clearance"];
        var before = value with { Residuals = [0, 0, 0, 2, 1], ConstraintTangents = null };
        var trade = value with { Residuals = [0, 0, .01, 1, .1], ConstraintTangents = null };
        Assert.True(new TopologyTransmissionCompensation(guardIds, 3).Accept(before, trade));
        Assert.False(new TopologyTransmissionCompensation(guardIds, 4).Accept(before, trade));
    }

    [Fact]
    public async Task VersionFourResumeIsExactAndRejectsOldModelIdentityBeforeTracing()
    {
        var f = await Fixture(); var options = f.State.Plan.Options.Full with
        {
            TransmissionPolicyVersion = 4,
            Sampling = f.State.Plan.Options.Full.Sampling with { IncludeBoundaryMaximum = true },
            MaximumUpdates = 4,
            MaximumFunctionEvaluations = 5,
            MaximumGradientEvaluations = 5
        };
        var full = FixedTopologyOptimizer.Run(f.Source, f.Binding, f.State.Plan.Targets, options);
        using var stop = new CancellationTokenSource();
        var part = FixedTopologyOptimizer.Run(f.Source, f.Binding, f.State.Plan.Targets, options, stop.Token, step => { if (step.Attempt == 1) stop.Cancel(); });
        Assert.NotNull(part.Continuation);
        var resumed = FixedTopologyOptimizer.Run(f.Source, f.Binding, f.State.Plan.Targets, options, continuation: part.Continuation);
        Assert.Equal(ContentFingerprint.Compute(full.Topology), ContentFingerprint.Compute(resumed.Topology));
        Assert.Equal(full.Work.TracedRays, resumed.Work.TracedRays);
        using var measurement = SequentialTraceMeasurement.Begin();
        Assert.Throws<TopologyValidationException>(() => FixedTopologyOptimizer.Run(f.Source, f.Binding, f.State.Plan.Targets, options with { TransmissionPolicyVersion = 3 }, continuation: part.Continuation));
        Assert.Equal(0, measurement.RayCount); Evidence("joint-resume", new { full, part, resumed });
    }

    private static async Task<(AutonomousSearchCheckpoint State, CompiledTopology Source, BoundTopologyParameters Binding)> Fixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        var state = await new AutonomousSearchCheckpointStore().LoadAsync(Path.Combine(directory!.FullName, "validation/rechecks/2026-10-27/topology-integrated-compensation/paired-runs/repr-new-wide4-3-42-C-axial/run.autonomous.json"));
        var endpoint = state.Candidates.OrderBy(candidate => candidate.Score).First();
        var source = TopologyCompiler.Compile(endpoint.Topology, state.Plan.Constraints);
        return (state, source, TopologyParameterBinder.Bind(source, state.Events.First(item => item.Optimization is not null).Optimization!.Variables));
    }
    private static void Evidence(string name, object value)
    { if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_WIDE21_EVIDENCE") is not { } directory) return; Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals })); }
}
