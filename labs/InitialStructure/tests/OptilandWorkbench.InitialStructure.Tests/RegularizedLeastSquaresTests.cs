using OptilandWorkbench.InitialStructure.Engine.Optimization;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class RegularizedLeastSquaresTests
{
    [Fact]
    public void PivotedCompressedModelMatchesIndependentRidgeSolution()
    {
        double[,] matrix = { { 1, 2 }, { 3, 4 }, { 5, 6 } };
        var copy = (double[,])matrix.Clone();
        var model = new RegularizedLeastSquaresModel(matrix, [1, 0, -1]);
        var step = model.Solve(2);
        Assert.Equal(56.0 / 210, step[0], 12);
        Assert.Equal(-28.0 / 210, step[1], 12);
        Assert.Equal(copy.Cast<double>(), matrix.Cast<double>());
        Assert.Equal(2, model.Rank);
    }

    [Fact]
    public void RankDeficientStepTreatsEquivalentColumnsSymmetricallyWithinTrustRadius()
    {
        var model = new RegularizedLeastSquaresModel(new double[,] { { 1, 1, 0 } }, [-3]);
        Assert.Equal(1, model.Rank);
        var unconstrained = model.Step(10, out _);
        Assert.Equal(1.5, unconstrained[0], 8);
        Assert.Equal(unconstrained[0], unconstrained[1], 12);
        Assert.Equal(0, unconstrained[2]);
        var constrained = model.Step(.2, out var solves);
        var norm = constrained.Aggregate(0.0, double.Hypot);
        Assert.InRange(norm, .18, .2);
        Assert.True(solves > 0);
        Assert.Equal(constrained[0], constrained[1], 12);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegularizedSolverHandlesNonlinearValleyActiveBoundsAndUnderdeterminedData(bool scale)
    {
        var options = new LeastSquaresOptions { StepMethod = LeastSquaresStepMethod.Regularized, ScaleByJacobian = scale };
        var valley = Solve([-1.2, 1], [-10, -10], [10, 10],
            x => [10 * (x[1] - x[0] * x[0]), 1 - x[0]], options);
        Assert.Equal(1, valley.Variables[0], 7);
        Assert.Equal(1, valley.Variables[1], 7);
        Assert.True(valley.EvaluationCount < 500);
        var bounded = Solve([0, 0], [0, -10], [10, 10], x => [x[0] + x[1] + 1, x[1] - 3], options);
        Assert.Equal(0, bounded.Variables[0]);
        Assert.Equal(1, bounded.Variables[1], 7);
        var deficient = Solve([0, 0, 0], [-10, -10, -10], [10, 10, 10], x => [x[0] + x[1] - 3], options);
        Assert.Equal(1.5, deficient.Variables[0], 7);
        Assert.Equal(1.5, deficient.Variables[1], 7);
        Assert.Equal(0, deficient.Variables[2]);
        Assert.Equal(LeastSquaresTermination.Stationary, deficient.Termination);
    }

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(1e3, 1e-3)]
    [InlineData(1e-3, 1e3)]
    public void JacobianScalingSolvesEquivalentProblemsWithDifferentVariableUnits(double a, double b)
    {
        var result = Solve([0, 0], [-10 / a, -10 / b], [10 / a, 10 / b],
            x => [a * x[0] + b * x[1] - 3, a * x[0] - b * x[1] + 1],
            new() { StepMethod = LeastSquaresStepMethod.Regularized, ScaleByJacobian = true, InitialRadius = .2 });
        Assert.Equal(1, a * result.Variables[0], 7);
        Assert.Equal(2, b * result.Variables[1], 7);
        Assert.True(result.EvaluationCount < 100);
        Assert.All(result.Trials.Where(t => t.Accepted), t => Assert.True(t.ActualReduction > 0));
    }

    [Fact]
    public void RejectionsReuseOpticalJacobianAndAllProbesRespectBudgetAndCancellation()
    {
        var calls = 0;
        var result = BoundedTrustRegionLeastSquares.Solve([.01], [-100], [100], (x, _) =>
        {
            calls++;
            return new([x[0] * x[0] - 1], WorkUnits: 7);
        }, new() { StepMethod = LeastSquaresStepMethod.Regularized, MaximumEvaluations = 6, InitialRadius = 100 });
        Assert.Equal(6, calls);
        Assert.Equal(42, result.WorkUnits);
        Assert.Equal(1, result.JacobianBuildCount);
        Assert.Equal(2, result.DifferenceEvaluationCount);
        Assert.All(result.Trials, t => Assert.False(t.Accepted));
        Assert.True(result.FactorizationCount > result.JacobianBuildCount);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => BoundedTrustRegionLeastSquares.Solve([0], [-1], [1],
            (_, _) => throw new InvalidOperationException("No calls after cancellation."),
            new() { StepMethod = LeastSquaresStepMethod.Regularized }, cancellation.Token));
    }

    private static LeastSquaresResult Solve(double[] initial, double[] lower, double[] upper,
        Func<IReadOnlyList<double>, IReadOnlyList<double>> residuals, LeastSquaresOptions options) =>
        BoundedTrustRegionLeastSquares.Solve(initial, lower, upper, (x, _) => new(residuals(x)), options);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidPropagationRemainsInvalidAndFixedCoordinatesAreNotProbed(bool isolated)
    {
        var result = BoundedTrustRegionLeastSquares.Solve([.1, 2], [-10, 2], [10, 2], (x, _) =>
        {
            Assert.Equal(2, x[1]);
            Assert.InRange(x[0], -10, 10);
            return (isolated ? x[0] != .1 : x[0] > 2) ? new([], false, 3) : new([x[0] * x[0] - 1], WorkUnits: 3);
        }, new() { StepMethod = LeastSquaresStepMethod.Regularized, ScaleByJacobian = true, InitialRadius = 10 });
        Assert.Equal(3L * result.EvaluationCount, result.WorkUnits);
        if (isolated)
        {
            Assert.Equal(LeastSquaresTermination.DerivativeUnavailable, result.Termination);
            Assert.Equal(.1, result.Variables[0]);
            Assert.Empty(result.Trials);
        }
        else
        {
            Assert.Equal(1, result.Variables[0], 7);
            Assert.Contains(result.Trials, trial => trial.ReductionRatio is null && !trial.Accepted);
        }
    }
}
