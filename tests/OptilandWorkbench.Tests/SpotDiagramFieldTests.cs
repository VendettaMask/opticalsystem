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
                    ? $"{field.Label} (Y={field.Y:0.###} \u00B0)"
                    : $"{field.Label} (X={field.X:0.###}, Y={field.Y:0.###} \u00B0)",
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
            var expectedTitle = $"{optic.Fields[index].Label} (Y={optic.Fields[index].Y:0.###} \u00B0)";
            Assert.Equal(expectedTitle, result.PlotPanes[index * 2].Title);
            Assert.Equal(expectedTitle, result.PlotPanes[(index * 2) + 1].Title);
        }
    }

    [Fact]
    public void RayFanAppliesZemaxSettingsToTraceAndPlot()
    {
        var optic = Optic.CreateCookeTriplet();
        var surfaceNumber = optic.SurfaceGroup.Items[1].Number;
        var result = new RayFanAnalysis(
            optic,
            plotScaleMicrometers: 5,
            numberOfRaysEachSide: 2,
            useDashes: true,
            vignettedPupil: true,
            checkApertures: false,
            wavelengthNumber: 2,
            fieldNumber: 2,
            tangentialAberration: "X Aberration",
            sagittalAberration: "Y Aberration",
            surfaceNumber: surfaceNumber,
            zemaxCompatible: true).GenerateData();

        Assert.NotNull(result.PlotPanes);
        Assert.Equal(2, result.PlotPanes.Count);
        Assert.All(result.PlotPanes, pane =>
        {
            Assert.Equal(-0.005, pane.PlotOptions.YMinimum);
            Assert.Equal(0.005, pane.PlotOptions.YMaximum);
            Assert.True(pane.PlotOptions.HideTickLabels);
            var series = Assert.Single(pane.Series);
            Assert.Equal(5, series.Points.Count);
            Assert.Equal(AnalysisLineStyle.Dashed, series.LineStyle);
        });
        Assert.Equal("epsilon_x (mm)", result.PlotPanes[0].Series[0].YAxisLabel);
        Assert.Equal("epsilon_y (mm)", result.PlotPanes[1].Series[0].YAxisLabel);
        Assert.Equal(surfaceNumber, Assert.IsType<int>(result.Values["SurfaceNumber"]));
        Assert.Equal(2, Assert.IsType<int>(result.Values["NumberOfRaysEachSide"]));
        Assert.Equal(1, Assert.IsType<int>(result.Values["FieldCount"]));
        Assert.Equal(1, Assert.IsType<int>(result.Values["WavelengthCount"]));
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
            var expectedTitle = $"{optic.Fields[index].Label} (Y={optic.Fields[index].Y:0.###} \u00B0)";
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
