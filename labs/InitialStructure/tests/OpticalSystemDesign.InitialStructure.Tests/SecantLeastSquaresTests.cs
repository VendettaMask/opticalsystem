using OpticalSystemDesign.InitialStructure.Engine.Optimization;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using System.Text.Json;
using Xunit.Abstractions;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class SecantLeastSquaresTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(6)]
    [InlineData(9)]
    public void FrozenOpticalCasesReachOriginalTargetsWithFreshCoreValidation(int index)
        => ValidateOpticalCase(index, 0, false);

    [Theory]
    [InlineData(-1e-12)]
    [InlineData(1e-12)]
    public void TinyInitialRoundoffDoesNotLoseTheOriginalFullTarget(double perturbation)
        => ValidateOpticalCase(9, perturbation, true);

    private void ValidateOpticalCase(int index, double perturbation, bool strictRequestedTarget)
    {
        var input = FlatStartQualityRegressionTests.Inputs()[index];
        // Preserve the original frozen-case inputs and 0.03 mm assertion.
        // The additional roundoff controls explicitly request that stricter
        // target instead of depending on the frozen spec's 0.05 mm headroom.
        var spec = FlatStartQualityRegressionTests.Upgrade(input.Specification);
        if (strictRequestedTarget) spec = spec with { MaximumRmsSpotRadiusMillimeters = .03 };
        var problem = new FlatStartDesignProblem(spec, input.Family.ElementCount, input.Initial, input.Family);
        var initial = problem.Vector(input.Initial);
        for (var column = 0; column < initial.Length; column++)
            initial[column] += perturbation * Math.Max(1, Math.Abs(initial[column]));
        var best = Evaluate(initial);
        var updates = 0;
        var result = FlatStartLocalSolver.Solve(problem.SolverCoordinates, initial, best, 600, Evaluate,
            true, () => false, default, (_, evaluation, _) =>
            {
                var rank = Rank(evaluation); var previous = Rank(best);
                if (rank < previous || rank == previous && evaluation.Merit < best.Merit) best = evaluation;
            }, (_, statistics) => updates += statistics.SecantUpdateCount);
        var fresh = problem.Evaluate(Optic.FromSnapshot(best.EvaluatedOptic!), FlatStartDesignProblem.FullStage, true, default, true);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            index,
            perturbation,
            strictRequestedTarget,
            result.Evaluations,
            result.Termination,
            updates,
            fresh.MeetsTargets,
            fresh.GeometryFeasible,
            maximumRms = fresh.Fields.Max(field => field.RmsRadiusMillimeters),
            fresh.Violations
        }));
        Assert.InRange(result.Evaluations, 1, 600);
        Assert.True(updates > 0);
        Assert.True(fresh.MeetsTargets);
        Assert.True(fresh.GeometryFeasible);
        Assert.True(fresh.Objective!.IndependentValidation);
        Assert.InRange(fresh.Fields.Max(f => f.RmsRadiusMillimeters)!.Value, 0, .03);
        Assert.All(fresh.Fields, f => Assert.Equal(f.CheckAttemptedRays, f.CheckValidRays));
        DesignEvaluation Evaluate(double[] vector) => problem.Evaluate(problem.CreateOptic(vector, FlatStartDesignProblem.FullStage),
            FlatStartDesignProblem.FullStage, true, default);
        static int Rank(DesignEvaluation e) => e.MeetsTargets ? 0 : e.IsFeasible ? 1 : 2;
    }

    [Theory]
    [InlineData("13")]
    [InlineData("19")]
    [InlineData("20")]
    public async Task PreviousAreaVersionRemainsReadableWithItsScheduleButRequiresSeparateRefinement(string version)
    {
        var spec = FlatStartQualityRegressionTests.Upgrade(FlatStartRefinementTests.Spec());
        using var stop = new CancellationTokenSource(); stop.Cancel();
        var current = (await new FlatStartSearchService().RunAsync(spec, cancellationToken: stop.Token)).Checkpoint;
        var previous = current with { Algorithm = current.Algorithm with { Version = version } };
        Assert.Equal(2, previous.Schedule!.PolicyVersion);
        FlatStartCheckpointValidation.Validate(previous);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new FlatStartSearchService().RunAsync(spec, previous.Options, checkpoint: previous));
        Assert.Contains("historical search version", error.Message);
    }

    [Theory]
    [InlineData(FlatStartOptimizationMethod.AutoLensCpuExperimentV1)]
    [InlineData(FlatStartOptimizationMethod.AutoLensManagedV1)]
    public async Task Version20ExperimentalRecordsRemainReadableWithoutSilentResume(FlatStartOptimizationMethod method)
    {
        var spec = method == FlatStartOptimizationMethod.AutoLensManagedV1
            ? AutoLensManagedTests.Specification() : AutoLensCpuTests.Specification();
        using var stop = new CancellationTokenSource(); stop.Cancel();
        var current = (await new FlatStartSearchService().RunAsync(spec, cancellationToken: stop.Token)).Checkpoint;
        var previous = current with { Algorithm = current.Algorithm with { Version = "20" } };
        FlatStartCheckpointValidation.Validate(previous);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new FlatStartSearchService().RunAsync(spec,
            previous.Options, checkpoint: previous));
        Assert.Contains("historical search version", error.Message);
        Assert.Throws<InvalidDataException>(() => FlatStartCheckpointValidation.Validate(previous with
        { Algorithm = previous.Algorithm with { Version = "19" } }));
    }

    [Fact]
    public void OneUlpAboveTheBoundResolutionCannotFreezeAnIndependentCoordinate()
    {
        var result = BoundedTrustRegionLeastSquares.Solve([.5, double.BitIncrement(1e-12)], [0, 0], [1, 1],
            (point, _) => new([point[0] - .8, point[1] + 1]),
            new() { InitialRadius = .2, StepMethod = LeastSquaresStepMethod.Regularized, MaximumSecantUpdates = 4 });
        Assert.Equal(LeastSquaresTermination.Stationary, result.Termination);
        Assert.Equal(.8, result.Variables[0], 8);
        Assert.InRange(result.Variables[1], 0, 1.01e-12);
        Assert.Contains(result.Trials, trial => trial.Accepted && trial.ActualReduction > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScaledLinearSystemSavesDifferenceCallsAndConfirmsStationarity(bool scaled)
    {
        var fresh = Solve(0);
        var secant = Solve(4);
        Assert.Equal(LeastSquaresTermination.Stationary, secant.Termination);
        Assert.InRange(secant.Evaluation!.Residuals.Sum(r => r * r), 0, 1e-16);
        Assert.True(secant.DifferenceEvaluationCount < fresh.DifferenceEvaluationCount);
        Assert.True(secant.SecantUpdateCount > 0);
        Assert.All(secant.Trials, t => Assert.True(t.Accepted && t.ActualReduction > 0));
        LeastSquaresResult Solve(int updates) => BoundedTrustRegionLeastSquares.Solve([0, 0, 2], [-10, -10, 2], [10, 10, 2],
            (x, _) => new([3 * (x[0] - 3), .02 * (x[1] + 2), x[2] - 2]),
            new() { InitialRadius = .01, ScaleByJacobian = scaled, StepMethod = LeastSquaresStepMethod.Regularized, MaximumSecantUpdates = updates });
    }

    [Fact]
    public void NonlinearBoundedProblemRefreshesRejectedModelsAndOnlyAcceptsRealDescent()
    {
        var calls = 0;
        var result = BoundedTrustRegionLeastSquares.Solve([-1.2, 1], [-2, -2], [2, 2], (x, _) =>
        { calls++; return new([10 * (x[1] - x[0] * x[0]), 1 - x[0]], WorkUnits: 7); },
            new() { MaximumEvaluations = 600, StepMethod = LeastSquaresStepMethod.Regularized, ScaleByJacobian = true, MaximumSecantUpdates = 4 });
        Assert.Equal(LeastSquaresTermination.Stationary, result.Termination);
        Assert.InRange(result.Evaluation!.Residuals.Sum(r => r * r), 0, 1e-14);
        Assert.True(result.SecantUpdateCount > 0);
        Assert.True(result.JacobianBuildCount > 1);
        Assert.Contains(result.Trials, t => !t.Accepted);
        Assert.All(result.Trials.Where(t => t.Accepted), t => Assert.True(t.ActualReduction > 0));
        Assert.Equal(calls, result.EvaluationCount);
        Assert.Equal(7L * calls, result.WorkUnits);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(30)]
    public void InvalidProbesAndPartialModelsStillConsumeTheExactBudget(int budget)
    {
        var calls = 0;
        var result = BoundedTrustRegionLeastSquares.Solve([.05, .5], [0, 0], [10, 10], (x, _) =>
        {
            calls++;
            return x[0] > 1 ? new([], false, 11) : new([Math.Exp(x[0]) - 8, x[1] - 2], WorkUnits: 11);
        }, new() { MaximumEvaluations = budget, InitialRadius = .1, StepMethod = LeastSquaresStepMethod.Regularized, MaximumSecantUpdates = 4 });
        Assert.InRange(calls, 1, budget);
        Assert.Equal(calls, result.EvaluationCount);
        Assert.Equal(11L * calls, result.WorkUnits);
        Assert.True(result.Evaluation!.IsValid);
        Assert.InRange(result.Variables[0], 0, 1);
        Assert.All(result.Trials.Where(t => t.Accepted), t => Assert.True(t.ActualReduction > 0));
    }
}
