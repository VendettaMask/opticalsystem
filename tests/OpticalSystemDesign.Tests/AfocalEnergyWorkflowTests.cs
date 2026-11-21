using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class AfocalEnergyWorkflowTests
{
    [Theory]
    [InlineData("X")]
    [InlineData("Y")]
    public void AfocalLineEdgeSpreadUsesAnglesAndIgnoresReadoutPlaneTranslation(string orientation)
    {
        var optic = Riflescope();
        AnalysisData Run() => new GeometricLineEdgeSpreadAnalysis(optic, pupilSampling: 32,
            numPoints: 257, orientation: orientation).GenerateData();
        var before = Run();
        optic.SurfaceGroup.Items[^2].Thickness += 100;
        var after = Run();
        Assert.InRange(before.PlotOptions!.XMaximum!.Value, .5, 2);
        Assert.True(before.Values.ContainsKey("MaximumRadiusMilliradians"));
        Assert.False(before.Values.ContainsKey("MaximumRadiusMicrometers"));
        Assert.Equal("mrad", before.Values["CoordinateUnit"]);
        for (var series = 0; series < before.PlotSeries.Count; series++)
        {
            Assert.Equal(AnalysisAxisUnit.Milliradian, before.PlotSeries[series].XUnit);
            Assert.Equal(AnalysisAxisQuantity.Angle, before.PlotSeries[series].XQuantity);
            Assert.Equal(before.PlotSeries[series].Points.Count, after.PlotSeries[series].Points.Count);
            foreach (var pair in before.PlotSeries[series].Points.Zip(after.PlotSeries[series].Points))
            {
                Assert.Equal(pair.First.X, pair.Second.X, 8);
                Assert.Equal(pair.First.Y, pair.Second.Y, 10);
            }
        }
        var edge = before.PlotSeries.Single(series => series.Name == "Edge Spread");
        Assert.All(edge.Points.Zip(edge.Points.Skip(1)), pair => Assert.True(pair.Second.Y >= pair.First.Y));
    }

    [Fact]
    public void ManualLineEdgeWindowUsesTheDeclaredAngularHalfWidth()
    {
        var optic = Riflescope();
        var halfWidth = Math.PI * 1000 / (180 * 60) * 5;
        var data = new GeometricLineEdgeSpreadAnalysis(optic, maximumRadiusMicrometers: halfWidth).GenerateData();
        Assert.Equal(-halfWidth, data.PlotOptions!.XMinimum);
        Assert.Equal(halfWidth, data.PlotOptions.XMaximum);
        Assert.Equal(halfWidth, data.Values["MaximumRadiusMilliradians"]);
        Assert.All(data.PlotSeries, series => Assert.All(series.Points, p => Assert.InRange(p.Y, 0, 1)));
    }

    [Fact]
    public void FocalLineEdgeSpreadRetainsTheMicrometerContract()
    {
        var data = new GeometricLineEdgeSpreadAnalysis(Optic.CreateCookeTriplet(), pupilSampling: 8,
            maximumRadiusMicrometers: 10).GenerateData();
        Assert.All(data.PlotSeries, series => Assert.Equal(AnalysisAxisUnit.Micrometer, series.XUnit));
        Assert.Equal(10d, data.Values["MaximumRadiusMicrometers"]);
        Assert.True(data.Values.ContainsKey("HistogramBinWidthMicrometers"));
        Assert.False(data.Values.ContainsKey("MaximumRadiusMilliradians"));
    }

    internal static Optic Riflescope() => OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(
        RepositoryRoot(), "src/OpticalSystemDesign.App/Assets/SequentialExamples/Afocal/Afocal Riflescope.ZMX")), ".zmx");

    private static string RepositoryRoot()
    {
        for (var path = new DirectoryInfo(AppContext.BaseDirectory); path is not null; path = path.Parent)
            if (File.Exists(Path.Combine(path.FullName, "OpticalSystemDesign.slnx"))) return path.FullName;
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
