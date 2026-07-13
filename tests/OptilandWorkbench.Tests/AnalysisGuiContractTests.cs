using System.Text.Json;
using OptilandWorkbench.App.Connectors;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.Tests;

public sealed class AnalysisGuiContractTests
{
    [Fact]
    public void ConnectorExposesAndAppliesAnalysisParameters()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());

        var descriptors = connector.GetAnalysisParameters("点扩散函数 PSF");
        Assert.Contains(descriptors, item => item.Key == "NumRays" && item.Kind == AnalysisParameterKind.Integer);
        Assert.Contains(descriptors, item => item.Key == "GridSize" && item.DefaultValue == "0");
        Assert.Equal("PSF", connector.CanonicalAnalysisKey("点扩散函数 PSF"));

        var settings = connector.MergeAnalysisSettings("点扩散函数 PSF", new Dictionary<string, string>
        {
            ["NumRays"] = "16",
            ["GridSize"] = "32",
            ["Ignored"] = "not persisted"
        });

        Assert.Equal("16", settings["NumRays"]);
        Assert.Equal("32", settings["GridSize"]);
        Assert.DoesNotContain("Ignored", settings.Keys);

        var view = connector.BuildAnalysisView("点扩散函数 PSF", settings);

        Assert.Equal("点扩散函数 PSF", view.Name);
        AssertRow(view, "方法", "FFT");
        AssertRow(view, "瞳面采样数", "16");
        AssertRow(view, "网格尺寸", "32");
        var series = Assert.Single(view.SeriesList);
        Assert.Equal(AnalysisSeriesKind.Heatmap, series.Kind);
        Assert.NotEmpty(series.Points);
    }

    [Fact]
    public void AppSettingsRoundTripsAnalysisSettings()
    {
        var settings = new AppSettings();
        settings.AnalysisSettings["PSF"] = new Dictionary<string, string>
        {
            ["NumRays"] = "16",
            ["GridSize"] = "32"
        };

        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal("16", restored.AnalysisSettings["PSF"]["NumRays"]);
        Assert.Equal("32", restored.AnalysisSettings["PSF"]["GridSize"]);
    }

    private static void AssertRow(AnalysisView view, string metric, string value)
    {
        var row = Assert.Single(view.Rows, item => item.Metric == metric);
        Assert.Equal(value, row.Value);
    }
}
