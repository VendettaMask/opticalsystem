using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.Tests;

public sealed class ActualFieldSamplingTests
{
    [Fact]
    public void FieldBasedAnalysesUseExactSystemFieldRowsInsteadOfProportionalSamples()
    {
        var optic = Optic.CreateCookeTriplet();
        var expectedCoordinates = new[] { 0.0, 2.75, 9.125 };
        var expectedLabels = new[] { "Center", "Actual 2.75 deg", "Actual 9.125 deg" };
        for (var index = 0; index < optic.Fields.Count; index++)
        {
            optic.Fields[index].X = 0;
            optic.Fields[index].Y = expectedCoordinates[index];
            optic.Fields[index].Label = expectedLabels[index];
        }

        var rms = new RmsVsFieldAnalysis(optic, numFields: 101, numRings: 1).GenerateData();
        AssertSeriesFields(rms.PlotSeries, expectedCoordinates, expectedLabels, coordinateOnX: true);

        var wavefront = new RmsWavefrontVsFieldAnalysis(optic, numFields: 101, numRings: 2).GenerateData();
        AssertSeriesFields(wavefront.PlotSeries, expectedCoordinates, expectedLabels, coordinateOnX: true);

        var mtf = new MtfVsFieldAnalysis(
            optic,
            MtfComputationMethod.Geometric,
            spatialFrequency: 20,
            fieldPointCount: 101,
            settings: new MtfComputationSettings(GeometricRayCount: 4)).GenerateData();
        AssertSeriesFields(mtf.PlotSeries, expectedCoordinates, expectedLabels, coordinateOnX: true);


        var incidentAngle = new IncidentAngleVsHeightAnalysis(
            optic,
            AngleScanMode.ThroughField,
            numPoints: 101).GenerateData();
        var incidentSeries = Assert.Single(incidentAngle.PlotSeries);
        Assert.Equal(expectedCoordinates, incidentSeries.Points.Select(point => point.Value!.Value));
        Assert.Equal(expectedLabels, incidentSeries.Points.Select(point => point.Label));
    }

    private static void AssertSeriesFields(
        IReadOnlyList<AnalysisSeries> seriesList,
        IReadOnlyList<double> expectedCoordinates,
        IReadOnlyList<string> expectedLabels,
        bool coordinateOnX)
    {
        Assert.NotEmpty(seriesList);
        foreach (var series in seriesList)
        {
            Assert.Equal(expectedCoordinates.Count, series.Points.Count);
            Assert.Equal(
                expectedCoordinates,
                series.Points.Select(point => coordinateOnX ? point.X : point.Y));
            Assert.Equal(expectedLabels, series.Points.Select(point => point.Label));
        }
    }
}
