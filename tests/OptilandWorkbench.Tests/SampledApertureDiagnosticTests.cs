using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Tests;

public sealed class SampledApertureDiagnosticTests
{
    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 0)]
    [InlineData(3, -1)]
    public void PlaneReportsSignedPhysicalClearanceWithoutChangingNormalClipping(double height, double expected)
    {
        var optic = new Optic();
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Label = "Object", Thickness = double.PositiveInfinity },
            new OpticalSurface { Label = "Stop", IsStop = true, Thickness = 10, PhysicalAperture = new CircularAperture(2) },
            new OpticalSurface { Label = "Image", Thickness = 0 }
        ]);
        var ray = new RealRay(new Vector3D(height, 0, -1), new Vector3D(0, 0, 1), 587.6);
        var json = new System.Text.Json.JsonSerializerOptions
        { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var snapshot = System.Text.Json.JsonSerializer.Serialize(optic.ToSnapshot(), json);
        var diagnostic = optic.SequentialRayTracer.DiagnoseCircularApertures(ray);
        Assert.NotNull(diagnostic.UnclippedImage);
        Assert.Equal(expected, diagnostic.MinimumClearanceMillimeters);
        Assert.Equal(1, diagnostic.LimitingSurfaceIndex);
        Assert.Equal(height, diagnostic.UnclippedImage.Position.X, 12);
        var normal = optic.SequentialRayTracer.Trace(new RealRayBundle([ray]));
        Assert.Equal(expected < 0, normal.RayHistories[0].Any(sample => sample.Vignetted));
        Assert.Equal(snapshot, System.Text.Json.JsonSerializer.Serialize(optic.ToSnapshot(), json));
    }

    [Fact]
    public void UnclippedDiagnosticsUseFormalSpectralWeightsAndDoNotReplacePhysicalMetrics()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.Wavelengths[0].Weight = 1;
        optic.Wavelengths[1].Weight = 3;
        optic.Wavelengths[2].Weight = 0;
        var samples = ApertureSampler.GenerateHexapolarRings(2);
        var formal = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, .5, samples, includeSurfaceTransmission: false);
        var diagnostic = SampledApertureDiagnostics.Evaluate(optic, 0, .5, samples);
        Assert.True(diagnostic.IsComplete);
        Assert.Equal(formal.Metrics!.RmsSpotRadius, diagnostic.UnclippedSpot.Metrics!.RmsSpotRadius, 12);
        Assert.Equal(formal.Metrics.MaximumSpotRadius, diagnostic.UnclippedSpot.Metrics.MaximumSpotRadius, 12);
        for (var wave = 0; wave < formal.Wavelengths.Count; wave++)
            for (var ray = 0; ray < samples.Count; ray++)
            {
                Assert.Equal(formal.Wavelengths[wave].Rays[ray].X, diagnostic.UnclippedSpot.Wavelengths[wave].Rays[ray].X, 12);
                Assert.Equal(formal.Wavelengths[wave].Rays[ray].Y, diagnostic.UnclippedSpot.Wavelengths[wave].Rays[ray].Y, 12);
            }
        optic.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(.1);
        var before = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, .5, samples, includeSurfaceTransmission: false);
        var clipped = SampledApertureDiagnostics.Evaluate(optic, 0, .5, samples);
        var after = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, .5, samples, includeSurfaceTransmission: false);
        Assert.True(before.VignettedRayCount > 0);
        Assert.True(clipped.IsComplete);
        Assert.Contains(clipped.Rays, ray => ray.MinimumClearanceMillimeters < 0);
        Assert.Equal(before.Metrics, after.Metrics);
        Assert.Equal(before.VignettedRayCount, after.VignettedRayCount);
        Assert.Equal(diagnostic.UnclippedSpot.Metrics, clipped.UnclippedSpot.Metrics);
    }

    [Fact]
    public void UnsupportedAperturesAndCancelledDiagnosticsFailExplicitly()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[1].PhysicalAperture = new RectangularAperture(1, 2);
        Assert.Throws<NotSupportedException>(() => SampledApertureDiagnostics.Evaluate(optic, 0, 0, [new(0, 0, 1)]));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => SampledApertureDiagnostics.Evaluate(optic, 0, 0,
            [new(0, 0, 1)], cancellationToken: cancellation.Token));
    }

    [Fact]
    public void MissedIntersectionDoesNotInventAnImageOrAnApertureMargin()
    {
        var optic = new Optic();
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Label = "Object", Thickness = double.PositiveInfinity },
            new OpticalSurface { Radius = 2, PhysicalAperture = new CircularAperture(5), Thickness = 10 },
            new OpticalSurface { Label = "Image", Thickness = 0 }
        ]);
        var diagnostic = optic.SequentialRayTracer.DiagnoseCircularApertures(
            new RealRay(new Vector3D(3, 0, -1), new Vector3D(0, 0, 1), 587.6));
        Assert.Null(diagnostic.UnclippedImage);
        Assert.Null(diagnostic.MinimumClearanceMillimeters);
        Assert.Null(diagnostic.LimitingSurfaceIndex);
    }
}
