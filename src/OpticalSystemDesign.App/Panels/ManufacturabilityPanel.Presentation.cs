using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Manufacturing;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class ManufacturabilityPanel
{
    private readonly StackPanel _findingCards = new() { Name = "ManufacturingFindings", Spacing = 10 };
    private readonly TextBlock _elementCount = Count("ManufacturingElementCount");
    private readonly TextBlock _passCount = Count("ManufacturingPassCount");
    private readonly TextBlock _warningCount = Count("ManufacturingWarningCount");
    private readonly TextBlock _errorCount = Count("ManufacturingErrorCount");
    private readonly UniformGrid _statistics = new() { Columns = 4 };
    private readonly Grid _details = new() { Name = "ManufacturingDetails" };
    private Border _metricsCard = null!;
    private Border _findingsCard = null!;

    private Control BuildPage()
    {
        _summary.Name = "ManufacturingSummary";
        _summary.FontSize = DisplayTypography.LargeTitle;
        _summary.TextWrapping = TextWrapping.Wrap;
        var heading = new StackPanel
        {
            Spacing = 5,
            Children =
            {
                Muted("可加工性评估"), _summary,
                Muted("基于当前处方的几何指标与规则筛查")
            }
        };
        var evaluate = new Button
        {
            Name = "ReevaluateManufacturing",
            Content = new LocalIconLabel("clipboard-check", "重新评估"),
            MinHeight = 36,
            MinWidth = 120,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        evaluate.Classes.Add("accent");
        evaluate.Click += (_, _) => Refresh();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(heading);
        Grid.SetColumn(evaluate, 1);
        header.Children.Add(evaluate);

        _statistics.Children.Add(Statistic("玻璃元件", _elementCount, ThemeResourceBindings.TextPrimary));
        _statistics.Children.Add(Statistic("通过项", _passCount, ThemeResourceBindings.TextSuccess));
        _statistics.Children.Add(Statistic("需评审项", _warningCount, ThemeResourceBindings.TextWarning));
        _statistics.Children.Add(Statistic("不可加工项", _errorCount, ThemeResourceBindings.TextError));
        var fields = new WrapPanel
        {
            Children =
            {
                Field("最小中心厚度 (mm)", _minimumCenterThickness, "MinimumCenterThickness"),
                Field("最小边厚 (mm)", _minimumEdgeThickness, "MinimumEdgeThickness"),
                Field("最大 D/CT", _maximumAspectRatio, "MaximumDiameterThicknessRatio"),
                Field("最小 |R|/D", _minimumRadiusRatio, "MinimumRadiusDiameterRatio"),
                Field("最大边缘斜率 (°)", _maximumEdgeSlope, "MaximumEdgeSlope")
            }
        };
        var rules = Card(new StackPanel
        {
            Spacing = 10,
            Children = { Title("评估规则"), fields }
        }, 14);
        rules.Name = "ManufacturingRules";

        _metricsCard = Section("几何加工指标", "逐元件查看尺寸、面型与边缘几何", _metricGrid);
        _metricsCard.Name = "ManufacturingMetricSection";
        _findingsCard = Section("规则评估结论", "优先处理不可加工与需评审项目", new ScrollViewer
        {
            Content = _findingCards,
            Padding = new Thickness(14),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        _findingsCard.Name = "ManufacturingFindingSection";
        _details.Children.Add(_metricsCard);
        _details.Children.Add(_findingsCard);
        _basis.Name = "ManufacturingScreeningNote";
        _basis.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextMuted);
        _basis.FontSize = DisplayTypography.BodySmall;
        var page = new StackPanel
        {
            Spacing = 14,
            Margin = new Thickness(18),
            Children = { header, _statistics, rules, _details, _basis }
        };
        var scroll = new ScrollViewer
        {
            Name = "ManufacturingPageScroll",
            Content = page,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        scroll.BindThemeResource(BackgroundProperty, ThemeResourceBindings.SubtleSurface);
        SizeChanged += (_, _) => ArrangeSections();
        ArrangeSections();
        return scroll;
    }

    private void ArrangeSections()
    {
        var sideBySide = Bounds.Width >= 1120;
        _statistics.Columns = Bounds.Width < 680 ? 2 : 4;
        _details.Height = Math.Max(sideBySide ? 400 : 740, Bounds.Height - 420);
        _details.ColumnDefinitions = new ColumnDefinitions(sideBySide ? "2.2*,14,1*" : "*");
        _details.RowDefinitions = new RowDefinitions(sideBySide ? "*" : "1.4*,14,1*");
        Grid.SetColumn(_findingsCard, sideBySide ? 2 : 0);
        Grid.SetRow(_findingsCard, sideBySide ? 0 : 2);
    }

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        if (!_disposed && _report is { } report)
            PresentReport(report);
    }

    private void PresentReport(ManufacturabilityReport report)
    {
        _metricGrid.ItemsSource = report.GeometryMetrics.Select(metric => metric with
        {
            Item = ManufacturingText(metric.Item), Value = ManufacturingText(metric.Value), Note = ManufacturingText(metric.Note)
        }).ToArray();
        _elementCount.Text = report.Elements.Count.ToString(CultureInfo.CurrentCulture);
        _passCount.Text = report.PassCount.ToString(CultureInfo.CurrentCulture);
        _warningCount.Text = report.WarningCount.ToString(CultureInfo.CurrentCulture);
        _errorCount.Text = report.ErrorCount.ToString(CultureInfo.CurrentCulture);
        _summary.Text = UiLocalization.Text(report.Elements.Count == 0
            ? "没有识别到可评估的玻璃元件"
            : report.ErrorCount > 0 ? "存在不可加工项目"
            : report.WarningCount > 0 ? "部分项目需要工艺评审" : "当前规则未发现问题");
        _summary.BindThemeResource(TextBlock.ForegroundProperty, report.Elements.Count == 0
            ? ThemeResourceBindings.TextSecondary
            : report.ErrorCount > 0 ? ThemeResourceBindings.TextError
            : report.WarningCount > 0 ? ThemeResourceBindings.TextWarning : ThemeResourceBindings.TextPrimary);
        _findingCards.Children.Clear();
        foreach (var finding in report.Findings.OrderByDescending(item => item.Severity).ThenBy(item => item.ElementNumber))
            _findingCards.Children.Add(FindingCard(finding));
        if (report.Findings.Count == 0)
            _findingCards.Children.Add(Muted("没有可显示的评估结论"));
    }

    private void PresentFailure(string message)
    {
        foreach (var count in new[] { _elementCount, _passCount, _warningCount, _errorCount }) count.Text = "—";
        _summary.Text = UiLocalization.Text("无法评估");
        _summary.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextError);
        _findingCards.Children.Clear();
        _findingCards.Children.Add(Muted(message));
    }

    private static Border FindingCard(ManufacturabilityFinding finding)
    {
        var foreground = finding.Severity switch
        {
            ManufacturabilitySeverity.Error => ThemeResourceBindings.TextError,
            ManufacturabilitySeverity.Warning => ThemeResourceBindings.TextWarning,
            _ => ThemeResourceBindings.TextSuccess
        };
        var surface = finding.Severity switch
        {
            ManufacturabilitySeverity.Error => ThemeResourceBindings.ErrorSurface,
            ManufacturabilitySeverity.Warning => ThemeResourceBindings.WarningSurface,
            _ => ThemeResourceBindings.SuccessSurface
        };
        var badgeText = new TextBlock
        {
            Text = UiLocalization.Text(finding.SeverityText),
            FontWeight = FontWeight.SemiBold,
            FontSize = DisplayTypography.BodySmall
        };
        badgeText.BindThemeResource(TextBlock.ForegroundProperty, foreground);
        var badge = new Border
        {
            CornerRadius = new CornerRadius(5), Padding = new Thickness(9, 4), Child = badgeText,
            Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top
        };
        badge.BindThemeResource(Border.BackgroundProperty, surface);
        var identity = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        identity.Children.Add(new StackPanel
        {
            Spacing = 3,
            Children =
            {
                Title(string.Format(CultureInfo.CurrentCulture, UiLocalization.Text("元件 {0}"), finding.ElementNumber)),
                Muted(finding.Surfaces)
            }
        });
        Grid.SetColumn(badge, 1);
        identity.Children.Add(badge);
        var measured = Muted(ManufacturingText(finding.MeasuredValue));
        var recommendation = new SelectableTextBlock
        {
            Text = ManufacturingText(finding.Recommendation), TextWrapping = TextWrapping.Wrap
        };
        var content = new StackPanel
        {
            Spacing = 8,
            Children = { identity, Title(ManufacturingText(finding.Check)), measured, recommendation }
        };
        var card = Card(content, 14);
        card.DataContext = finding;
        card.Classes.Add("manufacturing-finding");
        return card;
    }

    private static Control Field(string label, NumericUpDown input, string name)
    {
        input.Name = name;
        AutomationProperties.SetName(input, label);
        var caption = Muted(label);
        caption.MinHeight = DisplayTypography.Scale(29);
        return new StackPanel
        {
            Width = DisplayTypography.Scale(160), Spacing = 5,
            Margin = new Thickness(0, 0, 14, 4),
            Children = { caption, input }
        };
    }

    private static Border Statistic(string label, TextBlock count, string foreground)
    {
        count.BindThemeResource(TextBlock.ForegroundProperty, foreground);
        var card = Card(new StackPanel { Spacing = 5, Children = { Muted(label), count } }, 14);
        card.Margin = new Thickness(0, 0, 8, 4);
        return card;
    }

    private static Border Section(string title, string description, Control content)
    {
        var heading = new Border
        {
            Padding = new Thickness(16, 13), BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel { Spacing = 4, Children = { Title(title), Muted(description) } }
        };
        heading.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        body.Children.Add(heading);
        Grid.SetRow(content, 1);
        body.Children.Add(content);
        var card = Card(body, 0);
        card.ClipToBounds = true;
        return card;
    }

    private static Border Card(Control child, double padding)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            Padding = new Thickness(padding), Child = child
        };
        card.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        card.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        return card;
    }

    private static TextBlock Count(string name) => new()
    {
        Name = name, Text = "—", FontSize = DisplayTypography.PageTitle, FontWeight = FontWeight.SemiBold
    };

    private static TextBlock Title(string text) => new()
    {
        Text = text, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap
    };

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = UiLocalization.Text(text), TextWrapping = TextWrapping.Wrap };
        block.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextSecondary);
        return block;
    }

    private static string ManufacturingText(string source)
    {
        var translated = UiLocalization.Text(source);
        if (translated != source || UiLocalization.CurrentLanguage == UiLocalization.Chinese) return translated;
        // Preserve formatted measured values verbatim; only localize display prose.
        (string Prefix, string Suffix, string Template)[] patterns =
        [
            ("建议不小于 ", " mm，或由工艺人员确认夹持方案。", "建议不小于 {0} mm，或由工艺人员确认夹持方案。"),
            ("建议不小于 ", " mm，以满足倒边和装夹余量。", "建议不小于 {0} mm，以满足倒边和装夹余量。"),
            ("高于设定上限 ", "，薄片加工和检测易变形。", "高于设定上限 {0}，薄片加工和检测易变形。"),
            ("超过 ", "°，需要专用工装或非传统加工路线。", "超过 {0}°，需要专用工装或非传统加工路线。")
        ];
        foreach (var pattern in patterns)
        {
            if (source.StartsWith(pattern.Prefix, StringComparison.Ordinal)
                && source.EndsWith(pattern.Suffix, StringComparison.Ordinal))
            {
                var value = source[pattern.Prefix.Length..^pattern.Suffix.Length];
                return string.Format(CultureInfo.CurrentCulture, UiLocalization.Text(pattern.Template), value);
            }
        }
        return source.Replace("标准面", UiLocalization.Text("标准面"), StringComparison.Ordinal)
            .Replace("超出实数矢高范围", UiLocalization.Text("超出实数矢高范围"), StringComparison.Ordinal);
    }
}
