using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Engine.Optimization;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class ParaxialGeometryRecoveryTests
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    [Fact]
    public async Task EdgeConstrainedDeletionRecoversWithinTheFrozenWindowAndOriginalGates()
    {
        var checkpoint = await Recorded(); var plan = checkpoint.Plan;
        var history = checkpoint.Events.Single(item => item.Recovery is not null);
        var edit = TopologyOperators.Delete(plan.Seeds[0], history.Change!.RemovedElementIds.Single(), plan.Constraints);
        var parent = TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints);
        var child = TopologyCompiler.Compile(edit.Topology!, plan.Constraints);
        var options = plan.Options.Recovery with { UseGeometryConstraintTangents = true };
        using var noRays = SequentialTraceMeasurement.Begin();
        var result = ParaxialRestorer.Run(parent, child, history.Recovery!.Variables, plan.Targets, options);
        Evidence("result", new { plan, options, result });
        Assert.InRange(result.Work.FunctionEvaluations, 1, plan.Options.Recovery.MaximumFunctionEvaluations);
        Assert.InRange(result.Steps.Length, 0, plan.Options.Recovery.MaximumAcceptedSteps);
        Assert.Equal(0, noRays.RayCount);
        var compiled = TopologyCompiler.Compile(result.Topology, plan.Constraints);
        var optic = compiled.CreateOptic();
        var geometry = TopologyEvaluationPlan.Geometry(compiled, plan.Targets, 64).Select(row =>
        {
            var value = TopologyEvaluationPlan.FormalMeasure(optic, row);
            return new { row.Id, row.Limit, Value = value, Margin = row.Upper ? row.Limit - value : value - row.Limit };
        }).ToArray();
        Evidence("dense-geometry", geometry);
        Assert.All(geometry, row => Assert.True(row.Margin >= 0, row.Id + ": " + row.Margin));
        Assert.Equal(ParaxialRecoveryTermination.Recovered, result.Termination);
        Assert.InRange(Math.Abs(result.EndpointAError!.Value), 0, options.ATolerance);
        Assert.InRange(Math.Abs(result.EndpointFocalCError!.Value), 0, options.FocalCTolerance);
        Assert.InRange(Math.Abs(result.EndpointPupilError!.Value), 0, options.FocalCTolerance);
        Assert.Equal(plan.Seeds[0].Stop, result.Topology.Stop); Assert.Equal(plan.Seeds[0].Image, result.Topology.Image);
        Assert.Equal(plan.Seeds[0].Reference, result.Topology.Reference);
        foreach (var element in result.Topology.Elements)
        {
            var original = edit.Topology!.Elements.Single(item => item.Id == element.Id);
            Assert.Equal(original.Material, element.Material); Assert.Equal(original.Front.ClearSemiDiameterMillimeters, element.Front.ClearSemiDiameterMillimeters);
            Assert.Equal(original.Back.ClearSemiDiameterMillimeters, element.Back.ClearSemiDiameterMillimeters);
            Assert.InRange(Math.Abs(element.FrontZMillimeters - original.FrontZMillimeters), 0, .25);
            Assert.InRange(Math.Abs(element.BackZMillimeters - original.BackZMillimeters), 0, .25);
        }
        using (var finalBudget = SequentialTraceMeasurement.BeginCpuBudget(plan.Options.FinalValidationRayReserve))
        {
            var assessment = TopologyValidator.Validate(compiled, plan.Targets);
            Evidence("independent-assessment", new { assessment, PhysicalRays = finalBudget.RayCount });
            Assert.True(assessment.Completed); Assert.True(assessment.MeetsTargets);
        }
        var productTargets = AutonomousDesignExamples.ThreeElement().Targets;
        using (var strictBudget = SequentialTraceMeasurement.BeginCpuBudget(plan.Options.FinalValidationRayReserve))
        {
            var strict = TopologyValidator.Validate(compiled, productTargets);
            Evidence("strict-product-assessment", new { assessment = strict, PhysicalRays = strictBudget.RayCount });
            Assert.True(strict.Completed); Assert.False(strict.MeetsTargets);
            Assert.Contains(strict.Violations, item => item.Code.Contains("rms", StringComparison.OrdinalIgnoreCase));
            if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_RECOVERY_EVIDENCE") is { } directory)
                await AutonomousDesignFiles.ExportAsync(new("edge-recovered", null, result.Topology, strict, 0), plan.Constraints,
                    Path.Combine(directory, "edge-recovered-unqualified.staropt"));
        }
        var searchPlan = plan with { Options = plan.Options with { Recovery = options } };
        var search = AutonomousSearchService.Run(AutonomousSearchService.Create("edge-constrained-integration", searchPlan));
        Evidence("autonomous-search", search);
        var recovered = Assert.Single(search.Events, item => item.Recovery is not null).Recovery!;
        Assert.Equal(ParaxialRecoveryTermination.Recovered, recovered.Termination);
        var candidate = Assert.Single(search.AcceptedArchive, item => item.Topology.Elements.Length == 2);
        Assert.Equal(ContentFingerprint.Compute(result.Topology), ContentFingerprint.Compute(candidate.Topology));
        Assert.InRange(search.Consumed.TracedRays, 1, plan.Options.MaximumTracedRays);
        AutonomousSearchService.Validate(search);
    }

    [Fact]
    public async Task ActiveEdgeTangentsAgreeWithCorePerturbationsAndPolicyKeepsHardBudgets()
    {
        var checkpoint = await Recorded(); var plan = checkpoint.Plan;
        var history = checkpoint.Events.Single(item => item.Recovery is not null);
        var original = JsonSerializer.Deserialize<ParaxialRecoveryResult>(await File.ReadAllTextAsync(Path.Combine(Root(),
            "validation/rechecks/2026-10-27/autonomous-recovery-progress/front-stop-pupil-full-current.json")), Json)!;
        var source = TopologyCompiler.Compile(original.Topology, plan.Constraints);
        var binding = TopologyParameterBinder.Bind(source, original.Variables);
        var edge = TopologyEvaluationPlan.Geometry(source, plan.Targets, 8).Single(row => row.Id == "lens-3:edge:8");
        using var measurement = SequentialTraceMeasurement.Begin();
        var tangent = TopologyEvaluationPlan.Measure(source.CreateOptic(), binding.CoreParameters, edge);
        var differences = new List<object>();
        for (var column = 0; column < binding.Slots.Length; column++)
        {
            const double h = 1e-6;
            var plus = binding.InitialCoordinates.ToArray(); var minus = plus.ToArray(); plus[column] += h; minus[column] -= h;
            var up = TopologyEvaluationPlan.FormalMeasure(TopologyParameterBinder.Apply(source, binding, plus).CreateOptic(), edge);
            var down = TopologyEvaluationPlan.FormalMeasure(TopologyParameterBinder.Apply(source, binding, minus).CreateOptic(), edge);
            var fd = (up - down) / (2 * h);
            Assert.InRange(Math.Abs(tangent.Derivative(column) - fd), 0, 1e-6 + 1e-5 * Math.Abs(fd));
            differences.Add(new { Parameter = binding.Slots[column].Request, Automatic = tangent.Derivative(column), Difference = fd });
        }
        var parent = TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints);
        var child = TopologyCompiler.Compile(TopologyOperators.Delete(plan.Seeds[0], history.Change!.RemovedElementIds.Single(), plan.Constraints).Topology!, plan.Constraints);
        var options = plan.Options.Recovery with { UseGeometryConstraintTangents = true };
        var invalid = ParaxialRestorer.Run(parent, child, original.Variables, plan.Targets, options with { UseAutomaticMatrixDerivatives = false });
        Assert.Equal(ParaxialRecoveryTermination.Unsupported, invalid.Termination); Assert.Equal(0, invalid.Work.FunctionEvaluations);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        var cancelled = ParaxialRestorer.Run(parent, child, original.Variables, plan.Targets, options, stop.Token);
        Assert.Equal(ParaxialRecoveryTermination.Cancelled, cancelled.Termination); Assert.Equal(0, cancelled.Work.FunctionEvaluations);
        var bounded = ParaxialRestorer.Run(parent, child, original.Variables, plan.Targets, options with { MaximumFunctionEvaluations = 3 });
        Assert.Equal(ParaxialRecoveryTermination.BudgetExhausted, bounded.Termination); Assert.InRange(bounded.Work.FunctionEvaluations, 1, 3);
        using var during = new CancellationTokenSource();
        var interrupted = ParaxialRestorer.Run(parent, child, original.Variables, plan.Targets, options, during.Token, _ => during.Cancel());
        Assert.Equal(ParaxialRecoveryTermination.Cancelled, interrupted.Termination); Assert.Single(interrupted.Steps);
        var noPupil = ParaxialRestorer.Run(parent, child, original.Variables, plan.Targets, options with { RestorePupilMagnification = null });
        Assert.Equal(ParaxialRecoveryTermination.Recovered, noPupil.Termination); Assert.Null(noPupil.Work.PupilEvaluations);
        Assert.Equal(0, measurement.RayCount);
        Assert.DoesNotContain("UseGeometryConstraintTangents", JsonSerializer.Serialize(plan.Options.Recovery, Json));
        Evidence("tangents-and-budgets", new { differences, invalid, cancelled, bounded, interrupted, noPupil });
    }

    [Fact]
    public void AProjectedLeastSquaresStepPreservesTheBoundaryWithoutClaimingZeroResidual()
    {
        var accepted = new List<double[]>();
        var result = BoundedTrustRegionLeastSquares.Solve([0, 0], [-2, -2], [2, 2],
            (point, _) => new([point[0] - 1, point[1] - 1]),
            new() { MaximumEvaluations = 8, StepMethod = LeastSquaresStepMethod.Regularized },
            acceptedStep: (point, _) => accepted.Add(point.ToArray()), analyticJacobian: _ => new double[,] { { 1, 0 }, { 0, 1 } },
            trialProjector: (_, point, _) => LinearFeasibilityProjection.Solve([(Offset: -1, Normal: new double[] { 1, 1 })], 2, preferredPoint: point));
        Assert.NotEmpty(accepted); Assert.All(accepted, point => Assert.InRange(point.Sum(), -4, 1 + 1e-10));
        Assert.InRange(Math.Abs(result.Variables[0] - .5), 0, 1e-8); Assert.InRange(Math.Abs(result.Variables[1] - .5), 0, 1e-8);
        Assert.True(result.Evaluation!.Residuals.Sum(value => value * value) > .49);
    }

    private static async Task<AutonomousSearchCheckpoint> Recorded() => await new AutonomousSearchCheckpointStore().LoadAsync(
        Path.Combine(Root(), "validation/rechecks/2026-10-27/autonomous-recovery-progress/recovery-True-resumed.autonomous.json"));
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory!.FullName;
    }
    private static void Evidence(string name, object value)
    {
        if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_RECOVERY_EVIDENCE") is not { } directory) return;
        Directory.CreateDirectory(directory);
        var label = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_EDGE_RECOVERY_ATTEMPT") ?? "run";
        File.WriteAllText(Path.Combine(directory, label + "-" + name + ".json"), JsonSerializer.Serialize(value, Json));
    }
}
