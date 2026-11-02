using System.Numerics;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

public sealed class PolychromeSfrTests
{
    [Fact]
    public void SpectralOtfIsCombinedBeforeMagnitudeAndPreservesItsPhaseSign()
    {
        const int n = 8; const double phase = .41;
        var pupil = new Complex[n, n];
        for (var y = 0; y < n; y++) for (var x = 0; x < n; x++) pupil[y, x] = Complex.FromPolarCoordinates(1, phase * x);
        var otf = DiffractionEngine.ComputeCellPupilOtf(pupil, [0, .125, .25], 1, 0, 8, 8);
        for (var i = 0; i < 3; i++)
            Assert.InRange((otf[i] - (1 - i / 8d) * Complex.FromPolarCoordinates(1, -phase * i)).Magnitude, 0, 1e-12);
        var mixed = MonochromeSlantedEdge.CombineSpectralOtf([[1, Complex.ImaginaryOne], [1, -Complex.ImaginaryOne]], [.5, .5]);
        Assert.Equal(1, mixed[0], 12); Assert.InRange(mixed[1], 0, 1e-12); // Averaging MTF magnitudes would incorrectly return one.
    }

    [Fact]
    public void LinearPsfMixUsesRawCapturedEnergyBeforeSingleNormalization()
    {
        var settings = new SlantedEdgeSettings(PupilSampling: 32, ImageSamplesPerPixel: 4, PsfRadiusPixels: 4);
        var a = new double[33, 33]; a[16, 14] = .1;
        var b = new double[33, 33]; b[16, 20] = .9;
        var mixed = new double[33, 33];
        MonochromeSlantedEdge.AccumulateSpectralPsf(mixed, a, .75);
        MonochromeSlantedEdge.AccumulateSpectralPsf(mixed, b, .25);
        var fa = MonochromeSlantedEdge.RenderPsf(new(a, 32, 33, 4, .75), settings);
        var fb = MonochromeSlantedEdge.RenderPsf(new(b, 32, 33, 4, .75), settings);
        var actual = MonochromeSlantedEdge.RenderPsf(new(mixed, 32, 33, 4, .75), settings);
        for (var i = 0; i < actual.LinearPixels.Count; i++)
            Assert.Equal((.75 * .1 * fa.LinearPixels[i] + .25 * .9 * fb.LinearPixels[i]) / (.75 * .1 + .25 * .9), actual.LinearPixels[i], 11);
        Assert.Equal((.75 * .1 + .25 * .9) * .75 * .75, actual.RelativePsfReadoutIntegral, 12);
    }

