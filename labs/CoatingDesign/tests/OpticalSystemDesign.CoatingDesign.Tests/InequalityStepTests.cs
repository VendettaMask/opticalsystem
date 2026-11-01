using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class InequalityStepTests
{
    [Fact]
    public void TangentProjectorPreservesTheNullspaceUnderRedundantAndRescaledNormals()
    {
        var a = InequalityLeastSquaresStep.TangentProjector(new double[,] { { 1, 2, 3 }, { 2, 4, 6 } });
        var b = InequalityLeastSquaresStep.TangentProjector(new double[,] { { 1e200, 2e200, 3e200 } });
        for (var i = 0; i < 3; i++) for (var j = 0; j < 3; j++)
        {
            Assert.InRange(Math.Abs(a[i, j] - b[i, j]), 0, 1e-12);
            Assert.InRange(Math.Abs(Enumerable.Range(0, 3).Sum(k => a[i, k] * a[k, j]) - a[i, j]), 0, 1e-12);
        }
        for (var j = 0; j < 3; j++) Assert.InRange(Math.Abs(a[0, j] + 2 * a[1, j] + 3 * a[2, j]), 0, 1e-12);
    }

    [Fact]
    public void CoupledUpperBoundFindsTheConstrainedMinimumRatherThanClippingAnUnconstrainedStep()
    {
        var result = InequalityLeastSquaresStep.Solve(new double[,] { { 1, 0 }, { 0, 1 } }, [-2, -2],
            new double[,] { { 1, 1 }, { -1, 0 }, { 0, -1 } }, [1, 0, 0], 1e-6);
        Assert.True(result.Converged); Assert.InRange(result.MaximumViolation, 0, 1e-12);
        Assert.InRange(Math.Abs(result.Step[0] - .5), 0, 1e-10); Assert.InRange(Math.Abs(result.Step[1] - .5), 0, 1e-10);
    }

    [Fact]
    public void DampingAlsoControlsColumnsPresentOnlyInTheInequalities()
    {
        var a = InequalityLeastSquaresStep.Solve(new double[,] { { 0, 1 } }, [1], new double[,] { { -1, -1 } }, [0], .001);
        var b = InequalityLeastSquaresStep.Solve(new double[,] { { 0, 1 } }, [1], new double[,] { { -1, -1 } }, [0], 1);
        Assert.True(a.Converged && b.Converged);
        Assert.True(a.Step[0] > b.Step[0]); Assert.True(Math.Abs(a.Step[1]) > Math.Abs(b.Step[1]));
        Assert.InRange(a.MaximumViolation, 0, 1e-12); Assert.InRange(b.MaximumViolation, 0, 1e-12);
    }

    [Fact]
    public void AnActiveProtectionCanReleaseInwardMotionAndRedundantRowsDoNotChangeTheAnswer()
    {
        var result = InequalityLeastSquaresStep.Solve(new double[,] { { 1, 0 }, { 0, 1 } }, [-1, 2],
            new double[,] { { -1, 0 }, { -2, 0 }, { 0, -1 }, { 0, 1 }, { 1, 0 } }, [0, 0, 0, 3, 4], 1e-6);
        Assert.True(result.Converged); Assert.InRange(result.Step[0], .99999, 1); Assert.InRange(Math.Abs(result.Step[1]), 0, 1e-10);
    }

    [Fact]
    public void ScalingResidualsAndInequalityRowsPreservesThePhysicalStep()
    {
        var a = InequalityLeastSquaresStep.Solve(new double[,] { { 1, 2 }, { 3, -1 } }, [-2, 1], new double[,] { { 1, 1 }, { -1, 0 } }, [.5, 0], .001);
        var b = InequalityLeastSquaresStep.Solve(new double[,] { { 7, 14 }, { 21, -7 } }, [-14, 7], new double[,] { { 3, 3 }, { -9, 0 } }, [1.5, 0], .001);
        Assert.True(a.Converged && b.Converged);
        for (var j = 0; j < 2; j++) Assert.InRange(Math.Abs(a.Step[j] - b.Step[j]), 0, 1e-10);
    }

    [Fact]
    public void NearlyParallelActiveBoundsRemainFeasibleWithoutSquaringTheirConditionNumber()
    {
        var result = InequalityLeastSquaresStep.Solve(new double[,] { { 1, 0 }, { 0, 1 } }, [-1, -2],
            new double[,] { { 1, 1 }, { 1, 1.000001 } }, [.5, .5000005], 1e-6);
        Assert.True(result.Converged); Assert.InRange(result.ActiveSetIterations, 1, 31);
        Assert.True(result.Step[0] + result.Step[1] <= .5 + 1e-10);
        Assert.True(result.Step[0] + 1.000001 * result.Step[1] <= .5000005 + 1e-10);
        Assert.InRange(Math.Abs(result.Step[0] + .25), 0, 1e-5); Assert.InRange(Math.Abs(result.Step[1] - .75), 0, 1e-5);
    }

    [Fact]
    public void InvalidOrInfeasibleModelsAreExplicitAndCancellationIsObserved()
    {
        Assert.Throws<ArgumentException>(() => InequalityLeastSquaresStep.Solve(new double[,] { { 1 } }, [1], new double[,] { { 1 } }, [-1], .001));
        Assert.Throws<ArgumentException>(() => InequalityLeastSquaresStep.Solve(new double[,] { { double.NaN } }, [1], new double[0, 1], [], .001));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.Throws<OperationCanceledException>(() => InequalityLeastSquaresStep.Solve(new double[,] { { 1 } }, [1], new double[0, 1], [], .001));
    }
}
