using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Tests;

public sealed class RmsAnalysisTests
{
    [Fact]
    public void RmsVsWavelengthProducesFiniteSeriesForEveryDefinedField()
    {
        var optic = Optic.CreateCookeTriplet();
        var data = new RmsVsWavelengthAnalysis(
            optic,
            waveDensity: 5,
            numRings: 2).GenerateData();

        Assert.Equal(optic.Fields.Count, data.PlotSeries.Count);
        Assert.All(data.PlotSeries, series =>
        {
            Assert.Equal(5, series.Points.Count);
            Assert.All(series.Points, point =>
            {
                Assert.True(double.IsFinite(point.X));
                Assert.True(double.IsFinite(point.Y));
                Assert.True(point.Y >= 0);
            });
        });
    }

    [Fact]
    public void RmsVsFocusProducesFiniteSeriesForEveryDefinedField()
    {
        var optic = Optic.CreateCookeTriplet();
        var data = new RmsVsFocusAnalysis(
            optic,
            focusDensity: 5,
            minimumFocus: -0.1,
            maximumFocus: 0.1,
            numRings: 2).GenerateData();

        Assert.Equal(optic.Fields.Count, data.PlotSeries.Count);
        Assert.All(data.PlotSeries, series =>
        {
            Assert.Equal(5, series.Points.Count);
            Assert.Equal(-0.1, series.Points[0].X, 6);
            Assert.Equal(0.1, series.Points[^1].X, 6);
            Assert.All(series.Points, point =>
            {
                Assert.True(double.IsFinite(point.X));
                Assert.True(double.IsFinite(point.Y));
                Assert.True(point.Y >= 0);
            });
        });
    }

    [Fact]
    public void RmsFieldMapProducesFiniteTwoDimensionalGrid()
    {
        var optic = Optic.CreateCookeTriplet();
        var data = new RmsFieldMapAnalysis(
            optic,
            xFieldSamples: 5,
            yFieldSamples: 5,
            numRings: 2).GenerateData();

        var series = Assert.Single(data.PlotSeries);
        Assert.Equal(AnalysisSeriesKind.Heatmap, series.Kind);
        Assert.Equal(25, series.Points.Count);
        Assert.All(series.Points, point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
            Assert.NotNull(point.Value);
            Assert.True(double.IsFinite(point.Value!.Value));
            Assert.True(point.Value.Value >= 0);
        });
    }
}