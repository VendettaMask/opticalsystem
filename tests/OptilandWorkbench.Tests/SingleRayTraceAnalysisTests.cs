using OptilandWorkbench.Application.Legacy;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.Tests;

public sealed class SingleRayTraceAnalysisTests
{
    [Fact]
    public void DirectionCosineTraceProducesPlotsAndPerSurfaceTable()
    {
        var optic = Optic.CreateCookeTriplet();
        var data = new SingleRayTraceAnalysis(
            optic,
            fieldNumber: 1,
            wavelengthNumber: 1,
            px: 0,
            py: 0.5,
            globalCoordinates: false,
            type: "方向余弦",
            useRayAiming: false).GenerateData();

        Assert.Equal("Single Ray Trace", data.Name);
        Assert.NotNull(data.Table);
        Assert.Equal(
            new[]
            {
                "表面", "X-坐标", "Y-坐标", "Z-坐标",
                "X-余弦", "Y-余弦", "Z-余弦",
                "X-法线", "Y-法线", "Z-法线",
                "角", "路径长度", "注释"
            },
            data.Table.Columns);
        Assert.NotNull(data.Table.RowGroups);
        Assert.Contains("实光线", data.Table.RowGroups);
        Assert.Contains("近轴光线", data.Table.RowGroups);
        Assert.All(
            data.Table.Rows.Where((_, index) => data.Table.RowGroups[index] == "近轴光线"),
            row => Assert.All(row.Skip(7), Assert.Empty));
        Assert.DoesNotContain(
            data.Table.Rows.SelectMany(row => row),
            value => value.EndsWith(".000000000", StringComparison.Ordinal));
        Assert.Single(data.PlotPanes!);
        Assert.All(data.PlotPanes!, pane => Assert.True(pane.Series.Count >= 2));
        Assert.Contains(data.PlotPanes![0].Series, series => series.Name == "实光线");
        Assert.Contains(data.PlotPanes![0].Series, series => series.Name == "近轴光线");
    }

    [Theory]
    [InlineData("正切角", "Tan X")]
    [InlineData("Ym, Um, Yc, Uc", "Ym")]
    public void ZemaxTraceTypesProduceExpectedColumns(string type, string expectedColumn)
    {
        var data = new SingleRayTraceAnalysis(
            Optic.CreateCookeTriplet(),
            type: type,
            useRayAiming: false).GenerateData();

        Assert.NotNull(data.Table);
        Assert.Contains(expectedColumn, data.Table.Columns);
        Assert.NotEmpty(data.Table.Rows);
    }

    [Fact]
    public void ConnectorExposesZemaxStyleInputsAndDocumentReport()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());
        var parameters = connector.GetAnalysisParameters("单光线追迹");

        Assert.Equal(
            new[]
            {
                "FieldNumber", "Hx", "Hy", "WavelengthNumber", "Px", "Py",
                "GlobalCoordinates", "Type", "UseRayAiming", "ShowRaySegments"
            },
            parameters.Select(parameter => parameter.Key));

        var view = connector.BuildAnalysisView("单光线追迹", new Dictionary<string, string>
        {
            ["FieldNumber"] = "任意",
            ["Hx"] = "0",
            ["Hy"] = "0.5",
            ["WavelengthNumber"] = "1",
            ["Px"] = "0",
            ["Py"] = "0.7",
            ["GlobalCoordinates"] = "true",
            ["Type"] = "方向余弦",
            ["UseRayAiming"] = "false",
            ["ShowRaySegments"] = "false"
        });

        Assert.Equal("单光线追迹", view.Name);
        Assert.NotNull(view.Table);
        Assert.Contains("实际光线追迹数据：", view.ReportText);
        Assert.Contains("近轴光线追迹数据：", view.ReportText);
        Assert.Contains("X-法线", view.ReportText);
        Assert.Contains("0.0000000000E+00", view.ReportText);
    }

    [Fact]
    public void ZemaxSquarePupilCoordinatesAreAcceptedAndThenTraced()
    {
        var data = new SingleRayTraceAnalysis(
            Optic.CreateCookeTriplet(),
            fieldNumber: 0,
            hx: 1,
            hy: 1,
            px: 0.8,
            py: 0.8,
            type: "方向余弦",
            useRayAiming: true).GenerateData();

        Assert.NotNull(data.Table);
        Assert.NotEmpty(data.Table.Rows);
        Assert.Contains("0.8000000000", data.ReportText);
    }
}
