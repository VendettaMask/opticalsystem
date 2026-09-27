using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class JointGeometryPhaseTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(20)]
    public void FiniteCoreGeometryViolationRetainsOpticalCorrectionAndHardAcceptance(int index)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OptilandWorkbench.slnx"))) directory = directory.Parent;
        var fixture = JsonSerializer.Deserialize<Fixture>(File.ReadAllText(Path.Combine(directory!.FullName,
            "labs/InitialStructure/benchmarks/stage-recovery-20260927/joint-inputs.json")), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
                Converters = { new JsonStringEnumConverter() }
            })!;
        var input = fixture.Inputs.Single(input => input.Index == index);
        var problem = new FlatStartDesignProblem(fixture.Specification, input.Family.ElementCount, input.Template, input.Family);
        var vector = problem.Vector(input.Start);
        var entry = Evaluate(vector);
        Assert.False(entry.GeometryFeasible);
        Assert.True(entry.HasContinuousSearchResiduals);
        Assert.False(entry.MeetsTargets);
        var phases = new List<OpticalSolvePhase>();
        var jacobians = 0;
        var result = FlatStartLocalSolver.Solve(problem.SolverCoordinates, vector, entry, 90, Evaluate,
            true, () => false, default, (_, _, phase) => phases.Add(phase), (_, statistics) => jacobians += statistics.JacobianBuildCount);
        Assert.NotEmpty(phases);
        Assert.All(phases, phase => Assert.Equal(OpticalSolvePhase.RealRay, phase));
        Assert.True(jacobians > 0);
        Assert.True(result.Evaluation.Merit < entry.Merit);
        Assert.InRange(result.Evaluations, 1, 90);
        Assert.True(result.Evaluation.HasContinuousSearchResiduals);
        var fresh = Evaluate(result.Variables);
        Assert.Equal(ContentFingerprint.Compute(result.Evaluation), ContentFingerprint.Compute(fresh));
        var final = problem.Evaluate(Optic.FromSnapshot(fresh.EvaluatedOptic!), input.Stage, true, default, true);
        Assert.Equal(.5, fixture.Specification.FlatStart!.MinimumEdgeThicknessMillimeters);
        if (!final.GeometryFeasible) Assert.False(final.MeetsTargets);
        DesignEvaluation Evaluate(double[] values) => problem.Evaluate(problem.CreateOptic(values, input.Stage), input.Stage,
            input.Stage == FlatStartDesignProblem.FullStage, default);
    }

    private sealed record Fixture(InitialStructureSpecification Specification, Input[] Inputs);
    private sealed record Input(int Index, FlatStartFamily Family, OpticSnapshot Template, OpticSnapshot Start, DesignStage Stage);
}
