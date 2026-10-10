using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Tests;

public sealed class WavefrontAimingRepairTests
{
    private static readonly string FixtureRoot = Path.Combine(
        AppContext.BaseDirectory, "Validation", "Zemax", "StandardSampleOpd");
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void MapSamplingFollowsSystemAimingNotExitPupilDisplay(
        bool aiming, bool useExitPupilShape, bool hexapolar)
    {
        var optic = Optic.CreateCookeTriplet();
        optic.RayAimingEnabled = aiming;
        var field = SpotAnalysisEngine.DefinedFields(optic).Last();
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var expected = hexapolar
            ? WavefrontEngine.GenerateChiefRaySamples(optic, field, wave,
                ApertureSampler.GenerateHexapolarRings(3).Select(s => (s.X, s.Y)).ToArray(),
                aimAtStop: aiming)
            : WavefrontEngine.GenerateChiefRayUniform(optic, field, wave, 16,
                aimAtStop: aiming, zemaxCentered: true);
        var valid = expected.Samples.Where(s => s.Intensity > 0).ToArray();
        Assert.NotEmpty(valid);
        var result = new WavefrontAnalysis(optic, numRings: 3, mapSize: 16,
            pupilSampling: hexapolar ? null : 16,
            useExitPupilShape: useExitPupilShape).GenerateData();

        Assert.Equal(expected.Samples.Count, result.Values["RayCount"]);
        Assert.Equal(expected.VignettedRayCount, result.Values["VignettedRayCount"]);
        Assert.Equal(expected.ReferenceOpticalPath, Convert.ToDouble(result.Values["ReferenceOpticalPathLength"]), 12);
        var mean = valid.Average(s => s.OpdWaves);
        var rms = Math.Sqrt(valid.Average(s => Math.Pow(s.OpdWaves - (hexapolar ? 0 : mean), 2)));
        Assert.Equal(mean * wave.Micrometers * 1e-3, Convert.ToDouble(result.Values["MeanOpticalPathDifference"]), 12);
        Assert.Equal(rms, Convert.ToDouble(result.Values["RmsWaves"]), 12);
        Assert.Equal(valid.Max(s => s.OpdWaves) - valid.Min(s => s.OpdWaves),
            Convert.ToDouble(result.Values["PeakToValleyWaves"]), 12);
        if (!hexapolar)
        {
            var byPupil = valid.ToDictionary(s => (s.NormalizedPupilX, s.NormalizedPupilY));
            var points = Assert.Single(result.PlotSeries).Points;
            Assert.Equal(valid.Length, points.Count);
            var minimum = valid.Min(s => s.OpdWaves);
            foreach (var point in points)
                Assert.Equal(byPupil[(point.X, point.Y)].OpdWaves - minimum, point.Value!.Value, 12);
        }
        Assert.Equal(aiming, result.Values["UseRayAiming"]);
        Assert.Equal(useExitPupilShape, result.Values["UseExitPupilShape"]);
        Assert.Equal(aiming, optic.RayAimingEnabled);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExitPupilDisplayFlagDoesNotChangePhysicalWavefront(bool aiming, bool hexapolar)
    {
        var optic = Optic.CreateCookeTriplet();
        optic.RayAimingEnabled = aiming;
        var a = new WavefrontAnalysis(optic, numRings: 3, mapSize: 16,
            pupilSampling: hexapolar ? null : 16, useExitPupilShape: false).GenerateData();
        var b = new WavefrontAnalysis(optic, numRings: 3, mapSize: 16,
            pupilSampling: hexapolar ? null : 16, useExitPupilShape: true).GenerateData();
        foreach (var key in new[] { "RayCount", "VignettedRayCount", "ReferenceOpticalPathLength",
                     "MeanOpticalPathDifference", "RmsWaves", "PeakToValleyWaves" })
            Assert.Equal(a.Values[key], b.Values[key]);
        // Keep physical data coordinates unchanged; typed plot metadata now carries
        // the display-only exit-pupil projection to every surface/contour renderer.
        Assert.Equal(Assert.Single(a.PlotSeries).Points, Assert.Single(b.PlotSeries).Points);
        Assert.Equal(1, a.PlotOptions!.SpatialDisplayScaleX);
        Assert.Equal(1, a.PlotOptions.SpatialDisplayScaleY);
        Assert.True(b.PlotOptions!.SpatialDisplayScaleX != 1 || b.PlotOptions.SpatialDisplayScaleY != 1);
    }

    public static IEnumerable<object[]> NativeMapFanCases()
    {
        foreach (var lens in new[] { "cooke-40-degree-field", "double-gauss-28-degree-field", "relay-lens" })
        foreach (var setting in new[] { "primary", "edge-shortwave" })
        foreach (var shape in new[] { false, true })
            yield return [lens, setting, shape];
    }

    [Theory]
    [MemberData(nameof(NativeMapFanCases))]
    public void ExactMapFanKnotsMatchUnchangedNativeCaptures(string lens, string setting, bool shape)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureRoot, "manifest.json")));
        var fixture = manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(row =>
            row.GetProperty("lens").GetString() == lens && row.GetProperty("setting").GetString() == setting);
        var snapshotPath = Path.Combine(FixtureRoot, lens, "snapshot.json");
        var nativePath = Path.Combine(FixtureRoot, lens, setting + "-native.json");
        Assert.Equal(fixture.GetProperty("snapshotSha256").GetString(), Hash(snapshotPath));
        Assert.Equal(fixture.GetProperty("nativeSha256").GetString(), Hash(nativePath));
        var optic = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(File.ReadAllText(snapshotPath), Options)!);
        var request = fixture.GetProperty("request");
        Assert.Equal(request.GetProperty("useRayAiming").GetBoolean(), optic.RayAimingEnabled);
        var wavelengthNumber = request.GetProperty("wavelength").GetInt32();
        var wave = optic.Wavelengths[wavelengthNumber - 1];
        var map = new WavefrontAnalysis(optic, pupilSampling: 64, wavelengthNumber: wavelengthNumber,
            fieldNumber: request.GetProperty("field").GetInt32(), useExitPupilShape: shape).GenerateData();
        var points = Assert.Single(map.PlotSeries).Points;
        // Recover the signed phase from the documented display-only minimum shift.
        // Never fit the map to the native fan or interpolate between native knots.
        var signedOffset = Convert.ToDouble(map.Values["MeanOpticalPathDifference"]) / (wave.Micrometers * 1e-3)
            - points.Average(p => p.Value!.Value);
        using var native = JsonDocument.Parse(File.ReadAllText(nativePath));
        Assert.Empty(native.RootElement.GetProperty("messages").EnumerateArray());
        var tolerance = fixture.GetProperty("tolerances").GetProperty("WavefrontError");
        var absolute = tolerance.GetProperty("absolute").GetDouble();
        var relative = tolerance.GetProperty("relative").GetDouble();
        var errors = new List<double>();
        var referencePeak = 0d;
        var axis = 0;
        foreach (var fan in native.RootElement.GetProperty("dataSeries").EnumerateArray())
        {
            var x = fan.GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var y = fan.GetProperty("y");
            for (var i = 0; i < x.Length; i++)
            {
                var pupilX = axis == 0 ? 0 : x[i];
                var pupilY = axis == 0 ? x[i] : 0;
                var shared = points.Where(p => Math.Abs(p.X - pupilX) < 1e-13 && Math.Abs(p.Y - pupilY) < 1e-13).ToArray();
                if (shared.Length == 0) continue;
                Assert.Equal(JsonValueKind.Number, y[i][0].ValueKind);
                var expected = y[i][0].GetDouble();
                var error = Assert.Single(shared).Value!.Value + signedOffset - expected;
                Assert.InRange(Math.Abs(error), 0, absolute + relative * Math.Abs(expected));
                errors.Add(error);
                referencePeak = Math.Max(referencePeak, Math.Abs(expected));
            }
            axis++;
        }
        Assert.Equal(6, errors.Count); // Five independent coordinates; the chief ray occurs in both fans.
        Assert.InRange(Math.Sqrt(errors.Average(e => e * e)) / referencePeak,
            0, tolerance.GetProperty("nrmse").GetDouble());
        Assert.Equal(optic.RayAimingEnabled, map.Values["UseRayAiming"]);
    }

    [Fact]
    public void HexapolarEngineKeepsItsUnspecifiedAimingDefault()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.RayAimingEnabled = true;
        var field = SpotAnalysisEngine.DefinedFields(optic).Last();
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var expected = WavefrontEngine.GenerateChiefRaySamples(optic, field, wave,
            ApertureSampler.GenerateHexapolarRings(3).Select(s => (s.X, s.Y)).ToArray(), aimAtStop: false);
        var actual = WavefrontEngine.GenerateChiefRay(optic, field, wave, 3);
        Assert.Equal(expected.Samples, actual.Samples);
        Assert.Equal(expected.ReferenceOpticalPath, actual.ReferenceOpticalPath);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
