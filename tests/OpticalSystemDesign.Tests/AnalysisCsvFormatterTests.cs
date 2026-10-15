using Microsoft.VisualBasic.FileIO;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Formatting;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

public sealed class AnalysisCsvFormatterTests
{
    [Theory]
    [InlineData("Prescription Report")]
    [InlineData("System Data Report")]
    public async Task RealTableReportsExportEveryColumnAndRow(string key)
    {
        using var app = WorkbenchApplication.Create();
        var result = await app.Analyses.RunAsync(new(Guid.NewGuid(), 1, key, new Dictionary<string, string>()));
        var table = Assert.IsType<AnalysisTableDto>(result.View.Table);
        Assert.NotEmpty(table.Rows);
        var rows = Parse(AnalysisCsvFormatter.Format(result.View));
        Assert.Equal(table.Rows.Count + 1, rows.Count);
        Assert.Equal(table.Columns, rows[0].Take(table.Columns.Count));
        for (var index = 0; index < table.Rows.Count; index++)
            Assert.Equal(table.Rows[index], rows[index + 1].Take(table.Columns.Count));
    }

    [Fact]
    public void TableTextAndRowGroupsRoundTripWithoutParsingDisplayStrings()
    {
        var table = new AnalysisTableDto(
            ["标签,名称", "值 \"说明\""],
            [new[] { "  多行\n标签\"A\"  ", "1,234.50 mm" }, new[] { "第二面", string.Empty }],
            ["实光线,一组", "近轴光线"]);
        var rows = Parse(AnalysisCsvFormatter.Format(View(table: table)));
        Assert.Equal(new[] { "标签,名称", "值 \"说明\"", "row_group" }, rows[0]);
        Assert.Equal(new[] { "  多行\n标签\"A\"  ", "1,234.50 mm", "实光线,一组" }, rows[1]);
        Assert.Equal(new[] { "第二面", string.Empty, "近轴光线" }, rows[2]);
    }

    [Fact]
    public void SummaryOnlyResultsExportMetricAndValue()
    {
        var view = View() with { Rows = [new("名称", "任意,标题"), new("长度", "1.25 mm")] };
        var rows = Parse(AnalysisCsvFormatter.Format(view));
        Assert.Equal(new[] { "metric", "value" }, rows[0]);
        Assert.Equal(new[] { "名称", "任意,标题" }, rows[1]);
        Assert.Equal(new[] { "长度", "1.25 mm" }, rows[2]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void RaggedTablesAreRejectedInsteadOfDroppingOrInventingCells(int cells)
    {
        var table = new AnalysisTableDto(["a", "b"], [Enumerable.Repeat("value", cells).ToArray()]);
        Assert.Throws<InvalidDataException>(() => AnalysisCsvFormatter.Format(View(table: table)));
    }

    [Fact]
    public void PlotCsvRetainsExistingSchemaTypedUnitsAndNumericPrecision()
    {
        var series = Series();
        var view = View(series: [series], table: new(["supplement"], [new[] { "summary" }]));
        var rows = Parse(AnalysisCsvFormatter.Format(view));
        Assert.Equal(new[] { "pane", "series", "x_quantity", "x_unit", "x", "y_quantity", "y_unit", "y",
            "value_quantity", "value_unit", "value", "label" }, rows[0]);
        Assert.Equal(new[] { "", "名称,任意", "Coordinate", "mm", "1.2345678901234567", "Intensity", "1", "2",
            "Unspecified", "", "", "点\"一\"" }, rows[1]);
    }

    [Fact]
    public void EmptyPanesDoNotHideAvailableTopLevelPlotData()
    {
        var view = View(series: [Series()]) with
        {
            PlotPanes = [new("empty", Array.Empty<AnalysisSeriesDto>(), new())]
        };
        var rows = Parse(AnalysisCsvFormatter.Format(view));
        Assert.Equal(2, rows.Count);
        Assert.Equal("名称,任意", rows[1][1]);
    }

    [Fact]
    public void LegitimatelyEmptyTableKeepsItsColumnHeader()
    {
        var rows = Parse(AnalysisCsvFormatter.Format(View(table: new(["a", "b"], []))));
        Assert.Equal(new[] { "a", "b" }, Assert.Single(rows));
    }

    [Fact]
    public void ResultWithoutAnyExportableDataDoesNotReportAnEmptyPlotExportAsSuccess()
        => Assert.Throws<InvalidOperationException>(() => AnalysisCsvFormatter.Format(View()));

    private static AnalysisSeriesDto Series() => new("名称,任意", "值",
        [new(1.2345678901234567, 2, Label: "点\"一\"")], Name: "名称,任意",
        XQuantity: AnalysisAxisQuantity.Coordinate, XUnit: AnalysisAxisUnit.Millimeter,
        YQuantity: AnalysisAxisQuantity.Intensity, YUnit: AnalysisAxisUnit.Dimensionless);

    private static AnalysisViewDto View(AnalysisTableDto? table = null, IReadOnlyList<AnalysisSeriesDto>? series = null)
        => new("修改过的显示标题", [], string.Empty, series ?? [], new(), [], 1, Table: table);

    private static List<string[]> Parse(string csv)
    {
        using var parser = new TextFieldParser(new StringReader(csv))
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        var rows = new List<string[]>();
        while (!parser.EndOfData) rows.Add(parser.ReadFields()!);
        return rows;
    }
}
