using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Tests;

public sealed class FullFieldAberrationAnalysisTests
{
    [Fact]
    public void GeneratesEllipseOfValueScaledFieldIcons()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.FieldDefinition = FieldDefinitionKind.RealImageHeight;

        var data = new FullFieldAberrationAnalysis(
            optic,
            xFieldWidth: 4.5,
            yFieldWidth: 4.5,
            maximumTerm: 9,
            xFieldSamples: 5,
            yFieldSamples: 5,
            pupilSampling: 8).GenerateData();

        var series = Assert.Single(data.PlotSeries);
        Assert.Equal(AnalysisSeriesKind.Scatter, series.Kind);
        Assert.Equal(13, series.Points.Count);
        Assert.All(series.Points, point =>
        {
            Assert.True(point.Value.HasValue);
            Assert.True(double.IsFinite(point.Value!.Value));
            Assert.True(
                (point.X * point.X / (4.5 * 4.5))
                + (point.Y * point.Y / (4.5 * 4.5))
                <= 1 + 1e-12);
        });
        Assert.Equal("X视场，单位：毫米", series.XAxisLabel);
        Assert.Equal("Y视场，单位：毫米", series.YAxisLabel);
        Assert.Equal("离焦", Assert.IsType<string>(data.Values["Aberration"]));
    }

    [Fact]
    public void RectangleAndSignedDisplayRetainTheCompleteGrid()
    {
        var data = new FullFieldAberrationAnalysis(
            Optic.CreateCookeTriplet(),
            fieldShape: "矩形",
            xFieldWidth: 1,
            yFieldWidth: 1,
            maximumTerm: 9,
            xFieldSamples: 3,
            yFieldSamples: 3,
            pupilSampling: 8,
            displayMode: "带符号").GenerateData();

        Assert.Equal(9, data.PlotSeries[0].Points.Count);
        Assert.Equal("矩形", Assert.IsType<string>(data.Values["FieldShape"]));
        Assert.Equal("带符号", Assert.IsType<string>(data.Values["DisplayMode"]));
    }
}
