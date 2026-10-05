using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class SurfaceSlopeModulusTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(-100)]
    public void SphericalModulusIsUnsignedAndBaseRemovalPrecedesLength(double radius)
    {
        var surface = new OpticalSurface { Geometry = new StandardGeometry(radius), SemiDiameter = 5 };
        Assert.Equal(5 / Math.Sqrt(radius * radius - 25), SurfaceProfileMetrics.AtPoint(surface, SurfaceProfileQuantity.Slope, 3, 4, orientation: 4), 12);
        Assert.Equal(0, SurfaceProfileMetrics.AtPoint(surface, SurfaceProfileQuantity.Slope, 3, 4, remove: 1, orientation: 4));
        surface.Geometry = new EvenAsphereGeometry(radius, 0, [.01]);
        Assert.Equal(.1, SurfaceProfileMetrics.AtPoint(surface, SurfaceProfileQuantity.Slope, 3, 4, remove: 1, orientation: 4), 12);
    }

    [Fact]
    public void TiltedPlaneModulusIsConstantAndBothOperandsUseIt()
    {
        var optic = Optic.CreateBlank(); var surface = optic.SurfaceGroup.Items[1];
        surface.Geometry = new PolynomialGeometry(new Dictionary<(int, int), double> { [(1, 0)] = .3, [(0, 1)] = -.4 });
        var stats = SurfaceProfileMetrics.Evaluate(surface, SurfaceProfileQuantity.Slope, 1, 0, 4);
        Assert.Equal(.5, stats.Rms, 12); Assert.Equal(.5, stats.Minimum, 12); Assert.Equal(.5, stats.Maximum, 12);
        Assert.Equal(0, stats.Value(2));
        foreach (var code in new[] { "SSLP", "DSLP" })
        {
            var result = MeritFunctionCatalog.Evaluate(optic, new()
            {
                Type = code,
                ZemaxIntegerParameters = [1, 1],
                ZemaxDataParameters = code == "SSLP" ? [3, 4, 0, 0, 0, 4] : [1, 0, 0, 0, 4]
            });
            Assert.Empty(result.Error); Assert.Equal(.5, result.Value, 12);
        }
    }

    [Fact]
    public void CurvatureModulusIsStillRejectedRatherThanSubstitutingSlopeLength()
    {
        var surface = new OpticalSurface { Geometry = new StandardGeometry(100) };
        Assert.Throws<NotSupportedException>(() => SurfaceProfileMetrics.AtPoint(surface, SurfaceProfileQuantity.Curvature, 3, 4, orientation: 4));
        Assert.Throws<NotSupportedException>(() => SurfaceProfileMetrics.Evaluate(surface, SurfaceProfileQuantity.Curvature, 1, 0, 4));
    }
}
