using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

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

    [Fact]
    public void AfocalRiflescopeUsesRelativeVergenceAndStaysContinuousThroughCollimation()
    {
        var data = new ColorFocusShiftAnalysis(AfocalRiflescope()).GenerateData();
        var series = Assert.Single(data.PlotSeries);

        Assert.Equal(AnalysisAxisUnit.Diopter, series.XUnit);
        Assert.Equal(101, series.Points.Count);
        Assert.Equal("D", data.Values["FocusShiftUnit"]);
        Assert.Equal(true, data.Values["ImageSpaceAfocal"]);
        Assert.Equal("PrimaryParaxialVergence", data.Values["Reference"]);
        // The supplied reference displays 0.1585 D. These precise anchors come
        // from the prescription's independent signed output slope/height ratio.
        Near(0.158470469044, Convert.ToDouble(data.Values["MaximumFocalShiftChangeDiopters"]));
        Near(0.0881561849085, Convert.ToDouble(data.Values["PrimaryParaxialVergenceDiopters"]));
        Near(-0.0002557220849, series.Points[0].X);
        Near(-0.1164330952877, series.Points[^1].X);
        Assert.All(series.Points, point => Assert.InRange(point.X, -0.2, 0.2));
        Assert.All(series.Points.Zip(series.Points.Skip(1)), pair =>
            Assert.InRange(Math.Abs(pair.First.X - pair.Second.X), 0, 0.005));
        Assert.False(data.Values.ContainsKey("MaximumFocalShiftChangeMicrometers"));
        Assert.False(data.Values.ContainsKey("DiffractionLimitChangeMicrometers"));
    }

    [Fact]
    public void AfocalPrimaryParaxialWavelengthIsZeroAndManualScaleUsesDiopters()
    {
        var data = new ColorFocusShiftAnalysis(AfocalRiflescope(),
            maximumShiftMicrometers: 0.07, sampleCount: 171).GenerateData();
        var series = Assert.Single(data.PlotSeries);
        var primary = Assert.Single(series.Points, point => Math.Abs(point.Y - 0.587) < 1e-12);

        Near(0, primary.X);
        Assert.Equal(-0.07, data.PlotOptions!.XMinimum);
        Assert.Equal(0.07, data.PlotOptions.XMaximum);
        Assert.Equal(0.07, data.Values["MaximumShiftDiopters"]);
        // A manual view range must not clamp the calculated chromatic curve.
        Assert.True(series.Points.Min(point => point.X) < -0.1);
    }

    [Fact]
    public void AfocalNonzeroZoneUsesRealRayVergenceWithTheParaxialPrimaryReference()
    {
        var data = new ColorFocusShiftAnalysis(AfocalRiflescope(), pupilZone: 0.5,
            sampleCount: 171).GenerateData();
        var series = Assert.Single(data.PlotSeries);

        Near(-0.03932884919205, series.Points[0].X);
        Near(-0.13675782362403, series.Points[^1].X);
        var primary = Assert.Single(series.Points, point => Math.Abs(point.Y - 0.587) < 1e-12);
        Near(-0.02491656753681, primary.X);
        Assert.Equal(0.5, data.Values["PupilZone"]);
        Assert.All(series.Points, point => Assert.True(double.IsFinite(point.X)));
    }

    [Fact]
    public void AfocalExactlyParallelOutputIsValidZeroVergence()
    {
        var optic = OpticalFormatCatalog.Import(
            "MODE SEQ\nENPD 2\nSURF 0\nDISZ INFINITY\nSURF 1\nSTOP\nDISZ 10\nSURF 2", ".zmx");
        optic.ImageSpaceAfocal = true;
        var data = new ColorFocusShiftAnalysis(optic).GenerateData();
        var series = Assert.Single(data.PlotSeries);

        Assert.Equal(101, series.Points.Count);
        Assert.Equal(AnalysisAxisUnit.Diopter, series.XUnit);
        Assert.All(series.Points, point => Assert.Equal(0, point.X, 12));
        Assert.Equal(0.0, data.Values["MaximumFocalShiftChangeDiopters"]);
    }

    [Fact]
    public void AfocalFullyBlockedZoneIsUnavailableInsteadOfReportingZeroChange()
    {
        var optic = AfocalRiflescope();
        optic.SurfaceGroup.Items[^2].PhysicalAperture = new CircularAperture(1e-10);
        var data = new ColorFocusShiftAnalysis(optic, pupilZone: 0.5).GenerateData();

        Assert.Equal(AnalysisOutcome.Unavailable, data.Outcome);
        Assert.Empty(data.PlotSeries);
        Assert.Contains("No valid afocal", data.OutcomeReason);
    }

    [Theory]
    [InlineData(0.0, -13.12940411083531, 8.058921777963292, 66.15629917385621, 81.70448377066683)]
    [InlineData(0.5, -87.9770888351457, -74.69320337217513, -20.984604121591673, 71.83233538981243)]
    public void FocusedCookeRetainsItsExistingMicrometerCurveAndSummary(double zone,
        double first, double middle, double last, double maximumChange)
    {
        var data = new ColorFocusShiftAnalysis(Optic.CreateCookeTriplet(), pupilZone: zone).GenerateData();
        var series = Assert.Single(data.PlotSeries);

        Assert.Equal(AnalysisAxisUnit.Micrometer, series.XUnit);
        Assert.Equal("焦移：µm", series.XAxisLabel);
        Near(first, series.Points[0].X);
        Near(middle, series.Points[50].X);
        Near(last, series.Points[100].X);
        Near(maximumChange, Convert.ToDouble(data.Values["MaximumFocalShiftChangeMicrometers"]));
        Near(207.077558958829, Convert.ToDouble(data.Values["PrimaryParaxialFocusMicrometers"]));
        Near(54.52137465066463, Convert.ToDouble(data.Values["DiffractionLimitChangeMicrometers"]));
        Assert.Equal("PrimaryParaxialFocus", data.Values["Reference"]);
        Assert.False(data.Values.ContainsKey("MaximumFocalShiftChangeDiopters"));
    }

    private static void Near(double expected, double actual) =>
        Assert.InRange(Math.Abs(actual - expected), 0, 1e-8);

    private static Optic AfocalRiflescope()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpticalSystemDesign.slnx")))
            directory = directory.Parent;
        var root = directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
        return OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(root,
            "src/OpticalSystemDesign.App/Assets/SequentialExamples/Afocal/Afocal Riflescope.ZMX")), ".zmx");
    }
}
