using System.Text.Json;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.ZemaxComparison.Metrics;
using OptilandWorkbench.ZemaxComparison.Normalization;

namespace OptilandWorkbench.ZemaxComparison.Tests;

public sealed class CapturedEnergyRepairTests
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "Fixtures", "energy-repair");

    [Theory]
    [InlineData("Encircled Energy")]
    [InlineData("Geometric Line Edge Spread")]
    [InlineData("Contrast Loss Map")]
    public void IndependentPrimaryLensMatchesCapturedEnergyAndPhase(string key)
        => Verify("primary.ZMX", "primary-" + JsonFiles.Slug(key) + "-c1", key);

    [Theory]
    [InlineData(5)]
    [InlineData(20)]
    public void ExtendedSourceMatchesIndependentCaptureRanges(int radius)
        => Verify("ms-l7.ZMX", "ms-l7-extended-" + radius, "Extended Source Encircled Energy");

    private static void Verify(string lens, string directory, string key)
    {
        var native = Path.Combine(Root, directory);
        using var captured = JsonDocument.Parse(File.ReadAllText(Path.Combine(native, "captured-settings.json")));
        var request = captured.RootElement.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        var settings = new Dictionary<string, string>(request.WorkbenchSettings);
        settings["ZemaxCompatibleOutput"] = "True";
        if (request.SourceImagePath is not null)
        {
            settings["SourceFile"] = Path.Combine(Root, "source-image.IMA");
            Assert.Equal(request.SourceImageSha256, JsonFiles.Hash(File.ReadAllBytes(settings["SourceFile"])));
            Assert.Equal(Convert.ToDouble(settings["MaximumDistanceMicrometers"], System.Globalization.CultureInfo.InvariantCulture),
                captured.RootElement.GetProperty("properties").GetProperty("MaximumDistance").GetDouble());
        }
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(Root, lens)), ".zmx");
        var data = new WorkbenchRuntime(optic).BuildAnalysisData(key, settings);
        data = JsonSerializer.Deserialize<AnalysisData>(JsonSerializer.Serialize(data, JsonFiles.Options), JsonFiles.Options)!;
        var actual = ExtendedResultNormalizer.Workbench(data, request);
        var expected = ExtendedResultNormalizer.Zemax(Path.Combine(native, "data.json"), request);
        Assert.True(expected.Series.Count + expected.Grids.Count > 0);
        Assert.Equal(expected.Series.Count, actual.Series.Count);
        Assert.Equal(expected.Grids.Count, actual.Grids.Count);
        foreach (var curve in expected.Series)
        {
            var metrics = ComparisonMetrics.Calculate(curve.Id, curve.YAxis.Unit,
                ComparisonMetrics.Align(actual.Series.Single(s => s.Id == curve.Id), curve, []),
                AnalysisComparisonRegistry.Get(key).DefaultTolerances[curve.YAxis.Quantity]);
            Assert.True(metrics.Conclusion == Conclusion.Pass,
                $"{directory}/{curve.Id}: {metrics.Conclusion}; NRMSE {metrics.Nrmse:R}, max {metrics.MaxAbsolute:R}");
        }
        foreach (var grid in expected.Grids)
        {
            var metrics = ComparisonMetrics.Calculate(grid.Id, grid.ValueAxis.Unit,
                ComparisonMetrics.Align(actual.Grids.Single(g => g.Id == grid.Id), grid, []),
                AnalysisComparisonRegistry.Get(key).DefaultTolerances[grid.ValueAxis.Quantity]);
            Assert.True(metrics.Conclusion == Conclusion.Pass,
                $"{directory}/{grid.Id}: {metrics.Conclusion}; NRMSE {metrics.Nrmse:R}, max {metrics.MaxAbsolute:R}");
        }
    }

    [Fact]
    public void NativeExtendedCumulativeKnotsVerifyTheBinOffsetAcrossThreeRanges()
    {
        double[] Values(string path)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.GetProperty("dataSeries")[0].GetProperty("y")
                .EnumerateArray().Select(row => row[0].GetDouble()).ToArray();
        }
        var five = Values(Path.Combine(Root, "ms-l7-extended-5", "data.json"));
        var twenty = Values(Path.Combine(Root, "ms-l7-extended-20", "data.json"));
        var ten = Values(Path.Combine(AppContext.BaseDirectory, "Fixtures", "analysis-expansion", "extended-source-encircled-energy-c1", "data.json"));
        Assert.Equal(396, five.Length);
        Assert.Equal(396, ten.Length);
        Assert.Equal(396, twenty.Length);
        // For display knot i, all three captures enclose radius R*(i-1)/99.
        // Exact equality over their common knot range rules out a fitted scale or ray shift.
        for (var i = 1; i <= 25; i++)
        {
            Assert.Equal(twenty[4 * i], ten[4 * ((2 * i) - 1)], 14);
            Assert.Equal(twenty[4 * i], five[4 * ((4 * i) - 3)], 14);
        }
    }

    [Fact]
    public void StructuredPhaseGridsRemainAvailableWithoutLeakingClrTypesIntoTheSummary()
    {
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(Root, "primary.ZMX")), ".zmx");
        var runtime = new WorkbenchRuntime(optic);
        var settings = new Dictionary<string, string> { ["ShowOPD"] = "True", ["Sampling"] = "9" };
        var data = runtime.BuildAnalysisData("Contrast Loss Map", settings);
        Assert.Equal(2, Assert.IsType<AnalysisSeries[]>(data.Values["UnshiftedPupilPhaseSeries"]).Length);
        var view = runtime.BuildAnalysisView("Contrast Loss Map", settings);
        Assert.DoesNotContain(view.Rows, row => row.Value.Contains("OptilandWorkbench.Core", StringComparison.Ordinal));
        Assert.DoesNotContain("OptilandWorkbench.Core", view.ReportText);
    }

    [Fact]
    public void EnergyRepairCaptureManifestProtectsAllRawEvidence()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "manifest.json")));
        foreach (var item in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = Path.GetFullPath(Path.Combine(Root, item.GetProperty("path").GetString()!));
            Assert.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, path);
            Assert.Equal(item.GetProperty("sha256").GetString(), JsonFiles.Hash(File.ReadAllBytes(path)));
        }
    }
}
