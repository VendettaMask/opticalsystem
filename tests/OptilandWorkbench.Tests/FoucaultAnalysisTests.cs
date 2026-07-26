using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.Tests;

public sealed class FoucaultAnalysisTests
{
    [Fact]
    public void ProducesNormalizedKnifeEdgeResponseAcrossPupil()
    {
        var result = new FoucaultAnalysis(
            Optic.CreateCookeTriplet(),
            sampling: 24,
            wavelengthNumber: 1,
            fieldNumber: 1).GenerateData();

        Assert.Equal("Foucault Analysis", result.Name);
        var series = Assert.Single(result.PlotSeries);
        Assert.Equal(AnalysisSeriesKind.Heatmap, series.Kind);
        Assert.NotEmpty(series.Points);
        Assert.All(series.Points, point =>
        {
            Assert.InRange(point.Value!.Value, 0, 1);
            Assert.True((point.X * point.X) + (point.Y * point.Y) <= 1 + 1e-12);
        });
        Assert.Equal("24 x 24", result.Values["Sampling"]);
        Assert.Equal("水平线上", result.Values["KnifeEdge"]);
    }

    [Fact]
    public void KnifeDirectionAndPositionChangeResponse()
    {
        var optic = Optic.CreateCookeTriplet();
        var upper = new FoucaultAnalysis(
            optic,
            sampling: 16,
            knifeEdge: "水平线上",
            positionMicrometers: 0).GenerateData();
        var lower = new FoucaultAnalysis(
            optic,
            sampling: 16,
            knifeEdge: "水平线下",
            positionMicrometers: 20).GenerateData();

        var upperValues = upper.PlotSeries.Single().Points.Select(point => point.Value).ToArray();
        var lowerValues = lower.PlotSeries.Single().Points.Select(point => point.Value).ToArray();
        Assert.NotEqual(upperValues, lowerValues);
    }
}
