using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Tests;

public sealed class ActualFieldSamplingTests
{
    [Fact]
    public void FieldSamplingHonorsEachAnalysisContract()
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

        var rms = new RmsVsFieldAnalysis(
            optic,
            numRings: 1,
            data: "spot",
            fieldDensity: 4,
            scanDirection: "-x").GenerateData();
        var expectedSpotScan = new[] { 0.0, -2.28125, -4.5625, -6.84375, -9.125 };
        Assert.All(rms.PlotSeries, series =>
            Assert.Equal(expectedSpotScan, series.Points.Select(point => point.X)));
        Assert.Equal(5, (int)rms.Values["FieldCount"]);
        Assert.Equal(4, (int)rms.Values["FieldDensity"]);
        Assert.Equal("-x", rms.Values["ScanDirection"]);

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

        var pupil = new[]
        {
            new PupilSample(-1, -1, 1),
            new PupilSample(1, -1, 2),
            new PupilSample(-1, 1, 3),
            new PupilSample(1, 1, 4)
        };
        var tiltedWavefront = pupil.Select(sample => new WavefrontSample(
            sample.X,
            sample.Y,
            sample.X,
            sample.Y,
            0,
            7 + (2 * sample.X) - (3 * sample.Y),
            1)).ToArray();
        var centroidRms = RmsScanSupport.WeightedWavefrontRms(
            tiltedWavefront,
            pupil,
            "centroid");
        var chiefRms = RmsScanSupport.WeightedWavefrontRms(
            tiltedWavefront,
            pupil,
            "chief");
        Assert.InRange(centroidRms, 0, 1e-12);
        Assert.True(chiefRms > 1);
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
