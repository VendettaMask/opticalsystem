using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Tests;

public sealed class FftAimingStopBranchTests
{
    private static readonly Lazy<IReadOnlyDictionary<string, byte[]>> Fixture = new(Load);

    public static IEnumerable<object[]> Captures()
    {
        foreach (var name in new[] { "discovery", "fresh" })
        {
            using var manifest = JsonDocument.Parse(Fixture.Value[name + "/manifest.json"]);
            foreach (var capture in manifest.RootElement.GetProperty("cases").EnumerateArray())
                yield return [name + "/" + capture.GetProperty("id").GetString()!];
        }
    }

    [Theory]
    [MemberData(nameof(Captures))]
    public void CachedMappingPreservesNativeStoppingBranches(string id)
    {
        var files = Fixture.Value;
        using var request = JsonDocument.Parse(files[id + "/request.json"]);
        using var native = JsonDocument.Parse(files[id + "/ray-audit.json"]);
        using var reader = new StreamReader(new MemoryStream(files[id + "/source.zmx"]));
        var optic = OpticalFormatCatalog.Import(reader.ReadToEnd(), ".zmx");
        optic.RayAimingEnabled = true;
        var root = native.RootElement;
        var aiming = root.GetProperty("rayAiming");
        Assert.Equal("Paraxial", aiming.GetProperty("RayAiming").GetString());
        Assert.True(aiming.GetProperty("UseRayAimingCache").GetBoolean());
        Assert.False(aiming.GetProperty("UseEnhancedRayAiming").GetBoolean());
        var wave = request.RootElement.GetProperty("wavelength").GetInt32();
        Assert.Equal(wave, root.GetProperty("wavelength").GetInt32());
        var inputs = root.GetProperty("inputs");
        var pupils = inputs.EnumerateArray().Select(input => new PupilSample(
            input.GetProperty("px").GetDouble(), input.GetProperty("py").GetDouble(), 1)).ToArray();
        Assert.True(pupils.Length >= 2924);
        var stop = optic.SurfaceGroup.Items.ToList().FindIndex(surface => surface.IsStop);
        var expected = root.GetProperty("surfaces").EnumerateArray()
            .Single(surface => surface.GetProperty("surface").GetInt32() == stop).GetProperty("rays");
        Assert.Equal(pupils.Length, expected.GetArrayLength());
        Assert.True(optic.SequentialRayTracer.RayGenerator.TryGenerateCachedFftPupil(0, 0,
            optic.Wavelengths[wave - 1].Micrometers, pupils, out var bundle));
        for (var i = 0; i < pupils.Length; i++)
        {
            Assert.Equal(0, expected[i].GetProperty("error").GetInt32());
            var sample = optic.SequentialRayTracer.TraceToSurface(bundle!.Rays[i], stop)!;
            var actual = optic.SurfaceGroup.Items[stop].CoordinateSystem.ToLocalPoint(sample.Position);
            var difference = double.Hypot(actual.X - expected[i].GetProperty("x").GetDouble(),
                actual.Y - expected[i].GetProperty("y").GetDouble());
            // Retain the existing finite-FFT stop-map regression limit.
            Assert.True(difference < 1e-9,
                $"{id}, pupil {i} ({pupils[i].X:R}, {pupils[i].Y:R}): {difference:R} mm");
        }
    }

    private static IReadOnlyDictionary<string, byte[]> Load()
    {
        var result = new Dictionary<string, byte[]>();
        foreach (var (name, file) in new[]
        {
            ("discovery", "fft-cumulative-stop-branch-zemax-2026-r1.zip"),
            ("fresh", "fft-cumulative-stop-branch-holdout-zemax-2026-r1.zip")
        })
        {
            using var zip = ZipFile.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", file));
            foreach (var entry in zip.Entries)
            {
                using var stream = entry.Open();
                using var data = new MemoryStream();
                stream.CopyTo(data);
                result.Add(name + "/" + entry.FullName, data.ToArray());
            }
            using var manifest = JsonDocument.Parse(result[name + "/manifest.json"]);
            foreach (var entry in manifest.RootElement.GetProperty("entries").EnumerateArray())
                Assert.Equal(entry.GetProperty("sha256").GetString(), Convert.ToHexStringLower(
                    SHA256.HashData(result[name + "/" + entry.GetProperty("path").GetString()!])));
        }
        return result;
    }
}
