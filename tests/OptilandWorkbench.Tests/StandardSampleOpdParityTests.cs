using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Tests;

public sealed class StandardSampleOpdParityTests
{
    private static readonly string FixtureRoot = Path.Combine(
        AppContext.BaseDirectory, "Validation", "Zemax", "StandardSampleOpd");
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    [Theory]
    [InlineData("cooke-40-degree-field", "primary")]
    [InlineData("cooke-40-degree-field", "edge-shortwave")]
    [InlineData("double-gauss-28-degree-field", "primary")]
    [InlineData("double-gauss-28-degree-field", "edge-shortwave")]
    [InlineData("relay-lens", "primary")]
    [InlineData("relay-lens", "edge-shortwave")]
    public void CapturedSettingsMatchBothNativeFans(string lens, string setting)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureRoot, "manifest.json")));
        var fixture = manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(row =>
            row.GetProperty("lens").GetString() == lens && row.GetProperty("setting").GetString() == setting);
        var snapshotPath = Path.Combine(FixtureRoot, lens, "snapshot.json");
        var nativePath = Path.Combine(FixtureRoot, lens, setting + "-native.json");
        Assert.Equal(fixture.GetProperty("snapshotSha256").GetString(), Hash(snapshotPath));
        Assert.Equal(fixture.GetProperty("nativeSha256").GetString(), Hash(nativePath));
        var optic = Load(snapshotPath);
        var request = fixture.GetProperty("request");
        Assert.Equal(request.GetProperty("useRayAiming").GetBoolean(), optic.RayAimingEnabled);
        var settings = request.GetProperty("workbenchSettings");
        var data = new OpticalPathDifferenceAnalysis(optic,
            numberOfRaysEachSide: request.GetProperty("rayCount").GetInt32(),
            vignettedPupil: bool.Parse(settings.GetProperty("VignettedPupil").GetString()!),
            checkApertures: bool.Parse(settings.GetProperty("CheckApertures").GetString()!),
            wavelengthNumber: request.GetProperty("wavelength").GetInt32(),
            fieldNumber: request.GetProperty("field").GetInt32(),
            surfaceNumber: request.GetProperty("surface").GetInt32()).GenerateData();
        Assert.Equal(optic.RayAimingEnabled, data.Values["UseRayAiming"]);
        using var native = JsonDocument.Parse(File.ReadAllText(nativePath));
        Assert.Empty(native.RootElement.GetProperty("messages").EnumerateArray());
        var reference = native.RootElement.GetProperty("dataSeries");
        Assert.Equal(reference.GetArrayLength(), data.PlotPanes!.Count);
        for (var pane = 0; pane < reference.GetArrayLength(); pane++)
        {
            var points = Assert.Single(data.PlotPanes[pane].Series).Points;
            var x = reference[pane].GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var y = reference[pane].GetProperty("y").EnumerateArray().Select(v => v[0].GetDouble()).ToArray();
            Assert.Equal(x.Length, points.Count);
            var error = points.Select((point, index) =>
            {
                Assert.InRange(Math.Abs(point.X - x[index]), 0, 1e-14);
                Assert.True(double.IsFinite(point.Y));
                return point.Y - y[index];
            }).ToArray();
            var tolerance = fixture.GetProperty("tolerances").GetProperty("WavefrontError");
            var nrmse = Math.Sqrt(error.Select(v => v * v).Average()) / y.Select(Math.Abs).Max();
            Assert.InRange(nrmse, 0, tolerance.GetProperty("nrmse").GetDouble());
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1e-9, false)]
    [InlineData(1e-9, true)]
    [InlineData(250, false)]
    [InlineData(250, true)]
    public void EquivalentFiniteAngleAndObjectHeightFieldsHaveTheSamePhase(double distance, bool aiming)
    {
        var angle = Load(Path.Combine(FixtureRoot, "relay-lens", "snapshot.json"));
        angle.SurfaceGroup.Items[0].Thickness = distance;
        angle.Aperture.Value = .1;
        angle.RayAimingEnabled = aiming;
        angle.Fields.Clear();
        angle.Fields.Add(new FieldPoint { X = .01, Y = -.02 });
        var field = SpotAnalysisEngine.DefinedFields(angle).Single();
        var wave = angle.Wavelengths[0];
        var chief = angle.SequentialRayTracer.RayGenerator.GenerateGeneric(
            field.Hx, field.Hy, 0, 0, wave.Micrometers, aimAtStop: aiming).Rays.Single();
        var height = Optic.FromSnapshot(angle.ToSnapshot());
        height.FieldDefinition = FieldDefinitionKind.ObjectHeight;
        height.Fields.Clear();
        height.Fields.Add(new FieldPoint { X = chief.Origin.X, Y = chief.Origin.Y });
        var heightField = SpotAnalysisEngine.DefinedFields(height).Single();
        var samples = new[] { (X: -.2, Y: 0.0), (X: 0.0, Y: 0.0), (X: 0.0, Y: .2) };
        var a = WavefrontEngine.GenerateChiefRaySamples(angle, field, wave, samples, aimAtStop: aiming);
        var b = WavefrontEngine.GenerateChiefRaySamples(height, heightField, wave, samples, aimAtStop: aiming);
        AssertPhase(a.Samples, b.Samples);
        foreach (var strategy in new[] { ReferenceSphereStrategy.CentroidSphere, ReferenceSphereStrategy.BestFitSphere })
        {
            AssertPhase(
                ReferenceSphereWavefrontEngine.Generate(angle, field, wave, 2, strategy).Samples,
                ReferenceSphereWavefrontEngine.Generate(height, heightField, wave, 2, strategy).Samples);
        }
    }

    private static void AssertPhase(IReadOnlyList<WavefrontSample> actual, IReadOnlyList<WavefrontSample> expected)
    {
        Assert.Equal(expected.Count, actual.Count);
        var compared = 0;
        foreach (var (a, b) in actual.Zip(expected))
        {
            Assert.Equal(b.Intensity > 0, a.Intensity > 0);
            if (a.Intensity <= 0) continue;
            Assert.True(double.IsFinite(a.OpdWaves) && double.IsFinite(b.OpdWaves));
            Assert.InRange(Math.Abs(a.OpdWaves - b.OpdWaves), 0, 1e-7);
            compared++;
        }
        Assert.True(compared >= 3);
    }

    private static Optic Load(string path) => Optic.FromSnapshot(
        JsonSerializer.Deserialize<OpticSnapshot>(File.ReadAllText(path), Options)!);
    private static string Hash(string path) => Convert.ToHexString(
        SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
