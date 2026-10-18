using System.Numerics;
using OpticalSystemDesign.Core.Analysis;

namespace OpticalSystemDesign.Tests;

public sealed class CoherenceNumericalStabilityTests
{
    [Theory]
    [InlineData(SpatialCoherenceFunction.Gaussian, 1e6)]
    [InlineData(SpatialCoherenceFunction.Gaussian, 1e9)]
    [InlineData(SpatialCoherenceFunction.Gaussian, 1e12)]
    [InlineData(SpatialCoherenceFunction.Sinc, 1e6)]
    [InlineData(SpatialCoherenceFunction.Sinc, 1e9)]
    [InlineData(SpatialCoherenceFunction.Sinc, 1e12)]
    public void NearlyCoherentDestructivePairRetainsItsSmallPositiveIntensity(SpatialCoherenceFunction function, double alpha)
    {
        var actual = MutualIntensityEngine.PropagateSpatialCoherence(new double[,] { { 1, 1 } },
            new Complex[,] { { 1, -1 } }, function, alpha, 1)[0, 1];
        // Independent leading asymptotic terms: relative truncation < 1e-11
        // at these separations, including cases where exp(-q) rounds to one.
        var expected = function == SpatialCoherenceFunction.Gaussian
            ? 2 / (alpha * alpha) : Math.PI * Math.PI / (3 * alpha * alpha);
        Assert.True(Math.Abs(actual / expected - 1) < 1e-10, $"expected={expected:R}, actual={actual:R}");
    }

    [Theory]
    [InlineData(0, 1e-300, 1e200, 1e100)]
    [InlineData(.5, 1e-300, 1e200, 1e100)]
    [InlineData(1, 1e-300, 1e200, 1e100)]
    [InlineData(0, 1e300, 1e-200, 1e-100)]
    [InlineData(.5, 1e300, 1e-200, 1e-100)]
    [InlineData(1, 1e300, 1e-200, 1e-100)]
    public void UniformPropagationBalancesSourceAndKernelBeforeSquaring(double coherence, double source, double kernel, double expected)
    {
        var actual = MutualIntensityEngine.PropagateUniformCoherence(new double[,] { { source } },
            new Complex[,] { { kernel } }, coherence)[0, 0];
        Assert.True(Math.Abs(actual / expected - 1) < 1e-14);
    }

    [Theory]
    [InlineData(SpatialCoherenceMethod.ExactSourcePairs, 1e-300, 1e200, 1e100)]
    [InlineData(SpatialCoherenceMethod.FiniteCorrelationWindow, 1e-300, 1e200, 1e100)]
    [InlineData(SpatialCoherenceMethod.FourierModes, 1e-300, 1e200, 1e100)]
    [InlineData(SpatialCoherenceMethod.ExactSourcePairs, 1e300, 1e-200, 1e-100)]
    [InlineData(SpatialCoherenceMethod.FiniteCorrelationWindow, 1e300, 1e-200, 1e-100)]
    [InlineData(SpatialCoherenceMethod.FourierModes, 1e300, 1e-200, 1e-100)]
    public void SpatialPropagationBalancesSourceAndKernelBeforeSquaring(SpatialCoherenceMethod method, double source, double kernel, double expected)
    {
        var actual = MutualIntensityEngine.PropagateSpatialCoherence(new double[,] { { source } },
            new Complex[,] { { kernel } }, SpatialCoherenceFunction.Gaussian, 1, 1, method: method)[0, 0];
        Assert.True(Math.Abs(actual / expected - 1) < 1e-14);
    }

    [Fact]
    public void NegativeSincCorrelationUsesTheAntisymmetricMode()
    {
        var actual = MutualIntensityEngine.PropagateSpatialCoherence(new double[,] { { 4, 9 } },
            new Complex[,] { { new(1, 1), new(2, -1) } }, SpatialCoherenceFunction.Sinc, 2d / 3, 1)[0, 1];
        // gamma(1.5) = -2/(3*pi); fields are 2*(2-i), 3*(1+i).
        var expected = 38 - 8 / Math.PI;
        Assert.True(Math.Abs(actual - expected) < 1e-13);
    }

    [Theory]
    [InlineData(SpatialCoherenceFunction.Gaussian, 2e-100)]
    [InlineData(SpatialCoherenceFunction.Sinc, 3.289868133696453e-100)]
    public void DecorrelationBelowTheIntensityRangeStillContributesToABrightSource(SpatialCoherenceFunction function, double expected)
    {
        var actual = MutualIntensityEngine.PropagateSpatialCoherence(new double[,] { { 1e300, 1e300 } },
            new Complex[,] { { 1, -1 } }, function, 1e200, 1)[0, 1];
        Assert.True(Math.Abs(actual / expected - 1) < 1e-14, $"expected={expected:R}, actual={actual:R}");
    }

    [Fact]
    public void UnrepresentableOutputStillFailsInsteadOfReturningAnInfiniteImage()
    {
        var source = new double[,] { { 1e300 } };
        var kernel = new Complex[,] { { 1e200 } };
        foreach (var coherence in new[] { 0d, .5, 1 })
            Assert.Throws<AnalysisDataUnavailableException>(() => MutualIntensityEngine.PropagateUniformCoherence(source, kernel, coherence));
        foreach (var method in Enum.GetValues<SpatialCoherenceMethod>())
            Assert.Throws<AnalysisDataUnavailableException>(() => MutualIntensityEngine.PropagateSpatialCoherence(source, kernel,
                SpatialCoherenceFunction.Gaussian, 1, 1, method: method));
    }
}
