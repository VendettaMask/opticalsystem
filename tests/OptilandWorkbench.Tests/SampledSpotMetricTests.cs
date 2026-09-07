using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Tests;

public sealed class SampledSpotMetricTests
{
    [Fact]
    public void ExplicitSamplesMatchFormalSpotMetricsWithIdenticalSettings()
    {
        var optic = Optic.CreateCookeTriplet();
        var samples = ApertureSampler.GenerateHexapolarRings(2);
        var explicitResult = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, 0, samples);
        var formal = SpotMetricEvaluator.Evaluate(optic, rayDensity: 2, fieldNumber: 1);
        Assert.Equal(formal, explicitResult.Metrics);
        Assert.Equal(formal.RayCount, explicitResult.RayCount);
        Assert.Equal(formal.VignettedRayCount, explicitResult.VignettedRayCount);
        Assert.Equal(AnalysisAxisQuantity.ImageHeight, explicitResult.Quantity);
        Assert.Equal(AnalysisAxisUnit.Millimeter, explicitResult.Unit);
    }

    [Fact]
    public void CommonPolychromaticReferenceRetainsColorAndAppliesSpectralWeightOnce()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.Wavelengths[0].Weight = 1;
        optic.Wavelengths[1].Weight = 3;
        optic.Wavelengths[2].Weight = 0;
        var samples = ApertureSampler.GenerateHexapolarRings(2);
        var raw = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, .5, samples, reference: "absolute");
        var centered = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, .5, samples);
        Assert.Equal(2, raw.Wavelengths.Count);
        var rays = raw.Wavelengths.SelectMany(wave => wave.Rays.Select(ray =>
            (ray.X, ray.Y, Weight: wave.SpectralWeight * ray.Weight))).ToArray();
        var total = rays.Sum(ray => ray.Weight);
        var x = rays.Sum(ray => ray.X * ray.Weight) / total;
        var y = rays.Sum(ray => ray.Y * ray.Weight) / total;
        var expected = Math.Sqrt(rays.Sum(ray => ray.Weight * (Math.Pow(ray.X - x, 2) + Math.Pow(ray.Y - y, 2))) / total);
        Assert.Equal(expected, centered.Metrics!.RmsSpotRadius, 12);
        for (var wave = 0; wave < raw.Wavelengths.Count; wave++)
            for (var index = 0; index < raw.Wavelengths[wave].Rays.Count; index++)
            {
                Assert.Equal(raw.Wavelengths[wave].Rays[index].X - x, centered.Wavelengths[wave].Rays[index].X, 12);
                Assert.Equal(raw.Wavelengths[wave].Rays[index].Y - y, centered.Wavelengths[wave].Rays[index].Y, 12);
            }
    }

    [Fact]
    public void BlockedBundleReportsAllAttemptsAndNoSyntheticMetric()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(.1);
        var result = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, 0,
            [new(.9, 0, 1), new(-.9, 0, 1)]);
        Assert.True(result.RayCount > 0);
        Assert.Equal(result.RayCount, result.VignettedRayCount);
        Assert.Null(result.Metrics);
        Assert.All(result.Wavelengths, wave => Assert.Empty(wave.Rays));
    }

    [Theory]
    [InlineData(double.NaN, 0, 1)]
    [InlineData(1.1, 0, 1)]
    [InlineData(0, 0, -1)]
    public void InvalidPupilSamplesAreRejected(double x, double y, double weight)
    {
        Assert.ThrowsAny<ArgumentException>(() => SpotMetricEvaluator.EvaluatePupilSamples(
            Optic.CreateCookeTriplet(), 0, 0, [new(x, y, weight)]));
    }

    [Fact]
    public void PowerIsFiniteForPlanesAndReciprocalOfFormalFocalLength()
    {
        var optic = Optic.CreateCookeTriplet();
        Assert.Equal(1 / optic.Paraxial.EstimateEffectiveFocalLength(), optic.Paraxial.EstimateOpticalPower(), 12);
        foreach (var surface in optic.SurfaceGroup.Items) surface.Radius = 0;
        Assert.Equal(0, optic.Paraxial.EstimateOpticalPower());
    }

    [Fact]
    public void AfocalCoordinatesPublishAngularUnitsAndCancellationIsHonored()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.ImageSpaceAfocal = true;
        var result = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, 0, [new(0, 0, 1)]);
        Assert.Equal(AnalysisAxisQuantity.IncidentAngle, result.Quantity);
        Assert.Equal(AnalysisAxisUnit.Milliradian, result.Unit);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, 0,
            [new(0, 0, 1)], cancellationToken: cancellation.Token));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, 0,
            [new(0, 0, 1)], wavelengthNumber: optic.Wavelengths.Count + 1));
    }
}
