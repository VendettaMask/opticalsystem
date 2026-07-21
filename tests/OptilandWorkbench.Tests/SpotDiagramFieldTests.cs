using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Tests;

public sealed class SpotDiagramFieldTests
{
    [Fact]
    public void SpotDiagramUsesEveryConfiguredFieldInSystemOrder()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { Label = "Center", X = 0, Y = 0 });
        optic.Fields.Add(new FieldPoint { Label = "Lower", X = 0.5, Y = 2.5 });
        optic.Fields.Add(new FieldPoint { Label = "Upper", X = -1.5, Y = 4.75 });

        var result = new SpotDiagramAnalysis(optic, numRings: 2).GenerateData();

        Assert.NotNull(result.PlotPanes);
        Assert.Equal(optic.Fields.Count, result.PlotPanes.Count);
        Assert.Equal(optic.Fields.Count, Assert.IsType<int>(result.Values["FieldCount"]));

        for (var index = 0; index < optic.Fields.Count; index++)
        {
            var field = optic.Fields[index];
            Assert.Equal(
                Math.Abs(field.X) <= 1e-12
                    ? $"物面: {field.Y:0.00} (度)"
                    : $"物面: X {field.X:0.00}, Y {field.Y:0.00} (度)",
                result.PlotPanes[index].Title);
            Assert.Collection(
                result.PlotPanes[index].Metrics!,
                metric =>
                {
                    Assert.Equal("RMS 半径", metric.Label);
                    Assert.Equal("µm", metric.Unit);
                    Assert.True(double.IsFinite(metric.Value));
                },
                metric =>
                {
                    Assert.Equal("GEO 半径", metric.Label);
                    Assert.Equal("µm", metric.Unit);
                    Assert.True(double.IsFinite(metric.Value));
                });
            Assert.Contains("参考：主波长质心", result.PlotPanes[index].Footer);
        }

        Assert.Equal(3, result.PlotPaneColumns);
    }

    [Fact]
    public void RayFanUsesEveryConfiguredFieldInSystemOrder()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { Label = "Center", X = 0, Y = 0 });
        optic.Fields.Add(new FieldPoint { Label = "Mid", X = 0, Y = 10 });
        optic.Fields.Add(new FieldPoint { Label = "Edge", X = 0, Y = 14 });

        var result = new RayFanAnalysis(optic, numPoints: 5).GenerateData();

        Assert.NotNull(result.PlotPanes);
        Assert.Equal(optic.Fields.Count * 2, result.PlotPanes.Count);
        Assert.Equal(optic.Fields.Count, Assert.IsType<int>(result.Values["FieldCount"]));
        Assert.Equal(2, result.PlotPaneColumns);

        for (var index = 0; index < optic.Fields.Count; index++)
        {
            var expectedTitle = $"物面: {optic.Fields[index].Y:0.00} (度)";
            Assert.Equal(expectedTitle, result.PlotPanes[index * 2].Title);
            Assert.Equal(expectedTitle, result.PlotPanes[(index * 2) + 1].Title);
        }
    }

    [Fact]
    public void PupilAberrationUsesEveryConfiguredFieldInSystemOrder()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { Label = "Center", X = 0, Y = 0 });
        optic.Fields.Add(new FieldPoint { Label = "Mid", X = 0, Y = 10 });
        optic.Fields.Add(new FieldPoint { Label = "Edge", X = 0, Y = 14 });

        var result = new PupilAberrationAnalysis(optic, numPoints: 5).GenerateData();

        Assert.NotNull(result.PlotPanes);
        Assert.Equal(optic.Fields.Count * 2, result.PlotPanes.Count);
        Assert.Equal(optic.Fields.Count, Assert.IsType<int>(result.Values["FieldCount"]));
        Assert.Equal(2, result.PlotPaneColumns);

        for (var index = 0; index < optic.Fields.Count; index++)
        {
            var expectedTitle = $"物面: {optic.Fields[index].Y:0.00} (度)";
            Assert.Equal(expectedTitle, result.PlotPanes[index * 2].Title);
            Assert.Equal(expectedTitle, result.PlotPanes[(index * 2) + 1].Title);
        }
    }

    [Fact]
    public void ImportedDoubleGaussSpotDiagramContainsRaysForEveryFieldAndWavelength()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Samples",
            "double-gauss-50mm.zmx"));
        var optic = OpticalFormatCatalog.Import(source, ".zmx");

        var result = new SpotDiagramAnalysis(optic, numRings: 6).GenerateData();

        Assert.NotNull(result.PlotPanes);
        Assert.Equal(optic.Fields.Count, result.PlotPanes.Count);
        Assert.All(result.PlotPanes, pane =>
        {
            Assert.Equal(optic.Wavelengths.Count, pane.Series.Count);
            Assert.All(pane.Series, series => Assert.NotEmpty(series.Points));
        });
    }

    [Fact]
    public void FiniteObjectAngleFieldsReachTheImageForEveryConfiguredField()
    {
        var source = File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory,
                "Samples",
                "double-gauss-50mm.zmx"))
            .Replace("DISZ INFINITY", "DISZ 500", StringComparison.Ordinal);
        var optic = OpticalFormatCatalog.Import(source, ".zmx");

        var result = new SpotDiagramAnalysis(optic, numRings: 6).GenerateData();

        Assert.NotNull(result.PlotPanes);
        Assert.Equal(optic.Fields.Count, result.PlotPanes.Count);
        Assert.All(result.PlotPanes, pane =>
        {
            Assert.Equal(optic.Wavelengths.Count, pane.Series.Count);
            Assert.All(pane.Series, series => Assert.NotEmpty(series.Points));
        });
    }
}
