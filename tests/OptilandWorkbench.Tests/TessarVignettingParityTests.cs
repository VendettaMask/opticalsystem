using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Tests;

public sealed class TessarVignettingParityTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Validation", "Zemax", "TessarVignetting");
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    [Theory]
    [InlineData("original-primary", "ray-fan")]
    [InlineData("original-primary", "optical-path-difference")]
    [InlineData("original-edge", "ray-fan")]
    [InlineData("original-edge", "optical-path-difference")]
    [InlineData("original-axis-full", "ray-fan")]
    [InlineData("original-axis-full", "optical-path-difference")]
    [InlineData("original-field2-primary", "ray-fan")]
    [InlineData("original-field2-primary", "optical-path-difference")]
    [InlineData("rotated30-axis-compressed", "ray-fan")]
    [InlineData("rotated30-axis-compressed", "optical-path-difference")]
    [InlineData("rotated30-axis-full", "ray-fan")]
    [InlineData("rotated30-axis-full", "optical-path-difference")]
    public void CapturedPupilAxesValidityAndPhysicalValuesMatchNativeData(string capture, string key)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "manifest.json")));
        var fixture = manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(f => f.GetProperty("id").GetString() == capture + "/" + key);
        var snapshotPath = Path.Combine(Root, capture, "snapshot.json");
        var nativePath = Path.Combine(Root, capture, key + "-native.json");
        var settingsPath = Path.Combine(Root, capture, key + "-captured-settings.json");
        Assert.Equal(fixture.GetProperty("snapshotSha256").GetString(), Hash(snapshotPath));
        Assert.Equal(fixture.GetProperty("nativeSha256").GetString(), Hash(nativePath));
        Assert.Equal(fixture.GetProperty("capturedSettingsSha256").GetString(), Hash(settingsPath));
        var request = fixture.GetProperty("request");
        var flag = bool.Parse(request.GetProperty("workbenchSettings").GetProperty("VignettedPupil").GetString()!);
        using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath));
        Assert.Equal(flag, settings.RootElement.GetProperty("properties").GetProperty("VignettedPupil").GetBoolean());
        Assert.Equal(request.GetProperty("field").GetInt32(), settings.RootElement.GetProperty("selectors").GetProperty("Field").GetInt32());
        Assert.Equal(request.GetProperty("wavelength").GetInt32(), settings.RootElement.GetProperty("selectors").GetProperty("Wavelength").GetInt32());
        var data = Generate(Load(snapshotPath), request, key, flag);
        using var native = JsonDocument.Parse(File.ReadAllText(nativePath));
        Assert.Empty(native.RootElement.GetProperty("messages").EnumerateArray());
        var reference = native.RootElement.GetProperty("dataSeries");
        Assert.Equal(2, data.PlotPanes!.Count); Assert.Equal(2, reference.GetArrayLength());
        var tolerance = fixture.GetProperty("tolerances").GetProperty(key == "ray-fan" ? "ImageHeight" : "WavefrontError").GetProperty("nrmse").GetDouble();
        for (var pane = 0; pane < 2; pane++)
        {
            var series = Assert.Single(data.PlotPanes[pane].Series);
            Assert.Equal(AnalysisAxisQuantity.PupilCoordinate, series.XQuantity);
            Assert.Equal(AnalysisAxisUnit.Dimensionless, series.XUnit);
            var x = reference[pane].GetProperty("x"); var y = reference[pane].GetProperty("y");
            var column = Assert.Single(Enumerable.Range(0, y[0].GetArrayLength()),
                col => y.EnumerateArray().Any(row => row[col].ValueKind == JsonValueKind.Number));
            Assert.Equal(x.GetArrayLength(), series.Points.Count);
            var errors = new List<double>(); var values = new List<double>();
            for (var index = 0; index < series.Points.Count; index++)
            {
                var actual = series.Points[index];
                Assert.InRange(Math.Abs(actual.X - x[index].GetDouble()), 0, 1e-14);
                var expected = y[index][column];
                Assert.Equal(expected.ValueKind == JsonValueKind.Number, double.IsFinite(actual.Y));
                if (expected.ValueKind != JsonValueKind.Number) continue;
                var value = expected.GetDouble();
                errors.Add(actual.Y * (key == "ray-fan" ? 1000 : 1) - value); values.Add(value);
            }
            Assert.True(errors.Count >= 3);
            Assert.InRange(Math.Sqrt(errors.Select(e => e * e).Average()) / values.Select(Math.Abs).Max(), 0, tolerance);
        }
    }

    [Theory]
    [InlineData("original-edge", "ray-fan")]
    [InlineData("original-edge", "optical-path-difference")]
    [InlineData("rotated30-axis-compressed", "ray-fan")]
    [InlineData("rotated30-axis-compressed", "optical-path-difference")]
    public void PupilDisplayTogglePreservesEveryRayValueAndInvalidSample(string capture, string key)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "manifest.json")));
        var request = manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(f => f.GetProperty("id").GetString() == capture + "/" + key).GetProperty("request");
        var optic = Load(Path.Combine(Root, capture, "snapshot.json"));
        var a = Generate(optic, request, key, true); var b = Generate(optic, request, key, false);
        for (var pane = 0; pane < 2; pane++)
        {
            var actual = Assert.Single(a.PlotPanes![pane].Series).Points;
            var expected = Assert.Single(b.PlotPanes![pane].Series).Points;
            Assert.Equal(actual.Select(p => p.Y), expected.Select(p => p.Y));
            Assert.Equal(-1, expected[0].X); Assert.Equal(1, expected[^1].X);
            Assert.NotEqual(actual[0].X, expected[0].X);
        }
    }

    private static AnalysisData Generate(Optic optic, JsonElement r, string key, bool vignetted) => key == "ray-fan"
        ? new RayFanAnalysis(optic, numberOfRaysEachSide: r.GetProperty("rayCount").GetInt32(), vignettedPupil: vignetted,
            checkApertures: true, wavelengthNumber: r.GetProperty("wavelength").GetInt32(), fieldNumber: r.GetProperty("field").GetInt32(), zemaxCompatible: true).GenerateData()
        : new OpticalPathDifferenceAnalysis(optic, numberOfRaysEachSide: r.GetProperty("rayCount").GetInt32(), vignettedPupil: vignetted,
            checkApertures: true, wavelengthNumber: r.GetProperty("wavelength").GetInt32(), fieldNumber: r.GetProperty("field").GetInt32()).GenerateData();
    private static Optic Load(string path) => Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(File.ReadAllText(path), Options)!);
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
