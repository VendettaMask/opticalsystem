using OpticalSystemDesign.InitialStructure.Engine.Optimization;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class TangentCorrectionTests
{
    [Fact]
    public void NormalizedFeasibilityDoesNotGuaranteeOriginalRowTolerance()
    {
        (double Offset, double[] Normal)[] rows = [(0, [1e6, 0]), (.1, [-1, 1])];
        var legacy = LinearFeasibilityProjection.Solve(rows, 2)!;
        Assert.True(legacy[0] * 1e6 > 1e-8);
        var repaired = LinearFeasibilityProjection.Solve(rows, 2, originalTolerance: 1e-10)!;
        Assert.NotNull(repaired);
        Assert.All(rows, row => Assert.True(row.Offset + row.Normal.Zip(repaired, (a, b) => a * b).Sum() <= 1e-10));
    }

    [Fact]
    public void ExplicitOriginalToleranceRejectsInvalidInputsAndInfeasibleConstantRows()
    {
        (double Offset, double[] Normal)[] empty = [];
        Assert.Throws<ArgumentOutOfRangeException>(() => LinearFeasibilityProjection.Solve(empty, 1, originalTolerance: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => LinearFeasibilityProjection.Solve(empty, 1, originalTolerance: 0));
        Assert.Null(LinearFeasibilityProjection.Solve([(1e-8, new double[] { 0 })], 1, originalTolerance: 1e-10));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => LinearFeasibilityProjection.Solve([(0, new double[] { 1 })], 1,
            cancellationToken: cancelled.Token, originalTolerance: 1e-10));
    }
}
