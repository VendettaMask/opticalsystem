using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Engine.Optimization;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class TopologyTransmissionTests
{
    [Fact]
    public void LinearHalfspacesFindBoundedFeasiblePointAndRejectContradictoryBounds()
    {
        (double Offset, double[] Normal)[] rows = [(1, [-1, 0]), (2, [0, -1]), (-5, [1, 1])];
        var result = LinearFeasibilityProjection.Solve(rows, 2);
        Assert.NotNull(result); Assert.InRange(result[0], 1 - 1e-8, 1 + 1e-8); Assert.InRange(result[1], 2 - 1e-8, 2 + 1e-8);
        Assert.Null(LinearFeasibilityProjection.Solve([.. rows, (-.5, [1, 0])], 2));
    }

    [Fact]
    public void GateFilterDoesNotTradeNewPhysicalViolationForLowerClippingOrLowerImageEnergy()
    {
        var policy = new TopologyTransmissionCompensation(["target:power", "target:inverse-f-number", "geometry:edge", "field:0:boundary:0:clearance"]);
        var before = Value([0, 0, 0, 1]);
        Assert.False(policy.Accept(before, Value([0, 0, .001, .1])));
        Assert.False(policy.Accept(before, Value([1.1, 0, 0, .1])));
        Assert.False(policy.Accept(before, Value([0, 0, 0, 1.01])));
        Assert.True(policy.Accept(before, Value([0, 0, 0, .2]) with { Objective = 1000 }));
        Assert.False(policy.Accept(before, Value([0, 0, 0, .2]) with { Available = false }));
        static TopologyObjectiveEvaluation Value(double[] residuals) => new() { Available = true, Residuals = residuals.ToImmutableArray(), Objective = 0, LayoutFingerprint = "test", OpticFingerprint = "test" };
    }

    [Fact]
    public async Task SatisfiedConstraintsRetainTheirNonzeroTangentsAndEverySelectedParameterMatchesDifferences()
    {
        var f = await Fixture(); var objective = new FixedTopologyObjective(f.Source, f.Binding, f.Plan.Targets, f.Options.Sampling);
        var actual = objective.Evaluate(f.Binding.InitialCoordinates, includeJacobian: true, includeConstraintTangents: true);
        Assert.True(actual.Available, actual.Failure);
        var signed = actual.ConstraintTangents!.Value;
        Assert.Contains(signed, item => objective.ResidualIds[item.Row].EndsWith(":rms", StringComparison.Ordinal)
            && item.Value < 0 && actual.Residuals[item.Row] == 0 && item.Derivatives.Any(value => Math.Abs(value) > 1e-6));
        for (var parameter = 0; parameter < f.Binding.Slots.Length; parameter++)
        {
            const double step = 1e-6;
            var plus = f.Binding.InitialCoordinates.ToArray(); var minus = plus.ToArray(); plus[parameter] += step; minus[parameter] -= step;
            var a = objective.Evaluate(plus, includeConstraintTangents: true); var b = objective.Evaluate(minus, includeConstraintTangents: true);
            Assert.True(a.Available, a.Failure); Assert.True(b.Available, b.Failure);
            for (var row = 0; row < signed.Length; row++)
            {
                var expected = (a.ConstraintTangents!.Value[row].Value - b.ConstraintTangents!.Value[row].Value) / (2 * step);
                Assert.True(Math.Abs(expected - signed[row].Derivatives[parameter]) <= 1e-5 + 3e-4 * Math.Abs(expected), $"row {row}, parameter {parameter}, FD={expected:R}, AD={signed[row].Derivatives[parameter]:R}");
            }
        }
        var policy = new TopologyTransmissionCompensation(objective.ResidualIds);
        var selected = policy.Select(actual, 2);
        Assert.Equal(0, selected[0]); Assert.NotEqual(1, selected[1]); Assert.Equal(2, selected.Distinct().Count());
        Assert.True(policy.ValidTangents(actual, f.Binding.Slots.Length));
        Evidence("selection", new { Selected = selected.Select(index => f.Binding.Slots[index].Request), SignedRows = signed.Length, Parameters = f.Binding.Slots.Length });
    }

    [Fact]
    public async Task TransmissionStepsRetainTheGateFilterAndChargeEveryProposalWithinTheOriginalBounds()
    {
        var f = await Fixture(); var options = f.Options with { TransmissionFirst = true };
        using var measured = SequentialTraceMeasurement.Begin();
        var result = FixedTopologyOptimizer.Run(f.Source, f.Binding, f.Plan.Targets, options);
        Assert.Contains(result.Steps, step => step.Accepted);
        foreach (var step in result.Steps.Where(step => step.Accepted))
        {
            Assert.NotNull(step.Transmission);
            Assert.True(step.Transmission.AfterOtherGates <= 1e-16);
            Assert.True(step.Transmission.AfterClearance < step.Transmission.BeforeClearance);
        }
        Assert.Equal(result.Steps.Length + 1, result.Work.FunctionEvaluations);
        Assert.Equal(result.Work.FunctionEvaluations, result.Work.GradientEvaluations);
        Assert.Equal(measured.RayCount, result.Work.TracedRays);
        var end = TopologyParameterBinder.Bind(TopologyCompiler.Compile(result.Topology, f.Plan.Constraints), f.Binding.Slots.Select(slot => slot.Request));
        Assert.All(end.Slots, slot => Assert.InRange(slot.InitialCoordinate, slot.LowerCoordinate, slot.UpperCoordinate));
        Assert.Equal(f.Source.Topology.Stop, result.Topology.Stop); Assert.Equal(f.Source.Topology.Image, result.Topology.Image);
        Assert.Equal(f.Source.Topology.Elements.Select(element => (element.Front.ClearSemiDiameterMillimeters, element.Back.ClearSemiDiameterMillimeters)),
            result.Topology.Elements.Select(element => (element.Front.ClearSemiDiameterMillimeters, element.Back.ClearSemiDiameterMillimeters)));
        Evidence("priority", result);
    }

    [Fact]
    public async Task SelectionChangesOnlyItsRecordedParametersAndSavedStateResumesExactly()
    {
        var f = await Fixture(); var options = f.Options with { TransmissionFirst = true, ActiveParameterLimit = 2, MaximumUpdates = 4, MaximumFunctionEvaluations = 5, MaximumGradientEvaluations = 5 };
        var full = FixedTopologyOptimizer.Run(f.Source, f.Binding, f.Plan.Targets, options);
        using var stop = new CancellationTokenSource();
        var part = FixedTopologyOptimizer.Run(f.Source, f.Binding, f.Plan.Targets, options, stop.Token, _ => stop.Cancel());
        Assert.Equal(FixedTopologyTermination.Cancelled, part.Termination);
        var first = Assert.Single(part.Steps); Assert.Equal(2, first.ActiveParameters!.Value.Length);
        foreach (var parameter in Enumerable.Range(0, f.Binding.Slots.Length).Except(first.ActiveParameters.Value))
            Assert.Equal(f.Binding.InitialCoordinates[parameter], part.Continuation!.Adam.Coordinates[parameter]);
        var json = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var saved = JsonSerializer.Deserialize<FixedTopologyContinuation>(JsonSerializer.Serialize(part.Continuation, json), json)!;
        var resumed = FixedTopologyOptimizer.Run(f.Source, f.Binding, f.Plan.Targets, options, continuation: saved);
        Assert.Equal(ContentFingerprint.Compute(full.Topology), ContentFingerprint.Compute(resumed.Topology));
        Assert.Equal(ContentFingerprint.Compute(full.Steps), ContentFingerprint.Compute(resumed.Steps));
        Assert.Equal(full.Work.TracedRays, resumed.Work.TracedRays); Assert.Equal(full.Work.FunctionEvaluations, resumed.Work.FunctionEvaluations);
        using var measured = SequentialTraceMeasurement.Begin();
        Assert.Throws<TopologyValidationException>(() => FixedTopologyOptimizer.Run(f.Source, f.Binding, f.Plan.Targets, options with { ActiveParameterLimit = 3 }, continuation: saved));
        Assert.Throws<TopologyValidationException>(() => FixedTopologyOptimizer.Run(f.Source, f.Binding, f.Plan.Targets, options,
            continuation: saved with { EndpointEvaluation = saved.EndpointEvaluation! with { ConstraintTangents = [] } }));
        Assert.Equal(0, measured.RayCount);
    }

    [Fact]
    public async Task InvalidPoliciesAndMissingBoundarySamplingAreRejectedBeforeAnyTracing()
    {
        var f = await Fixture();
        using var measured = SequentialTraceMeasurement.Begin();
        foreach (var options in new[] { f.Options with { ActiveParameterLimit = 0 }, f.Options with { ActiveParameterLimit = 513 },
            f.Options with { TransmissionFirst = true, Sampling = f.Options.Sampling with { IncludeBoundaryClearance = null } } })
        {
            Assert.Throws<TopologyValidationException>(() => FixedTopologyOptimizer.Run(f.Source, f.Binding, f.Plan.Targets, options));
            Assert.Throws<ArgumentException>(() => AutonomousSearchService.Create("invalid-transmission", f.Plan with { Options = f.Plan.Options with { Local = options } }));
        }
        Assert.DoesNotContain("TransmissionFirst", JsonSerializer.Serialize(new FixedTopologyOptions()));
        Assert.DoesNotContain("ActiveParameterLimit", JsonSerializer.Serialize(new FixedTopologyOptions()));
        Assert.Equal(0, measured.RayCount);
    }

    [Fact]
    public async Task BacktrackingShrinksActualCoordinatesEvenWhenTheModelStepFitsBothBoxes()
    {
        var f = await Fixture(); var objective = new FixedTopologyObjective(f.Source, f.Binding, f.Plan.Targets, f.Options.Sampling);
        var value = objective.Evaluate(f.Binding.InitialCoordinates, includeJacobian: true, includeConstraintTangents: true);
        Assert.True(value.Available, value.Failure);
        var policy = new TopologyTransmissionCompensation(objective.ResidualIds);
        var active = Enumerable.Range(0, f.Binding.Slots.Length).ToArray();
        var full = policy.Propose(value, f.Binding.InitialCoordinates, f.Binding, active, 1);
        var half = policy.Propose(value, f.Binding.InitialCoordinates, f.Binding, active, .5);
        Assert.NotEqual(ContentFingerprint.Compute(full.Coordinates), ContentFingerprint.Compute(half.Coordinates));
        for (var index = 0; index < active.Length; index++)
            Assert.InRange(Math.Abs((full.Coordinates[index] - f.Binding.InitialCoordinates[index]) * .5
                - (half.Coordinates[index] - f.Binding.InitialCoordinates[index])), 0, 1e-12);
    }

    [Fact]
    public async Task UnversionedPrototypeCannotStartAnyOpticalWorkWhileItsHistoricalStateRemainsReadable()
    {
        var f = await Fixture(); var old = f.Options with { TransmissionFirst = true, TransmissionPolicyVersion = null };
        var checkpoint = AutonomousSearchService.Create("read-only-transmission", f.Plan with { Options = f.Plan.Options with { Local = old } });
        using var measured = SequentialTraceMeasurement.Begin();
        Assert.Throws<NotSupportedException>(() => AutonomousSearchService.Run(checkpoint));
        Assert.Throws<TopologyValidationException>(() => FixedTopologyOptimizer.Run(f.Source, f.Binding, f.Plan.Targets, old));
        AutonomousSearchService.Validate(checkpoint);
        Assert.Equal(0, measured.RayCount);
    }

    [Fact]
    public async Task ExplicitCurrentVersionReproducesTheRetainedV3LocalOpticalEndpointExactly()
    {
        var f = await Fixture();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        var old = await new AutonomousSearchCheckpointStore().LoadAsync(Path.Combine(root!.FullName,
            "validation/rechecks/2026-10-27/topology-transmission-priority/final-paired-runs/repr-qualified4-wide21-42-C-both/run.autonomous.json"));
        var recorded = old.Events.Single(item => item.Phase == AutonomousSearchPhase.Local).Optimization!;
        var options = old.Plan.Options.Local with { TransmissionPolicyVersion = 3 };
        var result = FixedTopologyOptimizer.Run(f.Source, f.Binding, f.Plan.Targets, options);
        Assert.Equal(ContentFingerprint.Compute(recorded.Topology), ContentFingerprint.Compute(result.Topology));
        Assert.Equal(ContentFingerprint.Compute(recorded.Steps), ContentFingerprint.Compute(result.Steps));
        Assert.Equal(recorded.Work.TracedRays, result.Work.TracedRays);
    }

    [Fact]
    public async Task InterruptedJacobianDoesNotChooseOrPublishParametersAndItsActualWorkIsCharged()
    {
        var f = await Fixture(); using var quota = SequentialTraceMeasurement.BeginCpuBudget(100);
        var result = FixedTopologyOptimizer.Run(f.Source, f.Binding, f.Plan.Targets, f.Options with { TransmissionFirst = true, ActiveParameterLimit = 2 });
        Assert.Equal(FixedTopologyTermination.BudgetExhausted, result.Termination); Assert.Empty(result.Steps);
        Assert.Null(result.EndpointEvaluation); Assert.Equal(1, result.Work.FunctionEvaluations); Assert.Equal(1, result.Work.GradientEvaluations);
        Assert.Equal(quota.RayCount, result.Work.TracedRays); Assert.InRange(quota.RayCount, 1, 100);
        Assert.Equal(f.Source.OpticFingerprint, TopologyCompiler.Compile(result.Topology, f.Plan.Constraints).OpticFingerprint);
    }

    private static async Task<(CompiledTopology Source, BoundTopologyParameters Binding, AutonomousSearchPlan Plan, FixedTopologyOptions Options)> Fixture()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        var state = await new AutonomousSearchCheckpointStore().LoadAsync(Path.Combine(root!.FullName,
            "validation/rechecks/2026-10-27/topology-edge-loss-repair/budget-control/repr-qualified4-wide21-42-C-boundary-budget/run.autonomous.json"));
        var recovered = state.Events.First(item => item.Recovery?.Termination == ParaxialRecoveryTermination.Recovered && item.Endpoint!.Elements.Length == 3);
        var source = TopologyCompiler.Compile(recovered.Endpoint!, state.Plan.Constraints);
        var variables = state.Events.Single(item => item.TrialId == recovered.TrialId && item.Phase == AutonomousSearchPhase.Full).Optimization!.Variables;
        return (source, TopologyParameterBinder.Bind(source, variables), state.Plan, state.Plan.Options.Full with { TransmissionPolicyVersion = 3 });
    }
    private static void Evidence(string name, object value)
    {
        if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_TRANSMISSION_EVIDENCE") is not { } directory) return;
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(value,
            new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals }));
    }
}
