using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class ExpandedSamplingEnergyTests
{
    private static readonly Lazy<IReadOnlyDictionary<string, byte[]>> Fixture = new(Load);
    private static readonly ConcurrentDictionary<string, Lazy<double[][]>> Results = new();

    public static IEnumerable<object[]> NativeCurves()
    {
        using var manifest = JsonDocument.Parse(Fixture.Value["manifest.json"]);
        foreach (var capture in manifest.RootElement.GetProperty("cases").EnumerateArray())
            foreach (var series in new[] { 0, 1 })
                yield return [capture.GetProperty("id").GetString()!, series];
    }

    [Theory]
    [MemberData(nameof(NativeCurves))]
    public void ExpandedPhysicalGridPreservesFrozenEnergyAcrossLensesAndPerturbations(string id, int series)
    {
        using var native = JsonDocument.Parse(Fixture.Value[id + "/data.json"]);
        var expected = native.RootElement.GetProperty("dataSeries")[series].GetProperty("y")
            .EnumerateArray().Select(value => value[0].GetDouble()).ToArray();
        var actual = Results.GetOrAdd(id, key => new(() => Calculate(key))).Value[series];
        Assert.Equal(401, expected.Length);
        Assert.Equal(expected.Length, actual.Length);
        var nrmse = Math.Sqrt(actual.Zip(expected, (a, b) => (a - b) * (a - b)).Average())
            / (expected.Max() - expected.Min());
        Assert.True(nrmse < 1e-8, $"{id}/{series}: unchanged strict limit 1e-8, measured {nrmse:R}");
    }

    [Theory]
    [InlineData("apo-epd-minus", 5)]
    [InlineData("apo-epd-plus", 5)]
    [InlineData("doublet-epd-minus", 3)]
    [InlineData("doublet-epd-plus-poly", 3)]
    public void ImportedApertureChangesReevaluateTheCapturedCurvatureAndThicknessSolves(string id, int index)
    {
        var files = Fixture.Value;
        using var model = JsonDocument.Parse(files[$"controls/{id}/model.json"]);
        using var reader = new StreamReader(new MemoryStream(files[$"controls/{id}/source.zmx"]));
        var optic = OpticalFormatCatalog.Import(reader.ReadToEnd(), ".zmx");
        var expected = model.RootElement.GetProperty("surfaces")[index];
        Assert.True(Math.Abs(optic.SurfaceGroup.Items[index].Radius - expected.GetProperty("Radius").GetDouble()) < 1e-10);
        Assert.True(Math.Abs(optic.SurfaceGroup.Items[index].Thickness - expected.GetProperty("Thickness").GetDouble()) < 1e-10);
    }

    private static double[][] Calculate(string id)
    {
        var files = Fixture.Value;
        using var request = JsonDocument.Parse(files[id + "/request.json"]);
        using var model = JsonDocument.Parse(files[id + "/model.json"]);
        using var settings = JsonDocument.Parse(files[id + "/captured-settings.json"]);
        using var reader = new StreamReader(new MemoryStream(files[id + "/source.zmx"]));
        var optic = OpticalFormatCatalog.Import(reader.ReadToEnd(), ".zmx");
        optic.RayAimingEnabled = model.RootElement.GetProperty("rayAiming").GetProperty("RayAiming").GetString() != "Off";
        var r = request.RootElement;
        var sampling = r.GetProperty("pupilSampling").GetInt32();
        var wavelength = r.GetProperty("wavelength").GetInt32();
        Assert.Contains(sampling, new[] { 256, 512 });
        Assert.Equal($"S_{sampling}x{sampling}", settings.RootElement.GetProperty("properties").GetProperty("SampleSize").GetString());
        Assert.Equal(wavelength, settings.RootElement.GetProperty("selectors").GetProperty("Wavelength").GetInt32());
        var result = new DiffractionEncircledEnergyAnalysis(optic, sampling, 2 * sampling, 401,
            wavelength, r.GetProperty("field").GetInt32(), "encircled", "centroid",
            r.GetProperty("zemaxSettings").GetProperty("RadiusMaximum").GetDouble()).GenerateData();
        Assert.Equal(2, result.PlotSeries.Count);
        return result.PlotSeries.Select(line => line.Points.Select(point => point.Y).ToArray()).ToArray();
    }

    private static IReadOnlyDictionary<string, byte[]> Load()
    {
        using var zip = ZipFile.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures",
            "fft-cumulative-expanded-zemax-2026-r1.zip"));
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
