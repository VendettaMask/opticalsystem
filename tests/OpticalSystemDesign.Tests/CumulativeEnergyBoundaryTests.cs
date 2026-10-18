using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class CumulativeEnergyBoundaryTests
{
    private static readonly Lazy<IReadOnlyDictionary<string, byte[]>> Matrix = new(() => Load("multi-lens-dee-zemax-2026-r1.zip"));
    private static readonly Lazy<IReadOnlyDictionary<string, byte[]>> Directional = new(() => Load("fft-cumulative-directional-zemax-2026-r1.zip"));
    private static readonly Lazy<IReadOnlyDictionary<string, byte[]>> Boundary = new(() => Load("fft-cumulative-boundary-perturb-zemax-2026-r1.zip"));

    public static IEnumerable<object[]> RemainingNativeCurves()
    {
        var existing = MultiLensEncircledEnergyTests.NativeCurves()
            .Select(row => (Id: (string)row[0], Series: (int)row[1])).ToHashSet();
        foreach (var (name, files) in new[] { ("Matrix", Matrix.Value), ("Directional", Directional.Value), ("Boundary", Boundary.Value) })
        {
            using var manifest = JsonDocument.Parse(files["manifest.json"]);
            foreach (var capture in manifest.RootElement.GetProperty("cases").EnumerateArray())
            {
                if (capture.GetProperty("group").GetString() == "Slit") continue;
                var id = capture.GetProperty("id").GetString()!;
                foreach (var series in new[] { 0, 1 })
                    if (name != "Matrix" || !existing.Contains((id, series))) yield return [name, id, series];
            }
        }
    }

    [Theory]
    [MemberData(nameof(RemainingNativeCurves))]
    public void RemainingMatrixAndDirectionalControlsPassTheOriginalEnergyTolerance(string fixture, string id, int series)
    {
        var files = fixture switch { "Matrix" => Matrix.Value, "Boundary" => Boundary.Value, _ => Directional.Value };
        using var request = JsonDocument.Parse(files[id + "/request.json"]);
        using var native = JsonDocument.Parse(files[id + "/data.json"]);
        using var model = JsonDocument.Parse(files[id + "/model.json"]);
        var r = request.RootElement;
        using var source = new StreamReader(new MemoryStream(files[id + "/source.zmx"]));
        var optic = OpticalFormatCatalog.Import(source.ReadToEnd(), ".zmx");
        optic.RayAimingEnabled = model.RootElement.GetProperty("rayAiming").GetProperty("RayAiming").GetString() != "Off";
        var n = r.GetProperty("pupilSampling").GetInt32();
        var data = new DiffractionEncircledEnergyAnalysis(optic, n, 2 * n, 401,
            r.GetProperty("wavelength").GetInt32(), r.GetProperty("field").GetInt32(), "encircled", "centroid",
            r.GetProperty("zemaxSettings").GetProperty("RadiusMaximum").GetDouble()).GenerateData();
        var expected = native.RootElement.GetProperty("dataSeries")[series].GetProperty("y")
            .EnumerateArray().Select(value => value[0].GetDouble()).ToArray();
        var actual = data.PlotSeries[series].Points.Select(point => point.Y).ToArray();
        Assert.Equal(401, expected.Length);
        Assert.Equal(expected.Length, actual.Length);
        var nrmse = Math.Sqrt(actual.Zip(expected, (a, b) => (a - b) * (a - b)).Average()) / (expected.Max() - expected.Min());
        Assert.True(nrmse < .003, $"{fixture}/{id}/{series}: unchanged energy NRMSE limit .003, measured {nrmse:R}");
    }

    [Theory]
    [InlineData(.5, 1)]
    [InlineData(.04386672586112075, 3)]
    public void MarginalRmsRoundsAboveTheExactHalfwayMean(double lower, int steps)
    {
        var upper = lower;
        for (var i = 0; i < steps; i++) upper = Math.BitIncrement(upper);
        var expected = lower;
        for (var i = 0; i < (steps + 1) / 2; i++) expected = Math.BitIncrement(expected);
        // For two positive unequal values, RMS exceeds their arithmetic mean.
        // These odd-ULP separations put that mean exactly at a rounding midpoint.
        Assert.Equal(expected, DiffractionEngine.CumulativeEnergyRootMeanSquare([lower, upper, lower, upper]));
    }

    [Theory]
    [InlineData(.5)]
    [InlineData(.04279780931824652)]
    [InlineData(.18225167832212755)]
    public void IdenticalMarginalAperturesRetainTheirExactScale(double value)
        => Assert.Equal(value, DiffractionEngine.CumulativeEnergyRootMeanSquare([value, value, value, value]));

    private static IReadOnlyDictionary<string, byte[]> Load(string name)
    {
        using var zip = ZipFile.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
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
