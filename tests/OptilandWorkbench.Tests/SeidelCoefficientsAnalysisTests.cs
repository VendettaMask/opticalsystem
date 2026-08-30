using System.Globalization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.Tests;

public sealed class SeidelCoefficientsAnalysisTests
{
    [Fact]
    public void GeneratesSurfaceContributionsAndCumulativeRow()
    {
        var optic = Optic.CreateCookeTriplet();
        var data = new SeidelCoefficientsAnalysis(optic).GenerateData();

        Assert.Equal("Seidel Coefficients", data.Name);
        Assert.NotNull(data.Table);
        Assert.Equal(8, data.Table!.Columns.Count);
        Assert.Equal(optic.SurfaceGroup.Items.Count, data.Table.Rows.Count);
        Assert.Equal("累计", data.Table.Rows[^1][0]);
        Assert.Contains(data.Table.Rows, row => row[0] == "光阑");
        Assert.Contains(data.Table.Rows, row => row[0] == "像面");
        Assert.All(
            data.Table.Rows,
            row => Assert.All(
                row.Skip(1),
                value => Assert.True(double.TryParse(value, CultureInfo.InvariantCulture, out _))));
        Assert.Contains("SPHA S1", data.ReportText);
        Assert.Contains("CTR (CT)", data.ReportText);
    }

    [Fact]
    public void UsesRequestedOneBasedWavelengthNumber()
    {
        var optic = Optic.CreateCookeTriplet();
        var expected = optic.Wavelengths[^1].Micrometers;

        var data = new SeidelCoefficientsAnalysis(optic, optic.Wavelengths.Count).GenerateData();

        Assert.Equal(expected, Assert.IsType<double>(data.Values["WavelengthMicrometers"]), 12);
    }

    [Fact]
    public void SurfacePresentationNamesDoNotSelectImageRow()
    {
        var optic = Optic.CreateCookeTriplet();
        var baseline = new SeidelCoefficientsAnalysis(optic).GenerateData();

        optic.SurfaceGroup.Items[1].Label = "Image";
        optic.SurfaceGroup.Items[^1].Label = "Sensor plane";
        var relabeled = new SeidelCoefficientsAnalysis(optic).GenerateData();

        Assert.Equal(
            baseline.Table!.Rows.Select(row => row[0]),
            relabeled.Table!.Rows.Select(row => row[0]));
    }

    [Fact]
    public void DiagramProvidesSevenBarSeriesAndTotalGroup()
    {
        var optic = Optic.CreateCookeTriplet();

        var data = new SeidelDiagramAnalysis(
            optic,
            maximumAberration: 0.2,
            gridInterval: 0.02).GenerateData();

        Assert.Equal("Seidel Diagram", data.Name);
        Assert.Equal(7, data.SeriesList?.Count);
        Assert.All(data.SeriesList!, series => Assert.Equal(AnalysisSeriesKind.Bar, series.Kind));
        Assert.Equal("总和", data.Table?.Rows[^1][0]);
        Assert.Equal(0.2, Assert.IsType<double>(data.Values["MaximumAberration"]), 12);
        Assert.Equal(0.02, Assert.IsType<double>(data.Values["GridInterval"]), 12);
    }
}
