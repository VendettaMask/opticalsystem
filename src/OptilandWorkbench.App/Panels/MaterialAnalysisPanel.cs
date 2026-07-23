using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Panels;

public sealed class MaterialAnalysisPanel : UserControl
{
    private const string AllCatalogs = "所有材料库";

    private readonly IMaterialCatalogService _materials;
    private readonly MaterialAnalysisKind _kind;
    private readonly IReadOnlyList<GlassMaterialDto> _glasses;
    private readonly ComboBox _manufacturer = new() { MinWidth = 180, MinHeight = 34 };
    private readonly ComboBox _glass = new() { MinWidth = 230, MinHeight = 34 };
    private readonly NumericUpDown _thickness = new()
    {
        Width = 120,
        MinHeight = 34,
        Minimum = 0.01m,
        Maximum = 1000m,
        Increment = 1m,
        Value = 10m,
        FormatString = "0.###"
    };
    private readonly AnalysisPlotControl _plot = new()
    {
        MinWidth = 520,
        MinHeight = 380,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch
    };
    private readonly StackPanel _details = new() { Spacing = 8 };
    private readonly TextBlock _status = new()
    {
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };

    public MaterialAnalysisPanel(IMaterialCatalogService materials, MaterialAnalysisKind kind)
    {
        _materials = materials;
        _kind = kind;
        _glasses = materials.GetGlasses();

        _manufacturer.ItemsSource = new[] { AllCatalogs }
            .Concat(_glasses.Select(glass => glass.Manufacturer)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        _manufacturer.SelectedIndex = 0;
        _glass.DisplayMemberBinding = new Binding(nameof(GlassOption.Label));
        PopulateGlassOptions();

        var refresh = new Button
        {
            Content = new LocalIconLabel("refresh-cw", "重新计算"),
            MinWidth = 118,
            MinHeight = 34,
            Padding = new Thickness(12, 6)
        };
        refresh.Click += (_, _) => RefreshAnalysis();
        _manufacturer.SelectionChanged += (_, _) =>
        {
            PopulateGlassOptions();
            RefreshAnalysis();
        };
        _glass.SelectionChanged += (_, _) => RefreshAnalysis();
        _thickness.ValueChanged += (_, _) =>
        {
            if (_kind == MaterialAnalysisKind.InternalTransmission)
            {
                RefreshAnalysis();
            }
        };

        var toolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            MinHeight = 58,
            VerticalAlignment = VerticalAlignment.Center
        };
        toolbar.Children.Add(Field("材料库", _manufacturer));
        toolbar.Children.Add(Field(
            _kind is MaterialAnalysisKind.GlassMap
                or MaterialAnalysisKind.AthermalGlassMap
                ? "参考玻璃"
                : "玻璃",
            _glass));
        if (_kind == MaterialAnalysisKind.InternalTransmission)
        {
            toolbar.Children.Add(Field("厚度 (mm)", _thickness));
        }

        toolbar.Children.Add(new Border
        {
            Margin = new Thickness(8, 18, 8, 0),
            Child = refresh
        });
        toolbar.Children.Add(new Border
        {
            Margin = new Thickness(8, 18, 8, 0),
            MinWidth = 220,
            Child = _status
        });

        var detailsFrame = new Border
        {
            Width = 280,
            Padding = new Thickness(14),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = new ScrollViewer { Content = _details }
        };
        detailsFrame.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.SubtleSurface);
        detailsFrame.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,280") };
        Grid.SetColumn(_plot, 0);
        Grid.SetColumn(detailsFrame, 1);
        body.Children.Add(_plot);
        body.Children.Add(detailsFrame);

        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var toolbarFrame = new Border
        {
            Padding = new Thickness(12, 8, 12, 12),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = toolbar
        };
        toolbarFrame.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        toolbarFrame.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        Grid.SetRow(toolbarFrame, 0);
        Grid.SetRow(body, 1);
        content.Children.Add(toolbarFrame);
        content.Children.Add(body);

