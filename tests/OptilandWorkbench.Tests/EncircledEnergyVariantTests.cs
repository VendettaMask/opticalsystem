using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.Tests;

public sealed class EncircledEnergyVariantTests
{
    [Fact]
    public void DiffractionEncircledEnergyIntegratesThePsfToUnity()
    {
        var data = new DiffractionEncircledEnergyAnalysis(
            Optic.CreateCookeTriplet(),
            pupilSampling: 8,
            imageSampling: 16,
            numPoints: 17,
            wavelengthNumber: 1,
            fieldNumber: 1).GenerateData();

        Assert.Equal(2, data.PlotSeries.Count);
        var diffractionLimit = data.PlotSeries[0];
        Assert.Equal("\u884d\u5c04\u6781\u9650", diffractionLimit.Name);
        AssertNormalizedCumulativeCurve(diffractionLimit, 17, requireUnityAtEnd: false);
        var field = data.PlotSeries[1];
        AssertNormalizedCumulativeCurve(field, 17, requireUnityAtEnd: false);
        Assert.InRange(field.Points[^1].Y, 0.99, 1);
        Assert.Equal("FFT PSF pixel-area integration", data.Values["Method"]);
        Assert.Equal(0, data.PlotOptions!.YMinimum);
        Assert.Equal(1, data.PlotOptions.YMaximum);
        Assert.True(data.PlotOptions.ShowLegend);
        Assert.True(data.PlotOptions.LegendBelow);
    }

    [Fact]
    public void DiffractionEncircledEnergyDefaultsToAllFields()
    {
        var optic = Optic.CreateCookeTriplet();
        var data = new DiffractionEncircledEnergyAnalysis(
            optic,
            pupilSampling: 8,
            imageSampling: 16,
            numPoints: 17,
            wavelengthNumber: 1).GenerateData();

        Assert.Equal(optic.Fields.Count + 1, data.PlotSeries.Count);
        Assert.Equal(optic.Fields.Count, data.Values["FieldCount"]);
        Assert.All(data.PlotSeries.Skip(1), series =>
        {
            AssertNormalizedCumulativeCurve(series, 17, requireUnityAtEnd: false);
        });
    }

    [Fact]
    public void GeometricLineEdgeSpreadProducesLineAndMonotonicEdgeResponses()
    {
        var data = new GeometricLineEdgeSpreadAnalysis(
            Optic.CreateCookeTriplet(),
            pupilSampling: 8,
            numPoints: 33,
            wavelengthNumber: 1).GenerateData();

        Assert.Equal(2, data.PlotSeries.Count);
        Assert.All(data.PlotSeries, series =>
        {
            Assert.Equal(33, series.Points.Count);
            Assert.All(series.Points, point =>
            {
                Assert.True(double.IsFinite(point.X));
                Assert.True(double.IsFinite(point.Y));
                Assert.InRange(point.Y, 0, 1);
            });
        });
        var edge = data.PlotSeries.Single(series => series.Name == "Edge Spread");
        AssertMonotonic(edge.Points);
        var line = data.PlotSeries.Single(series => series.Name == "Line Spread");
        // The last displayed coordinate is the last bin's center, not its right boundary.
        Assert.Equal(1 - (0.5 * line.Points[^1].Y / line.Points.Sum(p => p.Y)), edge.Points[^1].Y, 10);
    }

    [Fact]
    public void SymmetricLineSpreadPlacesHalfTheEnergyAtTheCentralBin()
    {
        var data = new GeometricLineEdgeSpreadAnalysis(
            Optic.CreateCookeTriplet(), pupilSampling: 32, numPoints: 101,
            wavelengthNumber: 1, fieldNumber: 1).GenerateData();
        var line = data.PlotSeries.Single(series => series.Name == "Line Spread");
        var edge = data.PlotSeries.Single(series => series.Name == "Edge Spread");
        Assert.True(line.Points[50].Y > 0);
        Assert.Equal(0.5, edge.Points[50].Y, 10);
        var binWidth = Convert.ToDouble(data.Values["HistogramBinWidthMicrometers"]);
        var coordinateStep = Convert.ToDouble(data.Values["DisplayCoordinateStepMicrometers"]);
        Assert.Equal(100.0 / 101, binWidth / coordinateStep, 12);
    }

    [Fact]
    public void CapturedEnergyPlotConventionIsExplicitAndRetainsGeneralPointCount()
    {
        var optic = Optic.CreateCookeTriplet();
        var general = new EncircledEnergyAnalysis(optic, numRays: 8, distribution: "uniform", numPoints: 17);
        Assert.False(general.ZemaxCompatibleOutput);
        Assert.All(general.GenerateData().PlotSeries, s => Assert.Equal(17, s.Points.Count));
        var captured = new EncircledEnergyAnalysis(optic, numRays: 8, distribution: "uniform", numPoints: 17)
        { ZemaxCompatibleOutput = true }.GenerateData();
        Assert.All(captured.PlotSeries, s =>
        {
            AssertNormalizedCumulativeCurve(s, 396, requireUnityAtEnd: false);
            Assert.Equal(0, s.Points[0].X);
        });
    }

    [Fact]
    public void ExtendedSourceEncircledEnergyCombinesFiniteFieldSamples()
    {
        var data = new ExtendedSourceEncircledEnergyAnalysis(
            Optic.CreateCookeTriplet(),
            fieldSize: 0.1,
            sourceSampling: 3,
            numRays: 300,
            numPoints: 17,
            wavelengthNumber: 1).GenerateData();

        var series = Assert.Single(data.PlotSeries);
        AssertNormalizedCumulativeCurve(series, 17);
        Assert.Equal(3, data.Values["SourceSampling"]);
        Assert.True((int)data.Values["RayCount"] > 0);
    }

    private static void AssertNormalizedCumulativeCurve(
        AnalysisSeries series,
        int pointCount,
        bool requireUnityAtEnd = true)
    {
        Assert.Equal(pointCount, series.Points.Count);
        Assert.All(series.Points, point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
            Assert.InRange(point.Y, 0, 1);
        });
        AssertMonotonic(series.Points);
        if (requireUnityAtEnd)
        {
            Assert.Equal(1, series.Points[^1].Y, 10);
        }
    }

    private static void AssertMonotonic(IReadOnlyList<AnalysisPoint> points)
    {
        for (var index = 1; index < points.Count; index++)
        {
            Assert.True(points[index].Y >= points[index - 1].Y - 1e-12);
        }
    }
}
