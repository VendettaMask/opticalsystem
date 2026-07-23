using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Tests;

public sealed class MtfMaximumFrequencyTests
{
    [Fact]
    public void MtfCurvesUseDeclaredFieldNamesAndCoordinates()
    {
        var optic = Optic.CreateCookeTriplet();

        var data = new MtfAnalysis(
            optic,
            numRays: 16,
            gridSize: 32,
            maximumFrequency: 20).GenerateData();

        Assert.Equal(new[]
        {
            "On axis (Y=0 °), Tangential",
            "On axis (Y=0 °), Sagittal",
            "14 deg (Y=14 °), Tangential",
            "14 deg (Y=14 °), Sagittal",
            "20 deg (Y=20 °), Tangential",
            "20 deg (Y=20 °), Sagittal"
        }, data.PlotSeries.Select(series => series.Name));
        for (var fieldIndex = 0; fieldIndex < optic.Fields.Count; fieldIndex++)
        {
            var tangential = data.PlotSeries[fieldIndex * 2];
            var sagittal = data.PlotSeries[(fieldIndex * 2) + 1];
            Assert.Equal(fieldIndex, tangential.ColorIndex);
            Assert.Equal(fieldIndex, sagittal.ColorIndex);
            Assert.Equal(AnalysisLineStyle.Solid, tangential.LineStyle);
            Assert.Equal(AnalysisLineStyle.Dashed, sagittal.LineStyle);
        }
    }

    [Fact]
    public void RealImageHeightMtfLegendUsesMillimetersInsteadOfNormalizedFields()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.FieldDefinition = FieldDefinitionKind.RealImageHeight;
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { Label = "轴上视场", Y = 0 });
        optic.Fields.Add(new FieldPoint { Label = "视场 2", Y = 1.125 });
        optic.Fields.Add(new FieldPoint { Label = "视场 3", Y = 2.25 });
        optic.Fields.Add(new FieldPoint { Label = "视场 4", Y = 3.375 });
        optic.Fields.Add(new FieldPoint { Label = "最大Y视场", Y = 4.5 });

        var data = new MtfAnalysis(
            optic,
            numRays: 16,
            gridSize: 32,
            maximumFrequency: 20).GenerateData();

        Assert.Equal(10, data.PlotSeries.Count);
        Assert.Equal("轴上视场 (Y=0 mm), Tangential", data.PlotSeries[0].Name);
        Assert.Equal("视场 2 (Y=1.125 mm), Tangential", data.PlotSeries[2].Name);
        Assert.Equal("视场 3 (Y=2.25 mm), Tangential", data.PlotSeries[4].Name);
        Assert.Equal("视场 4 (Y=3.375 mm), Tangential", data.PlotSeries[6].Name);
        Assert.Equal("最大Y视场 (Y=4.5 mm), Tangential", data.PlotSeries[8].Name);
        Assert.DoesNotContain(data.PlotSeries, series =>
            series.Name?.Contains("Hx:", StringComparison.Ordinal) == true
            || series.Name?.Contains("Hy:", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void FourierMtfHonorsRequestedMaximumFrequency()
    {
        const double maximumFrequency = 20;
        var data = new MtfAnalysis(
            Optic.CreateCookeTriplet(),
            numRays: 16,
            gridSize: 32,
            maximumFrequency: maximumFrequency).GenerateData();

        Assert.Equal(maximumFrequency, data.PlotOptions!.XMaximum);
        Assert.Equal(maximumFrequency, Convert.ToDouble(data.Values["MaximumFrequency"]));
        Assert.All(data.PlotSeries.SelectMany(series => series.Points), point =>
            Assert.InRange(point.X, 0, maximumFrequency));
        Assert.All(data.PlotSeries, series => Assert.Equal(maximumFrequency, series.Points[^1].X));
    }

    [Fact]
    public void HuygensMtfHonorsRequestedMaximumFrequency()
    {
        const double maximumFrequency = 20;
        var data = new HuygensMtfAnalysis(
            Optic.CreateCookeTriplet(),
            numRays: 5,
            imageSize: 32,
            pixelPitchMillimeters: 0.005,
            fields: new[] { (0.0, 0.0) },
            maximumFrequency: maximumFrequency).GenerateData();

        Assert.Equal(maximumFrequency, data.PlotOptions!.XMaximum);
        Assert.Equal(maximumFrequency, Convert.ToDouble(data.Values["MaximumFrequency"]));
        Assert.All(data.PlotSeries.SelectMany(series => series.Points), point =>
            Assert.InRange(point.X, 0, maximumFrequency));
        Assert.All(data.PlotSeries, series => Assert.Equal(maximumFrequency, series.Points[^1].X));
    }
}
