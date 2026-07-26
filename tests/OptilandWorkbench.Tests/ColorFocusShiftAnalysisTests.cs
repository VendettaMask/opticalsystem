using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.Tests;

public sealed class ColorFocusShiftAnalysisTests
{
    [Fact]
    public void GeneratesContinuousWavelengthVersusFocusShiftCurve()
    {
        var optic = Optic.CreateCookeTriplet();

        var data = new ColorFocusShiftAnalysis(optic).GenerateData();

        var series = Assert.Single(data.PlotSeries);
        Assert.Equal(101, series.Points.Count);
        Assert.Equal("焦移：µm", series.XAxisLabel);
        Assert.Equal("波长：µm", series.YAxisLabel);
        Assert.All(series.Points, point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
        });
        Assert.Equal(
            optic.Wavelengths.Min(wavelength => wavelength.Micrometers),
            series.Points.Min(point => point.Y),
            12);
        Assert.Equal(
            optic.Wavelengths.Max(wavelength => wavelength.Micrometers),
            series.Points.Max(point => point.Y),
            12);
        Assert.True(data.PlotOptions?.XMinimum < 0);
        Assert.True(data.PlotOptions?.XMaximum > 0);
    }

    [Fact]
    public void HonorsMaximumShiftAndPupilZoneSettings()
    {
        var data = new ColorFocusShiftAnalysis(
            Optic.CreateCookeTriplet(),
            maximumShiftMicrometers: 8,
            pupilZone: 0.5).GenerateData();

        Assert.Equal(-8, data.PlotOptions?.XMinimum);
        Assert.Equal(8, data.PlotOptions?.XMaximum);
        Assert.Equal(0.5, Assert.IsType<double>(data.Values["PupilZone"]), 12);
    }
}
