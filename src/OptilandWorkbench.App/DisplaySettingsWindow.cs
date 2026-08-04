using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App;

public sealed class DisplaySettingsWindow : Window
{
    private const string SystemDefaultFont = "系统默认";

    private readonly NumericUpDown _decimalPlaces = Number(0, 15, 1);
    private readonly NumericUpDown _upperExponent = Number(1, 15, 1);
    private readonly NumericUpDown _lowerExponent = Number(-15, -1, 1);
    private readonly ComboBox _theme = new() { MinWidth = 230 };
    private readonly ComboBox _fontFamily = new() { MinWidth = 230 };
    private readonly ComboBox _fontShape = new() { MinWidth = 150 };
    private readonly NumericUpDown _fontSize = Number(9, 32, 1);
    private readonly TextBlock _preview = new()
    {
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 82,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _validation = new()
    {
        MinHeight = 20
    };
    private readonly Button _save = new() { Content = "应用并保存", MinWidth = 108 };

    public DisplaySettingsWindow(AppSettings settings)
    {
        Title = "显示格式设置";
        Width = 560;
        Height = 630;
        MinWidth = 520;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        _validation.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextError);

        _decimalPlaces.Value = settings.DecimalPlaces;
        _upperExponent.Value = settings.UpperScientificExponent;
        _lowerExponent.Value = settings.LowerScientificExponent;
        _fontSize.Value = (decimal)settings.FontSize;

        var themes = new[]
        {
            new ThemeOption("Light", "普通模式"),
            new ThemeOption("Dark", "暗夜模式"),
            new ThemeOption("Isekai", "异世界"),
            new ThemeOption("System", "跟随系统")
        };
        _theme.ItemsSource = themes;
        _theme.SelectedItem = themes.First(option => option.Value == settings.Theme);

        var fontFamilies = FontManager.Current.SystemFonts
            .Select(font => font.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Prepend(SystemDefaultFont)
            .ToArray();
        _fontFamily.ItemsSource = fontFamilies;
        _fontFamily.SelectedItem = string.IsNullOrWhiteSpace(settings.FontFamily)
            ? SystemDefaultFont
            : fontFamilies.FirstOrDefault(name => string.Equals(
                name,
                settings.FontFamily,
                StringComparison.CurrentCultureIgnoreCase)) ?? SystemDefaultFont;

        var shapes = new[]
        {
            new FontShapeOption("Regular", "常规"),
            new FontShapeOption("Bold", "粗体"),
            new FontShapeOption("Italic", "斜体"),
            new FontShapeOption("BoldItalic", "粗斜体")
        };
        _fontShape.ItemsSource = shapes;
        _fontShape.SelectedItem = shapes.First(shape => shape.Value == settings.FontShape);

        var cancel = new Button { Content = "取消", MinWidth = 82 };
        var reset = new Button { Content = "恢复默认", MinWidth = 92 };
        cancel.Click += (_, _) => Close(false);
        reset.Click += (_, _) => ResetControls();
        _save.Click += (_, _) => Save(settings);

        Watch(_decimalPlaces);
        Watch(_upperExponent);
        Watch(_lowerExponent);
        Watch(_fontSize);
        _fontFamily.SelectionChanged += (_, _) => UpdatePreview();
        _fontShape.SelectionChanged += (_, _) => UpdatePreview();

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
            Margin = new Thickness(22),
            RowSpacing = 14,
            Children =
            {
                At(Section(
                    "主题",
                    SettingRow("界面主题", _theme, "切换普通模式、暗夜模式、异世界或跟随系统外观")), 0),
                At(Section(
                    "数字格式",
                    SettingRow("小数位数", _decimalPlaces, "普通与科学计数法尾数最多保留的位数"),
                    SettingRow("以上指数", _upperExponent, "数量级达到此指数时使用科学计数法"),
                    SettingRow("以下指数", _lowerExponent, "非零数量级低于或等于此指数时使用科学计数法")), 1),
                At(Section(
                    "界面字体",
                    SettingRow("字体", _fontFamily),
                    SettingRow("字形", _fontShape),
                    SettingRow("大小", _fontSize, "单位：pt")), 2),
                At(PreviewBox(new Border
                {
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(14),
                    Child = new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock { Text = "预览", FontWeight = FontWeight.SemiBold },
                            _preview,
                            _validation
                        }
                    }
                }), 3),
                At(new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
                    ColumnSpacing = 8,
                    Children =
                    {
                        InColumn(reset, 0),
                        InColumn(cancel, 2),
                        InColumn(_save, 3)
                    }
                }, 4)
            }
        };

        UpdatePreview();
    }

    private void Save(AppSettings settings)
    {
        if (!TryOptions(out var options))
        {
            return;
        }

        settings.DecimalPlaces = options.DecimalPlaces;
        settings.Theme = SelectedTheme();
        settings.UpperScientificExponent = options.UpperScientificExponent;
        settings.LowerScientificExponent = options.LowerScientificExponent;
        settings.FontFamily = SelectedFontFamily();
        settings.FontShape = SelectedFontShape();
        settings.FontSize = (double)(_fontSize.Value ?? (decimal)AppSettings.DefaultFontSize);
        settings.NormalizeDisplaySettings();
        settings.Save();
        Close(true);
    }

    private void ResetControls()
    {
        _decimalPlaces.Value = AppSettings.DefaultDecimalPlaces;
        _upperExponent.Value = AppSettings.DefaultUpperScientificExponent;
        _lowerExponent.Value = AppSettings.DefaultLowerScientificExponent;
        _theme.SelectedItem = (_theme.ItemsSource as IEnumerable<ThemeOption>)?
            .First(option => option.Value == AppSettings.DefaultTheme);
        _fontFamily.SelectedItem = SystemDefaultFont;
        _fontShape.SelectedIndex = 0;
        _fontSize.Value = (decimal)AppSettings.DefaultFontSize;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var valid = TryOptions(out var options);
        _save.IsEnabled = valid;
        if (!valid)
        {
            return;
        }

        _validation.Text = string.Empty;
        _preview.Text = string.Join("   ", new[]
        {
            NumericDisplayFormatter.Format(1234.567891, options),
            NumericDisplayFormatter.Format(0.0123456789, options),
            NumericDisplayFormatter.Format(12_345_678.9, options),
            NumericDisplayFormatter.Format(0.000000123456, options),
            "光学系统 Optical System"
        });
        _preview.FontFamily = string.IsNullOrWhiteSpace(SelectedFontFamily())
            ? Avalonia.Media.FontFamily.Default
            : new FontFamily(SelectedFontFamily());
        _preview.FontSize = (double)(_fontSize.Value ?? (decimal)AppSettings.DefaultFontSize);
        _preview.FontStyle = SelectedFontShape() is "Italic" or "BoldItalic"
            ? Avalonia.Media.FontStyle.Italic
            : Avalonia.Media.FontStyle.Normal;
        _preview.FontWeight = SelectedFontShape() is "Bold" or "BoldItalic"
            ? Avalonia.Media.FontWeight.Bold
            : Avalonia.Media.FontWeight.Normal;
    }

    private bool TryOptions(out NumericDisplayOptions options)
    {
        var decimals = (int)(_decimalPlaces.Value ?? AppSettings.DefaultDecimalPlaces);
        var upper = (int)(_upperExponent.Value ?? AppSettings.DefaultUpperScientificExponent);
        var lower = (int)(_lowerExponent.Value ?? AppSettings.DefaultLowerScientificExponent);
        options = new NumericDisplayOptions(decimals, upper, lower);
        if (lower >= upper)
        {
            _validation.Text = "“以下指数”必须小于“以上指数”。";
            return false;
        }

        return true;
    }

    private string SelectedFontFamily()
    {
        return _fontFamily.SelectedItem is string selected && selected != SystemDefaultFont
            ? selected
            : string.Empty;
    }

    private string SelectedTheme() =>
        (_theme.SelectedItem as ThemeOption)?.Value ?? AppSettings.DefaultTheme;

    private string SelectedFontShape()
    {
        return (_fontShape.SelectedItem as FontShapeOption)?.Value ?? "Regular";
    }

    private void Watch(NumericUpDown control)
    {
        control.ValueChanged += (_, _) => UpdatePreview();
    }

    private static NumericUpDown Number(decimal minimum, decimal maximum, decimal increment) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Increment = increment,
        Width = 130,
        HorizontalAlignment = HorizontalAlignment.Left
    };

    private static Control Section(string title, params Control[] controls)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = DisplayTypography.SectionTitle,
            FontWeight = FontWeight.SemiBold
        });
        foreach (var control in controls)
        {
            panel.Children.Add(control);
        }

        return panel;
    }

    private static Control SettingRow(string label, Control editor, string? hint = null)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("120,*"),
            RowDefinitions = new RowDefinitions(hint is null ? "Auto" : "Auto,Auto"),
            ColumnSpacing = 10
        };
        grid.Children.Add(InColumn(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center
        }, 0));
        grid.Children.Add(InColumn(editor, 1));
        if (hint is not null)
        {
            var text = new TextBlock
            {
                Text = hint,
                FontSize = DisplayTypography.RibbonText
            };
            text.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextMuted);
            Grid.SetColumn(text, 1);
            Grid.SetRow(text, 1);
            grid.Children.Add(text);
        }

        return grid;
    }

    private static T At<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    private static T InColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static Border PreviewBox(Border border)
    {
        SettingsPanelChrome.ApplySurfaceCardStyle(border, shadow: false);
        return border;
    }

    private sealed record FontShapeOption(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record ThemeOption(string Value, string Label)
    {
        public override string ToString() => Label;
    }
}
