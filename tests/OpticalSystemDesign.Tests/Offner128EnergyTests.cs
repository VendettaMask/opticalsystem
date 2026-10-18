using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class Offner128EnergyTests
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
    public void SignedPupilReferenceAndCommonPitchCloseTheOriginalFoldedConjugate(string id, int series)
    {
        var files = Fixture.Value;
        using var request = JsonDocument.Parse(files[id + "/request.json"]);
        using var native = JsonDocument.Parse(files[id + "/data.json"]);
        using var model = JsonDocument.Parse(files[id + "/model.json"]);
        using var source = new StreamReader(new MemoryStream(files[id + "/source.zmx"]));
        var optic = OpticalFormatCatalog.Import(source.ReadToEnd(), ".zmx");
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
        var limit = id == "decenter-minus1p5-128" ? .003 : 1e-8;
        Assert.True(nrmse < limit, $"{id}/{series}: limit {limit:R}, measured {nrmse:R}");
    }

    private static IReadOnlyDictionary<string, byte[]> Load()
    {
        using var zip = ZipFile.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures",
            "fft-cumulative-offner128-zemax-2026-r1.zip"));
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
