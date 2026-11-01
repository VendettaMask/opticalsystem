using OpticalSystemDesign.Core.Differentiation;

namespace OpticalSystemDesign.Tests;

public sealed class VectorTangentEquivalenceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(16)]
    [InlineData(48)]
    [InlineData(64)]
    [InlineData(512)]
    public void VectorLanesAndTailExactlyMatchTheOriginalScalarChainRule(int dimension)
    {
        var random = new Random(7411 + dimension);
        for (var sample = 0; sample < 25; sample++)
        {
            var a = Enumerable.Range(0, dimension).Select(i => i % 7 == 0 ? -0.0 : Math.ScaleB(random.NextDouble() - .5, (i % 31) - 15)).ToArray();
            var b = Enumerable.Range(0, dimension).Select(i => i % 9 == 0 ? 0.0 : Math.ScaleB(random.NextDouble() - .5, 15 - (i % 31))).ToArray();
            var x = DifferentialScalar.FromGradient(.3 + sample, a); var y = DifferentialScalar.FromGradient(-.7 - sample, b);
            Same(x + y, a, 1, b, 1); Same(x - y, a, 1, b, -1);
            Same(x * y, a, y.Value, b, x.Value);
            Same(3.0 * x, new double[dimension], x.Value, a, 3);
            Same(-x, a, -1, new double[dimension], 0);
            // Perturbation of an actual composite residual, not only single-lane seeds.
            var combined = (x * y + x) - y * .125;
            for (var i = 0; i < dimension; i++)
            {
                var product = y.Value * a[i] + x.Value * b[i];
                var expected = (product + a[i]) - (.125 * b[i] + y.Value * 0);
                Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(combined.Derivative(i)));
            }
        }
    }

    [Fact]
    public void VectorTangentsKeepNonfiniteRejectionAndDimensionChecks()
    {
        var x = DifferentialScalar.FromGradient(1e308, Enumerable.Repeat(1e308, 16).ToArray());
        Assert.False((x * x).IsFinite);
        Assert.Throws<ArgumentException>(() => x + DifferentialScalar.Variable(1, 7, 0));
        Assert.Equal(0, ((DifferentialScalar)2 + 3).Dimension);
    }

    private static void Same(DifferentialScalar actual, double[] a, double da, double[] b, double db)
    {
        for (var i = 0; i < a.Length; i++)
            Assert.Equal(BitConverter.DoubleToInt64Bits(da * a[i] + db * b[i]), BitConverter.DoubleToInt64Bits(actual.Derivative(i)));
    }
}
