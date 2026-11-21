using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Application.Runtime;

namespace OpticalSystemDesign.Tests;

public sealed class AfocalRmsReferenceTests
{
    [Fact]
    public void FocalMonochromaticFieldCurvesRetainTheSameMetricContract()
    {
        var optic = Optic.CreateCookeTriplet();
        Assert.False(optic.ImageSpaceAfocal);
        for (var wavelengthNumber = 1; wavelengthNumber <= optic.Wavelengths.Count; wavelengthNumber++)
        {
            var wavelength = optic.Wavelengths[wavelengthNumber - 1];
            var field = new RmsWavefrontVsFieldAnalysis(optic, numRings: 3, fieldDensity: 4,
                reference: "chief", wavelengthNumber: wavelengthNumber, zemaxCompatibleOutput: true).GenerateData();
            var points = Assert.Single(field.PlotSeries).Points;
            for (var index = 0; index < points.Count; index++)
                Assert.Equal(RmsScanSupport.Metric(optic, (0, index / 4d), [wavelength], 3,
                    "gaussian", "wavefront", "chief"), points[index].Y, 9);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EachColourFieldScanAgreesWithTheSameMonochromaticMetric(int wavelengthNumber)
    {
        var optic = Load();
        var wavelength = optic.Wavelengths[wavelengthNumber - 1];
        var field = new RmsWavefrontVsFieldAnalysis(optic, numRings: 6, fieldDensity: 10,
            reference: "chief", wavelengthNumber: wavelengthNumber, zemaxCompatibleOutput: true).GenerateData();
        var points = Assert.Single(field.PlotSeries).Points;
        for (var index = 0; index < points.Count; index++)
        {
            var expected = RmsScanSupport.Metric(optic, (0, index / 10d), [wavelength], 6,
                "gaussian", "wavefront", "chief");
            Assert.Equal(expected, points[index].Y, 9);
        }
    }

    [Fact]
    public void AngularGridDistortionKeepsTangentCoordinatesDimensionless()
    {
        var optic = Load();
        var data = new GridDistortionAnalysis(optic, numPoints: 3, wavelengthNumber: 2).GenerateData();
        Assert.All(data.PlotSeries, series =>
        {
            Assert.Equal(AnalysisAxisUnit.Dimensionless, series.XUnit);
            Assert.Equal(AnalysisAxisUnit.Dimensionless, series.YUnit);
        });
        var angularHalfWidth = Math.Tan((double)data.Values["GridFieldWidth"] * Math.PI / 360);
        Assert.Equal(angularHalfWidth, data.PlotSeries[0].Points.Max(p => Math.Abs(p.X)), 12);
        var finiteObject = Optic.CreateCookeTriplet();
        finiteObject.FieldDefinition = FieldDefinitionKind.ObjectHeight;
        finiteObject.SurfaceGroup.Items[0].Thickness = 100;
        finiteObject.SurfaceGroup.Renumber();
        finiteObject.Fields.Clear();
        finiteObject.Fields.Add(new FieldPoint { Y = 0 });
        finiteObject.Fields.Add(new FieldPoint { Y = 0.1 });
        var heights = new GridDistortionAnalysis(finiteObject, numPoints: 3, wavelengthNumber: 2, fieldWidth: 0.1).GenerateData();
        Assert.All(heights.PlotSeries, series => Assert.Equal(AnalysisAxisUnit.Millimeter, series.XUnit));
    }

    [Fact]
    public void SpectralMixtureRetainsTheCommonReferenceAndIndependentColourCurves()
    {
        var optic = Load();
        var data = new RmsWavefrontVsFieldAnalysis(optic, numRings: 6, fieldDensity: 10,
            reference: "chief", zemaxCompatibleOutput: true).GenerateData();
        Assert.Equal(4, data.PlotSeries.Count);
        Assert.Equal("rms:polychromatic", data.PlotSeries[0].LegendKey);
        Assert.Equal("Poly", data.PlotSeries[0].LegendLabel);
        var wavelengths = optic.Wavelengths.ToArray();
        var index = 7;
        var expected = RmsScanSupport.Metric(optic, (0, .7), wavelengths, 6, "gaussian", "wavefront", "chief");
        Assert.Equal(expected, data.PlotSeries[0].Points[index].Y, 9);
        Assert.InRange(data.PlotSeries[1].Points[index].Y, .74, .78);
        Assert.InRange(data.PlotSeries[2].Points[index].Y, .66, .70);
        Assert.InRange(data.PlotSeries[3].Points[index].Y, .62, .66);
        // A common-reference spectral RMS includes real lateral-colour tilt.
        // It need not equal the RMS mixture of independently tilted colour pupils.
        var ownReferenceMixture = Math.Sqrt(data.PlotSeries.Skip(1).Select((series, wave) =>
            wavelengths[wave].Weight * Math.Pow(series.Points[index].Y, 2)).Sum() / wavelengths.Sum(w => w.Weight));
        Assert.True(Math.Abs(expected - ownReferenceMixture) > .01);
    }

    [Fact]
    public void TheWavefrontWrapperUsesTheSameCorrectedCurvesWithoutChangingThePrescription()
    {
        var optic = Load();
        var before = optic.ToSnapshot();
        var direct = new RmsWavefrontVsFieldAnalysis(optic, numRings: 6, fieldDensity: 5,
            reference: "chief", zemaxCompatibleOutput: true).GenerateData();
        var wrapper = new RmsVsFieldAnalysis(optic, numRings: 6, fieldDensity: 5,
            data: "wavefront", reference: "chief").GenerateData();
        foreach (var pair in direct.PlotSeries.Zip(wrapper.PlotSeries)) Assert.Equal(pair.First.Points, pair.Second.Points);
        Assert.Equal(before.Fields, optic.ToSnapshot().Fields);
        Assert.Equal(before.Surfaces.Select(s => s.Thickness), optic.ToSnapshot().Surfaces.Select(s => s.Thickness));
        Assert.All(wrapper.PlotSeries, series => Assert.Equal(AnalysisAxisUnit.Wave, series.YUnit));
    }

    [Fact]
    public void DisplayingEightFringeTermsUsesTheSameCompleteFitAsTheCoefficientTable()
    {
        var optic = Load();
        var table = new ZernikeAnalysis(optic, ZernikeAnalysisKind.Fringe, numRings: 12,
            numTerms: 37, wavelengthNumber: 2, fieldNumber: 3).GenerateData();
        var sweep = new WorkbenchRuntime(optic).BuildAnalysisData("Zernike vs Field",
            new Dictionary<string, string> { ["FieldDensity"] = "4", ["NumRings"] = "12",
                ["ZernikeTerms"] = "8", ["WavelengthNumber"] = "2" });
        var expected = (double[])table.Values["CoefficientsWaves"];
        Assert.Equal(8, sweep.PlotSeries.Count);
        Assert.Equal(8, sweep.Values["DisplayedZernikeTerms"]);
        Assert.Equal(37, sweep.Values["FittedZernikeTerms"]);
        for (var term = 0; term < 8; term++) Assert.Equal(expected[term], sweep.PlotSeries[term].Points[^1].Y, 10);
    }

    [Fact]
    public void GeneralFringeFitDefaultsRemainExplicitAndDisplayedTermsCannotExceedTheFit()
    {
        var optic = Load();
        var data = new ZernikeVsFieldAnalysis(optic, fieldDensity: 2, numRings: 4, numTerms: 8).GenerateData();
        Assert.Equal(8, data.Values["FittedZernikeTerms"]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ZernikeVsFieldAnalysis(optic, numTerms: 8, fitTermCount: 4));
    }

    private static Optic Load()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "OpticalSystemDesign.slnx")))
                return OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(directory.FullName,
                    "src/OpticalSystemDesign.App/Assets/SequentialExamples/Afocal/Afocal Riflescope.ZMX")), ".zmx");
        throw new DirectoryNotFoundException("Repository was not found.");
    }
}
