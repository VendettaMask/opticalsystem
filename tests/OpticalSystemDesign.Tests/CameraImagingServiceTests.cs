using System.IO.Compression;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using SkiaSharp;

namespace OpticalSystemDesign.Tests;

public sealed class CameraImagingServiceTests
{
    [Fact]
    public async Task IdealControlRunsWithoutLensAndExportsOriginalRawAndBothConfigurations()
    {
        using var app = WorkbenchApplication.Create();
        var request = new CameraImagingRequest(Width: 16, Height: 16, Optics: CameraOptics.Ideal);
        var first = await app.CameraImaging.SimulateAsync(request);
        var second = await app.CameraImaging.SimulateAsync(request with { Sensor = new(ExposureMilliseconds: 20) });
        Assert.Equal(first.MeanPhotoElectrons * 2, second.MeanPhotoElectrons, 8);
        Assert.Equal(16 * 16 * 3, first.Developed.Rgb24.Count);
        Assert.Contains("not", first.ModelScope, StringComparison.OrdinalIgnoreCase);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        try
        {
            await app.CameraImaging.ExportAsync(second, path, first);
            var evidence = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_EVIDENCE_DIR");
            if (!string.IsNullOrWhiteSpace(evidence))
            { Directory.CreateDirectory(evidence); File.Copy(path, Path.Combine(evidence, "sample-AB-comparison.zip"), true); }
            using var zip = ZipFile.OpenRead(path);
            Assert.Equal(19, zip.Entries.Count);
            using var csv = new StreamReader(zip.GetEntry("A/sensor.csv")!.Open());
            var lines = (await csv.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(257, lines.Length);
            Assert.Equal(first.RawDn[0].ToString(), lines[1].Split(',')[2]);
            Assert.NotNull(zip.GetEntry("B/noiseless-isp.png"));
            using var metadata = new StreamReader(zip.GetEntry("B/metadata.json")!.Open());
            var text = await metadata.ReadToEndAsync();
            Assert.Contains("ExposureMilliseconds", text); Assert.Contains("BandsNanometers", text);
            Assert.Contains("RGGB", text); Assert.Contains("CameraLabV1", text); Assert.Contains("OpticalSampling", text);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CurrentLensUsesSnapshotAndSharedOpticsWithoutChangingPrescription()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var before = app.Documents.GetSnapshot(); var fields = app.Prescription.GetFields().ToArray();
        var request = new CameraImagingRequest(Width: 16, Height: 16, Aberration: CameraAberration.Geometric);
        var result = await app.CameraImaging.SimulateAsync(request);
        Assert.Equal(before.Revision, result.SourceRevision);
        Assert.Equal(before, app.Documents.GetSnapshot()); Assert.Equal(fields, app.Prescription.GetFields());
        Assert.Contains("Geometric", result.OpticalModel);
        var diffraction = await app.CameraImaging.SimulateAsync(request with { Aberration = CameraAberration.Diffraction });
        Assert.NotEqual("Ideal optical control", diffraction.OpticalModel);
        Assert.Equal(before, app.Documents.GetSnapshot());
    }

    [Fact]
    public async Task ModeEmptyLensMissingImageAndCancellationHaveExplicitFailures()
    {
        using var app = WorkbenchApplication.Create();
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.CameraImaging.SimulateAsync(new(Width: 16, Height: 16)));
        app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.CameraImaging.SimulateAsync(new()));
        var ideal = new CameraImagingRequest(Width: 16, Height: 16, Optics: CameraOptics.Ideal);
        Assert.NotNull(await app.CameraImaging.SimulateAsync(ideal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.CameraImaging.SimulateAsync(ideal with { Source = CameraSource.ExternalImage }));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.CameraImaging.SimulateAsync(ideal, cts.Token));
        var result = await app.CameraImaging.SimulateAsync(ideal);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        try
        {
            File.WriteAllText(path, "existing");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.CameraImaging.ExportAsync(result, path, cancellationToken: cts.Token));
            Assert.Equal("existing", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task MaximumOfferedResolutionsFitTheSharedBudgetAndOversizedOpticsAreRejected()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var optical = await app.CameraImaging.SimulateAsync(new(Width: 256, Height: 192, Aberration: CameraAberration.Geometric));
        Assert.Equal(256 * 192, optical.RawDn.Count);
        Assert.True(optical.MeanPhotoElectrons > 0);
        var sensor = await app.CameraImaging.SimulateAsync(new(Width: 512, Height: 384, Optics: CameraOptics.Ideal));
        Assert.Equal(512 * 384, sensor.RawDn.Count);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => app.CameraImaging.SimulateAsync(new(Width: 512, Height: 384)));
    }

    [Fact]
    public async Task ImportedSrgbIsLinearizedBeforePhysicalExposureAndInvalidExportPreservesFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        var output = path + ".zip";
        try
        {
            using (var bitmap = new SKBitmap(32, 24))
            {
                bitmap.Erase(new SKColor(128, 128, 128));
                using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, encoded.ToArray());
            }
            using var app = WorkbenchApplication.Create();
            var result = await app.CameraImaging.SimulateAsync(new(CameraSource.ExternalImage, path, 16, 16, CameraOptics.Ideal,
                Sensor: new(NoiseEnabled: false)));
            var linear = Math.Pow((128d / 255 + 0.055) / 1.055, 2.4);
            var expectedRed = linear * 0.02 * 9e-12 * 0.01 * 650e-9 / (6.62607015e-34 * 299792458) * 0.5;
            Assert.Equal(expectedRed, result.PhotoElectrons[0], 9);
            File.WriteAllText(output, "preserve");
            await Assert.ThrowsAsync<ArgumentException>(() => app.CameraImaging.ExportAsync(result with { RawDn = Array.Empty<ushort>() }, output));
            Assert.Equal("preserve", File.ReadAllText(output));
        }
        finally { File.Delete(path); File.Delete(output); }
    }
}
