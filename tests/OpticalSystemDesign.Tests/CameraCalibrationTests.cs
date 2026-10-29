using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Analysis.Imaging;

namespace OpticalSystemDesign.Tests;

public sealed class CameraCalibrationTests
{
    internal static CameraCalibrationCapture Capture() => new(CameraCalibrationCapture.FormatIdentifier, 1,
        "Synthetic dark/flat fixture", "Analytic synthetic fixture, not a measured sensor", "Synthetic Bayer device",
        "Linear RAW; fixed 0.5 DN/e- gain; uniform flat per CFA; no ISP", 20, 8, 8, 12, CameraBayerPattern.RGGB, 0.5,
        new[] { Frame(0, -2, false), Frame(0, 2, false), Frame(1000, -2, false), Frame(1000, 2, false) },
        new[] { Frame(1000, -2, true), Frame(1000, 2, true) }, Synthetic: true);

    private static CameraCalibrationFrame Frame(double exposure, int noise, bool flat) => new(exposure,
        Enumerable.Range(0, 64).Select(i => (ushort)(100 + (i % 8 / 2 % 2 == 0 ? -3 : 3) + exposure / 100 + noise
            + (flat ? new[] { 1000, 1500, 1800, 800 }[(i / 8 % 2) * 2 + i % 2] + (i % 8 / 2 % 2 == 0 ? -40 : 40) : 0))).ToArray());

    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public void DarkFlatEstimatesMatchIndependentAnalyticMomentsAndSeparateCfaPhases()
    {
        var c = Capture();
        var result = CameraSensorCalibration.Estimate(c.Width, c.Height, c.BitDepth, c.GainDnPerElectron,
            c.DarkFrames.Select(f => new SensorCalibrationFrame(f.ExposureMilliseconds, f.RawDn)).ToArray(),
            c.FlatFrames.Select(f => new SensorCalibrationFrame(f.ExposureMilliseconds, f.RawDn)).ToArray());
        Assert.Equal(100, result.BlackLevelDn, 12); Assert.Equal(20, result.DarkElectronsPerSecond, 12);
        Assert.Equal(Math.Sqrt(8 - 1d / 12) / 0.5, result.ReadNoiseElectrons, 12);
        Assert.Equal(Math.Sqrt(9d * 64 / 63 - 4) / 0.5, result.DsnuElectrons, 12);
        var relative = new[] { 1000d, 1500, 1800, 800 }.Average(m => (1600d * 16 / 15 - 8) / (m * m));
        Assert.Equal(Math.Sqrt(relative) * 100, result.PrnuPercent, 12);
    }

    [Fact]
    public async Task RawCalibrationCreatesReusableTemplateWithEvidenceAndPreservesUnmeasuredParameters()
    {
        using var app = WorkbenchApplication.Create();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"); var output = path + ".profile.json";
        try
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(Capture(), Json));
            var baseline = new CameraSensorProfile(CameraSensorProfile.FormatIdentifier, 1, "baseline", "manual", new(RedQe: 0.23, PixelPitchMicrometers: 4.2));
            var profile = await app.CameraImaging.CalibrateSensorAsync(path, baseline);
            Assert.Equal(0.23, profile.Sensor.RedQe); Assert.Equal(4.2, profile.Sensor.PixelPitchMicrometers);
            Assert.Equal(100, profile.Sensor.BlackLevel); Assert.Equal(20, profile.Sensor.DarkElectronsPerSecond, 10);
            Assert.Contains("SYNTHETIC", profile.Source); Assert.Equal(64, profile.Calibration!.Origin.Sha256.Length);
            File.Delete(path);
            await app.CameraImaging.SaveSensorAsync(profile, output);
            var imported = await app.CameraImaging.ImportSensorAsync(output);
            Assert.Equal(profile.Sensor, imported.Sensor); Assert.Equal(profile.Calibration.Origin, imported.Calibration!.Origin);
            Assert.Equal(profile.Calibration.Capture.DarkFrames[0].RawDn, imported.Calibration.Capture.DarkFrames[0].RawDn);
            var request = new CameraImagingRequest(Width: 8, Height: 8, Optics: CameraOptics.Ideal, Sensor: imported.Sensor,
                SensorIdentity: new(imported.Name, imported.Source, imported.Origin, Calibration: imported.Calibration));
            var run = await app.CameraImaging.SimulateAsync(request);
            Assert.NotNull(run.Request.SensorIdentity?.Calibration);
            await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, run), path);
            var opened = await app.CameraImaging.OpenExperimentAsync(path);
            Assert.Equal(profile.Calibration.PrnuPercent, opened.B.Request.SensorIdentity!.Calibration!.PrnuPercent);
            var evidence = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_EXPERIMENT_EVIDENCE_DIR");
            if (!string.IsNullOrWhiteSpace(evidence))
            {
                Directory.CreateDirectory(evidence);
                File.Copy(output, Path.Combine(evidence, "synthetic-calibrated.camera-sensor.json"), true);
                File.Copy(path, Path.Combine(evidence, "synthetic-calibrated.camera-experiment.zip"), true);
            }
        }
        finally { File.Delete(path); File.Delete(output); }
    }

    [Theory]
    [InlineData("clipped")]
    [InlineData("wrong-exposure")]
    [InlineData("missing-dark")]
    [InlineData("unknown-field")]
    [InlineData("tampered-summary")]
    public async Task InvalidCalibrationIsRejected(string kind)
    {
        using var app = WorkbenchApplication.Create(); var capture = Capture();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            if (kind == "clipped") capture = capture with { FlatFrames = capture.FlatFrames.Select(f => f with { RawDn = new ushort[64] }).ToArray() };
            if (kind == "wrong-exposure") capture = capture with { FlatFrames = capture.FlatFrames.Select(f => f with { ExposureMilliseconds = 20 }).ToArray() };
            if (kind == "missing-dark") capture = capture with { DarkFrames = capture.DarkFrames.Take(2).ToArray() };
            var text = JsonSerializer.Serialize(capture, Json);
            if (kind == "unknown-field") text = text.Replace("\"Device\":", "\"Unexpected\":0,\"Device\":");
            await File.WriteAllTextAsync(path, text);
            var baseline = new CameraSensorProfile(CameraSensorProfile.FormatIdentifier, 1, "baseline", "manual", new());
            if (kind == "tampered-summary")
            {
                var profile = await app.CameraImaging.CalibrateSensorAsync(path, baseline);
                await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.SaveSensorAsync(profile with
                { Calibration = profile.Calibration! with { PrnuPercent = 20 } }, path));
            }
            else await Assert.ThrowsAnyAsync<Exception>(() => app.CameraImaging.CalibrateSensorAsync(path, baseline));
        }
        finally { File.Delete(path); }
    }
}
