using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Tests;

public sealed class RmsSamplingRepairTests
{
    [Theory]
    [InlineData(64, "chief")]
    [InlineData(128, "chief")]
    [InlineData(256, "chief")]
    [InlineData(64, "centroid")]
    [InlineData(128, "centroid")]
    [InlineData(256, "centroid")]
    public void RectangularWavefrontActuallyUsesRequestedGrid(int density, string reference)
    {
        var optic = Optic.CreateCookeTriplet();
        var result = new RmsWavefrontVsFieldAnalysis(optic, numRings: density, fieldDensity: 1,
            method: "RA", reference: reference, wavelengthNumber: 1, zemaxCompatibleOutput: true).GenerateData();
        // Independently enumerate the retained endpoint-grid contract, not merely metadata.
        var pupil = (from row in Enumerable.Range(0, density)
                     from column in Enumerable.Range(0, density)
                     let x = -1 + 2.0 * column / (density - 1)
                     let y = -1 + 2.0 * row / (density - 1)
                     where x * x + y * y <= 1
                     select new PupilSample(x, y, 1)).ToArray();
        var wavefront = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 1), optic.Wavelengths[0],
            pupil.Select(p => (p.X, p.Y)).ToArray(), aimAtStop: optic.RayAimingEnabled);
        var expected = WavefrontStatistics.Measure(wavefront.Samples, pupil,
            reference == "chief" ? WavefrontReferenceKind.ChiefRay : WavefrontReferenceKind.Centroid).Rms;
        Assert.Equal(density, result.Values["RayDensity"]);
        Assert.Equal(pupil.Length, result.Values["PupilSampleCount"]);
        Assert.Equal(expected, Assert.Single(result.PlotSeries).Points[^1].Y, 12);
        var oldDensity = new RmsWavefrontVsFieldAnalysis(optic, numRings: 32, fieldDensity: 1,
            method: "RA", reference: reference, wavelengthNumber: 1, zemaxCompatibleOutput: true).GenerateData();
        Assert.True(Math.Abs(expected - oldDensity.PlotSeries.Single().Points[^1].Y) > 1e-9);
    }

    [Theory]
    [InlineData("RMS vs Field", "NumRings")]
    [InlineData("RMS Wavefront vs Field", "RayDensity")]
    public void ApplicationWavefrontPreservesDensityAndPolarization(string name, string densityKey)
    {
        var runtime = new WorkbenchRuntime(LosslessCooke());
        var settings = runtime.MergeAnalysisSettings(name, new Dictionary<string, string>
        {
            ["Method"] = "RA",
            [densityKey] = "64",
            ["Data"] = "wavefront",
            ["FieldDensity"] = "1",
            ["WavelengthNumber"] = "1",
            ["Reference"] = "chief",
            ["UsePolarization"] = "true",
            ["GaussianAzimuthalSamples"] = "12"
        });
        var result = runtime.BuildAnalysisData(name, settings);
        var direct = new RmsWavefrontVsFieldAnalysis(runtime.CurrentOptic, numRings: 64, fieldDensity: 1,
            method: "RA", wavelengthNumber: 1, zemaxCompatibleOutput: true, usePolarization: true).GenerateData();
        Assert.Equal(true, result.Values["UsePolarization"]);
        Assert.Equal(64, result.Values["RayDensity"]);
        Assert.Equal("12", settings["GaussianAzimuthalSamples"]);
        Assert.Equal(direct.PlotSeries.Single().Points.Select(p => p.Y), result.PlotSeries.Single().Points.Select(p => p.Y));
    }

    [Theory]
    [InlineData("RMS vs Wavelength")]
    [InlineData("RMS vs Focus")]
    [InlineData("RMS Field Map")]
    public void AdjacentRmsScansDoNotReduceRectangularDensity(string name)
    {
        var optic = Optic.CreateCookeTriplet();
        var result = new WorkbenchRuntime(optic).BuildAnalysisData(name, new Dictionary<string, string>
        {
            ["Method"] = "RA",
            ["NumRings"] = "64",
            ["Data"] = "wavefront",
            ["WaveDensity"] = "2",
            ["FocusDensity"] = "2",
            ["XFieldSamples"] = "3",
            ["YFieldSamples"] = "3",
            ["WavelengthNumber"] = "1"
        });
        Assert.Equal(64, result.Values["RayDensity"]);
        Assert.All(result.PlotSeries.SelectMany(s => s.Points), p => Assert.True(double.IsFinite(p.Value ?? p.Y)));
    }

    [Theory]
    [InlineData("GQ", 0)]
    [InlineData("GQ", 33)]
    [InlineData("RA", 0)]
    [InlineData("RA", 1025)]
    public void InvalidDensityIsRejectedAcrossAllRmsConstructors(string method, int density)
    {
        var optic = Optic.CreateCookeTriplet();
        Assert.Throws<ArgumentOutOfRangeException>(() => new RmsVsFieldAnalysis(optic, numRings: density, method: method));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RmsWavefrontVsFieldAnalysis(optic, numRings: density, method: method));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RmsVsWavelengthAnalysis(optic, numRings: density, method: method));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RmsVsFocusAnalysis(optic, numRings: density, method: method));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RmsFieldMapAnalysis(optic, numRings: density, method: method));
    }

    [Fact]
    public void AggregateBudgetRejectsBeforeGeneratingHighDensityWavefronts()
    {
        var optic = Optic.CreateCookeTriplet();
        var analysis = new RmsWavefrontVsFieldAnalysis(optic, numRings: 1024, fieldDensity: 200,
            method: "RA", zemaxCompatibleOutput: true);
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => analysis.GenerateData());
        Assert.Contains("safety budget", error.Message);
    }

    [Theory]
    [InlineData("chief")]
    [InlineData("centroid")]
    public void PolarizedWavefrontUsesFormalTransmittedPowerAndSystemJonesInput(string reference)
    {
        var optic = LosslessCooke();
        optic.Polarization = new SystemPolarization(Unpolarized: false, Jx: 1, Jy: 0);
        var pupil = ApertureSampler.GenerateGaussianQuadrature(6, 12);
        var coordinates = pupil.Select(p => (p.X, p.Y)).ToArray();
        var wave = optic.Wavelengths[0];
        var plain = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 1), wave, coordinates, aimAtStop: optic.RayAimingEnabled, usePolarization: false);
        var polarized = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 1), wave, coordinates, aimAtStop: optic.RayAimingEnabled, usePolarization: true);
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 1, wave.Micrometers,
            coordinates.Select(p => new PupilSample(p.X, p.Y, 1)).ToArray(), optic.RayAimingEnabled);
        for (var i = 0; i < pupil.Count; i++)
        {
            var diagnostic = optic.SequentialRayTracer.DiagnoseApertures(bundle.Rays[i], usePolarization: true);
            Assert.Equal(diagnostic.InsideAllApertures ? diagnostic.PolarizationWeightedIntensity!.Value : 0,
                polarized.Samples[i].Intensity, 12);
            // Scalar and batched traces accumulate OPL differently; allow 1e-8 waves,
            // far below the numerical-parity thresholds, without adding polarization phase.
            if (polarized.Samples[i].Intensity > 0)
                Assert.InRange(Math.Abs(plain.Samples[i].OpdWaves - polarized.Samples[i].OpdWaves), 0, 1e-8);
        }
        var result = new RmsVsFieldAnalysis(optic, numRings: 6, fieldDensity: 1, method: "GQ", data: "wavefront",
            reference: reference, wavelengthNumber: 1, usePolarization: true)
        { GaussianAzimuthalSamples = 12 }.GenerateData();
        var kind = reference == "chief" ? WavefrontReferenceKind.ChiefRay : WavefrontReferenceKind.Centroid;
        var expected = WavefrontStatistics.Measure(polarized.Samples, pupil, kind, useIntensityWeights: true).Rms;
        Assert.Equal(expected, result.PlotSeries.Single().Points[^1].Y, 12);
        Assert.True(Math.Abs(expected - WavefrontStatistics.Measure(plain.Samples, pupil, kind).Rms) > 1e-8);
        optic.Polarization = new SystemPolarization(Unpolarized: false, Jx: 0, Jy: 1);
        var orthogonal = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 1), wave, coordinates, aimAtStop: optic.RayAimingEnabled, usePolarization: true);
        Assert.Contains(polarized.Samples.Zip(orthogonal.Samples), pair => Math.Abs(pair.First.Intensity - pair.Second.Intensity) > 1e-5);
    }

    [Fact]
    public void IntensityWeightingHasIndependentVarianceAndPreservesDefaultStatistics()
    {
        PupilSample[] pupil = [new(-1, 0, 1), new(0, 0, 1), new(1, 0, 2)];
        WavefrontSample[] samples = [new(-1, 0, 0, 0, 0, 0, 1), new(0, 0, 0, 0, 0, 2, 1), new(1, 0, 0, 0, 0, 5, .25)];
        // Geometric weights [1,1,2]: mean=3, variance=4.5.
        Assert.Equal(Math.Sqrt(4.5), WavefrontStatistics.Measure(samples, pupil, WavefrontReferenceKind.ChiefRay).Rms, 12);
        // Power weights [1,1,.5]: mean=1.8, variance=3.36; centroid fit leaves variance 2/35.
        Assert.Equal(Math.Sqrt(3.36), WavefrontStatistics.Measure(samples, pupil, WavefrontReferenceKind.ChiefRay, true).Rms, 12);
        Assert.Equal(Math.Sqrt(2.0 / 35), WavefrontStatistics.Measure(samples, pupil, WavefrontReferenceKind.Centroid, true).Rms, 12);
        var scaled = samples.Select(s => s with { Intensity = s.Intensity * 1e200 }).ToArray();
        var scaledPupil = pupil.Select(p => p with { Weight = p.Weight * 1e200 }).ToArray();
        Assert.Equal(Math.Sqrt(3.36), WavefrontStatistics.Measure(scaled, scaledPupil, WavefrontReferenceKind.ChiefRay, true).Rms, 12);
    }

    [Fact]
    public void UnsupportedAbsorbingPolarizationDoesNotSilentlyFallBackToGeometricRms()
    {
        var optic = Optic.CreateCookeTriplet();
        var error = Assert.Throws<NotSupportedException>(() => new RmsVsFieldAnalysis(optic, fieldDensity: 1,
            data: "wavefront", wavelengthNumber: 1, usePolarization: true).GenerateData());
        Assert.Contains("Absorbing interface polarization", error.Message);
    }

    private static Optic LosslessCooke()
    {
        // A test model only: retain the prescription but explicitly remove glass absorption.
        // The external Zemax fixtures continue to use their original physical materials.
        var optic = Optic.CreateCookeTriplet();
        foreach (var surface in optic.SurfaceGroup.Items)
            if (surface.MaterialAfter is not AirMaterial)
                surface.MaterialAfter = new ConstantIndexMaterial("Rms lossless " + surface.MaterialAfter.Name,
                    surface.MaterialAfter.RefractiveIndex(550));
        return optic;
    }
}
