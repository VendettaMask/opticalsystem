using System.Text.Json;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.ZemaxComparison.Metrics;
using OptilandWorkbench.ZemaxComparison.Normalization;

namespace OptilandWorkbench.ZemaxComparison.Tests;

public sealed class HuygensOffAxisDensityTests
{
    [Fact]
    public void CapturedDensityBelowNativeMinimumUsesFivePositionsThroughTheProductJsonBoundary()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "huygens-offaxis");
        var folder = Path.Combine(root, "mtf", "scan-three", "Auto");
        using var captured = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "captured-settings.json")));
        Assert.Equal(3, captured.RootElement.GetProperty("properties").GetProperty("FieldDensity").GetInt32());
        var request = captured.RootElement.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        var entry = AnalysisComparisonRegistry.Get("Huygens MTF vs Field");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(root, "source.ZMX")), ".zmx");
        var data = new WorkbenchRuntime(optic).BuildAnalysisData("Huygens MTF vs Field", AnalysisComparisonRegistry.MapWorkbench(entry, request));
        data = JsonSerializer.Deserialize<AnalysisData>(JsonSerializer.Serialize(data, JsonFiles.Options), JsonFiles.Options)!;
        Assert.Equal(5, ((JsonElement)data.Values["FieldPointCount"]).GetInt32());
        Assert.Equal(300, ((JsonElement)data.Values["PlotPointCount"]).GetInt32());
        var actual = ExtendedResultNormalizer.Workbench(data, request);
        var native = ExtendedResultNormalizer.Zemax(Path.Combine(folder, "data.json"), request);
        Assert.Equal(12, native.Series.Count);
        foreach (var curve in native.Series)
        {
            var metric = ComparisonMetrics.Calculate(curve.Id, curve.YAxis.Unit,
                ComparisonMetrics.Align(actual.Series.Single(s => s.Id == curve.Id), curve, []),
                entry.DefaultTolerances[curve.YAxis.Quantity]);
            Assert.Equal(Conclusion.Pass, metric.Conclusion);
            Assert.Equal(1, metric.Coverage);
            Assert.InRange(metric.MaxAbsolute, 0, 1e-6);
        }
    }
}
