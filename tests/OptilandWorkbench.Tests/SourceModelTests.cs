using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Sources;

namespace OptilandWorkbench.Tests;

public sealed class SourceModelTests
{
    [Fact]
    public void PointSourceSamplesTheFullThreeDimensionalCone()
    {
        const double coneAngleDegrees = 12;
        var rays = new PointSource(Vector3D.Zero, coneAngleDegrees)
            .Generate(550, sampleCount: 64)
            .Rays;
        var minimumZ = Math.Cos(coneAngleDegrees * Math.PI / 180);

        Assert.Equal(64, rays.Count);
        Assert.Contains(rays, ray => Math.Abs(ray.Direction.X) > 1e-6);
        Assert.Contains(rays, ray => Math.Abs(ray.Direction.Y) > 1e-6);
        Assert.All(rays, ray =>
        {
            Assert.Equal(1, ray.Direction.Length, precision: 12);
            Assert.InRange(ray.Direction.Z, minimumZ - 1e-12, 1 + 1e-12);
        });
    }

    [Fact]
    public void SingleModeFiberSamplesGaussianModeAndNumericalApertureCone()
    {
        const double modeFieldDiameter = 10;
        const double numericalAperture = 0.22;
        var rays = new SingleModeFiberSource(modeFieldDiameter, numericalAperture)
            .Generate(550, sampleCount: 4096)
            .Rays;
        var minimumZ = Math.Cos(Math.Asin(numericalAperture));
        var expectedRadialRms = (modeFieldDiameter * 0.5) / Math.Sqrt(2);
        var radialRms = Math.Sqrt(rays.Average(ray =>
            (ray.Origin.X * ray.Origin.X) + (ray.Origin.Y * ray.Origin.Y)));

        Assert.Contains(rays, ray => Math.Abs(ray.Direction.X) > 1e-6);
        Assert.Contains(rays, ray => Math.Abs(ray.Direction.Y) > 1e-6);
        Assert.All(rays, ray =>
        {
            Assert.Equal(1, ray.Direction.Length, precision: 12);
            Assert.InRange(ray.Direction.Z, minimumZ - 1e-12, 1 + 1e-12);
        });
        Assert.InRange(
            radialRms,
            expectedRadialRms * 0.95,
            expectedRadialRms * 1.05);
    }
}
