using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

public sealed class CameraSensorFileTests
{
    internal static string Examples => Path.Combine(AppContext.BaseDirectory, "Examples", "CameraImaging");
    [Fact]
    public async Task BundledFractionAndPercentCurvesHaveEquivalentAbsoluteResponseAndFingerprints()
    {
        using var app = WorkbenchApplication.Create();
        var a = await app.CameraImaging.ImportResponseAsync(Path.Combine(Examples, "example-qe.csv"));
        var b = await app.CameraImaging.ImportResponseAsync(Path.Combine(Examples, "example-qe-percent.csv"));
        Assert.Equal(a.Samples.Count, b.Samples.Count);
        foreach (var (x, y) in a.Samples.Zip(b.Samples))
        { Assert.Equal(x.WavelengthNanometers, y.WavelengthNanometers); Assert.Equal(x.RedQe, y.RedQe, 12); Assert.Equal(x.GreenQe, y.GreenQe, 12); Assert.Equal(x.BlueQe, y.BlueQe, 12); }
        Assert.NotEqual(a.Origin!.Sha256, b.Origin!.Sha256);
        var raw = File.ReadAllBytes(Path.Combine(Examples, "example-qe.csv"));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(raw)), a.Origin.Sha256);
        var sensor = new CameraSensorParameters(NoiseEnabled: false);
        var request = new CameraImagingRequest(Width: 16, Height: 16, Optics: CameraOptics.Ideal, Sensor: sensor, SpectralResponse: a);
        var first = await app.CameraImaging.SimulateAsync(request); var second = await app.CameraImaging.SimulateAsync(request with { SpectralResponse = b });
        Assert.Equal(first.RawDn, second.RawDn);
    }

    [Fact]
    public async Task SensorProfileRoundTripsWithEmbeddedResponseAndRunsWithImportedParameters()
    {
        using var files = new TemporaryFiles(); using var app = WorkbenchApplication.Create();
        var template = await app.CameraImaging.ImportSensorAsync(Path.Combine(Examples, "generic-sensor.camera-sensor.json"));
        var curve = await app.CameraImaging.ImportResponseAsync(Path.Combine(Examples, "example-qe.csv"));
        var profile = template with { Sensor = template.Sensor with { ExposureMilliseconds = 25, Pattern = CameraBayerPattern.BGGR }, SpectralResponse = curve };
        var path = files.Path("roundtrip.json"); await app.CameraImaging.SaveSensorAsync(profile, path);
        var loaded = await app.CameraImaging.ImportSensorAsync(path);
        Assert.Equal(profile.Sensor, loaded.Sensor); Assert.Equal(profile.SpectralResponse.Samples, loaded.SpectralResponse!.Samples);
        Assert.Equal(Path.GetFileName(path), loaded.Origin!.FileName);
        var bomPath = files.Path("bom.json"); File.WriteAllBytes(bomPath, Encoding.UTF8.GetPreamble().Concat(File.ReadAllBytes(path)).ToArray());
        Assert.Equal(loaded.Sensor, (await app.CameraImaging.ImportSensorAsync(bomPath)).Sensor);
        var result = await app.CameraImaging.SimulateAsync(new(Width: 16, Height: 16, Optics: CameraOptics.Ideal,
            Sensor: loaded.Sensor, SpectralResponse: loaded.SpectralResponse));
        Assert.Equal(25, result.Request.Sensor!.ExposureMilliseconds); Assert.Contains("three discrete", result.ModelScope);
    }

    [Theory]
    [InlineData("wavelength_nm,qe_r,qe_g,qe_b\n450,50,20,10\n650,60,10,0")]
    [InlineData("wavelength_nm,qe_r,qe_g,qe_b\n650,.5,.2,.1\n450,.6,.1,0")]
    [InlineData("wavelength_nm,qe_r,qe_g,qe_b\n450,.5,.2,.1\n450,.6,.1,0")]
    [InlineData("wavelength_nm,qe_r,qe_g,qe_b\n100,.5,.2,.1\n650,.6,.1,0")]
    [InlineData("wavelength_nm,qe_r,qe_g,qe_b\n450,NaN,.2,.1\n650,.6,.1,0")]
    [InlineData("wavelength_nm,response_r,response_g,response_b\n450,.5,.2,.1\n650,.6,.1,0")]
    public async Task InvalidUnitsOrderCoverageAndNumbersAreRejected(string text)
    {
        using var files = new TemporaryFiles(); using var app = WorkbenchApplication.Create();
        var path = files.Path("invalid.csv"); File.WriteAllText(path, text);
        await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.ImportResponseAsync(path));
    }

    [Theory]
    [InlineData("missing")] [InlineData("unknown")] [InlineData("duplicate")]
    [InlineData("schema")] [InlineData("negative")] [InlineData("enum")]
    public async Task InvalidProfilesCannotSilentlyFillMissingFieldsOrAcceptTypos(string kind)
    {
        using var files = new TemporaryFiles(); using var app = WorkbenchApplication.Create();
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(Examples, "generic-sensor.camera-sensor.json")))!;
        switch (kind)
        {
            case "missing": node["Sensor"]!.AsObject().Remove("GainDnPerElectron"); break;
            case "unknown": node["Sensor"]!["GainDnPerElctron"] = .3; break;
            case "schema": node["SchemaVersion"] = 2; break;
            case "negative": node["Sensor"]!["ReadNoiseElectrons"] = -1; break;
            case "enum": node["Sensor"]!["Pattern"] = 0; break;
        }
        var text = node.ToJsonString(); if (kind == "duplicate") text = text.Replace("\"SchemaVersion\":1", "\"SchemaVersion\":1,\"schemaversion\":1");
        var path = files.Path("invalid.json"); File.WriteAllText(path, text);
        await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.ImportSensorAsync(path));
    }

    [Fact]
    public async Task ImportAndExportFreezeABResponsesAndPreserveCancelledFiles()
    {
        using var files = new TemporaryFiles(); using var app = WorkbenchApplication.Create();
        var imported = await app.CameraImaging.ImportResponseAsync(Path.Combine(Examples, "example-qe.csv"));
        var callerSamples = imported.Samples.ToArray();
        var request = new CameraImagingRequest(Width: 16, Height: 16, Optics: CameraOptics.Ideal,
            Sensor: new(NoiseEnabled: false), SpectralResponse: imported with { Samples = callerSamples });
        var a = await app.CameraImaging.SimulateAsync(request);
        callerSamples[0] = callerSamples[0] with { RedQe = .9 };
        var b = await app.CameraImaging.SimulateAsync(request with { SpectralResponse = null });
        Assert.Equal(0, a.Request.SpectralResponse!.Samples[0].RedQe);
        var output = files.Path("AB.zip"); await app.CameraImaging.ExportAsync(b, output, a);
        using (var zip = ZipFile.OpenRead(output))
        {
            using var profileStream = zip.GetEntry("A/sensor-profile.json")!.Open(); using var document = JsonDocument.Parse(profileStream);
            Assert.Equal(0, document.RootElement.GetProperty("SpectralResponse").GetProperty("Samples")[0].GetProperty("RedQe").GetDouble());
            Assert.NotNull(zip.GetEntry("A/spectral-response.csv")); Assert.Null(zip.GetEntry("B/spectral-response.csv"));
            var exportedProfile = files.Path("from-A.json");
            zip.GetEntry("A/sensor-profile.json")!.ExtractToFile(exportedProfile);
            var loaded = await app.CameraImaging.ImportSensorAsync(exportedProfile);
            Assert.Equal(a.Request.SpectralResponse.Samples, loaded.SpectralResponse!.Samples);
        }
        var evidence = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(evidence)) { Directory.CreateDirectory(evidence); File.Copy(output, System.IO.Path.Combine(evidence, "spectral-AB.zip"), true); }
        var profile = new CameraSensorProfile(CameraSensorProfile.FormatIdentifier, 1, "test", "synthetic", new());
        var target = files.Path("preserve.json"); File.WriteAllText(target, "preserve");
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.CameraImaging.SaveSensorAsync(profile, target, cts.Token));
        Assert.Equal("preserve", File.ReadAllText(target));
        var huge = files.Path("huge.csv"); using (var stream = File.Create(huge)) stream.SetLength(1024 * 1024 + 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.ImportResponseAsync(huge));
    }

    internal sealed class TemporaryFiles : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "camera-import-" + Guid.NewGuid().ToString("N"));
        public TemporaryFiles() => Directory.CreateDirectory(_root);
        internal string Path(string name) => System.IO.Path.Combine(_root, name);
        public void Dispose()
        {
            var full = System.IO.Path.GetFullPath(_root);
            if (!full.StartsWith(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !System.IO.Path.GetFileName(full).StartsWith("camera-import-", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected test directory.");
            Directory.Delete(full, true);
        }
    }
}
