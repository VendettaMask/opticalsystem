using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Panels;

internal sealed record ToleranceChartView(
    IReadOnlyList<AnalysisSeriesDto> Series,
    AnalysisPlotOptionsDto PlotOptions,
    string Summary,
    string EmptyMessage = "");

internal static class ToleranceChartBuilder
{
    public static ToleranceChartView Histogram(
        TolerancingResultDto? result,
        ToleranceCriterion criterion)
    {
        var values = TrialValues(result);
        if (values.Length == 0)
        {
            return Empty("Monte Carlo 评价值直方图", "尚未运行 Monte Carlo 公差分析，或结果中没有有效数值。");
        }

        var minimum = values.Min();
        var maximum = values.Max();
        var span = maximum - minimum;
        var binCount = values.Length == 1 || span <= NumericTolerance(minimum, maximum)
            ? 1
            : Math.Clamp((int)Math.Ceiling(Math.Log2(values.Length) + 1), 5, 30);
        var binWidth = binCount == 1
            ? Math.Max(Math.Abs(minimum) * 0.04, 1e-6)
            : span / binCount;
        var bins = new int[binCount];
        foreach (var value in values)
        {
            var index = binCount == 1
                ? 0
                : Math.Clamp((int)Math.Floor((value - minimum) / binWidth), 0, binCount - 1);
            bins[index]++;
        }

        var lowerBound = binCount == 1 ? minimum - (binWidth / 2) : minimum;
        var upperBound = binCount == 1 ? maximum + (binWidth / 2) : maximum;
        var points = bins
            .Select((count, index) =>
            {
                var left = lowerBound + (index * binWidth);
                var right = left + binWidth;
                return new AnalysisPointDto(
                    left + (binWidth / 2),
                    count,
                    $"{Format(left)} – {Format(right)}：{count}");
            })
            .ToArray();
        var mean = values.Average();
        var sigma = Math.Sqrt(values.Average(value => Math.Pow(value - mean, 2)));
        var axisLabel = CriterionAxisLabel(criterion);

        return new ToleranceChartView(
            new[]
            {
                new AnalysisSeriesDto(
                    axisLabel,
                    "试验数",
                    points,
                    Kind: AnalysisSeriesKind.Bar,
                    Name: "Monte Carlo 试验",
                    ColorIndex: 0,
                    Opacity: 0.82)
            },
            new AnalysisPlotOptionsDto(
                Title: "Monte Carlo 评价值直方图",
                XMinimum: lowerBound,
                XMaximum: upperBound,
                YMinimum: 0,
                YMaximum: Math.Max(1, bins.Max() * 1.12),
                HideTopAndRightAxes: true,
                GridOpacity: 0.28),
            $"样本数：{values.Length}    均值：{Format(mean)}    σ：{Format(sigma)}    "
            + $"最小值：{Format(minimum)}    最大值：{Format(maximum)}");
    }

    public static ToleranceChartView Yield(
        TolerancingResultDto? result,
        ToleranceCriterion criterion,
        double yieldLimit)
    {
        var values = TrialValues(result);
        if (values.Length == 0)
        {
            return Empty("Monte Carlo 良率", "尚未运行 Monte Carlo 公差分析，或结果中没有有效数值。");
        }

        Array.Sort(values);
        var points = new List<AnalysisPointDto>(values.Length + 1)
        {
            new(values[0], 0)
        };
        points.AddRange(values.Select((value, index) =>
            new AnalysisPointDto(value, (index + 1) * 100.0 / values.Length)));

        var axisLabel = CriterionAxisLabel(criterion);
        var series = new List<AnalysisSeriesDto>
        {
            new(
                axisLabel,
                "累计通过率 (%)",
                points,
                Kind: AnalysisSeriesKind.Line,
                Name: "累计分布",
                ColorIndex: 0,
                ShowMarkers: values.Length <= 50,
                LineWidth: 2,
                MarkerSize: 2.6)
        };

        var hasLimit = double.IsFinite(yieldLimit) && yieldLimit > 0;
        if (hasLimit)
        {
            series.Add(new AnalysisSeriesDto(
                axisLabel,
                "累计通过率 (%)",
                new[]
                {
                    new AnalysisPointDto(yieldLimit, 0),
                    new AnalysisPointDto(yieldLimit, 100)
                },
                Kind: AnalysisSeriesKind.Line,
                Name: "合格上限",
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: 3,
                LineWidth: 1.5));
        }

        var passed = hasLimit ? values.Count(value => value <= yieldLimit) : 0;
        var summary = hasLimit
            ? $"合格上限：{Format(yieldLimit)}    合格样本：{passed} / {values.Length}    "
              + $"估算良率：{passed * 100.0 / values.Length:0.###}%"
            : $"样本数：{values.Length}    未设置合格上限；曲线显示评价值的累计分布。";

        return new ToleranceChartView(
            series,
            new AnalysisPlotOptionsDto(
                Title: "Monte Carlo 良率（累计分布）",
                YMinimum: 0,
                YMaximum: 100,
                ShowLegend: hasLimit,
                HideTopAndRightAxes: true,
                GridOpacity: 0.28,
                LegendBelow: hasLimit),
            summary);
    }

