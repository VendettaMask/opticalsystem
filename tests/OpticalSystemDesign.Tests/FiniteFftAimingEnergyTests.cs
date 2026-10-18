using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Tests;

public sealed class FiniteFftAimingEnergyTests
{
    private static readonly Lazy<IReadOnlyDictionary<string, byte[]>> Fixture = new(Load);

    public static IEnumerable<object[]> NativeCurves()
    {
        using var manifest = JsonDocument.Parse(Fixture.Value["manifest.json"]);
        foreach (var capture in manifest.RootElement.GetProperty("cases").EnumerateArray())
            foreach (var series in new[] { 0, 1 })
                yield return [capture.GetProperty("id").GetString()!, series];
    }

    [Theory]
    [MemberData(nameof(NativeCurves))]
    public void CachedPupilMatchesFrozenEnergyAcrossConjugateApertureAndSampling(string id, int series)
    {
        var files = Fixture.Value;
        using var request = JsonDocument.Parse(files[id + "/request.json"]);
        using var native = JsonDocument.Parse(files[id + "/data.json"]);
        using var model = JsonDocument.Parse(files[id + "/model.json"]);
        var optic = OpticalFormatCatalog.Import(Source(id + "/source.zmx"), ".zmx");
        optic.RayAimingEnabled = model.RootElement.GetProperty("rayAiming").GetProperty("RayAiming").GetString() != "Off";
        var r = request.RootElement;
        var sampling = r.GetProperty("pupilSampling").GetInt32();
        var result = new DiffractionEncircledEnergyAnalysis(optic, sampling, 2 * sampling, 401,
            r.GetProperty("wavelength").GetInt32(), r.GetProperty("field").GetInt32(), "encircled", "centroid",
            r.GetProperty("zemaxSettings").GetProperty("RadiusMaximum").GetDouble()).GenerateData();
        var expected = native.RootElement.GetProperty("dataSeries")[series].GetProperty("y")
            .EnumerateArray().Select(value => value[0].GetDouble()).ToArray();
        var actual = result.PlotSeries[series].Points.Select(point => point.Y).ToArray();
        Assert.Equal(401, expected.Length);
        Assert.Equal(expected.Length, actual.Length);
        var nrmse = Math.Sqrt(actual.Zip(expected, (a, b) => (a - b) * (a - b)).Average())
            / (expected.Max() - expected.Min());
        Assert.True(nrmse < 1e-8, $"{id}/{series}: unchanged limit 1e-8, measured {nrmse:R}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CachedLaunchMapMatchesNativeFirstQuadrantStopIntercepts(int wave)
    {
        var files = Fixture.Value;
        var optic = OpticalFormatCatalog.Import(Source("original64/source.zmx"), ".zmx");
        optic.RayAimingEnabled = true;
        using var audit = JsonDocument.Parse(files[$"rays/w{wave}/ray-audit.json"]);
        var inputs = audit.RootElement.GetProperty("inputs");
        var stop = optic.SurfaceGroup.Items.ToList().FindIndex(surface => surface.IsStop);
        var native = audit.RootElement.GetProperty("surfaces").EnumerateArray()
            .Single(surface => surface.GetProperty("surface").GetInt32() == stop).GetProperty("rays");
        var indices = Enumerable.Range(0, inputs.GetArrayLength()).Where(i =>
            inputs[i].GetProperty("px").GetDouble() >= 0 && inputs[i].GetProperty("py").GetDouble() >= 0).ToArray();
        var pupils = indices.Select(i => new PupilSample(inputs[i].GetProperty("px").GetDouble(),
            inputs[i].GetProperty("py").GetDouble(), 1)).ToArray();
        Assert.True(optic.SequentialRayTracer.RayGenerator.TryGenerateCachedFftPupil(0, 0,
            optic.Wavelengths[wave - 1].Micrometers, pupils, out var bundle));
        for (var i = 0; i < indices.Length; i++)
        {
            var hit = optic.SequentialRayTracer.TraceToSurface(bundle!.Rays[i], stop)!;
            var point = optic.SurfaceGroup.Items[stop].CoordinateSystem.ToLocalPoint(hit.Position);
            var expected = native[indices[i]];
            var error = double.Hypot(point.X - expected.GetProperty("x").GetDouble(),
                point.Y - expected.GetProperty("y").GetDouble());
            Assert.True(error < 1e-9, $"wave {wave}, input {indices[i]}: native stop-map error {error:R} mm");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CumulativeSamplingPreservesOrdinaryPreciseStopAiming(int wave)
    {
        var optic = OpticalFormatCatalog.Import(Source("original64/source.zmx"), ".zmx");
        optic.RayAimingEnabled = true;
        var wavelength = optic.Wavelengths[wave - 1];
        var generator = optic.SequentialRayTracer.RayGenerator;
        var before = generator.GenerateGeneric(0, 0, .5, .375, wavelength.Micrometers, true).Rays[0];
        _ = new DiffractionEncircledEnergyAnalysis(optic, 64, 128, 401, wave, 1,
            "encircled", "centroid", 5).GenerateData();
        var after = generator.GenerateGeneric(0, 0, .5, .375, wavelength.Micrometers, true).Rays[0];
        Assert.Equal(before, after);
        var stop = optic.SurfaceGroup.Items.ToList().FindIndex(surface => surface.IsStop);
        var primary = optic.Wavelengths.First(w => w.IsPrimary);
        var radius = optic.Paraxial.TraceNormalizedPupil(0, new[] { 1d }, primary.Micrometers).Heights[stop][0];
        var sample = optic.SequentialRayTracer.TraceToSurface(after, stop)!;
        var point = optic.SurfaceGroup.Items[stop].CoordinateSystem.ToLocalPoint(sample.Position);
        Assert.True(double.Hypot(point.X - radius * .5, point.Y - radius * .375) < 1e-11);
    }

    private static string Source(string path)
    {
        using var reader = new StreamReader(new MemoryStream(Fixture.Value[path]));
        return reader.ReadToEnd();
    }

    private static IReadOnlyDictionary<string, byte[]> Load()
    {
        using var zip = ZipFile.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures",
            "fft-cumulative-finite-aiming-zemax-2026-r1.zip"));
        var files = zip.Entries.ToDictionary(entry => entry.FullName, entry =>
        {
            using var stream = entry.Open();
            using var data = new MemoryStream();
            stream.CopyTo(data);
            return data.ToArray();
        });
        using var manifest = JsonDocument.Parse(files["manifest.json"]);
        foreach (var entry in manifest.RootElement.GetProperty("entries").EnumerateArray())
            Assert.Equal(entry.GetProperty("sha256").GetString(), Convert.ToHexStringLower(
                SHA256.HashData(files[entry.GetProperty("path").GetString()!])));
        return files;
    }
}
