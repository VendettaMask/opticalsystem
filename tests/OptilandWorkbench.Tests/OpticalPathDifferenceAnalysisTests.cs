using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.Tests;

public sealed class OpticalPathDifferenceAnalysisTests
{
    [Fact]
    public void GeneratesPairedPupilFansForEveryField()
    {
        var optic = Optic.CreateCookeTriplet();
        var result = new OpticalPathDifferenceAnalysis(
            optic,
            numberOfRaysEachSide: 6).GenerateData();

        Assert.Equal("Optical Path Difference", result.Name);
        Assert.NotNull(result.PlotPanes);
        Assert.Equal(Math.Max(1, optic.Fields.Count) * 2, result.PlotPanes.Count);
        Assert.Equal(2, result.PlotPaneColumns);
        Assert.All(result.PlotPanes, pane =>
        {
            Assert.Equal(optic.Wavelengths.Count, pane.Series.Count);
            Assert.Equal(-1, pane.PlotOptions.XMinimum);
            Assert.Equal(1, pane.PlotOptions.XMaximum);
            Assert.Equal(
                -pane.PlotOptions.YMaximum!.Value,
                pane.PlotOptions.YMinimum!.Value,
                precision: 12);
            Assert.All(pane.Series, series => Assert.Equal(13, series.Points.Count));
        });
        Assert.Equal("P_y", result.PlotPanes[0].Series[0].XAxisLabel);
        Assert.Equal("P_x", result.PlotPanes[1].Series[0].XAxisLabel);
    }

    [Fact]
    public void HonorsManualGraphScale()
    {
        var result = new OpticalPathDifferenceAnalysis(
            Optic.CreateCookeTriplet(),
            graphScaleWaves: 0.25,
            numberOfRaysEachSide: 2,
            wavelengthNumber: 1,
            fieldNumber: 1).GenerateData();

        Assert.NotNull(result.PlotPanes);
        Assert.Equal(2, result.PlotPanes.Count);
        Assert.All(result.PlotPanes, pane =>
        {
            Assert.Equal(-0.25, pane.PlotOptions.YMinimum);
            Assert.Equal(0.25, pane.PlotOptions.YMaximum);
            Assert.Single(pane.Series);
        });
    }
}
