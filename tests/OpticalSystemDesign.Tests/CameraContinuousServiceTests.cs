using System.IO.Compression;
using System.Security.Cryptography;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

public sealed class CameraContinuousServiceTests
{
    internal static async Task<CameraImagingRequest> Request(ICameraImagingService service) => new(Width: 16, Height: 16,
        Optics: CameraOptics.Ideal, Sensor: new(NoiseEnabled: false, DarkElectronsPerSecond: 0),
        SpectralResponse: await service.ImportResponseAsync(Path.Combine(CameraSensorFileTests.Examples, "example-qe.csv")),
        SpectralModel: CameraSpectralModel.Continuous,
        Illuminant: await service.ImportIlluminantAsync(Path.Combine(CameraSensorFileTests.Examples, "illuminant-flat.csv")),
        Reflectance: await service.ImportReflectanceAsync(Path.Combine(CameraSensorFileTests.Examples, "reflectance-neutral.csv")));

    [Fact]
    public async Task ImportsAreAbsoluteAndPercentReflectanceMatchesFraction()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); using var app = WorkbenchApplication.Create();
        var request = await Request(app.CameraImaging);
        Assert.Equal(.0001, request.Illuminant!.Samples[0].Value);
        var percentPath = files.Path("percent.csv"); File.WriteAllText(percentPath, "wavelength_nm,reflectance_percent\n400,50\n700,50");
        var percent = await app.CameraImaging.ImportReflectanceAsync(percentPath);
        Assert.Equal(request.Reflectance!.Samples, percent.Samples);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(percentPath))), percent.Origin!.Sha256);
        var a = await app.CameraImaging.SimulateAsync(request);
        var b = await app.CameraImaging.SimulateAsync(request with { Reflectance = percent });
        Assert.Equal(a.RawDn, b.RawDn); Assert.Equal(.03, a.Integration!.IlluminantIrradianceWattsPerSquareMeter, 12);
        var scaled = await app.CameraImaging.SimulateAsync(request with
        {
            Illuminant = request.Illuminant with
            { Samples = request.Illuminant.Samples.Select(s => s with { Value = s.Value / 2 }).ToArray() }
        });
        Assert.Equal(a.MeanPhotoElectrons / 2, scaled.MeanPhotoElectrons, 9);
    }

    [Theory]
    [InlineData(true, "wavelength_nm,relative_power\n400,1\n700,1")]
    [InlineData(true, "wavelength_nm,irradiance_w_m2_nm\n400,-1\n700,0")]
    [InlineData(true, "wavelength_nm,irradiance_w_m2_nm\n400,NaN\n700,0")]
    [InlineData(true, "wavelength_nm,irradiance_w_m2_nm\n400,.01\n400,.01")]
    [InlineData(false, "wavelength_nm,reflectance\n400,50\n700,50")]
    [InlineData(false, "wavelength_nm,reflectance_percent\n400,101\n700,0")]
    [InlineData(false, "wavelength_nm,reflectance\n700,1\n400,1")]
    [InlineData(false, "wavelength_nm,irradiance_w_m2_nm\n400,1\n700,1")]
    public async Task IncorrectUnitsInvalidNumbersAndOrderingCannotSilentlyChangeMeaning(bool light, string content)
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); using var app = WorkbenchApplication.Create();
        var path = files.Path("invalid.csv"); File.WriteAllText(path, content);
        await Assert.ThrowsAsync<InvalidDataException>(() => light ? app.CameraImaging.ImportIlluminantAsync(path) : app.CameraImaging.ImportReflectanceAsync(path));
    }

    [Fact]
    public async Task LegacyAndContinuousABKeepFrozenOriginsAndExportReimportableSpectra()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); using var app = WorkbenchApplication.Create();
        var request = await Request(app.CameraImaging);
        var caller = request.Reflectance!.Samples.ToArray(); request = request with { Reflectance = request.Reflectance with { Samples = caller } };
        var a = await app.CameraImaging.SimulateAsync(request with { SpectralModel = CameraSpectralModel.ThreeBand });
        var b = await app.CameraImaging.SimulateAsync(request);
        caller[0] = caller[0] with { Value = 0 };
        Assert.Equal(.5, b.Request.Reflectance!.Samples[0].Value); Assert.Null(a.Integration);
        Assert.Equal(CameraSpectralModel.ThreeBand, a.Request.SpectralModel); Assert.NotEqual(a.MeanPhotoElectrons, b.MeanPhotoElectrons);
        var output = files.Path("continuous-AB.zip"); await app.CameraImaging.ExportAsync(b, output, a);
        using (var zip = ZipFile.OpenRead(output))
        {
            Assert.Null(zip.GetEntry("A/illuminant.csv")); Assert.NotNull(zip.GetEntry("B/integration.csv"));
            foreach (var name in new[] { "illuminant", "reflectance" }) zip.GetEntry("B/" + name + ".csv")!.ExtractToFile(files.Path(name + ".csv"));
            var light = await app.CameraImaging.ImportIlluminantAsync(files.Path("illuminant.csv"));
            var rho = await app.CameraImaging.ImportReflectanceAsync(files.Path("reflectance.csv"));
            var restored = await app.CameraImaging.SimulateAsync(request with { Illuminant = light, Reflectance = rho });
            Assert.Equal(b.RawDn, restored.RawDn);
            using var metadata = new StreamReader(zip.GetEntry("B/metadata.json")!.Open()); var json = await metadata.ReadToEndAsync();
            Assert.Contains("CameraLabV3-Continuous", json); Assert.Contains(b.Request.Illuminant!.Origin!.Sha256, json);
        }
        var evidence = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(evidence)) { Directory.CreateDirectory(evidence); File.Copy(output, Path.Combine(evidence, "continuous-AB.zip"), true); }
    }

    [Fact]
    public async Task CurrentLensRunsEveryQuadratureWavelengthOnSnapshot()
    {
        using var app = WorkbenchApplication.Create("cooke"); var before = app.Documents.GetSnapshot();
        var request = await Request(app.CameraImaging);
        var result = await app.CameraImaging.SimulateAsync(request with { Optics = CameraOptics.CurrentLens, Aberration = CameraAberration.Geometric });
        Assert.Equal(before, app.Documents.GetSnapshot()); Assert.Equal(9, result.Integration!.Nodes.Count);
        Assert.Equal(9, result.Integration.Nodes.Select(n => n.WavelengthNanometers).Distinct().Count());
        Assert.Contains("Geometric", result.OpticalModel); Assert.True(result.MeanPhotoElectrons > 0);
        CameraContinuousSpectrumTests.Record("current-lens-continuous", new
        {
            Nodes = result.Integration.Nodes.Count,
            result.OpticalModel,
            result.OpticalFallbackCount,
            result.MeanPhotoElectrons,
            UnchangedSnapshot = before == app.Documents.GetSnapshot()
        });
    }

    [Fact]
    public async Task ContinuousInfraredRangeUsesImportedCoverageWithoutRequiringLegacyVisibleBands()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); using var app = WorkbenchApplication.Create();
        var path = files.Path("infrared-qe.csv"); File.WriteAllText(path, "wavelength_nm,qe_r,qe_g,qe_b\n800,.2,.3,.4\n1000,.2,.3,.4");
        var qe = await app.CameraImaging.ImportResponseAsync(path);
        var request = new CameraImagingRequest(Width: 16, Height: 16, Optics: CameraOptics.Ideal,
            Sensor: new(NoiseEnabled: false), SpectralResponse: qe, SpectralModel: CameraSpectralModel.Continuous,
            Illuminant: new("IR light", "Synthetic", CameraSpectrumKind.Illuminant, new[] { new CameraSpectrumSample(800, .0001), new(1000, .0001) }),
            Reflectance: new("IR material", "Synthetic", CameraSpectrumKind.Reflectance, new[] { new CameraSpectrumSample(800, .5), new(1000, .5) }),
            Integration: new(800, 1000));
        var result = await app.CameraImaging.SimulateAsync(request);
        Assert.True(result.MeanPhotoElectrons > 0); Assert.Equal(6, result.Integration!.Nodes.Count);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => app.CameraImaging.SimulateAsync(request with { SpectralModel = CameraSpectralModel.ThreeBand }));
    }

    [Fact]
    public async Task MissingCoverageSwappedKindsAndGlobalWorkBudgetAreRejected()
    {
        using var app = WorkbenchApplication.Create(); var request = await Request(app.CameraImaging);
        await Assert.ThrowsAsync<ArgumentException>(() => app.CameraImaging.SimulateAsync(request with { Illuminant = null }));
        await Assert.ThrowsAsync<ArgumentException>(() => app.CameraImaging.SimulateAsync(request with { SpectralResponse = null }));
        await Assert.ThrowsAsync<ArgumentException>(() => app.CameraImaging.SimulateAsync(request with { Illuminant = request.Reflectance }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => app.CameraImaging.SimulateAsync(request with { Integration = new(350, 700) }));
        await Assert.ThrowsAsync<ArgumentException>(() => app.CameraImaging.SimulateAsync(request with { Integration = new(MaximumIntervalNanometers: 10), Optics = CameraOptics.CurrentLens }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => app.CameraImaging.SimulateAsync(request with { Width = 256, Height = 192, Optics = CameraOptics.CurrentLens }));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.CameraImaging.SimulateAsync(request, cts.Token));
    }
}
