using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class AfocalReferenceFocusTests
{
    private static JsonElement Fixture(string file) => JsonFiles.Read<JsonElement>(
        Path.Combine(AppContext.BaseDirectory, "Fixtures/afocal-reference-focus", file));

    public static IEnumerable<object[]> Configurations() => Fixture("through-focus.json")
        .GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("lens").GetString()!, row.GetProperty("configuration").GetInt32() });

    public static IEnumerable<object[]> CentroidConfigurations() => Fixture("angular-centroid-energy.json")
        .GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("lens").GetString()!, row.GetProperty("configuration").GetInt32() });

    [Theory]
    [MemberData(nameof(CentroidConfigurations))]
    public async Task AngularCentroidEnergyMatchesIndependentNativeRays(string lens, int configuration)
    {
        var fixture = Fixture("angular-centroid-energy.json");
        Assert.False(fixture.GetProperty("builtInPlotReplaced").GetBoolean());
        Assert.Equal("NativeExplicitRayAngularCentroidV1", fixture.GetProperty("contract").GetString());
        var row = fixture.GetProperty("cases").EnumerateArray().Single(r => r.GetProperty("lens").GetString() == lens && r.GetProperty("configuration").GetInt32() == configuration);
        var request = row.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        var optic = (await WorkbenchExecutor.Import(Path.Combine(AppContext.BaseDirectory, "Fixtures/afocal-repair/sources", lens))).Configurations[configuration - 1];
        // This fixture is an independent angular mean/CDF, not the built-in
        // plot. Keep its interval grid and declare its physical reference.
        var physicalSettings = new Dictionary<string, string>(request.WorkbenchSettings)
        {
            ["UseNativeAfocalReferenceRay"] = "False"
        };
        var data = new WorkbenchRuntime(optic).BuildAnalysisData("Encircled Energy", physicalSettings);
        Assert.Equal("PhysicalCoordinates", data.Values["ReferenceConvention"]);
        var policy = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json"))
            .Analyses["Encircled Energy"].Quantities["EnergyFraction"];
        var field = 0;
        foreach (var native in row.GetProperty("fields").EnumerateArray())
        {
            Assert.Equal(797, native.GetProperty("rayCount").GetInt32());
            var knots = native.GetProperty("knots").EnumerateArray().ToArray();
            var actual = data.PlotSeries[field++].Points;
            var x = knots.Select(k => k.GetProperty("x").GetDouble()).ToArray();
            for (var i = 0; i < x.Length; i++) Assert.Equal(x[i], actual[i * 4].X, 10);
            var metric = ComparisonMetrics.Calculate("centroid-energy", "Dimensionless",
                new MatchedValues(x, null, Enumerable.Range(0, x.Length).Select(i => actual[i * 4].Y).ToArray(), knots.Select(k => k.GetProperty("y").GetDouble()).ToArray(), 1), policy);
            Assert.Equal(Conclusion.Pass, metric.Conclusion);
        }
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public async Task HuygensThroughFocusMatchesNativeWithoutChangingFocalConvention(string lens, int configuration)
    {
        var fixture = Fixture("through-focus.json");
        Assert.Equal(260127, fixture.GetProperty("zemaxBuild").GetInt32());
        var row = fixture.GetProperty("cases").EnumerateArray().Single(r => r.GetProperty("lens").GetString() == lens && r.GetProperty("configuration").GetInt32() == configuration);
        var request = row.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        var optic = (await WorkbenchExecutor.Import(Path.Combine(AppContext.BaseDirectory, "Fixtures/afocal-repair/sources", lens))).Configurations[configuration - 1];
        var data = new WorkbenchRuntime(optic).BuildAnalysisData("Huygens Through Focus MTF", request.WorkbenchSettings);
        var policy = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json"))
            .Analyses["Huygens Through Focus MTF"].Quantities["Modulation"];
        Assert.Equal(2, data.PlotSeries.Count);
        for (var component = 0; component < 2; component++)
        {
            var curve = data.PlotSeries[component];
            Assert.Equal(AnalysisAxisUnit.Diopter, curve.XUnit);
            var expected = row.GetProperty("y").EnumerateArray().Select(values => values[component].GetDouble()).ToArray();
            var x = row.GetProperty("x").Deserialize<double[]>()!;
            Assert.Equal(x.Length, curve.Points.Count);
            for (var i = 0; i < x.Length; i++) Assert.Equal(x[i], curve.Points[i].X, 12);
            var metric = ComparisonMetrics.Calculate("through-focus", "Dimensionless",
                new MatchedValues(x, null, curve.Points.Select(p => p.Y).ToArray(), expected, 1), policy);
            Assert.Equal(Conclusion.Pass, metric.Conclusion);
        }
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public async Task GeometricEnergyChiefReferenceMatchesIndependentNativeCapture(string lens, int configuration)
    {
        var row = Fixture("chief-energy.json").GetProperty("cases").EnumerateArray().Single(r => r.GetProperty("lens").GetString() == lens && r.GetProperty("configuration").GetInt32() == configuration);
        var request = row.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        var optic = (await WorkbenchExecutor.Import(Path.Combine(AppContext.BaseDirectory, "Fixtures/afocal-repair/sources", lens))).Configurations[configuration - 1];
        var data = new WorkbenchRuntime(optic).BuildAnalysisData("Encircled Energy", request.WorkbenchSettings);
        var scale = PhysicalNormalization.UnitScale(request.NativeAfocalAngleUnit!, "Milliradian");
        var expectedCurves = row.GetProperty("curves").EnumerateArray().ToArray();
        Assert.Equal(expectedCurves.Length, data.PlotSeries.Count);
        var policy = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json"))
            .Analyses["Encircled Energy"].Quantities["EnergyFraction"];
        for (var field = 0; field < expectedCurves.Length; field++)
        {
            var expected = expectedCurves[field];
            var actual = data.PlotSeries[field];
            var x = expected.GetProperty("x").EnumerateArray().Select(v => v.GetDouble() * scale).ToArray();
            Assert.Equal(x.Length, actual.Points.Count);
            for (var i = 0; i < x.Length; i++) Assert.Equal(x[i], actual.Points[i].X, 10);
            var metric = ComparisonMetrics.Calculate("energy", "Dimensionless",
                new MatchedValues(x, null, actual.Points.Select(p => p.Y).ToArray(), expected.GetProperty("y").EnumerateArray().Select(v => v[0].GetDouble()).ToArray(), 1), policy);
            Assert.Equal(Conclusion.Pass, metric.Conclusion);
        }
    }
}
