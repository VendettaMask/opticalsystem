using OpticalSystemDesign.Core.Differentiation;

namespace OpticalSystemDesign.Tests;

public sealed class DifferentiableCheckMaximumTests
{
    [Fact]
    public void IndependentChecksShareIntegrationCentroidWithoutChangingRmsOrWeights()
    {
        var center = DifferentialScalar.Variable(10, 1, 0);
        DifferentiableSpotRay Ray(double x) => new(x, 0, 1, 1, 0, 1);
        var integral = new DifferentiableSpotField(1, center, 0, 2, 2, [Ray(8), Ray(12)]);
        var checks = new DifferentiableSpotField(1, -1000, 0, 100, 100, [Ray(3), Ray(12)]);
        var maximum = DifferentiableOpticalMetrics.MaximumRadiusWithChecks(integral, checks);
        Assert.Equal(7, maximum.Value); Assert.Equal(1, maximum.Derivative(0));
        Assert.Equal(2, integral.RmsRadiusMillimeters.Value); Assert.Equal(2, integral.Rays.Count);
        Assert.Equal(2, integral.MaximumRadiusMillimeters.Value);
    }

    [Fact]
    public void MissingMismatchedOrNonfiniteCheckDataRemainFailures()
    {
        var ray = new DifferentiableSpotRay(1, 0, 1, 1, 0, 1);
        var field = new DifferentiableSpotField(1, 0, 0, 1, 1, [ray]);
        Assert.Throws<ArgumentException>(() => DifferentiableOpticalMetrics.MaximumRadiusWithChecks(field, field with { NormalizedFieldY = .5 }));
        Assert.Throws<ArgumentException>(() => DifferentiableOpticalMetrics.MaximumRadiusWithChecks(field, field with { Rays = [] }));
        Assert.Throws<ArithmeticException>(() => DifferentiableOpticalMetrics.MaximumRadiusWithChecks(field, field with { Rays = [ray with { X = double.NaN }] }));
    }
}
