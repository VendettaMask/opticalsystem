using OptilandWorkbench.Application.Legacy;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.Tests;

public sealed class RelativeIlluminationTests
{
    public static TheoryData<string, Func<Optic>> SampleOptics => new()
    {
        { "cooke", Optic.CreateCookeTriplet },
        { "tessar", Optic.CreateTessarLens }
    };

    [Theory]
    [MemberData(nameof(SampleOptics))]
    public void RelativeIlluminationProducesNormalizedFiniteCurve(
        string sampleName,
        Func<Optic> createOptic)
    {
        var optic = createOptic();
        var data = new RelativeIlluminationAnalysis(
            optic,
            rayDensity: 10,
            fieldDensity: 97).GenerateData();

        var series = Assert.Single(data.SeriesList!);
        Assert.Equal(AnalysisSeriesKind.Line, series.Kind);
        Assert.False(series.ShowMarkers);
        Assert.Equal(97, series.Points.Count);
        Assert.All(series.Points, point =>
        {
            Assert.True(double.IsFinite(point.X), $"{sampleName}: non-finite field coordinate");
            Assert.InRange(point.Y, 0, 1);
        });
        Assert.Equal(1, series.Points.Max(point => point.Y), precision: 12);

        var raw = Assert.IsType<double[]>(data.Values["RawProjectedCosineArea"]);
        var effectiveFNumbers = Assert.IsType<double[]>(data.Values["EffectiveFNumbers"]);
        var validRayCounts = Assert.IsType<int[]>(data.Values["ValidRayCounts"]);
        Assert.Equal(97, raw.Length);
        Assert.All(raw, value => Assert.True(double.IsFinite(value) && value >= 0));
        Assert.Contains(raw, value => value > 0);
        Assert.All(effectiveFNumbers.Where(double.IsFinite), value => Assert.True(value > 0));
        Assert.All(validRayCounts, value => Assert.True(value > 0));
    }

    [Fact]
    public void RelativeIlluminationUsesIndependentRadialFieldScan()
    {
        var optic = Optic.CreateCookeTriplet();
        var expectedCoordinates = new[] { 0.0, 3.25, 7.5 };
        for (var index = 0; index < optic.Fields.Count; index++)
        {
            optic.Fields[index].X = 0;
            optic.Fields[index].Y = expectedCoordinates[index];
            optic.Fields[index].Label = $"Actual field {index + 1}";
        }

        var data = new RelativeIlluminationAnalysis(
            optic,
            rayDensity: 8,
            fieldDensity: 99,
            scanDirection: "-x").GenerateData();

        var series = Assert.Single(data.SeriesList!);
        var points = series.Points;
        Assert.Equal(99, points.Count);
        Assert.Equal(0, points[0].X, precision: 12);
        Assert.Equal(-7.5, points[^1].X, precision: 12);
        Assert.All(points, point => Assert.True(string.IsNullOrEmpty(point.Label)));
        Assert.Equal("-x", data.Values["ScanDirection"]);
        Assert.Equal("X Field Angle (deg)", series.XAxisLabel);
    }

    [Fact]
    public void RemovingVignettingFactorsRestoresFullPupilWithoutMutatingOptic()
    {
        var optic = Optic.CreateCookeTriplet();
        foreach (var field in optic.Fields)
        {
            field.VignetteFactorX = 0.5;
            field.VignetteFactorY = 0.5;
        }

        var vignetted = new RelativeIlluminationAnalysis(
            optic,
            rayDensity: 12,
            fieldDensity: 3,
            removeVignettingFactors: false).GenerateData();
        var fullPupil = new RelativeIlluminationAnalysis(
            optic,
            rayDensity: 12,
            fieldDensity: 3,
            removeVignettingFactors: true).GenerateData();

        var vignettedArea = Assert.IsType<double[]>(vignetted.Values["RawProjectedCosineArea"]);
        var fullPupilArea = Assert.IsType<double[]>(fullPupil.Values["RawProjectedCosineArea"]);
        Assert.True(fullPupilArea[0] > vignettedArea[0]);
        Assert.All(optic.Fields, field =>
        {
            Assert.Equal(0.5, field.VignetteFactorX, precision: 12);
            Assert.Equal(0.5, field.VignetteFactorY, precision: 12);
        });
    }

    [Fact]
    public void ConnectorExposesRelativeIlluminationAnalysisContract()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());

        Assert.Equal("Relative Illumination", connector.CanonicalAnalysisKey("相对照度"));
        var parameters = connector.GetAnalysisParameters("相对照度");
        Assert.Contains(parameters, item => item.Key == "RayDensity");
        Assert.Contains(parameters, item => item.Key == "FieldDensity");
        Assert.Contains(parameters, item => item.Key == "WavelengthNumber");
        Assert.Contains(parameters, item => item.Key == "ScanDirection");
        Assert.Contains(parameters, item => item.Key == "RemoveVignettingFactors");

        var view = connector.BuildAnalysisView("相对照度", new Dictionary<string, string>
        {
            ["RayDensity"] = "8",
            ["FieldDensity"] = "31",
            ["ScanDirection"] = "+x",
            ["RemoveVignettingFactors"] = "true"
        });

        Assert.Equal("相对照度", view.Name);
        var series = Assert.Single(view.SeriesList);
        Assert.Equal(AnalysisSeriesKind.Line, series.Kind);
        Assert.Equal(31, series.Points.Count);
        Assert.False(series.ShowMarkers);
    }
}
