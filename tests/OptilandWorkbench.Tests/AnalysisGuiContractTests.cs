using System.Text.Json;
using OptilandWorkbench.App.Connectors;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Phase;

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

    [Fact]
    public void ConnectorExposesAndAppliesApodizationSettings()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());

        Assert.Equal(
            new[] { "无", "均匀", "高斯", "余弦平方", "Hann", "多项式", "超高斯", "Tukey" },
            connector.ApodizationKinds);

        connector.SetApodization("超高斯", 0.7, 1.0);
        var superGaussian = Assert.IsType<SuperGaussianApodization>(connector.CurrentOptic.Apodization);
        Assert.Equal(0.7, superGaussian.Width, precision: 12);
        Assert.Equal(2.0, superGaussian.Exponent, precision: 12);

        connector.SetApodization("Tukey", 0.9, 1.5);
        var tukey = Assert.IsType<TukeyApodization>(connector.CurrentOptic.Apodization);
        Assert.Equal(0.9, tukey.Radius, precision: 12);
        Assert.Equal(1.0, tukey.Alpha, precision: 12);

        connector.SetApodization("无", 1, 1);
        Assert.Null(connector.CurrentOptic.Apodization);
    }

    [Fact]
    public void ConnectorCreatesPhaseInteractionWithSerializableProfile()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());
        var surface = connector.CurrentOptic.SurfaceGroup.Items[1];

        connector.ApplySurfaceComponents(surface, "平面", "Air", "无镀膜", "相位", "无");

        var phase = Assert.IsType<PhaseInteractionModel>(surface.InteractionModel);
        Assert.IsType<ConstantPhaseProfile>(phase.Profile);
        var restored = Optic.FromSnapshot(connector.CurrentOptic.ToSnapshot());
        var restoredPhase = Assert.IsType<PhaseInteractionModel>(restored.SurfaceGroup.Items[1].InteractionModel);
        Assert.IsType<ConstantPhaseProfile>(restoredPhase.Profile);
    }

    private static void AssertRow(AnalysisView view, string metric, string value)
    {
        var row = Assert.Single(view.Rows, item => item.Metric == metric);
        Assert.Equal(value, row.Value);
    }
}
