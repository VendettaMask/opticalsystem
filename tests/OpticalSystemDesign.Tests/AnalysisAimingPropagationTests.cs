using System.Reflection;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Tests;

public sealed class AnalysisAimingPropagationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicMeridionalTraceUsesTheSystemAiming(bool aiming)
    {
        var optic = Fixture(aiming);
        var wave = optic.Wavelengths.Single();
        var actual = optic.RealRayTracer.TraceMeridionalRays(3);
        Assert.Equal(3, actual.Paths.Count);
        for (var ray = 0; ray < 3; ray++)
        {
            var sample = optic.TraceGenericFinalSample(0, 1, 0, (ray - 1) * .85,
                wave.Micrometers, aimAtStop: aiming)!;
            var last = actual.Paths[ray].Segments[^1].End;
            Assert.Equal(sample.Position.Z, last.Z, 12);
            Assert.Equal(sample.Position.Y, last.Y, 12);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void HuygensMtfPresentationDoesNotOverrideTheSystemAiming(bool aiming, bool nativePresentation)
    {
        var optic = Fixture(aiming);
        var wave = optic.Wavelengths.Single();
        var settings = new MtfComputationSettings(PupilSampling: 8, ImageSize: 16,
            PixelPitchMillimeters: .0005, UseZemaxHuygensSemantics: nativePresentation);
        var psf = DiffractionEngine.ComputeHuygensPsf(optic, (0, 1), wave, 8, 16, .0005,
            aimAtStop: aiming, useParaxialPupilDefocus: nativePresentation);
        var expected = MtfMethodEvaluator.SampleHuygensMtf(DiffractionEngine.ComputePsfMtf(psf),
            psf.GridSize, 20, nativePresentation);
        var actual = MtfMethodEvaluator.Evaluate(optic, MtfComputationMethod.Huygens,
            (0, 1), wave, 20, settings);
        Assert.InRange(Math.Abs(expected.Tangential - actual.Tangential), 0, 2e-12);
        Assert.InRange(Math.Abs(expected.Sagittal - actual.Sagittal), 0, 2e-12);
        Assert.Equal(aiming, psf.UseRayAiming);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeometricMtfUsesTheAcceptedSystemPupil(bool aiming)
    {
        var optic = Fixture(aiming);
        var data = new GeometricMtfAnalysis(optic, 4, "hexapolar", 3, 100, scale: false).GenerateData();
        var pupil = SpotAnalysisEngine.Generate(optic, [(0, 1)], optic.Wavelengths,
            4, "hexapolar", aimAtStop: aiming).Fields.Single().Wavelengths.Single().Rays;
        for (var axis = 0; axis < 2; axis++)
        {
            var series = data.PlotSeries[axis];
            // Keep the existing binned estimator; this contract checks the accepted pupil feeding it.
            var expected = GeometricMtfAnalysis.Compute(
                pupil.Select(ray => axis == 0 ? ray.Y : ray.X).ToArray(),
                pupil.Select(ray => ray.Intensity).ToArray(), series.Points.Select(p => p.X).ToArray(),
                Enumerable.Repeat(1d, series.Points.Count).ToArray());
            Assert.Equal(expected, series.Points.Select(p => p.Y));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RadiometricPowerAndMomentsMatchDirectAimedRays(bool aiming, bool angular)
    {
        var optic = Fixture(aiming);
        var imageIndex = optic.SurfaceGroup.Items.Count - 1;
        optic.SurfaceGroup.Items[imageIndex].PhysicalAperture = new RectangularAperture(20, 20);
        const int bins = 96;
        var data = angular
            ? new RadiantIntensityAnalysis(optic, bins, bins, -89, 89, -89, 89,
                useAbsoluteUnits: false, referenceSurfaceIndex: imageIndex,
                numRays: 4, distribution: "hexapolar", normalize: false).GenerateData()
            : new IncoherentIrradianceAnalysis(optic, 4, bins, bins,
                distribution: "hexapolar", normalize: false).GenerateData();
        var wave = optic.Wavelengths.Single();
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
            0, 1, wave.Micrometers, ApertureSampler.GenerateHexapolarRings(4), aiming);
        using var traced = optic.SequentialRayTracer.Trace(bundle, angular
            ? TraceRequest.Selected([imageIndex]) : TraceRequest.IncidentSurface(imageIndex));
        var minimum = angular ? -89d : -20d;
        var maximum = -minimum;
        var step = (maximum - minimum) / bins;
        double expectedPower = 0, expectedX = 0, expectedY = 0;
        var expectedCount = 0;
        foreach (var value in traced.GetSurfaceSamples(imageIndex))
        {
            if (value is not { Vignetted: false, Intensity: > 1e-12 } sample) continue;
            var local = optic.SurfaceGroup.Items[imageIndex].CoordinateSystem.ToLocalPoint(sample.Position);
            var x = angular ? Math.Atan2(sample.Direction.X, sample.Direction.Z) * 180 / Math.PI : local.X;
            var y = angular ? Math.Atan2(sample.Direction.Y, sample.Direction.Z) * 180 / Math.PI : local.Y;
            if (x < minimum || x > maximum || y < minimum || y > maximum) continue;
            var ix = Math.Min(bins - 1, (int)((x - minimum) / step));
            var iy = Math.Min(bins - 1, (int)((y - minimum) / step));
            var power = sample.Intensity / (angular ? 1 : step * step);
            expectedPower += power;
            expectedX += power * (minimum + (ix + .5) * step);
            expectedY += power * (minimum + (iy + .5) * step);
            expectedCount++;
        }
        var points = data.PlotPanes![0].Series[0].Points;
        Assert.Equal(expectedCount, Convert.ToInt32(data.Values["ValidRayCount"]));
        Near(expectedPower, points.Sum(p => p.Value ?? 0));
        Near(expectedX, points.Sum(p => p.X * (p.Value ?? 0)));
        Near(expectedY, points.Sum(p => p.Y * (p.Value ?? 0)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealImageConversionPreservesTheActualChiefImageTarget(bool aiming)
    {
        var optic = Fixture(aiming);
        optic.FieldDefinition = FieldDefinitionKind.RealImageHeight;
        optic.Fields[0].Y = 3;
        var wave = optic.Wavelengths.Single();
        var original = optic.TraceGenericFinalSample(0, 1, 0, 0, wave.Micrometers, aimAtStop: aiming)!;
        var converted = RealImageFieldConversion.ForDistortion(optic);
        var after = converted.TraceGenericFinalSample(0, 1, 0, 0, wave.Micrometers, aimAtStop: aiming)!;
        Assert.InRange((original.Position - after.Position).Length, 0, 2e-8);
        Assert.Equal(aiming, converted.RayAimingEnabled);
        Assert.Equal(FieldDefinitionKind.RealImageHeight, optic.FieldDefinition);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImageSimulationDetectorPitchUsesTheSystemChiefRays(bool aiming)
    {
        var optic = Fixture(aiming);
        var wave = optic.Wavelengths.Single();
        double Hit(double hx, double hy, bool x)
        {
            var sample = optic.TraceGenericFinalSample(hx, hy, 0, 0, wave.Micrometers, aimAtStop: aiming)!;
            var local = optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalPoint(sample.Position);
            return x ? local.X : local.Y;
        }
        var expected = (Math.Abs(Hit(.4, .2, true) - Hit(0, .2, true)) / 31
            + Math.Abs(Hit(.2, .4, false) - Hit(.2, 0, false)) / 23) / 2;
        var method = typeof(ImageSimulationEngine).GetMethod("EstimateDetectorPixelPitch", BindingFlags.NonPublic | BindingFlags.Static)!;
        var actual = (double)method.Invoke(null, [optic, wave, .2, .2, 32, 24, .2, .2])!;
        Assert.InRange(Math.Abs(expected - actual), 0, 2e-12);
    }

    [Fact]
    public void ImageSimulationDistortionRespondsToTheSystemAimingSetting()
    {
        var optic = Fixture(false);
        var wave = optic.Wavelengths.Single();
        var off = ImageSimulationEngine.GenerateDistortionGrid(optic, wave, 12, 12,
            numGridPoints: 9, degree: 3, halfFieldX: .4, halfFieldY: .4);
        optic.RayAimingEnabled = true;
        var on = ImageSimulationEngine.GenerateDistortionGrid(optic, wave, 12, 12,
            numGridPoints: 9, degree: 3, halfFieldX: .4, halfFieldY: .4);
        Assert.True(off.Cast<(double X, double Y)>().Zip(on.Cast<(double X, double Y)>(),
            (a, b) => double.Hypot(a.X - b.X, a.Y - b.Y)).Max() > 1e-7);
    }

    private static void Near(double expected, double actual) =>
        Assert.InRange(Math.Abs(expected - actual), 0, 1e-10 * Math.Max(1, Math.Abs(expected)));

    private static Optic Fixture(bool aiming)
    {
        var optic = Optic.CreateCookeTriplet();
        foreach (var surface in optic.SurfaceGroup.Items)
        {
            surface.SemiDiameterDefinesPhysicalAperture = false;
            surface.PhysicalAperture = null;
        }
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { Y = 20 });
        optic.Wavelengths.Clear();
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.RayAimingEnabled = aiming;
        return optic;
    }
}