    [Fact]
    public void LensWeightsKeepOriginalRowsIgnoreZeroAndRejectEmptyPower()
    {
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(DoubleGaussSfrTests.StandardLens), ".zmx");
        optic.Wavelengths[0].Weight = 0; optic.Wavelengths[1].Weight = double.MaxValue; optic.Wavelengths[2].Weight = double.MaxValue / 2;
        var bands = MonochromeSlantedEdge.LensSpectrum(optic);
        Assert.Equal(new[] { 1, 2, 3 }, bands.Select(b => b.WavelengthNumber));
        Assert.Equal(0, bands[0].NormalizedWeight); Assert.Equal(2d / 3, bands[1].NormalizedWeight, 12); Assert.Equal(1d / 3, bands[2].NormalizedWeight, 12);
        foreach (var w in optic.Wavelengths) w.Weight = 0;
        Assert.Contains("全为零", Assert.Throws<ArgumentException>(() => MonochromeSlantedEdge.LensSpectrum(optic)).Message);
        while (optic.Wavelengths.Count <= 16) optic.Wavelengths.Add(new Wavelength());
        Assert.Throws<ArgumentException>(() => MonochromeSlantedEdge.LensSpectrum(optic));
    }

    [Fact]
    public void SingleBandAndZeroWeightBandReduceToOriginalMonochromeImage()
    {
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(DoubleGaussSfrTests.StandardLens), ".zmx");
        optic.Wavelengths.Clear(); optic.Wavelengths.Add(new() { Nanometers = 550, Weight = 7, IsPrimary = true });
        var settings = new SlantedEdgeSettings(PupilSampling: 128, PsfRadiusPixels: 16);
        var mono = MonochromeSlantedEdge.Render(optic, settings);
        optic.Wavelengths.Add(new() { Nanometers = 100, Weight = 0, IsPrimary = false }); // Must never be traced or require dispersion there.
        var poly = MonochromeSlantedEdge.Render(optic, settings with { UseLensWavelengths = true });
        Assert.InRange(mono.LinearPixels.Zip(poly.LinearPixels, (a, b) => Math.Abs(a - b)).Max(), 0, 1e-12);
        Assert.InRange(mono.FullPupilMtf!.Zip(poly.FullPupilMtf!, (a, b) => Math.Abs(a.Response - b.Response)).Max(), 0, 1e-12);
        Assert.Equal(550, poly.ReferenceWavelengthNanometers); Assert.Equal(2, poly.Spectrum!.Count);
    }

    [Fact]
    public void PrimaryReferencePreservesOffAxisChromaticDisplacement()
    {
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(DoubleGaussSfrTests.StandardLens), ".zmx");
        var field = optic.Fields[^1]; var normalized = FieldCoordinates.Normalize(optic.Fields, field.X, field.Y);
        var wave = optic.Wavelengths[0]; var reference = optic.Wavelengths.First(w => w.IsPrimary);
        var f = new[] { 0d, 30d, 70d };
        var own = DiffractionEngine.ComputeLocalPupilOtfAlongDirection(optic, (normalized.X, normalized.Y), wave, 128, f, 0, 1);
        var common = DiffractionEngine.ComputeLocalPupilOtfAlongDirection(optic, (normalized.X, normalized.Y), wave, 128, f, 0, 1, reference);
        Assert.True(own.Zip(common, (a, b) => (a - b).Magnitude).Max() > .001);
        Assert.Equal(1, common[0].Real, 10);
    }

    [Fact]
    public async Task UnsavedLensWeightsFreezeReplayAndValidateAgainstSnapshot()
    {
        using var app = WorkbenchApplication.Create(); await app.Documents.OpenAsync(DoubleGaussSfrTests.StandardLens);
        var waves = app.Prescription.GetWavelengths();
        app.Prescription.UpdateWavelength(waves[0] with { Weight = 2 });
        app.Prescription.UpdateWavelength(waves[1] with { Weight = 1 });
        app.Prescription.UpdateWavelength(waves[2] with { Weight = 0 });
        var before = app.Documents.GetSnapshot();
        var result = await app.CameraImaging.SimulateSlantedEdgeAsync(CameraSfrRequest.FromLensSpectrum());
        Assert.Equal(before, app.Documents.GetSnapshot()); Assert.True(result.SamplingConverged);
        Assert.Equal(CameraSfrResult.PolychromeAlgorithm, result.Algorithm);
        Assert.Equal(new[] { 2d, 1d, 0d }, result.Baseline.Spectrum!.Select(b => b.Weight));
        Assert.InRange(result.Baseline.MaximumSfrFullPupilDifference!.Value, 0, .01);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sfr-experiment.zip");
        try
        {
            await app.CameraImaging.ExportSlantedEdgeAsync(result, path);
            app.Documents.NewBlank(); app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
            var blank = app.Documents.GetSnapshot();
            var opened = await app.CameraImaging.OpenSlantedEdgeAsync(path);
            var replay = await app.CameraImaging.RecomputeSlantedEdgeAsync(opened);
            Assert.Equal(result.Baseline.LinearPixels, replay.Baseline.LinearPixels); Assert.Equal(result.Baseline.Curve, replay.Baseline.Curve);
            Assert.Equal(result.Baseline.Spectrum, replay.Baseline.Spectrum); Assert.Equal(blank, app.Documents.GetSnapshot());
            var invalid = result with { Baseline = result.Baseline with { Spectrum = result.Baseline.Spectrum!.Select((b, i) => i == 0 ? b with { Weight = 10 } : b).ToArray() } };
            await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.ExportSlantedEdgeAsync(invalid, path));
            Assert.Equal(result.Baseline.Curve, (await app.CameraImaging.OpenSlantedEdgeAsync(path)).Baseline.Curve);
            var wrongMode = result with { Algorithm = CameraSfrResult.CurrentAlgorithm };
            await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.ExportSlantedEdgeAsync(wrongMode, path));
            var wrongReference = result with { Baseline = result.Baseline with { ReferenceWavelengthNanometers = 550 } };
            await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.ExportSlantedEdgeAsync(wrongReference, path));
            var dir = Environment.GetEnvironmentVariable("OPTICAL_POLY_SFR_EVIDENCE_DIR");
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir); File.Copy(path, Path.Combine(dir, "unsaved-weights.sfr-experiment.zip"), true);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task OriginalMonochromeV3ArchiveStillReplaysWithoutSpectralMixing()
    {
        using var app = WorkbenchApplication.Create();
        var original = await app.CameraImaging.OpenSlantedEdgeAsync(Path.Combine(SlantedEdgeSfrTests.RepositoryRoot,
            "validation/rechecks/2026-11-02/sfr-directional/final/reference-lens.sfr-experiment.zip"));
        Assert.Equal(CameraSfrResult.CurrentAlgorithm, original.Algorithm); Assert.False(original.Request.UseLensWavelengths);
        Assert.Null(original.Baseline.Spectrum); Assert.True(CameraSfrResult.CanRecompute(original.Algorithm));
        var replay = await app.CameraImaging.RecomputeSlantedEdgeAsync(original);
        Assert.Equal(original.Baseline.LinearPixels, replay.Baseline.LinearPixels);
        Assert.Equal(original.Baseline.Curve, replay.Baseline.Curve); Assert.Null(replay.Baseline.Spectrum);
    }
}
