using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Interactions;

namespace OpticalSystemDesign.Tests;

public sealed class AfocalAxialAberrationTests
{
    [Theory]
    [InlineData(-0.0002, false, .2)]
    [InlineData(0, false, .001)]
    [InlineData(0.0002, false, .5)]
    [InlineData(-0.0002, true, .2)]
    [InlineData(0, true, .001)]
    [InlineData(0.0002, true, .5)]
    [InlineData(0.00002, false, .05)]
    [InlineData(0.002, false, 5)]
    public void AfocalZonalPowerMatchesTheIndependentThinLensSlopeLaw(double power, bool aiming, double expectedScale)
    {
        var optic = PlaneLens(power); optic.RayAimingEnabled = aiming;
        var data = new AxialAberrationAnalysis(optic, sampleCount: 21).GenerateData();
        var series = Assert.Single(data.PlotSeries);
        // A thin lens launches slope -P*y. After 10 mm propagation its
        // height is (1-10P)*y, so 1/(crossing distance in metres) is:
        var expected = 1000 * power / (1 - 10 * power);
        Assert.Equal(21, series.Points.Count);
        Assert.All(series.Points, point => Assert.Equal(expected, point.X, 9));
        Assert.Equal(-expectedScale, data.PlotOptions!.XMinimum);
        Assert.Equal(expectedScale, data.PlotOptions.XMaximum);
        Assert.Equal(AnalysisAxisUnit.Diopter, series.XUnit);
        Assert.Equal(AnalysisAxisQuantity.Defocus, series.XQuantity);
        Assert.Equal("D", data.Values["DefocusUnit"]);
        Assert.True(data.Values.ContainsKey("GraphScaleDiopters"));
        Assert.False(data.Values.ContainsKey("GraphScaleMillimeters"));
    }

    [Fact]
    public void AfocalManualScaleUsesDioptersAndPreservesWavelengthSelection()
    {
        var optic = PlaneLens(.0002);
        optic.Wavelengths.Add(new Wavelength { Nanometers = 650 });
        var data = new AxialAberrationAnalysis(optic, graphScaleMillimeters: .5,
            wavelengthNumber: 2, useDashes: true).GenerateData();
        Assert.Equal(-.5, data.PlotOptions!.XMinimum);
        Assert.Equal(.5, data.PlotOptions.XMaximum);
        Assert.Equal(.5, data.Values["GraphScaleDiopters"]);
        Assert.Equal("0.6500 µm", Assert.Single(data.PlotSeries).Name);
    }

    [Fact]
    public void RiflescopeCurvesRemainFiniteThroughTheCollimationZones()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "OpticalSystemDesign.App", "Assets",
            "SequentialExamples", "Afocal", "Afocal Riflescope.ZMX");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(path), ".zmx");
        var before = optic.ToSnapshot();
        var data = new AxialAberrationAnalysis(optic).GenerateData();
        Assert.Equal(-.5, data.PlotOptions!.XMinimum);
        Assert.Equal(.5, data.PlotOptions.XMaximum);
        Assert.Equal(3, data.PlotSeries.Count);
        Assert.All(data.PlotSeries, series =>
        {
            Assert.Equal(101, series.Points.Count);
            Assert.Equal(AnalysisAxisUnit.Diopter, series.XUnit);
            Assert.All(series.Points, point => Assert.True(double.IsFinite(point.X) && Math.Abs(point.X) < .51));
            Assert.All(series.Points.Zip(series.Points.Skip(1)), pair =>
                Assert.True(Math.Abs(pair.First.X - pair.Second.X) < .04));
        });
        // Coarse ranges are read from the user's frozen reference picture;
        // they guard polarity and scale without claiming a numerical capture.
        Assert.InRange(data.PlotSeries[0].Points[0].X, .08, .10);
        Assert.InRange(data.PlotSeries[1].Points[0].X, .08, .10);
        Assert.InRange(data.PlotSeries[2].Points[0].X, -.04, -.02);
        Assert.InRange(data.PlotSeries[0].Points[^1].X, -.49, -.47);
        Assert.InRange(data.PlotSeries[1].Points[^1].X, -.42, -.39);
        Assert.InRange(data.PlotSeries[2].Points[^1].X, -.51, -.49);
        Assert.Equal(before.Fields, optic.ToSnapshot().Fields);
        Assert.Equal(before.Surfaces.Select(s => s.Thickness), optic.ToSnapshot().Surfaces.Select(s => s.Thickness));
    }

    private static Optic PlaneLens(double power)
    {
        var optic = new Optic { ImageSpaceAfocal = true };
        optic.Aperture.Value = 2;
        optic.Fields.Add(new FieldPoint());
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { IsStop = true, SemiDiameter = 1, Thickness = 10,
                InteractionModel = new ParaxialLensInteractionModel(power, power) },
            new OpticalSurface { Thickness = 0, SemiDiameter = 10 }
        ]);
        return optic;
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "OpticalSystemDesign.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
