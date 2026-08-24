using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Panels;

public sealed class ToleranceWizardWindow : Window
{
    private readonly IPrescriptionService _prescription;
    private readonly ComboBox _vendor = Picker(0, "Generic");
    private readonly ComboBox _preset = Picker(1, "商用", "精确", "高精密");
    private readonly NumericUpDown _startSurface = Number(1, 0, 100_000, 1);
    private readonly NumericUpDown _endSurface = Number(1, 0, 100_000, 1);
    private readonly CheckBox _radiusEnabled = Check("曲率半径", true);
    private readonly ComboBox _radiusMode = Picker(0, "固定值 (mm)", "半径百分比 (%)");
    private readonly NumericUpDown _radius = Number(0.05m, 0, 1_000_000, 0.01m);
    private readonly CheckBox _conicEnabled = Check("圆锥系数", false);
    private readonly NumericUpDown _conic = Number(0.02m, 0, 100, 0.001m);
    private readonly CheckBox _thicknessEnabled = Check("厚度和空气间隔", true);
    private readonly NumericUpDown _thickness = Number(0.05m, 0, 1_000_000, 0.01m);
    private readonly CheckBox _decenterEnabled = Check("元件偏心 X/Y", true);
    private readonly NumericUpDown _decenter = Number(0.02m, 0, 1_000_000, 0.005m);
    private readonly CheckBox _tiltEnabled = Check("元件倾斜 X/Y", true);
    private readonly NumericUpDown _tilt = Number(0.02m, 0, 180, 0.005m);
    private readonly CheckBox _indexEnabled = Check("折射率", true);
    private readonly NumericUpDown _index = Number(0.0002m, 0, 1, 0.0001m);
    private readonly CheckBox _abbeEnabled = Check("阿贝数", true);
    private readonly NumericUpDown _abbe = Number(0.2m, 0, 500, 0.1m);
    private readonly CheckBox _compensatorEnabled = Check("加入像面焦距补偿器", true);
    private readonly NumericUpDown _compensatorMinimum = Number(-2, -1_000_000, 1_000_000, 0.1m);
    private readonly NumericUpDown _compensatorMaximum = Number(2, -1_000_000, 1_000_000, 0.1m);
    private readonly ComboBox _distribution = Picker(0, "修正正态（±2σ）", "均匀");
    private readonly CheckBox _replaceExisting = Check("替换当前公差数据", true);
    private readonly TextBlock _preview = new() { TextWrapping = TextWrapping.Wrap };

    public ToleranceWizardWindow(IPrescriptionService prescription)
    {
        _prescription = prescription;
        Title = "公差数据编辑器";
        Width = 960;
        Height = 720;
        MinWidth = 640;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.BindThemeResource(Window.BackgroundProperty, ThemeResourceBindings.Workspace);
        _preview.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);

        var surfaces = _prescription.GetSurfaces();
        _endSurface.Maximum = Math.Max(0, surfaces.Count - 1);
        _startSurface.Maximum = Math.Max(0, surfaces.Count - 1);
        _startSurface.Value = Math.Min(1, Math.Max(0, surfaces.Count - 1));
        _endSurface.Value = Math.Max(0, surfaces.Count - 2);

        var ok = EditorButton("确定");
        ok.Click += (_, _) => Generate();
        var apply = EditorButton("应用");
        apply.Click += (_, _) => Generate();
        var cancel = EditorButton("取消");
        cancel.Click += (_, _) => Close(null);
        var reset = EditorButton("重置");
        reset.Click += (_, _) =>
        {
            _preset.SelectedIndex = 1;
            ApplyPreset(1);
        };

        var save = EditorButton("保存");
        save.IsEnabled = false;
        ToolTip.SetTip(save, "预设保存由公差数据编辑器统一处理。");
        var load = EditorButton("载入");
        load.IsEnabled = false;
        ToolTip.SetTip(load, "预设载入由公差数据编辑器统一处理。");

