using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class AfocalFftEnergyTests
{
    private static JsonElement Fixture() => JsonFiles.Read<JsonElement>(Path.Combine(AppContext.BaseDirectory,
        "Fixtures/afocal-fft-energy/curves.json"));

    public static IEnumerable<object[]> CapturedCases() => Fixture().GetProperty("cases").EnumerateArray()
        .Select(r => new object[] { r.GetProperty("lens").GetString()!, r.GetProperty("configuration").GetInt32(), r.GetProperty("key").GetString()! });

    [Theory]
    [MemberData(nameof(CapturedCases))]
    public async Task CapturedScalarFftAndEnergyMeetOriginalNativeGates(string lens, int configuration, string key)
    {
        Assert.True(Fixture().GetProperty("nativeReferenceBytesUnchanged").GetBoolean());
        var row = Fixture().GetProperty("cases").EnumerateArray().Single(r =>
            r.GetProperty("lens").GetString() == lens && r.GetProperty("configuration").GetInt32() == configuration && r.GetProperty("key").GetString() == key);
        var request = row.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        Assert.False(request.Polarization);
        var optic = (await WorkbenchExecutor.Import(Path.Combine(AppContext.BaseDirectory,
            "Fixtures/afocal-repair/sources", lens))).Configurations[configuration - 1];
        var data = new WorkbenchRuntime(optic).BuildAnalysisData(key, request.WorkbenchSettings);
        var actual = ExtendedResultNormalizer.Workbench(data, request);
        var expected = row.GetProperty("curves").EnumerateArray().ToArray();
        Assert.Equal(expected.Length, actual.Series.Count);
        var scale = PhysicalNormalization.UnitScale(request.NativeAfocalAngleUnit!, "Milliradian");
        var quantity = key == "Diffraction Encircled Energy" ? "EnergyFraction" : "Irradiance";
        var policy = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json")).Analyses[key].Quantities[quantity];
        for (var i = 0; i < expected.Length; i++)
        {
            var curve = actual.Series[i];
            var native = new Series1DResult(curve.Id, "frozen native", expected[i].GetProperty("x").EnumerateArray().Select(v => v.GetDouble() * scale).ToArray(),
                expected[i].GetProperty("y").Deserialize<double[]>()!, curve.XAxis, curve.YAxis);
            var matched = ComparisonMetrics.Align(curve, native, []);
            var metric = ComparisonMetrics.Calculate(curve.Id, "Dimensionless", matched, policy);
            Assert.Equal(Conclusion.Pass, metric.Conclusion);
        }
    }
}
