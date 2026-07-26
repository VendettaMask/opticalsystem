namespace OptilandWorkbench.Core.Analysis;

public enum AnalysisSeriesKind
{
    Line,
    Scatter,
    Bar,
    Heatmap,
    Raster,
    ColoredLine
}

public enum AnalysisLineStyle
{
    Solid,
    Dashed,
    Dotted
}

public enum AnalysisMarkerStyle
{
    Circle,
    Square,
    Triangle,
    Cross
}

public enum AnalysisColorMap
{
    Viridis,
    Inferno,
    Jet
}

public sealed record AnalysisPoint(
    double X,
    double Y,
    string Label = "",
    double? Value = null,
    double? Red = null,
    double? Green = null,
    double? Blue = null);

public sealed record AnalysisSeries(
    string XAxisLabel,
    string YAxisLabel,
    IReadOnlyList<AnalysisPoint> Points,
    AnalysisSeriesKind Kind = AnalysisSeriesKind.Line,
    string Name = "",
    AnalysisLineStyle LineStyle = AnalysisLineStyle.Solid,
    int ColorIndex = 0,
    bool ShowMarkers = false,
    double LineWidth = 1.5,
    AnalysisMarkerStyle MarkerStyle = AnalysisMarkerStyle.Circle,
    double MarkerSize = 3.2,
    double Opacity = 1,
    string ValueLabel = "",
    AnalysisColorMap ColorMap = AnalysisColorMap.Viridis,
    double? ValueMinimum = null,
    double? ValueMaximum = null);

public sealed record AnalysisPlotOptions(
    string Title = "",
    bool SymmetricX = false,
    bool EqualAspect = false,
    bool ShowVerticalZeroLine = false,
    bool ShowHorizontalZeroLine = false,
    AnalysisLineStyle VerticalZeroLineStyle = AnalysisLineStyle.Solid,
    double VerticalZeroLineWidth = 0.5,
    double? XMinimum = null,
    double? XMaximum = null,
    double? YMinimum = null,
    double? YMaximum = null,
    bool ShowLegend = false,
    bool HideTopAndRightAxes = false,
    bool DottedGrid = false,
    double GridOpacity = 1,
    bool HideAxes = false,
    bool HideTickLabels = false,
    bool LegendBelow = false);

public sealed record AnalysisPlotPane(
    string Title,
    IReadOnlyList<AnalysisSeries> Series,
    AnalysisPlotOptions PlotOptions,
    IReadOnlyList<AnalysisPlotMetric>? Metrics = null,
    string Footer = "");

public sealed record AnalysisPlotMetric(
    string Label,
    double Value,
    string Unit = "");

public sealed record AnalysisTable(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<string>? RowGroups = null);

public sealed record AnalysisData(
    string Name,
    IReadOnlyDictionary<string, object> Values,
    AnalysisSeries? Series = null,
    IReadOnlyList<AnalysisSeries>? SeriesList = null,
    AnalysisPlotOptions? PlotOptions = null,
    IReadOnlyList<AnalysisPlotPane>? PlotPanes = null,
    int PlotPaneColumns = 3,
    AnalysisTable? Table = null,
    string? ReportText = null)
{
    public IReadOnlyList<AnalysisSeries> PlotSeries => SeriesList
        ?? (Series is null ? Array.Empty<AnalysisSeries>() : new[] { Series });

    public string ExportText()
    {
        return string.Join(Environment.NewLine, Values.Select(item => $"{item.Key}: {item.Value}"));
    }
}