        var vendorLabel = Label("供应商");
        var precisionLabel = Label("精度等级");
        var choosePreset = EditorButton("选择预设");
        var presetGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,16,Auto,*,16,Auto"),
            RowDefinitions = new RowDefinitions("Auto"),
            Children = { vendorLabel, _vendor, precisionLabel, _preset, choosePreset }
        };
        Grid.SetColumn(_vendor, 1);
        Grid.SetColumn(precisionLabel, 3);
        Grid.SetColumn(_preset, 4);
        Grid.SetColumn(choosePreset, 6);
        var presetGroup = Group("公差预设", presetGrid);

        var surfaceGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,34,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
            ColumnSpacing = 18,
            RowSpacing = 7,
            Children =
            {
                ToleranceRow(_radiusEnabled, _radiusMode, _radius),
                ToleranceRow(_thicknessEnabled, UnitLabel("毫米"), _thickness),
                ToleranceRow(_conicEnabled, UnitLabel("系数"), _conic),
                DisabledToleranceRow("S + A 不规则度", "光圈"),
                DisabledToleranceRow("Zernike 不规则度", "光圈")
            }
        };
        Grid.SetRow(surfaceGrid.Children[1], 1);
        Grid.SetRow(surfaceGrid.Children[2], 2);
        Grid.SetColumn(surfaceGrid.Children[3], 2);
        Grid.SetColumn(surfaceGrid.Children[4], 2);
        Grid.SetRow(surfaceGrid.Children[4], 1);
        var surfaceToleranceGroup = Group("表面公差", surfaceGrid);

        var lower = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,1.1*"),
            ColumnSpacing = 8,
            Children =
            {
                Group("元件公差", new StackPanel
                {
                    Spacing = 7,
                    Children =
                    {
                        ToleranceRow(_decenterEnabled, UnitLabel("毫米"), _decenter),
                        ToleranceRow(_tiltEnabled, UnitLabel("度"), _tilt)
                    }
                }),
                Group("折射率公差", new StackPanel
                {
                    Spacing = 7,
                    Children =
                    {
                        ToleranceRow(_indexEnabled, UnitLabel(""), _index),
                        ToleranceRow(_abbeEnabled, UnitLabel("%"), _abbe)
                    }
                }),
                Group("选项", new StackPanel
                {
                    Spacing = 7,
                    Children =
                    {
                        FormRow("起始面", _startSurface),
                        FormRow("终止面", _endSurface),
                        FormRow("统计分布", _distribution),
                        FormRow("补偿最小", _compensatorMinimum),
                        FormRow("补偿最大", _compensatorMaximum),
                        _compensatorEnabled,
                        _replaceExisting
                    }
                })
            }
        };
        Grid.SetColumn(lower.Children[1], 1);
        Grid.SetColumn(lower.Children[2], 2);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6,
            Children = { ok, apply, cancel, save, load, reset, HelpButton() }
        };

        var rightContent = new StackPanel
        {
            Margin = new Thickness(14, 12, 18, 8),
            Spacing = 10,
            Children =
            {
                presetGroup,
                surfaceToleranceGroup,
                lower,
                new Border { Height = 1 },
                buttons
            }
        };
        rightContent.Children[3].BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Border);

        var editorContent = new ResponsiveTwoPaneGrid(
            Navigation(),
            new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = rightContent
            },
            "170,0,*",
            "Auto,0,*",
            breakpoint: 760);

        var editorGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children =
            {
                EditorHeader(),
                editorContent
            }
        };
        Grid.SetRow(editorGrid.Children[1], 1);

        var operandInner = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("36,130,*"),
            RowDefinitions = new RowDefinitions("28,32"),
            Children =
            {
                GridCell("", true),
                GridCell("类型", true),
                GridCell("标注", true),
                GridCell("1", false),
                GridCell("TOFF ▾", false),
                GridCell("", false)
            }
        };
        Grid.SetColumn(operandInner.Children[1], 1);
        Grid.SetColumn(operandInner.Children[2], 2);
        Grid.SetRow(operandInner.Children[3], 1);
        Grid.SetRow(operandInner.Children[4], 1);
        Grid.SetColumn(operandInner.Children[4], 1);
        Grid.SetRow(operandInner.Children[5], 1);
        Grid.SetColumn(operandInner.Children[5], 2);

        var operandGrid = new Border
        {
            Margin = new Thickness(0, 0, 14, 14),
            MinHeight = 72,
            Child = operandInner
        };
        operandGrid.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);

        var page = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children = { Toolbar(), editorGrid }
        };
        Grid.SetRow(editorGrid, 1);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children = { page, operandGrid }
        };
        Grid.SetRow(operandGrid, 1);
        Content = root;

        _preset.SelectionChanged += (_, _) => ApplyPreset(_preset.SelectedIndex);
        _startSurface.ValueChanged += (_, _) => UpdatePreview();
        _endSurface.ValueChanged += (_, _) => UpdatePreview();
        _radiusEnabled.IsCheckedChanged += (_, _) => UpdatePreview();
        _conicEnabled.IsCheckedChanged += (_, _) => UpdatePreview();
        _thicknessEnabled.IsCheckedChanged += (_, _) => UpdatePreview();
        _decenterEnabled.IsCheckedChanged += (_, _) => UpdatePreview();
        _tiltEnabled.IsCheckedChanged += (_, _) => UpdatePreview();
        _indexEnabled.IsCheckedChanged += (_, _) => UpdatePreview();
        _abbeEnabled.IsCheckedChanged += (_, _) => UpdatePreview();
        _compensatorEnabled.IsCheckedChanged += (_, _) => UpdatePreview();
        ApplyPreset(1);
    }

    private void Generate()
    {
        var start = IntegerValue(_startSurface, 1);
        var end = Math.Max(start, IntegerValue(_endSurface, start));
        Close(new ToleranceWizardSettingsDto(
            start,
            end,
            _radiusEnabled.IsChecked == true,
            _radiusMode.SelectedIndex == 1 ? RadiusToleranceMode.Percent : RadiusToleranceMode.Fixed,
            DoubleValue(_radius, 0.05),
            _thicknessEnabled.IsChecked == true,
            DoubleValue(_thickness, 0.05),
            _decenterEnabled.IsChecked == true,
            DoubleValue(_decenter, 0.02),
            _tiltEnabled.IsChecked == true,
            DoubleValue(_tilt, 0.02),
            _indexEnabled.IsChecked == true,
            DoubleValue(_index, 0.0002),
            _abbeEnabled.IsChecked == true,
            DoubleValue(_abbe, 0.2),
            _compensatorEnabled.IsChecked == true,
            DoubleValue(_compensatorMinimum, -2),
            DoubleValue(_compensatorMaximum, 2),
            _distribution.SelectedIndex == 1 ? ToleranceDistribution.Uniform : ToleranceDistribution.Normal,
            _replaceExisting.IsChecked == true,
            IncludeConic: _conicEnabled.IsChecked == true,
            ConicTolerance: DoubleValue(_conic, 0.02)));
    }

    private void ApplyPreset(int preset)
    {
        switch (preset)
        {
            case 0:
                SetValues(0.1m, 0.05m, 0.1m, 0.05m, 0.05m, 0.0005m, 0.5m);
                break;
            case 2:
                SetValues(0.01m, 0.005m, 0.02m, 0.005m, 0.005m, 0.0001m, 0.1m);
                break;
            default:
                SetValues(0.05m, 0.02m, 0.05m, 0.02m, 0.02m, 0.0002m, 0.2m);
                break;
        }

        UpdatePreview();
    }

    private void SetValues(
        decimal radius,
        decimal conic,
        decimal thickness,
        decimal decenter,
        decimal tilt,
        decimal index,
        decimal abbe)
    {
        _radius.Value = radius;
        _conic.Value = conic;
        _thickness.Value = thickness;
        _decenter.Value = decenter;
        _tilt.Value = tilt;
        _index.Value = index;
        _abbe.Value = abbe;
    }

    private void UpdatePreview()
    {
        var start = IntegerValue(_startSurface, 1);
        var end = Math.Max(start, IntegerValue(_endSurface, start));
        var count = Math.Max(0, end - start + 1);
        var perSurface = (_radiusEnabled.IsChecked == true ? 1 : 0)
            + (_conicEnabled.IsChecked == true ? 1 : 0)
            + (_thicknessEnabled.IsChecked == true ? 1 : 0)
            + (_decenterEnabled.IsChecked == true ? 2 : 0)
            + (_tiltEnabled.IsChecked == true ? 2 : 0);
        var material = (_indexEnabled.IsChecked == true ? 1 : 0)
            + (_abbeEnabled.IsChecked == true ? 1 : 0);
        var maximum = (count * (perSurface + material)) + (_compensatorEnabled.IsChecked == true ? 1 : 0);
        _preview.Text = $"表面范围：{start}–{end}。{Environment.NewLine}"
            + $"预计最多生成 {maximum} 行；平面会跳过 TRAD/TCON，空气面会跳过 TIND/TABB。{Environment.NewLine}"
            + $"统计分布：{(_distribution.SelectedIndex == 1 ? "均匀" : "正态，公差极限为 ±2σ")}。";
    }

    private static Border Toolbar()
    {
        var toolbar = new Border
        {
            Padding = new Thickness(8, 6),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                ItemSpacing = 6,
                LineSpacing = 6,
                Children =
                {
                    ToolButton("save", "保存", enabled: false),
                    ToolButton("folder-open", "载入", enabled: false),
                    ToolButton("check", "验证", enabled: false),
                    ToolButton("sparkles", "公差分析向导"),
                    ToolButton("rotate-ccw", "重置", enabled: false),
                    ToolButton("help-circle", "帮助", enabled: false)
                }
            }
        };
        toolbar.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        toolbar.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        return toolbar;
    }

    private static Button ToolButton(string icon, string text, bool enabled = true) => new()
    {
        Content = new LocalIconLabel(icon, text),
        IsEnabled = enabled,
        MinHeight = 30,
        Padding = new Thickness(8, 4)
    };

    private static Border EditorHeader()
    {
        var header = new Border
        {
            Padding = new Thickness(8, 6),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new Button { Content = "⌃", Width = 34, Height = 30, IsEnabled = false },
                    new TextBlock
                    {
                        Text = "操作数: 1  属性",
                        FontSize = DisplayTypography.Body,
                        FontWeight = FontWeight.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new Button { Content = "‹", Width = 34, Height = 30, IsEnabled = false },
                    new Button { Content = "›", Width = 34, Height = 30, IsEnabled = false }
                }
            }
        };
        header.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.SubtleSurface);
        header.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        return header;
    }

    private static Border Navigation()
    {
        var selected = new Border
        {
            Padding = new Thickness(10, 7),
            Child = new TextBlock
            {
                Text = "公差分析向导",
                FontSize = DisplayTypography.Body,
                FontWeight = FontWeight.SemiBold
            }
        };
        selected.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.RibbonHover);
        selected.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.RibbonHoverBorder);

        var nav = new Border
        {
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new StackPanel
            {
                Margin = new Thickness(0, 6, 0, 0),
                Children =
                {
                    new TextBlock
                    {
                        Text = "操作数: 1",
                        FontSize = DisplayTypography.Body,
                        Margin = new Thickness(10, 7)
                    },
                    selected
                }
            }
        };
        nav.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        nav.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        return nav;
    }

    private static Border Group(string title, Control content)
    {
        var group = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10, 9),
            ClipToBounds = true,
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = DisplayTypography.CardTitle,
                        FontWeight = FontWeight.SemiBold
                    },
                    content
                }
            }
        };
        group.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.SettingsSurface);
        group.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        return group;
    }

    private static Grid FormRow(string label, Control input)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("76,*"),
            ColumnSpacing = 6,
            Children = { Label(label), input }
        };
        Grid.SetColumn(input, 1);
        return row;
    }

    private static Grid ToleranceRow(CheckBox enabled, Control unit, Control value)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.45*,0.65*,*"),
            ColumnSpacing = 7,
            Children = { enabled, unit, value }
        };
        enabled.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(unit, 1);
        Grid.SetColumn(value, 2);
        return row;
    }

    private static Grid DisabledToleranceRow(string label, string unit)
    {
        var enabled = Check(label, false);
        enabled.IsEnabled = false;
        var value = Number(0.2m, 0, 1_000_000, 0.01m);
        value.IsEnabled = false;
        return ToleranceRow(enabled, UnitLabel(unit), value);
    }

    private static TextBlock Label(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = DisplayTypography.Body,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextPrimary);
        return label;
    }

    private static TextBlock UnitLabel(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = DisplayTypography.Body,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextSecondary);
        return label;
    }

    private static Button EditorButton(string text) => new()
    {
        Content = text,
        MinWidth = 88,
        MinHeight = 28,
        Padding = new Thickness(10, 3)
    };

    private static Button HelpButton() => new()
    {
        Content = "?",
        Width = 30,
        Height = 28,
        IsEnabled = false
    };

    private static Border GridCell(string text, bool isHeader)
    {
        var cell = new Border
        {
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 3),
            Child = new TextBlock
            {
                Text = text,
                FontSize = DisplayTypography.Body,
                FontWeight = isHeader ? FontWeight.SemiBold : FontWeight.Normal,
                TextAlignment = isHeader ? TextAlignment.Center : TextAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        cell.BindThemeResource(
            Border.BackgroundProperty,
            isHeader ? ThemeResourceBindings.SubtleSurface : ThemeResourceBindings.Surface);
        cell.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        return cell;
    }

    private static CheckBox Check(string text, bool value)
    {
        var content = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextPrimary);
        return new CheckBox
        {
            Content = content,
            IsChecked = value,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
    }

    private static ComboBox Picker(int selectedIndex, params string[] values) => new()
    {
        ItemsSource = values,
        SelectedIndex = selectedIndex,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static NumericUpDown Number(decimal value, decimal minimum, decimal maximum, decimal increment) => new()
    {
        Value = value,
        Minimum = minimum,
        Maximum = maximum,
        Increment = increment,
        ShowButtonSpinner = false,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static int IntegerValue(NumericUpDown input, int fallback) =>
        input.Value.HasValue ? decimal.ToInt32(input.Value.Value) : fallback;

    private static double DoubleValue(NumericUpDown input, double fallback) =>
        input.Value.HasValue ? decimal.ToDouble(input.Value.Value) : fallback;
}
