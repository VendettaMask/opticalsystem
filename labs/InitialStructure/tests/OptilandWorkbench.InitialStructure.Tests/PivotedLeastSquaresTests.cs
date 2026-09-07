using OptilandWorkbench.InitialStructure.Engine.Optimization;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class PivotedLeastSquaresTests
{
    [Fact]
    public void NearlyDependentColumnsAreSolvedWithoutSquaringTheConditionNumber()
    {
        // J-transpose J loses the 1e-16 squared difference; QR retains the independent direction.
        double[,] matrix = { { 1, 1 }, { 1, 1 + 1e-8 }, { 1, 1 - 1e-8 } };
        var solution = PivotedLeastSquares.Solve(matrix, [3, 3 + 2e-8, 3 - 2e-8]);
        Assert.Equal(1, solution[0], 6);
        Assert.Equal(2, solution[1], 6);
        Assert.Equal(1, matrix[0, 0]);
    }

    [Fact]
    public void ColumnPermutationIsUndoneAndLargeColumnNormsDoNotOverflow()
    {
        double[,] matrix = { { 1e150, 0 }, { 0, 1e155 }, { 1e150, 0 } };
        var solution = PivotedLeastSquares.Solve(matrix, [2e150, -3e155, 2e150]);
        Assert.Equal(2, solution[0], 10);
        Assert.Equal(-3, solution[1], 10);
    }

    [Fact]
    public void SingularSystemReturnsAFiniteBasicLeastSquaresSolution()
    {
        double[,] matrix = { { 1, 2 }, { 2, 4 }, { 3, 6 } };
        var solution = PivotedLeastSquares.Solve(matrix, [3, 6, 9]);
        Assert.All(solution, value => Assert.True(double.IsFinite(value)));
        Assert.Equal(3, solution[0] + 2 * solution[1], 12);
    }

    [Fact]
    public void OverdeterminedSystemRetainsTheNonzeroLeastSquaresResidual()
    {
        double[,] matrix = { { 1 }, { 1 }, { 1 } };
        var solution = PivotedLeastSquares.Solve(matrix, [1, 2, 6]);
        Assert.Equal(3, solution[0], 12);
    }
}
