using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class JointGeometryRestartTests
{
    [Theory]
    [InlineData("trial-0056", .32)]
    [InlineData("trial-0078", .45)]
    public void CompleteRaysAcrossGeometryBoundKeepFullFieldDuringRestart(string id, double maximumRms)
    {
        var input = Load(id);
        var problem = Problem(input);
        var entry = problem.Evaluate(problem.CreateOptic(problem.Vector(input.Start), FlatStartDesignProblem.FullStage),
            FlatStartDesignProblem.FullStage, true, default);
        Assert.True(entry.HasContinuousSearchResiduals);
        Assert.False(entry.GeometryFeasible);
        Assert.False(entry.MeetsTargets);

        var result = Solve(input);
        Assert.Equal("full-target-refinement-entry", result.Steps[0].Operation);
        Assert.All(result.Steps, step => Assert.Equal(FlatStartDesignProblem.FullStage, step.Stage));
        Assert.True(result.FinalValidation!.IsFeasible);
        Assert.True(result.FinalValidation.Fields.Max(field => field.RmsRadiusMillimeters) < maximumRms);
        Assert.InRange(result.EvaluationCount, 1, input.Specification.Budget.MaximumEvaluations);
        var fresh = problem.Evaluate(Optic.FromSnapshot(result.Candidate!.Optic), FlatStartDesignProblem.FullStage, true, default, true);
        Assert.Equal(ContentFingerprint.Compute(result.FinalValidation), ContentFingerprint.Compute(fresh));
        Assert.Equal(ContentFingerprint.Compute(fresh.EvaluatedOptic!), result.Candidate.OpticFingerprint);
    }

    [Fact]
    public void MissingPropagationStillUsesTheRecoveryPath()
    {
        var input = Load("trial-0026");
        var problem = Problem(input);
        var entry = problem.Evaluate(problem.CreateOptic(problem.Vector(input.Start), FlatStartDesignProblem.FullStage),
            FlatStartDesignProblem.FullStage, true, default);
        Assert.False(entry.HasContinuousSearchResiduals);
        var result = Solve(input);
        Assert.Equal("progressive-stage-entry", result.Steps[0].Operation);
        Assert.DoesNotContain(result.Steps, step => step.Operation == "full-target-refinement-entry");
        Assert.InRange(result.EvaluationCount, 1, input.Specification.Budget.MaximumEvaluations);
    }

    [Fact]
    public void CompleteRaysDoNotWaiveUnrepairedGeometryAtTheBudgetLimit()
    {
        var input = Load("trial-0056");
        input = input with { Specification = input.Specification with { Budget = input.Specification.Budget with { MaximumEvaluations = 2 } } };
        var result = Solve(input);
        Assert.Equal(2, result.EvaluationCount);
        Assert.Equal("full-target-refinement-entry", result.Steps[0].Operation);
        Assert.True(result.FinalValidation!.HasContinuousSearchResiduals);
        Assert.False(result.FinalValidation.GeometryFeasible);
        Assert.False(result.FinalValidation.MeetsTargets);
        Assert.Equal(CandidateStatus.Rejected, result.Candidate!.Status);
    }

    private static FlatStartDesignResult Solve(Input input) => new FlatStartDesignService().Continue(
        input.Specification, input.Family, input.RootProof, input.Start, input.ParentCandidateId);

    private static FlatStartDesignProblem Problem(Input input) => new(input.Specification, input.Family.ElementCount, input.Start, input.Family);

    private static Input Load(string id)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OptilandWorkbench.slnx"))) directory = directory.Parent;
        var fixture = JsonSerializer.Deserialize<Fixture>(File.ReadAllText(Path.Combine(directory!.FullName,
            "labs/InitialStructure/benchmarks/field-continuation-20260927/restart-inputs.json")), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
                Converters = { new JsonStringEnumConverter() }
            })!;
        return fixture.Cases.Single(input => input.TrialId == id);
    }

    private sealed record Fixture(Input[] Cases);
    private sealed record Input(string TrialId, InitialStructureSpecification Specification, FlatStartFamily Family,
        OpticSnapshot Start, FlatStartBootstrapResult RootProof, string ParentCandidateId);
}
