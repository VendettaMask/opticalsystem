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
        Assert.Equal(FlatStartDesignProblem.FullStage, stages[0]);
        Assert.All(stages, stage => Assert.True(stage.AllWavelengths));
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
    [InlineData("4")]
    [InlineData("6")]
    [InlineData("7")]
    [InlineData("8")]
    [InlineData("9")]
    [InlineData("10")]
    [InlineData("11")]
    [InlineData("12")]
    [InlineData("13")]
    [InlineData("14")]
    [InlineData("15")]
    [InlineData("16")]
    public async Task HistoricalVersionCanBeReadButCannotSilentlyResumeWithChangedSolver(string version)
    {
        var spec = FlatStartRefinementTests.Spec();
        var options = FlatStartRefinementTests.Options;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var current = (await new FlatStartSearchService().RunAsync(spec, options, cancelled.Token)).Checkpoint;
        // Synthetic schema compatibility fixture using the historical root planner.
        // Merely relabeling a v8 binary plan as v7 would be a corrupt checkpoint.
        var roots = version is "8" or "9" or "10" or "11" or "12" or "13" or "14" or "15" or "16" ? DesignFormSearch.Roots(spec, options, current.UsableGlassNames)
            : FlatStartSearchPlanning.Roots(spec, current.UsableGlassNames);
        var quota = version is "8" or "9" or "10" or "11" or "12" or "13" or "14" or "15" or "16" ? DesignFormSearch.RootQuota(spec, options, roots.Count)
            : version is "6" or "7" ? FlatStartScheduler.Allocation(spec, options, "flat-root", spec.MaximumElementCount)
            : Math.Min(options.MaximumEvaluationsPerTrial, Math.Max(2, (int)(.55 * spec.Budget.MaximumEvaluations)));
        var design = new FlatStartDesignService().Solve(spec with { Budget = spec.Budget with { MaximumEvaluations = quota } }, 3, family: roots[0]);
        var trial = new FamilyTrial
        {
            TrialId = "trial-0000",
            Family = roots[0],
            State = FamilyTrialState.Completed,
            AllocatedEvaluations = quota,
            ChargedEvaluations = design.EvaluationCount,
            CompletedEvaluations = design.EvaluationCount,
            DesignState = design.State,
            BootstrapProof = design.Bootstrap,
            FlatRoot = design.Bootstrap.Steps[0].Optic,
            Candidate = version is "9" or "10" or "11" or "12" or "13" or "14" or "15" or "16" ? design.Candidate : design.Candidate! with { Evaluation = design.Candidate.Evaluation with { FlatStartObjective = null } },
            FinalValidation = version is "9" or "10" or "11" or "12" or "13" or "14" or "15" or "16" ? design.FinalValidation : design.FinalValidation! with { Objective = null }
        };
        var history = current with
        {
            Algorithm = current.Algorithm with { Version = version },
            Schedule = version is "6" or "7" or "8" or "9" or "10" or "11" or "12" or "13" or "14" or "15" or "16" ? current.Schedule : null,
            RootPlan = roots,
            RootEvaluationQuota = quota,
            NextRootIndex = 1,
            Trials = [trial]
        };
        FlatStartCheckpointValidation.Validate(history);
        await Assert.ThrowsAsync<InvalidDataException>(() => new FlatStartSearchService().RunAsync(history.Specification,
            history.Options, checkpoint: history));
        var child = FlatStartSearchService.CreateRefinementCheckpoint(history, design.Candidate!.CandidateId, 120, TimeSpan.FromMinutes(1));
        Assert.Equal(FlatStartAlgorithm.Version, child.Algorithm.Version);
        Assert.Equal(history.RunId, child.Origin!.RunId);
        if (version == "8")
        {
            var hash = ContentFingerprint.Compute(history);
            var result = await new FlatStartSearchService().RunAsync(child.Specification, child.Options, checkpoint: child);
            FlatStartCheckpointValidation.Validate(result.Checkpoint);
            Assert.NotEmpty(result.Candidates);
            Assert.All(result.Candidates, candidate => Assert.NotNull(candidate.Evaluation.FlatStartObjective));
            Assert.Null(result.Checkpoint.Origin!.Source.Candidate!.Evaluation.FlatStartObjective);
            Assert.Equal(hash, ContentFingerprint.Compute(history));
        }
    }
}
