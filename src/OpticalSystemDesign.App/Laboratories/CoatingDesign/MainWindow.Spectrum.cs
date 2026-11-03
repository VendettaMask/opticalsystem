using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Modules;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

public sealed partial class MainWindow
{
    private readonly List<(CoatingQuantity Quantity, ToggleButton Button)> _curveButtons = [];
    private readonly TextBlock _plotStatus = new() { TextWrapping = TextWrapping.Wrap };
    private Button? _plotPng, _plotRefresh;

    private Control BuildSpectrumToolbar()
    {
        var commands = new WrapPanel();
        foreach (var quantity in Enum.GetValues<CoatingQuantity>())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            row.Children.Add(new Ellipse { Fill = SpectrumPlot.CurveBrush(quantity), Width = 8, Height = 8, Margin = new Thickness(2, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = quantity.ToString(), FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            foreach (var polarization in Enum.GetValues<CoatingCurvePolarization>())
            {
                var label = polarization == CoatingCurvePolarization.Average ? "平均" : polarization == CoatingCurvePolarization.S ? "s" : "p";
                var button = new ToggleButton { Content = label, IsChecked = polarization == CoatingCurvePolarization.Average };
                button.Classes.Add("coating-curve");
                AutomationProperties.SetName(button, $"{quantity} {label}");
                ToolTip.SetTip(button, polarization == CoatingCurvePolarization.Average ? "平均 = (s + p) / 2；不是波段平均" : polarization == CoatingCurvePolarization.S ? "s 偏振 · 虚线" : "p 偏振 · 点线");
                button.Tag = ToolTip.GetTip(button);
                button.IsCheckedChanged += (_, _) => _plot.SetCurveVisible(quantity, polarization, button.IsChecked == true);
                row.Children.Add(button); _curveButtons.Add((quantity, button));
            }
            var group = new Border { Child = row, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(5, 2), Margin = new Thickness(0, 0, 6, 4) };
            group.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.SubtleSurface);
            group.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
            commands.Children.Add(group);
        }
        var targets = new ToggleButton { Content = ToolContent("coating-target", "目标"), IsChecked = true };
        targets.Classes.Add("coating-plot-tool"); AutomationProperties.SetName(targets, "显示目标线");
        ToolTip.SetTip(targets, "显示原设计目标，不改变验收偏振");
        targets.IsCheckedChanged += (_, _) => _plot.SetTargets(targets.IsChecked == true);
        commands.Children.Add(targets);
        var edit = PlotButton("编辑设计目标", "edit-target", async () => await Guard(() => ShowSettingsAsync(0)), "编辑");
        commands.Children.Add(edit);
        var settings = new DropDownButton { Content = ToolContent("settings-general", "设置"), Flyout = EvaluationSettingsFlyout() };
        settings.Classes.Add("coating-plot-tool"); AutomationProperties.SetName(settings, "光谱图设置");
        ToolTip.SetTip(settings, "光谱显示范围、网格与纵轴设置"); commands.Children.Add(settings);

        var tools = new WrapPanel();
        _plotPng = PlotButton("保存光谱 PNG", "file-export-image", () => TrackOperation(ExportSpectrumPngAsync));
        var widthMenu = new MenuFlyout();
        foreach (var (name, scale) in new[] { ("细", 1d), ("标准", 1.5), ("粗", 2d) })
        {
            var item = new MenuItem { Header = UiLocalization.Text(name), Tag = name, ToggleType = MenuItemToggleType.Radio, IsChecked = scale == 1.5 };
            item.Click += (_, _) => { _plot.SetCurveWidth(scale); foreach (var choice in widthMenu.Items.OfType<MenuItem>()) choice.IsChecked = ReferenceEquals(choice, item); };
            widthMenu.Items.Add(item);
        }
        widthMenu.Opening += (_, _) => { foreach (var item in widthMenu.Items.OfType<MenuItem>()) item.Header = UiLocalization.Text((string)item.Tag!); };
        var lineWidth = new DropDownButton { Content = "线宽", Flyout = widthMenu };
        lineWidth.Classes.Add("coating-plot-tool"); AutomationProperties.SetName(lineWidth, "光谱线宽");
        ToolTip.SetTip(lineWidth, "曲线线宽：细、标准、粗");
        tools.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { _plotPng, lineWidth } });
        _plotRefresh = PlotButton("刷新光谱", "action-recalculate", () => TrackOperation(() => RunAsync("evaluate")));
        tools.Children.Add(_plotRefresh);
        tools.Children.Add(PlotButton("重置光谱视图", "action-fit-view", () => { _plot.ResetView(); return Task.CompletedTask; }));
        var header = new StackPanel { Spacing = 2, Children = { commands, tools } };
        var curveStyle = new Style(s => s.OfType<ToggleButton>().Class("coating-curve"));
        curveStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(5, 2)));
        curveStyle.Setters.Add(new Setter(MinHeightProperty, 24d));
        curveStyle.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
        curveStyle.Setters.Add(new Setter(CornerRadiusProperty, new CornerRadius(4)));
        header.Styles.Add(curveStyle);
        var uncheckedStyle = new Style(s => s.OfType<ToggleButton>().Class("coating-curve").Not(x => x.Class(":checked")));
        uncheckedStyle.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
        uncheckedStyle.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension(ThemeResourceBindings.TextMuted)));
        header.Styles.Add(uncheckedStyle);
        var checkedStyle = new Style(s => s.OfType<ToggleButton>().Class("coating-curve").Class(":checked"));
        checkedStyle.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension(ThemeResourceBindings.SelectionBackground)));
        checkedStyle.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension(ThemeResourceBindings.TextPrimary)));
        header.Styles.Add(checkedStyle);
        var toolStyle = new Style(s => s.Is<Button>().Class("coating-plot-tool"));
        toolStyle.Setters.Add(new Setter(MinHeightProperty, 28d));
        toolStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(7, 4)));
        toolStyle.Setters.Add(new Setter(MarginProperty, new Thickness(0, 0, 4, 4)));
        header.Styles.Add(toolStyle);
        _plotStatus.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextMuted);
        _plotStatus.FontSize = DisplayTypography.Caption;
        _plot.StateChanged += (_, _) => UpdatePlotState();
        UpdatePlotState();
        return header;
    }

    private Button PlotButton(string name, string icon, Func<Task> action, string? text = null)
    {
        var button = new Button { Content = text is null ? ToolContent(icon, "") : ToolContent(icon, text) };
        button.Classes.Add("coating-plot-tool"); AutomationProperties.SetName(button, name); ToolTip.SetTip(button, name);
        button.Click += async (_, _) => await Guard(action); return button;
    }
    private void UpdatePlotState()
    {
        foreach (var (quantity, button) in _curveButtons)
        {
            button.IsEnabled = !_plot.OpticalDensity || quantity == CoatingQuantity.T;
            ToolTip.SetTip(button, button.IsEnabled ? button.Tag : "OD 只显示透射曲线，R/A 选择会保留。");
            AutomationProperties.SetHelpText(button, button.IsEnabled ? "" : "OD 只显示透射曲线，R/A 选择会保留。");
        }
        if (_plotPng is not null)
        {
            _plotPng.IsEnabled = _plot.HasData && !_plot.IsPreparing;
            ToolTip.SetTip(_plotPng, _plotPng.IsEnabled ? "保存光谱 PNG" : "请先计算当前输入的平均 / s / p 光谱");
            AutomationProperties.SetHelpText(_plotPng, _plotPng.IsEnabled ? "" : "请先计算当前输入的平均 / s / p 光谱");
        }
        if (_plotRefresh is not null) _plotRefresh.IsEnabled = !_busy;
        var polarization = _session.Current.Target.Polarization.ToString();
        if (polarization == "Unpolarized") polarization = UiLocalization.Text("平均");
        _plotStatus.Text = _plot.DisplayError ?? (_plot.IsPreparing ? UiLocalization.Text("正在计算平均 / s / p 显示光谱…")
            : $"{UiLocalization.Text("结果")} · {_plot.SampleCount:N0} {UiLocalization.Text("点")}    {string.Join(" / ", _plot.AnalysisOptions.AnglesDegrees.Select(a => a.ToString("G5") + "°"))} · {UiLocalization.Text("验收偏振")}：{_session.Current.Target.AngleDegrees:G5}° {polarization}    "
                + (_plot.OpticalDensity ? "OD ≤ 12（图形）" : "s — —   p · · ·"));
        _plotStatus.Text += "\n" + (_session.Current.Duplex is not null
            ? $"优化：{DesignService.OptimizationLabel(_session.Current)}；评价：{DesignService.EvaluationLabel(_session.Current)}（显示、优化与验收统一）"
            : UiLocalization.Text(_session.Current.StackGeometry is { IncludeBackSurface: true }
                ? "整块基板评价：含吸收、裸背面及多次反射；设计验收仍为前表面" : "前面单独评价"));
        if (_session.Current.ReferenceExample is not null)
            _plotStatus.Text += "\n" + UiLocalization.Text("参考示例 · 指标为本地比较目标");
        var options = _plot.AnalysisOptions;
        var notices = CoatingPresentation.AnalysisMaterials(_session.Current).Select(m => m.RangeNotice(options.MinimumNm, options.MaximumNm)).Where(n => n is not null);
        foreach (var notice in notices) _plotStatus.Text += "\n" + notice;
        ToolTip.SetTip(_plotStatus, UiLocalization.Text("深色底纹：材料域外延用（非实测）")
            + "\n评估使用独立的波段、步长和角度，不替代原设计的密集验收。");
    }
    private async Task ExportSpectrumPngAsync()
    {
        var bytes = _plot.CapturePng(); // Freeze the current view before the picker.
        string? path;
        if (_dialogs is not null) { var directory = await _dialogs.ExportDirectory(); path = directory is null ? null : System.IO.Path.Combine(directory, "coating-spectrum.png"); }
        else
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            { Title = "保存光谱 PNG", SuggestedFileName = "coating-spectrum.png", DefaultExtension = "png", FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }] });
            path = file?.TryGetLocalPath();
        }
        if (path is null) return;
        await BoundedFile.WriteAllBytesAtomicAsync(path, bytes, BoundedFile.MaximumImageDataBytes, "镀膜光谱 PNG");
        _status.Text = "光谱图已保存：" + path;
    }
}
