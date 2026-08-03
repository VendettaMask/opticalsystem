using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Tests;

public sealed class IncidentAngleVsImageHeightParityTests
{
    [Fact]
    public void AimedPupilRaysMatchTheZemaxReferenceValues()
    {
        var optic = LoadFixture("zemax-123456.ZMX");
        var data = new IncidentAngleVsImageHeightAnalysis(
            optic,
            fieldDensity: 20,
            wavelengthNumber: 2).GenerateData();

        Assert.Equal(true, data.Values["AimAtStop"]);
        Assert.Equal(3, data.PlotSeries.Count);
        AssertSeriesPoint(data.PlotSeries[0], 0, 0, 10.500941);
        AssertSeriesPoint(data.PlotSeries[0], 10, 2.25, 13.068963);
        AssertSeriesPoint(data.PlotSeries[0], 20, 4.5, 15.423702);
        AssertSeriesPoint(data.PlotSeries[1], 20, 4.5, 4.725466);
        AssertSeriesPoint(data.PlotSeries[2], 0, 0, -10.500941);
        AssertSeriesPoint(data.PlotSeries[2], 20, 4.5, -5.213345);
    }

    [Fact]
    public void NegativeFieldHighNaLensKeepsSignedHeightAndAllThreePupilRays()
    {
        var optic = LoadFixture("zemax-ms-l7-high-na.ZMX");
        var data = new IncidentAngleVsImageHeightAnalysis(
            optic,
            fieldDensity: 20,
            wavelengthNumber: 2).GenerateData();

        Assert.Equal(3, data.PlotSeries.Count);
        Assert.All(data.PlotSeries, series =>
        {
            Assert.Equal(21, series.Points.Count);
            Assert.All(series.Points, point =>
            {
                Assert.True(double.IsFinite(point.X));
                Assert.True(double.IsFinite(point.Y));
            });
            Assert.Equal(0, series.Points[0].X, 9);
            Assert.True(series.Points[^1].X < series.Points[0].X);
        });

        AssertSeriesPoint(data.PlotSeries[0], 0, 0, 23.941540);
        AssertSeriesPoint(data.PlotSeries[0], 20, -1.491645, 16.973821);
        AssertSeriesPoint(data.PlotSeries[1], 20, -1.491645, -7.597689);
        AssertSeriesPoint(data.PlotSeries[2], 0, 0, -23.941540);
        AssertSeriesPoint(data.PlotSeries[2], 20, -1.491645, -31.221499);
        Assert.Equal(-1.491645, data.PlotOptions!.XMinimum!.Value, 6);
        Assert.Equal(0, data.PlotOptions.XMaximum!.Value, 9);
    }

    private static Optic LoadFixture(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        return OpticalFormatCatalog.Import(File.ReadAllText(path), ".zmx");
    }

    private static void AssertSeriesPoint(
        AnalysisSeries series,
        int pointIndex,
        double expectedX,
        double expectedY)
    {
        Assert.Equal(expectedX, series.Points[pointIndex].X, 6);
        Assert.Equal(expectedY, series.Points[pointIndex].Y, 6);
    }
}
