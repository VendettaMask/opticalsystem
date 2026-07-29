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
    public void ShowDiffractionLimitAddsZemaxReferenceSeriesToAllRmsScans()
    {
        var optic = Optic.CreateCookeTriplet();
        var fNumber = Math.Abs(optic.Paraxial.EstimateFNumber());
        var wavelengthData = new RmsVsWavelengthAnalysis(
            optic,
            waveDensity: 5,
            numRings: 2,
            fieldNumber: 1,
            data: "spot",
            showDiffractionLimit: true).GenerateData();
        var wavelengthLimit = Assert.Single(
            wavelengthData.PlotSeries,
            series => series.Name == "Diffraction Limit");

        Assert.Equal(AnalysisLineStyle.Dashed, wavelengthLimit.LineStyle);
        Assert.Equal(5, wavelengthLimit.Points.Count);
        Assert.All(wavelengthLimit.Points, point => Assert.Equal(
            1.22 * fNumber * point.X * 1e-3,
            point.Y,
            12));
        Assert.Equal("mm", wavelengthData.Values["DiffractionLimitUnit"]);

        var focusData = new RmsVsFocusAnalysis(
            optic,
            focusDensity: 5,
            minimumFocus: -0.1,
            maximumFocus: 0.1,
            numRings: 2,
            wavelengthNumber: 1,
            data: "wavefront",
            showDiffractionLimit: true).GenerateData();
        var focusLimit = Assert.Single(
            focusData.PlotSeries,
            series => series.Name == "Diffraction Limit");

        Assert.Equal(AnalysisLineStyle.Dashed, focusLimit.LineStyle);
        Assert.All(focusLimit.Points, point => Assert.Equal(0.072, point.Y, 12));
        Assert.Equal("waves", focusData.Values["DiffractionLimitUnit"]);

        var fieldData = new RmsVsFieldAnalysis(
            optic,
            numRings: 2,
            data: "wavefront",
            wavelengthNumber: 1,
            showDiffractionLimit: true).GenerateData();
        var fieldLimit = Assert.Single(
            fieldData.PlotSeries,
            series => series.Name == "Diffraction Limit");

        Assert.Equal(AnalysisLineStyle.Dashed, fieldLimit.LineStyle);
        Assert.All(fieldLimit.Points, point => Assert.Equal(0.072, point.Y, 12));
    }

    [Fact]
    public void HiddenDiffractionLimitDoesNotAddReferenceSeries()
    {
        var data = new RmsVsWavelengthAnalysis(
            Optic.CreateCookeTriplet(),
            waveDensity: 3,
            numRings: 1,
            showDiffractionLimit: false).GenerateData();

        Assert.DoesNotContain(data.PlotSeries, series => series.Name == "Diffraction Limit");
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
