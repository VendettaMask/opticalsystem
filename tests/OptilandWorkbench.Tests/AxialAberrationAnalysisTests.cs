using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.Tests;

public sealed class AxialAberrationAnalysisTests
{
    [Fact]
    public void GeneratesOneLongitudinalAberrationCurvePerWavelength()
    {
        var optic = Optic.CreateCookeTriplet();

        var data = new AxialAberrationAnalysis(optic).GenerateData();

        Assert.Equal("Axial Aberration", data.Name);
        Assert.Equal(optic.Wavelengths.Count, data.PlotSeries.Count);
        Assert.All(data.PlotSeries, series =>
        {
            Assert.Equal(101, series.Points.Count);
            Assert.Equal("毫米", series.XAxisLabel);
            Assert.Equal("归一化光瞳坐标", series.YAxisLabel);
            Assert.Equal(0, series.Points.Min(point => point.Y), 12);
            Assert.Equal(1, series.Points.Max(point => point.Y), 12);
        });
        Assert.True(Assert.IsType<double>(data.Values["PupilRadiusMillimeters"]) > 0);
        Assert.True(data.PlotOptions?.XMinimum < 0);
        Assert.True(data.PlotOptions?.XMaximum > 0);
    }

    [Fact]
    public void SettingsSelectWavelengthScaleAndDashStyle()
    {
        var data = new AxialAberrationAnalysis(
            Optic.CreateCookeTriplet(),
            graphScaleMillimeters: 0.02,
            wavelengthNumber: 2,
            useDashes: true).GenerateData();

        var series = Assert.Single(data.PlotSeries);
        Assert.Equal(-0.02, data.PlotOptions?.XMinimum);
        Assert.Equal(0.02, data.PlotOptions?.XMaximum);
        Assert.Equal(AnalysisLineStyle.Solid, series.LineStyle);
        Assert.Equal(1, Assert.IsType<int>(data.Values["WavelengthCount"]));
    }
}
