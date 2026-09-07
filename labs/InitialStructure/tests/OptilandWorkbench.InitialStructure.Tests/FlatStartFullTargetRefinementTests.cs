using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class FlatStartFullTargetRefinementTests
{
    [Fact]
    public void ActiveBoundIsResolvedBeforeCoupledStepIsProjected()
    {
        // min (x+y+1)^2 + (y-3)^2, x >= 0: constrained solution (0,1).
        // Clipping the unconstrained solution (-4,3) to (0,3) increases the objective.
        double[,] jacobian = { { 1, 1 }, { 0, 1 } };
        var step = ProjectedDampedStep.Solve(jacobian, [1, -3], 1e-9, [0, 0],
            values => [Math.Max(0, values[0]), values[1]]);
        Assert.Equal(0, step[0]);
        Assert.Equal(1, step[1], 7);
        Assert.True(Math.Pow(step[0] + step[1] + 1, 2) + Math.Pow(step[1] - 3, 2) < 10);
        Assert.Equal(1, jacobian[0, 0]);
    }

    [Theory]
    [InlineData("03-50mm-monochrome.json")]
    [InlineData("04-75mm-visible.json")]
    public void FeasibleRestartUsesFullDenseTargetsAndRetainsTheIncumbent(string name)
    {
        var spec = FlatStartDesignTests.LoadSpecification(name);
        var source = new FlatStartDesignService().Solve(spec, spec.MinimumElementCount);
        Assert.True(source.FinalValidation!.IsFeasible);
        var stages = source.Steps.Where(step => step.Operation == "stage-entry").Select(step => step.Stage).ToArray();
        Assert.NotEmpty(stages);
        if (spec.Wavelengths.Any(wave => !wave.IsPrimary && wave.Weight > 0))
        {
            Assert.All(stages, stage => { Assert.Equal(1, stage.PupilFraction); Assert.True(stage.AllWavelengths); });
            Assert.Equal(new[] { .25, .5, 1 }, stages.Select(stage => stage.FieldFraction));
        }
        else Assert.Equal(new DesignStage(.6, .25, false), stages[0]);
        var n = spec.MinimumElementCount;
        var family = new FlatStartFamily
        {
            GlassNames = Enumerable.Repeat(spec.InitialGlass, n).ToArray(),
            CenterThicknesses = Enumerable.Repeat(spec.MinimumCenterThicknessMillimeters, n).ToArray(),
            AirGaps = Enumerable.Repeat(spec.MinimumAirGapMillimeters, n - 1).ToArray()
        };
        var request = spec with { Budget = spec.Budget with { MaximumEvaluations = 250 } };
        var result = new FlatStartDesignService().Continue(request, family, source.Bootstrap,
            source.Candidate!.Optic, source.Candidate.CandidateId, improveBeyondTargets: true);
        Assert.Equal("full-target-refinement-entry", result.Steps[0].Operation);
        Assert.All(result.Steps, step => Assert.Equal(FlatStartDesignProblem.FullStage, step.Stage));
        Assert.True(result.FinalValidation!.MeetsTargets);
        Assert.True(result.FinalValidation.Merit <= source.FinalValidation.Merit + 1e-10);
        Assert.All(result.FinalValidation.Fields, field =>
            Assert.Equal(97 * spec.Wavelengths.Count(wave => wave.Weight > 0), field.AttemptedRays));
        Assert.InRange(result.EvaluationCount, 1, 250);
        Assert.Equal(source.Candidate.Lineage.RootFingerprint, result.Candidate!.Lineage.RootFingerprint);
        Assert.Equal("full-target-dense-validation", result.Steps[^1].Operation);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("3")]
    public async Task HistoricalVersionCanBeReadButCannotSilentlyResumeWithChangedSolver(string version)
    {
        var result = await new FlatStartSearchService().RunAsync(FlatStartRefinementTests.Spec(), FlatStartRefinementTests.Options);
        var history = result.Checkpoint with { Algorithm = result.Checkpoint.Algorithm with { Version = version } };
        FlatStartCheckpointValidation.Validate(history);
        await Assert.ThrowsAsync<InvalidDataException>(() => new FlatStartSearchService().RunAsync(history.Specification,
            history.Options, checkpoint: history));
        var child = FlatStartSearchService.CreateRefinementCheckpoint(history, result.Candidates[0].CandidateId, 120, TimeSpan.FromMinutes(1));
        Assert.Equal("4", child.Algorithm.Version);
        Assert.Equal(history.RunId, child.Origin!.RunId);
    }
}
