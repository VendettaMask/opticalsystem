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
            wavelengthNumber: 1).GenerateData();

        var series = Assert.Single(data.PlotSeries);
        AssertNormalizedCumulativeCurve(series, 17);
        Assert.Equal("FFT PSF integration", data.Values["Method"]);
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
        Assert.Equal(1, edge.Points[^1].Y, 10);
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

    private static void AssertNormalizedCumulativeCurve(AnalysisSeries series, int pointCount)
    {
        Assert.Equal(pointCount, series.Points.Count);
        Assert.All(series.Points, point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
            Assert.InRange(point.Y, 0, 1);
        });
        AssertMonotonic(series.Points);
        Assert.Equal(1, series.Points[^1].Y, 10);
    }

    private static void AssertMonotonic(IReadOnlyList<AnalysisPoint> points)
    {
        for (var index = 1; index < points.Count; index++)
        {
            Assert.True(points[index].Y >= points[index - 1].Y - 1e-12);
        }
    }
}
