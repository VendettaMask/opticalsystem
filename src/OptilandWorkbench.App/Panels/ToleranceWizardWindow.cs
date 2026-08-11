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
    private readonly ComboBox _preset = Picker(1, "商用", "精密", "高精密");
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
    private readonly ComboBox _distribution = Picker(0, "正态（±值按 3σ）", "均匀");
    private readonly CheckBox _replaceExisting = Check("替换当前公差数据", true);
    private readonly TextBlock _preview = new() { TextWrapping = TextWrapping.Wrap };

    public ToleranceWizardWindow(IPrescriptionService prescription)
    {
        _prescription = prescription;
        Title = "公差向导";
        Width = 940;
        Height = 700;
        MinWidth = 760;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.BindThemeResource(Window.BackgroundProperty, ThemeResourceBindings.Workspace);
        _preview.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);

        var surfaces = _prescription.GetSurfaces();
        _endSurface.Maximum = Math.Max(0, surfaces.Count - 1);
        _startSurface.Maximum = Math.Max(0, surfaces.Count - 1);
        _startSurface.Value = Math.Min(1, Math.Max(0, surfaces.Count - 1));
        _endSurface.Value = Math.Max(0, surfaces.Count - 2);

        var generate = new Button
        {
            Content = new LocalIconLabel("sparkles", "生成公差"),
            MinWidth = 130
        };
        generate.Click += (_, _) => Generate();
        var cancel = new Button { Content = "取消", MinWidth = 90 };
        cancel.Click += (_, _) => Close(null);
        var reset = new Button { Content = "重置", MinWidth = 90 };
        reset.Click += (_, _) =>
        {
            _preset.SelectedIndex = 1;
            ApplyPreset(1);
        };

        var surfaceCard = Card("范围与统计", new StackPanel
        {
            Spacing = 9,
            Children =
            {
                Labeled("制造等级预设", _preset),
                Labeled("起始面", _startSurface),
                Labeled("终止面", _endSurface),
                Labeled("Monte Carlo 分布", _distribution),
                _replaceExisting
            }
        });
        var surfaceToleranceCard = Card("表面公差", new StackPanel
        {
            Spacing = 8,
            Children =
            {
                _radiusEnabled,
                Labeled("半径公差方式", _radiusMode),
                Labeled("半径公差", _radius),
                _conicEnabled,
                Labeled("圆锥系数公差", _conic),
                _thicknessEnabled,
                Labeled("厚度公差 (mm)", _thickness)
            }
        });
        var elementCard = Card("元件与材料公差", new StackPanel
        {
            Spacing = 8,
            Children =
            {
                _decenterEnabled,
                Labeled("偏心公差 (mm)", _decenter),
                _tiltEnabled,
                Labeled("倾斜公差 (deg)", _tilt),
                _indexEnabled,
                Labeled("折射率公差", _index),
                _abbeEnabled,
                Labeled("阿贝数公差", _abbe)
            }
        });
        var compensationCard = Card("补偿器", new StackPanel
        {
            Spacing = 8,
            Children =
            {
                _compensatorEnabled,
                new TextBlock
                {
                    Text = "默认使用最后一个面前的轴向间隔进行重新聚焦。",
                    TextWrapping = TextWrapping.Wrap
                },
                Labeled("最小补偿量 (mm)", _compensatorMinimum),
                Labeled("最大补偿量 (mm)", _compensatorMaximum)
            }
        });
        var previewCard = Card("向导摘要", _preview);

        var cards = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 12,
            RowSpacing = 12,
            Children = { surfaceCard, surfaceToleranceCard, elementCard, compensationCard }
        };
        Grid.SetColumn(surfaceToleranceCard, 1);
        Grid.SetRow(elementCard, 1);
        Grid.SetRow(compensationCard, 1);
        Grid.SetColumn(compensationCard, 1);

        var footer = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(18, 12),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Children = { reset, cancel, generate }
            }
        };
        footer.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        footer.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);

        var root = new DockPanel();
        DockPanel.SetDock(footer, Avalonia.Controls.Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = "公差向导", FontSize = DisplayTypography.PageTitle, FontWeight = FontWeight.SemiBold },
                    new TextBlock
                    {
                        Text = "为选定表面批量生成 Zemax 风格的 TDE 公差操作数，生成后仍可逐行编辑。",
                        TextWrapping = TextWrapping.Wrap
                    },
                    cards,
                    previewCard
                }
            }
        });
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
            + $"统计分布：{(_distribution.SelectedIndex == 1 ? "均匀" : "正态，公差极限按 ±3σ")}。";
    }

    private static Border Card(string title, Control content)
    {
        var card = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = title, FontSize = DisplayTypography.SectionTitle, FontWeight = FontWeight.SemiBold },
                    content
                }
            }
        };
        card.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        card.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        return card;
    }

    private static StackPanel Labeled(string label, Control input) => new()
    {
        Spacing = 3,
        Children =
        {
            new TextBlock { Text = label, FontWeight = FontWeight.SemiBold },
            input
        }
    };

    private static CheckBox Check(string text, bool value) => new()
    {
        Content = text,
        IsChecked = value
    };

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
