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
        }

        Assert.Equal(3, result.PlotPaneColumns);
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
