using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Tests;

public sealed class LateralColorAnalysisTests
{
    [Fact]
    public void GeneratesShortestToLongestCurveAndAiryBoundaries()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.FieldDefinition = FieldDefinitionKind.RealImageHeight;

        var data = new LateralColorAnalysis(optic).GenerateData();

        Assert.Equal("Lateral Color", data.Name);
        Assert.Equal(3, data.PlotSeries.Count);
        var color = data.PlotSeries[0];
        Assert.Equal("最短的-最长的", color.Name);
        Assert.Equal(101, color.Points.Count);
        Assert.Equal("µm", color.XAxisLabel);
        Assert.Equal("视场：实际像高 单位：毫米", color.YAxisLabel);
        Assert.Equal(AnalysisLineStyle.Dotted, data.PlotSeries[1].LineStyle);
        Assert.Equal(AnalysisLineStyle.Dotted, data.PlotSeries[2].LineStyle);
        Assert.Equal("艾里斑", data.PlotSeries[1].Name);
        Assert.True(data.PlotOptions?.XMinimum < 0);
        Assert.True(data.PlotOptions?.XMaximum > 0);
    }

    [Fact]
    public void SettingsControlScaleWavelengthCurvesAndAiryDisk()
    {
        var data = new LateralColorAnalysis(
            Optic.CreateCookeTriplet(),
            graphScaleMicrometers: 4,
            allWavelengths: true,
            useRealRays: false,
            showAiryDisk: false).GenerateData();

        Assert.Equal(-4, data.PlotOptions?.XMinimum);
        Assert.Equal(4, data.PlotOptions?.XMaximum);
        Assert.DoesNotContain(data.PlotSeries, series => series.Name == "艾里斑");
        Assert.All(data.PlotSeries, series => Assert.NotEqual("最短的-最长的", series.Name));
        Assert.False(Assert.IsType<bool>(data.Values["UseRealRays"]));
    }
}
