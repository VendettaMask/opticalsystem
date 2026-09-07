using OptilandWorkbench.InitialStructure.Engine.Optimization;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class BoundedTrustRegionLeastSquaresTests
{
    [Fact]
    public void LinearModelPredictsActualReductionAndExpandsTheTrustRegion()
    {
        var result = Solve([0, 0], x => [x[0] - 3, 2 * (x[1] + 2)],
            options: new() { InitialRadius = .1 });
        Assert.Equal(LeastSquaresTermination.Stationary, result.Termination);
        Assert.Equal(3, result.Variables[0], 8);
        Assert.Equal(-2, result.Variables[1], 8);
        Assert.NotEmpty(result.Trials);
        Assert.All(result.Trials, trial =>
        {
            Assert.True(trial.Accepted);
            Assert.Equal(1, trial.ReductionRatio!.Value, 7);
            Assert.Equal(trial.PredictedReduction, trial.ActualReduction!.Value, 7);
            Assert.InRange(trial.TrialPoint.Zip(trial.BasePoint, (a, b) => Math.Abs(a - b)).Max(), 0, trial.Radius * (1 + 1e-12));
        });
        Assert.Contains(result.Trials, trial => trial.NextRadius > trial.Radius);
    }

    [Fact]
    public void RejectedStepsReuseJacobianAndFactorizationAndChargeEveryCall()
    {
        var calls = 0;
        var result = BoundedTrustRegionLeastSquares.Solve([.01], [-100], [100], (x, _) =>
        {
            calls++;
            return new([x[0] * x[0] - 1], WorkUnits: 7);
        }, new() { MaximumEvaluations = 6, InitialRadius = 100 });
        Assert.Equal(LeastSquaresTermination.EvaluationLimit, result.Termination);
        Assert.Equal(6, calls);
        Assert.Equal(calls, result.EvaluationCount);
        Assert.Equal(42, result.WorkUnits);
        Assert.Equal(2, result.DifferenceEvaluationCount);
        Assert.Equal(1, result.JacobianBuildCount);
        Assert.Equal(1, result.FactorizationCount);
        Assert.Equal(3, result.Trials.Count);
        Assert.All(result.Trials, trial =>
        {
            Assert.False(trial.Accepted);
            Assert.Equal(1, trial.ModelNumber);
            Assert.Equal(.01, trial.BasePoint[0]);
            Assert.True(trial.NextRadius < trial.Radius);
        });
        Assert.Equal(.01, result.Variables[0]);
    }

    [Fact]
    public void RosenbrockValleyConvergesWithoutAProblemSpecificSchedule()
    {
        var result = Solve([-1.2, 1], x => [10 * (x[1] - x[0] * x[0]), 1 - x[0]]);
        Assert.Equal(LeastSquaresTermination.Stationary, result.Termination);
        Assert.Equal(1, result.Variables[0], 7);
        Assert.Equal(1, result.Variables[1], 7);
        Assert.Contains(result.Trials, trial => !trial.Accepted);
        Assert.True(result.EvaluationCount < 250);
    }

    [Fact]
    public void ActiveBoundIsRemovedFromTheCoupledLeastSquaresSystem()
    {
        // x >= 0; constrained minimizer is (0,1), whereas clipping (-4,3) gives (0,3).
        var result = Solve([0, 0], x => [x[0] + x[1] + 1, x[1] - 3], lower: [0, -10], upper: [10, 10]);
        Assert.Equal(LeastSquaresTermination.Stationary, result.Termination);
        Assert.Equal(0, result.Variables[0]);
        Assert.Equal(1, result.Variables[1], 7);
        Assert.Equal(8, result.Evaluation!.Residuals.Sum(value => value * value), 8);
    }

    [Fact]
    public void AVariableCanLeaveItsBoundWhenTheGradientPointsInward()
    {
        var result = Solve([0, 4], x => [x[0] - 2, x[1] - 1], lower: [0, 0], upper: [4, 4]);
        Assert.Equal(LeastSquaresTermination.Stationary, result.Termination);
        Assert.Equal(2, result.Variables[0], 8);
        Assert.Equal(1, result.Variables[1], 8);
    }

    [Fact]
    public void FixedVariablesAreNotProbed()
    {
        var observed = new List<double[]>();
        var result = Solve([2, 0], x =>
        {
            observed.Add(x.ToArray());
            return [x[0] + x[1] - 5];
        }, lower: [2, -10], upper: [2, 10]);
        Assert.Equal(LeastSquaresTermination.Stationary, result.Termination);
        Assert.Equal(3, result.Variables[1], 8);
        Assert.All(observed, point => Assert.Equal(2, point[0]));
        Assert.Equal(2 * result.JacobianBuildCount, result.DifferenceEvaluationCount);
    }

    [Fact]
    public void AllFixedVariablesReturnStationarityWithoutClaimingTargetsAreMet()
    {
        var result = Solve([1], _ => [3], lower: [1], upper: [1]);
        Assert.Equal(LeastSquaresTermination.Stationary, result.Termination);
        Assert.Equal(1, result.EvaluationCount);
        Assert.Equal(0, result.DifferenceEvaluationCount);
        Assert.Equal(0, result.FactorizationCount);
        Assert.Equal(3, result.Evaluation!.Residuals[0]);
    }

    [Fact]
    public void InconsistentResidualTargetsRemainVisibleAtTheLocalMinimum()
    {
        var result = Solve([0], x => [x[0] - 1, x[0] + 1]);
        Assert.Equal(LeastSquaresTermination.Stationary, result.Termination);
        Assert.Equal(0, result.Variables[0]);
        Assert.Equal(2, result.Evaluation!.Residuals.Sum(value => value * value));
    }

    [Fact]
    public void RankDeficientAndUnderdeterminedProblemsRetainTheirResidualSolution()
    {
        var deficient = Solve([0, 0], x => [x[0] + x[1] - 3, 2 * (x[0] + x[1] - 3)]);
        var underdetermined = Solve([0, 0, 0], x => [x[0] + 2 * x[1] + x[2] - 4]);
        Assert.Equal(LeastSquaresTermination.Stationary, deficient.Termination);
        Assert.Equal(LeastSquaresTermination.Stationary, underdetermined.Termination);
        Assert.Equal(3, deficient.Variables[0] + deficient.Variables[1], 8);
        Assert.Equal(4, underdetermined.Variables[0] + 2 * underdetermined.Variables[1] + underdetermined.Variables[2], 8);
    }

    [Fact]
    public void OneSidedDerivativeIsUsedAtAnEvaluationDomainBoundary()
    {
        var invalidCalls = 0;
        var result = BoundedTrustRegionLeastSquares.Solve([0.0], [-10], [10], (x, _) =>
        {
            if (x[0] < 0) { invalidCalls++; return new([], false, 3); }
            return new([x[0] - 2], WorkUnits: 3);
        });
        Assert.Equal(LeastSquaresTermination.Stationary, result.Termination);
        Assert.Equal(2, result.Variables[0], 8);
        Assert.True(invalidCalls > 0);
        Assert.Equal(3L * result.EvaluationCount, result.WorkUnits);
    }

    [Fact]
    public void InvalidTrialsShrinkTheRadiusInsteadOfBecomingFalseImprovements()
    {
        var result = BoundedTrustRegionLeastSquares.Solve([.1], [-10], [10], (x, _) =>
            x[0] > 2 ? new([], false, 11) : new([x[0] * x[0] - 1], WorkUnits: 11),
            new() { InitialRadius = 10 });
        Assert.Equal(LeastSquaresTermination.Stationary, result.Termination);
        Assert.Equal(1, result.Variables[0], 8);
        Assert.Contains(result.Trials, trial => trial.ReductionRatio is null && !trial.Accepted);
        Assert.All(result.Trials.Where(trial => trial.ReductionRatio is null), trial => Assert.True(trial.NextRadius < trial.Radius));
        Assert.Equal(11L * result.EvaluationCount, result.WorkUnits);
    }

    [Fact]
    public void IsolatedValidPointDoesNotProduceAFabricatedZeroDerivative()
    {
        var result = BoundedTrustRegionLeastSquares.Solve([0.0], [-1], [1], (x, _) =>
            x[0] == 0 ? new([1]) : new([], false));
        Assert.Equal(LeastSquaresTermination.DerivativeUnavailable, result.Termination);
        Assert.Equal(9, result.EvaluationCount);
        Assert.Equal(0, result.JacobianBuildCount);
        Assert.Empty(result.Trials);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.MaxValue)]
    public void NonFiniteCostAtTheInitialPointIsReportedAsInvalid(double value)
    {
        var result = Solve([0], _ => [value]);
        Assert.Equal(LeastSquaresTermination.InvalidInitialEvaluation, result.Termination);
        Assert.Equal(1, result.EvaluationCount);
        Assert.Empty(result.Trials);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void BudgetCanEndInsideAColumnWithoutOverrunOrAcceptingAProbe(int budget)
    {
        var calls = 0;
        var result = Solve([0, 0, 0], x => { calls++; return [x[0] - 1, x[1] - 2, x[2] - 3]; },
            options: new() { MaximumEvaluations = budget });
        Assert.Equal(LeastSquaresTermination.EvaluationLimit, result.Termination);
        Assert.Equal(budget, calls);
        Assert.Equal(budget, result.EvaluationCount);
        Assert.Equal(new[] { 0.0, 0, 0 }, result.Variables);
        Assert.Equal(0, result.JacobianBuildCount);
        Assert.Empty(result.Trials);
    }

    [Fact]
    public void CancellationIsCheckedBeforeEachDerivativeEvaluation()
    {
        using var source = new CancellationTokenSource();
        var calls = 0;
        Assert.Throws<OperationCanceledException>(() => BoundedTrustRegionLeastSquares.Solve([0.0], [-10], [10], (x, token) =>
        {
            Assert.Equal(source.Token, token);
            if (++calls == 2) source.Cancel();
            return new([x[0] - 1]);
        }, cancellationToken: source.Token));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void PreCancellationMakesNoEvaluation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => BoundedTrustRegionLeastSquares.Solve([0.0], [-1], [1],
            (_, _) => throw new Xunit.Sdk.XunitException("Cancelled work must not start."), cancellationToken: source.Token));
    }

    [Fact]
    public void ExternalStopCanInterruptAColumnAndRetainsItsChargedCosts()
    {
        var calls = 0;
        var result = BoundedTrustRegionLeastSquares.Solve([0.0], [-10], [10], (x, _) =>
        {
            calls++;
            return new([x[0] - 1], WorkUnits: 9);
        }, stopRequested: () => calls == 2);
        Assert.Equal(LeastSquaresTermination.StopRequested, result.Termination);
        Assert.Equal(2, result.EvaluationCount);
        Assert.Equal(18, result.WorkUnits);
        Assert.Equal(0, result.Variables[0]);
    }

    [Fact]
    public void ResidualShapeChangesAreRejectedInsteadOfReusingAnIncompatibleModel()
    {
        var calls = 0;
        Assert.Throws<InvalidOperationException>(() => Solve([0], x => ++calls == 1 ? [x[0] - 1] : [x[0] - 1, 0]));
    }

    [Fact]
    public void EvaluatorScratchStorageCannotMutateTheCurrentResidualVector()
    {
        double[] scratch = [0, 0];
        var result = Solve([0, 0], x =>
        {
            scratch[0] = x[0] - 3;
            scratch[1] = x[1] + 1;
            return scratch;
        });
        Assert.Equal(LeastSquaresTermination.Stationary, result.Termination);
        Assert.Equal(3, result.Variables[0], 8);
        Assert.Equal(-1, result.Variables[1], 8);
        scratch[0] = 900;
        Assert.InRange(Math.Abs(result.Evaluation!.Residuals[0]), 0, 1e-8);
    }

    [Fact]
    public void SameInputsProduceTheSameDecisionsAndCosts()
    {
        var first = Solve([-1.2, 1], x => [10 * (x[1] - x[0] * x[0]), 1 - x[0]]);
        var second = Solve([-1.2, 1], x => [10 * (x[1] - x[0] * x[0]), 1 - x[0]]);
        Assert.Equal(first.Variables, second.Variables);
        Assert.Equal(first.EvaluationCount, second.EvaluationCount);
        Assert.Equal(first.DifferenceEvaluationCount, second.DifferenceEvaluationCount);
        Assert.Equal(first.Trials.Select(trial => (trial.ModelNumber, trial.Radius, trial.ReductionRatio, trial.Accepted)),
            second.Trials.Select(trial => (trial.ModelNumber, trial.Radius, trial.ReductionRatio, trial.Accepted)));
    }

    [Fact]
    public void NarrowBoundsUseUnequalProbeDistancesAndAreNeverViolated()
    {
        var result = Solve([1e-7], x =>
        {
            Assert.InRange(x[0], 0, 1e-6);
            return [1e6 * (x[0] - 8e-7)];
        }, lower: [0], upper: [1e-6]);
        // At this scale a last-bit residual can leave |J'r| above the gradient tolerance even
        // though the remaining variable step is below machine resolution. Both stops are honest.
        Assert.True(result.Termination is LeastSquaresTermination.Stationary or LeastSquaresTermination.StepTooSmall);
        Assert.Equal(8e-7, result.Variables[0], 14);
    }

    [Fact]
    public void NearlyDependentResidualsConvergeUsingTheFullSolver()
    {
        var result = Solve([0, 0], x =>
            [x[0] + x[1] - 3, x[0] + (1 + 1e-7) * x[1] - (3 + 2e-7),
                x[0] + (1 - 1e-7) * x[1] - (3 - 2e-7)],
            options: new() { GradientTolerance = 1e-16 });
        Assert.True(result.Termination is LeastSquaresTermination.Stationary or LeastSquaresTermination.StepTooSmall);
        Assert.InRange(Math.Abs(result.Variables[0] - 1), 0, 1e-6);
        Assert.InRange(Math.Abs(result.Variables[1] - 2), 0, 1e-6);
        Assert.All(result.Evaluation!.Residuals, value => Assert.InRange(Math.Abs(value), 0, 1e-12));
    }

    [Fact]
    public void DifferenceProbesDoNotSendOverflowedCoordinatesToTheEvaluator()
    {
        var result = Solve([double.MaxValue], x =>
        {
            Assert.True(double.IsFinite(x[0]));
            return [x[0] / 1e308 - 1];
        });
        Assert.Equal(LeastSquaresTermination.Stationary, result.Termination);
        Assert.Equal(2, result.EvaluationCount);
    }

    [Fact]
    public void InvalidBoundsAndOptionsAreRejectedBeforeEvaluation()
    {
        Assert.Throws<ArgumentException>(() => Solve([0], _ => throw new InvalidOperationException(), lower: [1], upper: [-1]));
        Assert.Throws<ArgumentException>(() => Solve([2], _ => throw new InvalidOperationException(), lower: [0], upper: [1]));
        Assert.Throws<ArgumentException>(() => Solve([0], _ => throw new InvalidOperationException(), options: new() { MaximumEvaluations = 0 }));
        Assert.Throws<ArgumentException>(() => Solve([0], _ => throw new InvalidOperationException(), options: new() { InitialRadius = double.NaN }));
    }

    private static LeastSquaresResult Solve(double[] initial, Func<IReadOnlyList<double>, IReadOnlyList<double>> residuals,
        double[]? lower = null, double[]? upper = null, LeastSquaresOptions? options = null) =>
        BoundedTrustRegionLeastSquares.Solve(initial,
            lower ?? Enumerable.Repeat(double.NegativeInfinity, initial.Length).ToArray(),
            upper ?? Enumerable.Repeat(double.PositiveInfinity, initial.Length).ToArray(),
            (x, _) => new(residuals(x)), options);
}
