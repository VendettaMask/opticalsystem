using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.Tests;

public sealed class AfocalImageSpaceAnalysisTests
{
    [Fact]
    public void SpotDiagramUsesAngularCoordinatesForAfocalImageSpace()
    {
        var optic = AfocalCookeTriplet();

        var data = new SpotDiagramAnalysis(
            optic,
            new SpotDiagramSettings(
                RayDensity: 2,
                FieldNumber: 1,
                ShowAiryDisk: true)).GenerateData();

        var pane = Assert.Single(data.PlotPanes!);
        var raySeries = pane.Series.First(series => series.Kind == AnalysisSeriesKind.Scatter);
        Assert.Equal(AnalysisAxisUnit.Milliradian, raySeries.XUnit);
        Assert.Equal(AnalysisAxisUnit.Milliradian, raySeries.YUnit);
        Assert.Contains("(mrad)", raySeries.XAxisLabel, StringComparison.Ordinal);
        Assert.Contains("(mrad)", raySeries.YAxisLabel, StringComparison.Ordinal);
        Assert.Equal("mrad", pane.Metrics![0].Unit);
        Assert.Equal(true, data.Values["ImageSpaceAfocal"]);
        Assert.Equal("mrad", data.Values["ImageCoordinateUnit"]);
    }

    [Fact]
    public void RayFanUsesAngularAberrationForAfocalImageSpace()
    {
        var optic = AfocalCookeTriplet();

        var data = new RayFanAnalysis(
            optic,
            numPoints: 5,
            fieldNumber: 1,
            wavelengthNumber: 1).GenerateData();

        Assert.Equal(2, data.PlotPanes!.Count);
        Assert.All(data.PlotPanes, pane =>
        {
            var series = Assert.Single(pane.Series);
            Assert.Equal(AnalysisAxisUnit.Milliradian, series.YUnit);
            Assert.Contains("(mrad)", series.YAxisLabel, StringComparison.Ordinal);
        });
        Assert.Equal(true, data.Values["ImageSpaceAfocal"]);
        Assert.Equal("mrad", data.Values["RayAberrationUnit"]);
    }

    [Fact]
    public void ThroughFocusSpotUsesDioptersAndAngularSpotRadiusForAfocalImageSpace()
    {
        var optic = AfocalCookeTriplet();

        var data = new ThroughFocusAnalysis(
            optic,
            new ThroughFocusSpotSettings(
                RayDensity: 2,
                FieldNumber: 1,
                FocusPlaneCount: 3,
                DefocusStepMicrometers: 50)).GenerateData();

        var metricSeries = Assert.Single(data.PlotSeries);
        Assert.Equal(AnalysisAxisUnit.Diopter, metricSeries.XUnit);
        Assert.Equal(AnalysisAxisUnit.Milliradian, metricSeries.YUnit);
        Assert.Contains("Defocus (D)", metricSeries.XAxisLabel, StringComparison.Ordinal);
        Assert.Equal("D", data.Values["DefocusUnit"]);
        Assert.Equal(0.05, Convert.ToDouble(data.Values["FocusStepDiopters"]), 12);
        Assert.Equal(true, data.Values["ImageSpaceAfocal"]);
        Assert.Equal("mrad", data.Values["ImageCoordinateUnit"]);
    }

    [Fact]
    public void FourierMtfUsesAngularSpatialFrequencyForAfocalImageSpace()
    {
        var optic = AfocalCookeTriplet();

        var data = new MtfAnalysis(
            optic,
            numRays: 8,
            gridSize: 16,
            maximumFrequency: 1,
            fieldNumber: 1,
            wavelengthNumber: 1).GenerateData();

        Assert.NotEmpty(data.PlotSeries);
        Assert.All(data.PlotSeries, series =>
        {
            Assert.Equal(AnalysisAxisUnit.CyclesPerMilliradian, series.XUnit);
            Assert.Contains("cycles/mrad", series.XAxisLabel, StringComparison.Ordinal);
        });
        Assert.Equal("cycles/mrad", data.Values["FrequencyUnit"]);
        Assert.Equal(true, data.Values["ImageSpaceAfocal"]);
    }

    [Fact]
    public void RmsVsFocusUsesDioptersAndAngularSpotRadiusForAfocalImageSpace()
    {
        var optic = AfocalCookeTriplet();

        var data = new RmsVsFocusAnalysis(
            optic,
            focusDensity: 3,
            minimumFocus: -0.1,
            maximumFocus: 0.1,
            numRings: 2,
            wavelengthNumber: 1,
            showDiffractionLimit: true).GenerateData();

        Assert.NotEmpty(data.PlotSeries);
        Assert.All(data.PlotSeries, series =>
        {
            Assert.Equal(AnalysisAxisUnit.Diopter, series.XUnit);
            Assert.Equal(AnalysisAxisUnit.Milliradian, series.YUnit);
            Assert.Contains("Defocus (D)", series.XAxisLabel, StringComparison.Ordinal);
            Assert.Contains("(mrad)", series.YAxisLabel, StringComparison.Ordinal);
        });
        Assert.Equal("D", data.Values["DefocusUnit"]);
        Assert.Equal("mrad", data.Values["MetricUnit"]);
        Assert.Equal("mrad", data.Values["DiffractionLimitUnit"]);
        Assert.True(Convert.ToDouble(data.Values["DiffractionLimitMilliradians"]) > 0);
        Assert.Equal(true, data.Values["ImageSpaceAfocal"]);
    }

