using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class NonlinearCorrectionTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(7.0)]
    public void CurvedActiveBoundCanBeCorrectedWithoutLosingTangentProgress(double scale)
    {
        var r = NonlinearInequalityCorrection.Correct([0, 1], [.2, 1], [0, 0], [1, 1], (x, derivative) =>
            new([scale * (x[0] * x[0] + x[1] * x[1] - 1), double.NegativeInfinity], derivative ? new double[,] { { 2 * scale * x[0], 2 * scale * x[1] }, { 0, 0 } } : null));
        Assert.InRange(r.Iterations, 1, 2); Assert.True(r.FinalViolation <= 0);
        Assert.True(r.Point[0] > .15); Assert.InRange(r.Point[1], .9, 1);
        Assert.True(r.Point[0] * r.Point[0] + r.Point[1] * r.Point[1] <= 1);
        Assert.Equal(r.Iterations, r.Subproblems);
    }

    [Fact]
    public void AnAlreadyFeasibleTrialIsPreservedExactlyAndCancellationIsObserved()
    {
        var trial = new[] { .25 };
        var r = NonlinearInequalityCorrection.Correct([0], trial, [0], [1], (x, d) => new([x[0] - .5], d ? new double[,] { { 1 } } : null));
        Assert.Equal(trial, r.Point); Assert.Equal(0, r.Subproblems); Assert.Equal(1, r.ValueEvaluations);
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); using var scope = ComputationCancellation.Push(cancel.Token);
        Assert.Throws<OperationCanceledException>(() => NonlinearInequalityCorrection.Correct([0], trial, [0], [1], (_, _) => throw new Exception()));
    }
}