    private static ToleranceChartView Empty(string title, string message) => new(
        Array.Empty<AnalysisSeriesDto>(),
        new AnalysisPlotOptionsDto(Title: title, HideTopAndRightAxes: true, GridOpacity: 0.28),
        string.Empty,
        message);

    private static double[] TrialValues(TolerancingResultDto? result) =>
        result?.TrialRows
            .Select(row => TryParse(row.CompensatedMerit, out var compensated)
                ? compensated
                : TryParse(row.Merit, out var merit)
                    ? merit
                    : double.NaN)
            .Where(double.IsFinite)
            .ToArray()
        ?? Array.Empty<double>();

    private static string CriterionAxisLabel(ToleranceCriterion criterion) =>
        criterion == ToleranceCriterion.RmsWavefront
            ? "RMS 波前误差 (waves)"
            : "RMS 点列半径 (mm)";

    private static bool TryParse(string text, out double value)
    {
        var token = (text ?? string.Empty)
            .Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || double.TryParse(token, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    private static double NumericTolerance(double first, double second) =>
        Math.Max(1, Math.Max(Math.Abs(first), Math.Abs(second))) * 1e-12;

    private static string Format(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}

public sealed class ToleranceChartDocumentPanel : UserControl
{
    private readonly Func<ToleranceChartView> _viewProvider;
    private readonly AnalysisPlotControl _plot = new() { MinHeight = 240 };
    private readonly TextBlock _summary = new()
    {
        Margin = new Thickness(12, 8),
        TextWrapping = TextWrapping.Wrap
    };
    private readonly TextBlock _emptyMessage = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 520,
        Margin = new Thickness(24)
    };

    internal ToleranceChartDocumentPanel(Func<ToleranceChartView> viewProvider)
    {
        _viewProvider = viewProvider;
        _summary.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextSecondary);
        _emptyMessage.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextSecondary);

        var refresh = CommandButton("refresh-cw", "刷新");
        refresh.Click += (_, _) => Refresh();
        var reset = CommandButton("rotate-ccw", "重置视图");
        reset.Click += (_, _) => _plot.ResetView();

        var toolbar = new Border
        {
            Padding = new Thickness(8, 6),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                Children = { refresh, reset }
            }
        };
        toolbar.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        toolbar.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);

        var plotLayer = new Grid
        {
            Children = { _plot, _emptyMessage }
        };
        var summaryBorder = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = _summary
        };
        summaryBorder.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        summaryBorder.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);

        var page = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children = { toolbar, plotLayer, summaryBorder }
        };
        Grid.SetRow(plotLayer, 1);
        Grid.SetRow(summaryBorder, 2);
        Content = page;
        Refresh();
    }

    public void Refresh()
    {
        var view = _viewProvider();
        _plot.Series = view.Series;
        _plot.PlotOptions = view.PlotOptions;
        _summary.Text = view.Summary;
        _summary.IsVisible = !string.IsNullOrWhiteSpace(view.Summary);
        _emptyMessage.Text = view.EmptyMessage;
        _emptyMessage.IsVisible = view.Series.Count == 0;
    }

    private static Button CommandButton(string icon, string text) => new()
    {
        Content = new LocalIconLabel(icon, text),
        MinWidth = 94,
        Margin = new Thickness(0, 0, 6, 0)
    };
}