    [Fact]
    public void MtfThroughFocusUsesDioptersAndAngularFrequencyForAfocalImageSpace()
    {
        var optic = AfocalCookeTriplet();

        var data = new MtfThroughFocusAnalysis(
            optic,
            MtfComputationMethod.Geometric,
            spatialFrequency: 1,
            deltaFocus: 0.05,
            focusPlaneCount: 3,
            settings: new MtfComputationSettings(GeometricRayCount: 5),
            fieldNumber: 1,
            wavelengthNumber: 1).GenerateData();

        Assert.NotEmpty(data.PlotSeries);
        Assert.All(data.PlotSeries, series =>
        {
            Assert.Equal(AnalysisAxisUnit.Diopter, series.XUnit);
            Assert.Contains("Defocus (D)", series.XAxisLabel, StringComparison.Ordinal);
        });
        Assert.Equal("D", data.Values["DefocusUnit"]);
        Assert.Equal("cycles/mrad", data.Values["FrequencyUnit"]);
        Assert.Equal(true, data.Values["ImageSpaceAfocal"]);
        Assert.Equal(0.05, Convert.ToDouble(data.Values["DeltaFocus"]), 12);
    }

    [Fact]
    public void FftPsfUsesAngularSamplingForAfocalImageSpace()
    {
        var optic = AfocalCookeTriplet();

        var data = new PsfAnalysis(
            optic,
            numRays: 8,
            gridSize: 16,
            fieldNumber: 1,
            wavelengthNumber: 1).GenerateData();

        var series = Assert.Single(data.PlotSeries);
        Assert.Equal(AnalysisAxisUnit.Milliradian, series.XUnit);
        Assert.Equal(AnalysisAxisUnit.Milliradian, series.YUnit);
        Assert.Contains("(mrad)", series.XAxisLabel, StringComparison.Ordinal);
        Assert.Equal(true, data.Values["ImageSpaceAfocal"]);
        Assert.Equal("mrad", data.Values["ImageCoordinateUnit"]);
        Assert.True(Convert.ToDouble(data.Values["ImageDeltaMilliradians"]) > 0);
        Assert.Equal(0.0, Convert.ToDouble(data.Values["ImageDeltaMicrometers"]));
    }

    [Fact]
    public void HuygensPsfUsesAngularSamplingForAfocalImageSpace()
    {
        var optic = AfocalCookeTriplet();

        var data = new HuygensPsfAnalysis(
            optic,
            numRays: 4,
            imageSize: 8,
            pixelPitchMillimeters: 0.02,
            fieldNumber: 1,
            wavelengthNumber: 1).GenerateData();

        var series = Assert.Single(data.PlotSeries);
        Assert.Equal(AnalysisAxisUnit.Milliradian, series.XUnit);
        Assert.Equal(AnalysisAxisUnit.Milliradian, series.YUnit);
        Assert.Equal(true, data.Values["ImageSpaceAfocal"]);
        Assert.Equal("mrad", data.Values["ImageCoordinateUnit"]);
        Assert.Equal(0.02, Convert.ToDouble(data.Values["PixelPitchMilliradians"]), 12);
        Assert.Equal(0.0, Convert.ToDouble(data.Values["PixelPitchMicrometers"]));
    }

    [Fact]
    public void WavefrontUsesChiefRayPlaneForAfocalImageSpace()
    {
        var optic = AfocalCookeTriplet();

        var data = new WavefrontAnalysis(
            optic,
            pupilSampling: 8,
            wavelengthNumber: 1,
            fieldNumber: 1).GenerateData();

        Assert.Equal(true, data.Values["ImageSpaceAfocal"]);
        Assert.Equal("chief-ray plane", data.Values["ReferenceGeometry"]);
        Assert.Equal(0.0, Convert.ToDouble(data.Values["ReferenceSphereRadius"]));
        Assert.Equal("chief_ray_plane", data.Values["Reference"]);
    }

    [Fact]
    public void ReferenceSphereWavefrontFallsBackToChiefRayPlaneForAfocalImageSpace()
    {
        var optic = AfocalCookeTriplet();

        var data = new ReferenceSphereWavefrontAnalysis(
            optic,
            ReferenceSphereStrategy.BestFitSphere,
            numRings: 3,
            mapSize: 17,
            wavelengthNumber: 1,
            fieldNumber: 1).GenerateData();

        Assert.Equal(true, data.Values["ImageSpaceAfocal"]);
        Assert.Equal("chief-ray plane", data.Values["ReferenceGeometry"]);
        Assert.Equal(0.0, Convert.ToDouble(data.Values["ReferenceSphereRadius"]));
        Assert.Equal("chief_ray_plane", data.Values["Reference"]);
    }

    private static Optic AfocalCookeTriplet()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.ImageSpaceAfocal = true;
        return optic;
    }
}
