using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class SampledRayAimingConsistencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SampledSpotMatchesFormalDiagramWithTheSameAimingSetting(bool aiming)
    {
        var optic = Fixture(aiming);
        var pupils = ApertureSampler.GenerateHexapolarRings(3);
        var sampled = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, 1, pupils,
            includeSurfaceTransmission: false);
        var diagram = new SpotDiagramAnalysis(optic, new SpotDiagramSettings(
            RayDensity: 3, FieldNumber: optic.Fields.Count)).GenerateData();
        var series = diagram.PlotPanes!.Single().Series;
        for (var wave = 0; wave < sampled.Wavelengths.Count; wave++)
        {
            Assert.NotEmpty(sampled.Wavelengths[wave].Rays);
            Assert.Equal(series[wave].Points.Select(point => point.X), sampled.Wavelengths[wave].Rays.Select(ray => ray.X));
            Assert.Equal(series[wave].Points.Select(point => point.Y), sampled.Wavelengths[wave].Rays.Select(ray => ray.Y));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AggregateSpotUsesTheSameAimedRaysAsTheFormalEngine(bool aiming)
    {
        var optic = Fixture(aiming);
        var expected = SpotAnalysisEngine.Generate(optic, [(0, 1)], optic.Wavelengths,
            3, "hexapolar", aimAtStop: aiming);
        Assert.Equal(SpotMetricEvaluator.Summarize(expected, "Aiming reference"),
            SpotMetricEvaluator.Evaluate(optic, rayDensity: 3, fieldNumber: optic.Fields.Count));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnvelopeUsesAimedSurfaceInterceptsAndLeavesOtherSettingsUnchanged(bool aiming)
    {
        var optic = Fixture(aiming);
        var pupils = ApertureSampler.GenerateHexapolarRings(3);
        var indices = Enumerable.Range(1, optic.SurfaceGroup.Items.Count - 2).ToArray();
        var groups = indices.Select(index => (IReadOnlyList<int>)new[] { index }).ToArray();
        var expected = new double[optic.SurfaceGroup.Items.Count];
        foreach (var wave in optic.Wavelengths)
        {
            var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
                0, 1, wave.Micrometers, pupils, aimAtStop: aiming, applyVignettingFactors: false);
            using var trace = optic.SequentialRayTracer.Trace(bundle, TraceRequest.Selected(indices));
            for (var ray = 0; ray < trace.RayCount; ray++)
                foreach (var index in indices)
                {
                    Assert.True(trace.TryGetSample(ray, index, out var sample));
                    Assert.False(sample.Vignetted);
                    Assert.True(sample.Intensity > 0);
                    var local = optic.SurfaceGroup.Items[index].CoordinateSystem.ToLocalPoint(sample.Position);
                    expected[index] = Math.Max(expected[index], double.Hypot(local.X, local.Y));
                }
        }
        var aperture = optic.Aperture.Value;
        var result = AutomaticSemiDiameterSolver.UpdateFromRayEnvelope(optic, groups, [(0, 1)], pupils, 1.2);
        Assert.True(result.Applied);
        Assert.Equal(result.AttemptedRays, result.CompletedRays);
        foreach (var size in result.Surfaces)
        {
            Assert.Equal(expected[size.SurfaceIndex], size.SampledRadiusMillimeters, 11);
            Assert.Equal(Math.Max(.1, expected[size.SurfaceIndex] * 1.2), size.SemiDiameterMillimeters, 11);
        }
        Assert.Equal(aiming, optic.RayAimingEnabled);
        Assert.Equal(aperture, optic.Aperture.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnclippedDiagnosticsFollowAimingAndRetainPhysicalObstruction(bool aiming)
    {
        var optic = Fixture(aiming);
        var pupils = ApertureSampler.GenerateHexapolarRings(3);
        var reference = SpotAnalysisEngine.Generate(optic, [(0, 1)], optic.Wavelengths,
            3, "hexapolar", aimAtStop: aiming, includeSurfaceTransmission: false);
        optic.SurfaceGroup.Items[^2].PhysicalAperture = new CircularAperture(.1);
        var result = SampledApertureDiagnostics.Evaluate(optic, 0, 1, pupils);
        Assert.True(result.IsComplete);
        Assert.Contains(result.Rays, ray => ray.MinimumClearanceMillimeters < 0);
        var expected = SpotMetricEvaluator.Summarize(reference, "Unclipped aimed reference");
        var actual = result.UnclippedSpot.Metrics!;
        Assert.Equal(expected.RayCount, actual.RayCount);
        Assert.Equal(expected.VignettedRayCount, actual.VignettedRayCount);
        Assert.Equal(expected.RmsSpotRadius, actual.RmsSpotRadius, 12);
        Assert.Equal(expected.MaximumSpotRadius, actual.MaximumSpotRadius, 12);
        Assert.Equal(expected.Radius80, actual.Radius80, 12);
        var physical = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, 1, pupils);
        Assert.True(physical.VignettedRayCount > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisablingVignettingFactorsAlsoDisablesThemForStopTargets(bool aiming)
    {
        var optic = Fixture(aiming);
        var pupils = ApertureSampler.GenerateHexapolarRings(2);
        var generator = optic.SequentialRayTracer.RayGenerator;
        var expected = generator.GenerateNormalizedPupilSamples(0, 1, .5876, pupils,
            aimAtStop: aiming, applyVignettingFactors: false);
        optic.Fields[^1].VignetteFactorX = .2;
        optic.Fields[^1].VignetteFactorY = .35;
        var actual = generator.GenerateNormalizedPupilSamples(0, 1, .5876, pupils,
            aimAtStop: aiming, applyVignettingFactors: false);
        Assert.Equal(expected.Rays.Select(ray => ray.Origin), actual.Rays.Select(ray => ray.Origin));
        Assert.Equal(expected.Rays.Select(ray => ray.Direction), actual.Rays.Select(ray => ray.Direction));
        var vignetted = generator.GenerateNormalizedPupilSamples(0, 1, .5876, pupils, aimAtStop: aiming);
        Assert.Contains(Enumerable.Range(0, expected.Rays.Count), index =>
            expected.Rays[index].Origin != vignetted.Rays[index].Origin
            || expected.Rays[index].Direction != vignetted.Rays[index].Direction);
    }

    private static Optic Fixture(bool aiming)
    {
        var optic = Optic.CreateCookeTriplet();
        optic.RayAimingEnabled = aiming;
        // Remove clipping, not refraction or the stop designation. The off-axis
        // real stop intercept differs from its paraxial launch on this fixture.
        foreach (var surface in optic.SurfaceGroup.Items)
        {
            surface.SemiDiameterDefinesPhysicalAperture = false;
            surface.PhysicalAperture = null;
        }
        return optic;
    }
}
