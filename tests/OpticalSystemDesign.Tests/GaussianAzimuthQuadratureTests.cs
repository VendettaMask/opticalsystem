using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Tests;

public sealed class GaussianAzimuthQuadratureTests
{
    [Theory]
    [InlineData(6, 0)]
    [InlineData(8, 0)]
    [InlineData(10, 0)]
    [InlineData(12, 0)]
    [InlineData(12, .35)]
    public void GaussianHalfPupilUsesChebyshevRootsWithUnchangedRadialWeights(int arms, double obscuration)
    {
        var generic = ApertureSampler.GenerateGaussianQuadrature(6, arms, obscuration);
        var gaussian = ApertureSampler.GenerateGaussianQuadrature(6, arms, obscuration, useChebyshevAzimuths: true);
        Assert.Equal(6 * arms, gaussian.Count);
        for (var i = 0; i < gaussian.Count; i++)
        {
            var sample = gaussian[i];
            var radius = double.Hypot(sample.X, sample.Y);
            Assert.Equal(generic[i].Weight, sample.Weight);
            Assert.InRange(Math.Abs(radius - double.Hypot(generic[i].X, generic[i].Y)), 0, 5e-16);
            // Chebyshev recurrence is independent of the angle-generation formula.
            var meridional = sample.Y / radius;
            var t0 = 1d;
            var t1 = meridional;
            for (var order = 2; order <= arms / 2; order++)
                (t0, t1) = (t1, 2 * meridional * t1 - t0);
            Assert.InRange(Math.Abs(t1), 0, 1e-14);
        }
        // Angular moments against the arcsine density, through Gaussian order.
        var ring = gaussian.Take(arms).ToArray();
        for (var degree = 0; degree < arms; degree++)
        {
            var expected = 1d;
            if (degree % 2 != 0) expected = 0;
            else for (var k = 1; k <= degree / 2; k++) expected *= (2d * k - 1) / (2 * k);
            var actual = ring.Average(p => Math.Pow(p.Y / double.Hypot(p.X, p.Y), degree));
            Assert.InRange(Math.Abs(actual - expected), 0, 2e-15);
        }
    }

    [Theory]
    [InlineData(6)]
    [InlineData(12)]
    public void GeneralPeriodicRuleAndCacheIdentityRemainExplicit(int arms)
    {
        var original = ApertureSampler.GenerateGaussianQuadrature(6, arms);
        Assert.Same(original, ApertureSampler.GenerateGaussianQuadrature(6, arms, useChebyshevAzimuths: false));
        var first = original[0];
        Assert.Equal(0, first.Y);
        Assert.True(first.X > 0);
        var gaussian = ApertureSampler.GenerateGaussianQuadrature(6, arms, useChebyshevAzimuths: true);
        Assert.NotSame(original, gaussian);
        Assert.Same(gaussian, ApertureSampler.GenerateGaussianQuadrature(6, arms, useChebyshevAzimuths: true));
        if (arms == 12) Assert.True(gaussian[0].Y > 0);
        else Assert.Equal(original, gaussian);
    }

    [Fact]
    public void OddPeriodicArmCountKeepsItsExistingNodes()
    {
        Assert.Equal(ApertureSampler.GenerateGaussianQuadrature(3, 7),
            ApertureSampler.GenerateGaussianQuadrature(3, 7, useChebyshevAzimuths: true));
    }
}
