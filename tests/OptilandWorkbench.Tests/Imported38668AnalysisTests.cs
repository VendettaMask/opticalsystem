using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Apertures;
using System.Security.Cryptography;
using Xunit.Abstractions;

namespace OptilandWorkbench.Tests;

public sealed class Imported38668AnalysisTests(ITestOutputHelper output)
{
    internal static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "zmax_38668.ZMX");

    internal static Optic Import() => OpticalFormatCatalog.Import(File.ReadAllText(FixturePath), ".zmx");

    [Fact]
    public void FixtureMatchesTheReportedFileAndWorkingFNumberMatchesSnellsLaw()
    {
        Assert.Equal("8407918B31B4ADDD03BC0207368AC3571BE43549AADE2F5D395EFE703BC22684",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(FixturePath))));
        var optic = Import();
        var wavelength = Assert.Single(optic.Wavelengths);
        Assert.Equal(355, wavelength.Nanometers, 10);
        Assert.Single(optic.Fields);
        Assert.Equal(4, optic.SurfaceGroup.Items.Count);
        Assert.Equal("C79-80", optic.SurfaceGroup.Items[1].Material);
        var nAir = optic.SurfaceGroup.Items[0].MaterialAfter.RefractiveIndex(wavelength.Nanometers);
        var nGlass = optic.SurfaceGroup.Items[1].MaterialAfter.RefractiveIndex(wavelength.Nanometers);
        var sinIncidence = (optic.Aperture.Value / 2) / optic.SurfaceGroup.Items[1].Radius;
        var refractedAngle = Math.Asin(nAir * sinIncidence / nGlass);
        var internalDirection = Math.Asin(sinIncidence) - refractedAngle;
        var numericalAperture = nGlass * Math.Sin(internalDirection);
        var expected = 1 / (2 * numericalAperture);

        var actual = DiffractionEngine.WorkingFNumber(optic, (0, 0), wavelength);

        Assert.Equal(expected, actual, 8);
        output.WriteLine($"Snell Working F/#: {expected:R}; common engine: {actual:R}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScaleProbesIgnoreAperturesWhileWavefrontKeepsClipping(bool aimed)
    {
        var optic = Import();
        var wave = optic.Wavelengths[0];
        var before = optic.SurfaceGroup.Items.Select(surface => surface.PhysicalAperture).ToArray();
        var clippedPupil = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, 32, aimAtStop: aimed);
        var blockedSamples = clippedPupil.Samples.Count(sample => sample.Intensity <= 0);
        Assert.True(blockedSamples > 0);
        Assert.Contains(clippedPupil.Samples, sample => sample.Intensity > 0);
        var working = DiffractionEngine.WorkingFNumbers(optic, (0, 0), wave, aimed);
        Assert.Equal(before, optic.SurfaceGroup.Items.Select(surface => surface.PhysicalAperture).ToArray());

        var unblocked = Import();
        foreach (var surface in unblocked.SurfaceGroup.Items) surface.PhysicalAperture = null;
        Assert.Equal(DiffractionEngine.WorkingFNumbers(unblocked, (0, 0), wave, aimed), working);
        var unblockedPupil = WavefrontEngine.GenerateChiefRayUniform(unblocked, (0, 0), wave, 32, aimAtStop: aimed);
        Assert.True(unblockedPupil.Samples.Count(sample => sample.Intensity <= 0) < blockedSamples);
    }

    [Fact]
    public void DiffractionMtfRetainsTheMeasuredClearAperture()
    {
        var optic = Import();
        var wave = optic.Wavelengths[0];
        var psf = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 128, 256, ignoreOpd: true);
        var mtf = DiffractionEngine.ComputeFftMtf(psf, optic, wave);
        var clearPupilRadius = 5.85 / 6.35;
        foreach (var index in new[] { 16, 32, 48, 64, 96 })
        {
            var normalizedFrequency = mtf.Frequency[index] / mtf.CutoffFrequency / clearPupilRadius;
            var expected = 2 / Math.PI * (Math.Acos(normalizedFrequency)
                - normalizedFrequency * Math.Sqrt(1 - normalizedFrequency * normalizedFrequency));
            Assert.InRange(Math.Abs(mtf.Tangential[index] - expected), 0, 0.005);
            Assert.InRange(Math.Abs(mtf.Sagittal[index] - expected), 0, 0.005);
        }
    }

    [Fact]
    public void DensePupilRetainsUnitPeakForZeroOpd()
    {
        var optic = Import();
        // More than 46340 illuminated samples overflow a 32-bit squared count.
        var psf = DiffractionEngine.ComputeFftPsf(optic, (0, 0), optic.Wavelengths[0], 512, 1024, ignoreOpd: true);
        Assert.Equal(1, psf.PeakStrehlRatio, 10);
        Assert.All(psf.Values.Cast<double>(), value => Assert.InRange(value, 0, 100 + 1e-8));
    }

    [Fact]
    public void EntirelyBlockedPupilDoesNotProduceAZeroPsfAsSuccess()
    {
        var optic = Import();
        optic.SurfaceGroup.Items[2].PhysicalAperture = new CircularAperture(0.001);
        Assert.True(DiffractionEngine.WorkingFNumber(optic, (0, 0), optic.Wavelengths[0]) > 0);
        Assert.Throws<AnalysisDataUnavailableException>(() =>
            DiffractionEngine.ComputeFftPsf(optic, (0, 0), optic.Wavelengths[0], 32, 64));
    }

    [Theory]
    [InlineData("MTF")]
    [InlineData("PSF")]
    [InlineData("FFT PSF Cross Section")]
    [InlineData("Huygens MTF")]
    [InlineData("Huygens PSF")]
    [InlineData("Fourier Through Focus MTF")]
    [InlineData("Fourier MTF vs Field")]
    [InlineData("Contrast Loss Map")]
    [InlineData("Color Focus Shift")]
    [InlineData("Lateral Color")]
    [InlineData("Encircled Energy")]
    [InlineData("Full Field Aberration")]
    public async Task ImportedFileCompletesDesktopDefaultAnalysis(string key)
    {
        using var application = WorkbenchApplication.Create("blank");
        await application.Documents.OpenAsync(FixturePath);
        var settings = application.Analyses.MergeSettings(key, null);
        var result = await application.Analyses.RunAsync(new AnalysisRequestDto(Guid.NewGuid(), 1, key, settings));
        Assert.Equal(OptilandWorkbench.Application.Contracts.AnalysisOutcome.Success, result.View.Outcome);
        var points = result.View.Series.Concat(result.View.PlotPanes.SelectMany(pane => pane.Series))
            .SelectMany(series => series.Points).ToArray();
        Assert.NotEmpty(points);
        Assert.All(points, point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
            // Contrast-loss maps mark the blocked/outside pupil as NaN.
            if (point.Value.HasValue && key != "Contrast Loss Map") Assert.True(double.IsFinite(point.Value.Value));
        });
        Assert.Contains(points, point => !point.Value.HasValue || double.IsFinite(point.Value.Value));
        if (key == "MTF")
        {
            Assert.All(result.View.Series, series =>
            {
                Assert.Equal(1, series.Points[0].Y, 10);
                Assert.All(series.Points, point => Assert.InRange(point.Y, 0, 1 + 1e-10));
                Assert.Contains(series.Points, point => point.X > 0 && point.Y > 0 && point.Y < 0.99);
            });
        }
        output.WriteLine($"{key}: {points.Count(point => !point.Value.HasValue || double.IsFinite(point.Value.Value))} valid points, {points.Count(point => point.Value.HasValue && !double.IsFinite(point.Value.Value))} masked points");
    }
}
