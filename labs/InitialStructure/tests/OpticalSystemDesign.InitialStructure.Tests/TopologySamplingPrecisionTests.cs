using System.Text.Json;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class TopologySamplingPrecisionTests
{
    [Fact]
    public async Task MoreRadialAndAngularNodesRemoveTheRetainedRmsUnderestimateWithoutChangingTheTask()
    {
        var f = await Fixture();
        var coarse = Evaluate(f, 1, 8); var accurate = Evaluate(f, 4, 16); var dense = Evaluate(f, 10, 32);
        var errors = new List<object>();
        double coarseMaximum = 0, accurateMaximum = 0;
        for (var field = 0; field < dense.Objective.Fields.Length; field++)
        {
            var a = Rms(coarse, field); var b = Rms(accurate, field); var reference = Rms(dense, field);
            coarseMaximum = Math.Max(coarseMaximum, Math.Abs(a - reference) * 1000);
            accurateMaximum = Math.Max(accurateMaximum, Math.Abs(b - reference) * 1000);
            errors.Add(new { Field = dense.Objective.Fields[field], CoarseUm = a * 1000, AccurateUm = b * 1000, ReferenceUm = reference * 1000 });
        }
        Assert.True(coarseMaximum > 2);
        Assert.InRange(accurateMaximum, 0, .05);
        Assert.True(accurate.Value.TracedRayCount > coarse.Value.TracedRayCount);
        Assert.True(accurate.Value.TracedRayCount < dense.Value.TracedRayCount);
        Assert.NotEqual(coarse.Objective.Fingerprint, accurate.Objective.Fingerprint);
        Assert.True(TopologyValidator.MatchesProtocol(f.Source, f.Targets, f.ExpectedProtocol));
        Evidence("counterexample", new { CoarseMaximumErrorUm = coarseMaximum, AccurateMaximumErrorUm = accurateMaximum, Fields = errors });
    }

    [Theory]
    [InlineData(42)]
    [InlineData(101)]
    [InlineData(2026)]
    public async Task NearbyCurvaturesAlsoConvergeAtTheHigherPrecision(int seed)
    {
        var f = await Fixture(); var state = (uint)seed;
        var coordinates = f.Binding.InitialCoordinates.Select((value, index) =>
        {
            state = unchecked(1664525 * state + 1013904223);
            return Math.Clamp(value + .1 * (2 * ((double)state / uint.MaxValue) - 1), f.Binding.Slots[index].LowerCoordinate, f.Binding.Slots[index].UpperCoordinate);
        }).ToArray();
        var changed = TopologyParameterBinder.Apply(f.Source, f.Binding, coordinates);
        var fixture = f with { Source = changed, Binding = TopologyParameterBinder.Bind(changed, f.Binding.Slots.Select(slot => slot.Request)) };
        var actual = Evaluate(fixture, 4, 16); var reference = Evaluate(fixture, 10, 32);
        for (var field = 0; field < actual.Objective.Fields.Length; field++)
            Assert.InRange(Math.Abs(Rms(actual, field) - Rms(reference, field)) * 1000, 0, .05);
    }

    [Fact]
    public async Task BothLimitedMiddleLensPositionTangentsMatchDifferencesAtHigherPrecision()
    {
        var f = await Fixture(); var element = f.Source.Topology.Elements.Single(item => item.Id == "element-002");
        var front = element.FrontZMillimeters; var back = front + element.ThicknessMillimeters;
        var binding = TopologyParameterBinder.Bind(f.Source, [..f.Binding.Slots.Select(slot=>slot.Request),
            new(element.FrontSurfaceId,OpticalDesignParameterKind.AxialPosition,front-.25,front+.25,.1),
            new(element.BackSurfaceId,OpticalDesignParameterKind.AxialPosition,back-.25,back+.25,.1)]);
        var data = f with { Binding = binding }; var actual = Evaluate(data, 4, 16); var signed = actual.Value.ConstraintTangents!.Value;
        for (var parameter = 5; parameter < 7; parameter++)
        {
            const double step = 1e-5; var plus = binding.InitialCoordinates.ToArray(); var minus = plus.ToArray(); plus[parameter] += step; minus[parameter] -= step;
            var a = actual.Objective.Evaluate(plus, includeConstraintTangents: true); var b = actual.Objective.Evaluate(minus, includeConstraintTangents: true);
            Assert.True(a.Available, a.Failure); Assert.True(b.Available, b.Failure);
            for (var row = 0; row < signed.Length; row++)
            {
                var expected = (a.ConstraintTangents!.Value[row].Value - b.ConstraintTangents!.Value[row].Value) / (2 * step);
                Assert.True(Math.Abs(expected - signed[row].Derivatives[parameter]) <= 1e-5 + 3e-4 * Math.Abs(expected), $"row {row}, position {parameter}: FD={expected:R}, AD={signed[row].Derivatives[parameter]:R}");
            }
        }
    }

    [Fact]
    public async Task EveryHigherPrecisionConstraintTangentStillMatchesAllFiveParameterDifferences()
    {
        var f = await Fixture(); var evaluated = Evaluate(f, 4, 16);
        var signed = evaluated.Value.ConstraintTangents!.Value;
        double maximumError = 0;
        for (var parameter = 0; parameter < f.Binding.Slots.Length; parameter++)
        {
            const double step = 1e-6;
            var plus = f.Binding.InitialCoordinates.ToArray(); var minus = plus.ToArray(); plus[parameter] += step; minus[parameter] -= step;
            var a = evaluated.Objective.Evaluate(plus, includeConstraintTangents: true);
            var b = evaluated.Objective.Evaluate(minus, includeConstraintTangents: true);
            Assert.True(a.Available, a.Failure); Assert.True(b.Available, b.Failure);
            for (var row = 0; row < signed.Length; row++)
            {
                var expected = (a.ConstraintTangents!.Value[row].Value - b.ConstraintTangents!.Value[row].Value) / (2 * step);
                var error = Math.Abs(expected - signed[row].Derivatives[parameter]); maximumError = Math.Max(maximumError, error);
                Assert.True(error <= 1e-5 + 3e-4 * Math.Abs(expected), $"row {row}, parameter {parameter}: FD={expected:R}, AD={signed[row].Derivatives[parameter]:R}");
            }
        }
        Evidence("higher-precision-tangents", new { Rows = signed.Length, Parameters = f.Binding.Slots.Length, MaximumAbsoluteError = maximumError });
    }

    private static (FixedTopologyObjective Objective, TopologyObjectiveEvaluation Value) Evaluate(FixtureData f, int rings, int arms)
    {
        var objective = new FixedTopologyObjective(f.Source, f.Binding, f.Targets, f.Sampling with { PupilRings = rings, PupilArms = arms });
        var value = objective.Evaluate(f.Binding.InitialCoordinates, includeJacobian: true, includeConstraintTangents: true);
        Assert.True(value.Available, value.Failure); return (objective, value);
    }
    private static double Rms((FixedTopologyObjective Objective, TopologyObjectiveEvaluation Value) value, int field)
    {
        var index = value.Objective.ResidualIds.IndexOf($"field:{field}:rms");
        return (value.Value.ConstraintTangents!.Value.Single(row => row.Row == index).Value + 1) * .05;
    }
    private sealed record FixtureData(CompiledTopology Source, BoundTopologyParameters Binding, TopologyTargets Targets, TopologyTrainingSampling Sampling, string ExpectedProtocol);
    private static async Task<FixtureData> Fixture()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        var state = await new AutonomousSearchCheckpointStore().LoadAsync(Path.Combine(root!.FullName,
            "validation/rechecks/2026-10-27/topology-transmission-priority/final-paired-runs/repr-qualified4-wide21-42-C-both/run.autonomous.json"));
        var candidate = state.Candidates.Single(item => item.Topology.Elements.Length == 3);
        var source = TopologyCompiler.Compile(candidate.Topology, state.Plan.Constraints);
        var requests = state.Events.Single(item => item.Phase == AutonomousSearchPhase.Full).Optimization!.Variables;
        return new(source, TopologyParameterBinder.Bind(source, requests), state.Plan.Targets, state.Plan.Options.Full.Sampling, candidate.Assessment.ProtocolFingerprint);
    }
    private static void Evidence(string name, object value)
    {
        if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SAMPLING_PRECISION_EVIDENCE") is not { } directory) return;
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }
}