        Content = MaterialLibraryPanel.DatabasePage(
            Title(kind),
            Summary(kind),
            content);
        RefreshAnalysis();
    }

    private void PopulateGlassOptions()
    {
        var previous = (_glass.SelectedItem as GlassOption)?.QualifiedName;
        var manufacturer = SelectedManufacturer();
        var candidates = _glasses
            .Where(glass => manufacturer is null
                || glass.Manufacturer.Equals(manufacturer, StringComparison.OrdinalIgnoreCase))
            .Where(glass => _kind != MaterialAnalysisKind.InternalTransmission
                || glass.InternalTransmissionCount > 0)
            .Select(glass => new GlassOption(
                glass.Manufacturer,
                glass.Name,
                $"{glass.Manufacturer}: {glass.Name}"))
            .OrderBy(glass => glass.Manufacturer, StringComparer.OrdinalIgnoreCase)
            .ThenBy(glass => glass.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _glass.ItemsSource = candidates;
        _glass.SelectedItem = candidates.FirstOrDefault(glass =>
                glass.QualifiedName.Equals(previous, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(glass =>
                glass.Manufacturer.Equals("SCHOTT", StringComparison.OrdinalIgnoreCase)
                && glass.Name.Equals("N-BK7", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault();
    }

    private void RefreshAnalysis()
    {
        if (_glass.SelectedItem is not GlassOption selected)
        {
            _plot.Series = Array.Empty<AnalysisSeriesDto>();
            _details.Children.Clear();
            _status.Text = "当前范围没有适用的玻璃数据。";
            return;
        }

        try
        {
            var result = _materials.Analyze(new MaterialAnalysisRequestDto(
                _kind,
                SelectedManufacturer(),
                selected.QualifiedName,
                (double)(_thickness.Value ?? 10m)));
            _plot.PlotOptions = result.PlotOptions;
            _plot.Series = result.Series;
            UpdateDetails(result);
            _status.Text = result.Series.SelectMany(series => series.Points).Any()
                ? "分析已更新"
                : "所选玻璃缺少此分析所需的数据";
        }
        catch (Exception exception)
        {
            _plot.Series = Array.Empty<AnalysisSeriesDto>();
            _details.Children.Clear();
            _status.Text = $"分析失败：{exception.Message}";
        }
    }

    private void UpdateDetails(AnalysisViewDto result)
    {
        _details.Children.Clear();
        _details.Children.Add(new TextBlock
        {
            Text = "分析信息",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold
        });
        foreach (var row in result.Rows)
        {
            var label = new TextBlock { Text = row.Metric, FontSize = 11 };
            label.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
            _details.Children.Add(new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    label,
                    new TextBlock
                    {
                        Text = row.Value,
                        TextWrapping = TextWrapping.Wrap,
                        FontWeight = FontWeight.Medium
                    }
                }
            });
        }

        var note = new TextBlock
        {
            Text = result.ReportText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
            FontSize = 11
        };
        note.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        _details.Children.Add(note);
        _details.Children.Add(new TextBlock
        {
            Text = "操作：滚轮缩放，拖动平移，双击复位，悬停查看数据点。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11
        });
    }

    private string? SelectedManufacturer()
    {
        var selected = _manufacturer.SelectedItem as string;
        return string.IsNullOrWhiteSpace(selected) || selected == AllCatalogs
            ? null
            : selected;
    }

    private static Control Field(string label, Control input)
    {
        var labelControl = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 3)
        };
        labelControl.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        return new Border
        {
            Margin = new Thickness(8, 0),
            Child = new StackPanel
            {
                Spacing = 2,
                Children = { labelControl, input }
            }
        };
    }

    public static string Title(MaterialAnalysisKind kind) => kind switch
    {
        MaterialAnalysisKind.DispersionDiagram => "色散图",
        MaterialAnalysisKind.GlassMap => "玻璃图",
        MaterialAnalysisKind.AthermalGlassMap => "无热化玻璃图",
        MaterialAnalysisKind.InternalTransmission => "内部透过率 vs. 波长",
        MaterialAnalysisKind.DispersionVsWavelength => "色散 vs. 波长",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string Summary(MaterialAnalysisKind kind) => kind switch
    {
        MaterialAnalysisKind.DispersionDiagram => "查看所选玻璃的折射率随波长变化曲线",
        MaterialAnalysisKind.GlassMap => "按折射率和反向阿贝数坐标比较目录玻璃",
        MaterialAnalysisKind.AthermalGlassMap => "比较玻璃的色光焦和热光焦，辅助无热化消色差选材",
        MaterialAnalysisKind.InternalTransmission => "按指定材料厚度查看目录内部透过率数据",
        MaterialAnalysisKind.DispersionVsWavelength => "查看所选玻璃的色散 dn/dλ 随波长变化曲线",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private sealed record GlassOption(string Manufacturer, string Name, string Label)
    {
        public string QualifiedName => $"{Manufacturer}:{Name}";
    }
}
