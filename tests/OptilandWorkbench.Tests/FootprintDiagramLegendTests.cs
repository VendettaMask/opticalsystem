using System.Globalization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Tests;

public sealed class FootprintDiagramLegendTests
{
    [Fact]
    public void FootprintLegendMetadataFollowsTheSelectedColorBasis()
    {
        var optic = Optic.CreateCookeTriplet();

        var fieldData = new FootprintDiagramAnalysis(
            optic,
            rayDensity: 2).GenerateData();
        var fieldSeries = ScatterSeries(fieldData);
        var unit = optic.FieldDefinition == FieldDefinitionKind.Angle ? "°" : "mm";

        Assert.Equal("field", fieldData.Values["ColorRaysBy"]);
        Assert.Equal(
            Enumerable.Range(1, optic.Fields.Count).Select(index => $"field:{index}"),
            fieldSeries.Select(series => series.LegendKey).Distinct());
        Assert.Equal(
            optic.Fields.Select((field, index) =>
                $"F{index + 1}  ({field.X.ToString("0.####", CultureInfo.InvariantCulture)}, " +
                $"{field.Y.ToString("0.####", CultureInfo.InvariantCulture)}) {unit}"),
            fieldSeries.Select(series => series.LegendLabel).Distinct());
        Assert.All(
            fieldSeries.GroupBy(series => series.LegendKey),
            group => Assert.Single(group.Select(series => series.ColorIndex).Distinct()));
        Assert.True((double)fieldData.Values["XScaleMillimeters"] > 0);
        Assert.True((double)fieldData.Values["YScaleMillimeters"] > 0);
        Assert.True((double)fieldData.Values["ApertureDiameterMillimeters"] > 0);
        Assert.True((double)fieldData.Values["RayXMaximumMillimeters"]
            >= (double)fieldData.Values["RayXMinimumMillimeters"]);
        Assert.True((double)fieldData.Values["RayYMaximumMillimeters"]
            >= (double)fieldData.Values["RayYMinimumMillimeters"]);
        Assert.True((double)fieldData.Values["MaximumRayRadiusMillimeters"] >= 0);

        var wavelengthData = new FootprintDiagramAnalysis(
            optic,
            rayDensity: 2,
            colorRaysBy: "波长").GenerateData();
        var wavelengthSeries = ScatterSeries(wavelengthData);

        Assert.Equal("wavelength", wavelengthData.Values["ColorRaysBy"]);
        Assert.Equal(
            optic.Wavelengths.Select(wavelength =>
                $"wavelength:{wavelength.Micrometers.ToString("R", CultureInfo.InvariantCulture)}"),
            wavelengthSeries.Select(series => series.LegendKey).Distinct());
        Assert.Equal(
            optic.Wavelengths.Select(wavelength =>
                $"{wavelength.Micrometers.ToString("0.0000", CultureInfo.InvariantCulture)} µm"),
            wavelengthSeries.Select(series => series.LegendLabel).Distinct());
    }

    private static AnalysisSeries[] ScatterSeries(AnalysisData data) =>
        data.PlotSeries
            .Where(series => series.Kind == AnalysisSeriesKind.Scatter)
            .ToArray();
}
