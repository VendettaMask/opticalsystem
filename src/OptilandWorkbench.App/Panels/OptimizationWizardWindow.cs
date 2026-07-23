using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Panels;

public sealed class OptimizationWizardWindow : Window
{
    private readonly IPrescriptionService _prescription;
    private readonly IOptimizationService _optimization;
    private readonly ComboBox _quality = Picker(2, "波前", "对比度", "点列图", "角向");
    private readonly ComboBox _criterion = Picker(0, "RMS");
    private readonly ComboBox _reference = Picker(0, "质心", "主光线", "无参考");
    private readonly ComboBox _sampling = Picker(0, "高斯求积", "矩形阵列");
    private readonly NumericUpDown _spatialFrequency = Number(30, 0, 100_000, 1);
    private readonly NumericUpDown _xWeight = Number(1, 0, 1_000_000, 0.1m);
    private readonly NumericUpDown _yWeight = Number(1, 0, 1_000_000, 0.1m);
    private readonly NumericUpDown _rings = Number(3, 1, 20, 1);
    private readonly NumericUpDown _arms = Number(6, 3, 36, 1);
    private readonly NumericUpDown _obscuration = Number(0, 0, 0.95m, 0.05m);
    private readonly NumericUpDown _startRow = Number(1, 1, 100_000, 1);
    private readonly NumericUpDown _weightScale = Number(1, 0, 1_000_000, 0.1m);
    private readonly CheckBox _allWavelengths = new() { Content = "使用所有波长", IsChecked = true };
    private readonly CheckBox _ignoreLateralColor = new() { Content = "忽略垂轴色差", IsChecked = false };
    private readonly CheckBox _includeCommon = new() { Content = "加入常用约束", IsChecked = true };
    private readonly CheckBox _replaceExisting = new() { Content = "替换当前评价函数", IsChecked = true };
    private readonly Button _generate = new()
    {
        Content = new LocalIconLabel("sparkles", "生成评价函数"),
        MinWidth = 142,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };
    private readonly TextBlock _preview = new()
    {
        TextWrapping = TextWrapping.Wrap
    };

    public OptimizationWizardWindow(
        IPrescriptionService prescription,
        IOptimizationService optimization)
    {
        _prescription = prescription;
        _optimization = optimization;
        Title = "优化向导";
        Width = 920;
        Height = 650;
        MinWidth = 720;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.BindThemeResource(Window.BackgroundProperty, ThemeResourceBindings.Workspace);
        _preview.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);

        _generate.Click += (_, _) => Generate();
        var cancel = new Button { Content = "取消", MinWidth = 90 };
        cancel.Click += (_, _) => Close(false);
        var reset = new Button { Content = "重置", MinWidth = 90 };
        reset.Click += (_, _) => Reset();

        var columns = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 12,
            RowSpacing = 12
        };
        var functionCard = Card("优化函数", new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Labeled("成像质量", _quality),
                Labeled("空间频率", _spatialFrequency),
                Labeled("X 权重", _xWeight),
                Labeled("Y 权重", _yWeight),
                Labeled("类型", _criterion),
                Labeled("参考", _reference),
                Labeled("权重缩放", _weightScale),
                _allWavelengths,
                _ignoreLateralColor,
                new TextBlock
                {
                    Text = "当前目标为最佳名义性能。",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 115))
                }
            }
        });
        var samplingCard = Card("光瞳采样", new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Labeled("采样方式", _sampling),
                Labeled("环", _rings),
                Labeled("臂", _arms),
                Labeled("中心遮拦", _obscuration)
            }
        });
        Grid.SetColumn(samplingCard, 1);
        columns.Children.Add(functionCard);
        columns.Children.Add(samplingCard);

        var generationCard = Card("生成位置", new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Labeled("起始行", _startRow),
                _replaceExisting,
                _includeCommon
            }
        });
        Grid.SetRow(generationCard, 1);
        var previewCard = Card("向导摘要", _preview);
        Grid.SetRow(previewCard, 1);
        Grid.SetColumn(previewCard, 1);
        columns.Children.Add(generationCard);
        columns.Children.Add(previewCard);

        var footer = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(18, 12),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Children = { reset, cancel, _generate }
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
                    new TextBlock
                    {
                        Text = "优化向导与操作数",
                        FontSize = 22,
                        FontWeight = FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = "根据当前视场、波长和光瞳采样生成可直接执行的评价函数。",
                        Foreground = new SolidColorBrush(Color.FromRgb(99, 99, 102))
                    },
                    columns
                }
            }
        });
        Content = root;

        _quality.SelectionChanged += (_, _) => UpdateModeAndPreview();
        _criterion.SelectionChanged += (_, _) => UpdatePreview();
        _reference.SelectionChanged += (_, _) => UpdatePreview();
        _sampling.SelectionChanged += (_, _) =>
        {
            _arms.IsEnabled = _sampling.SelectedIndex == 0;
            UpdatePreview();
        };
        _rings.ValueChanged += (_, _) => UpdatePreview();
        _arms.ValueChanged += (_, _) => UpdatePreview();
        _obscuration.ValueChanged += (_, _) => UpdatePreview();
        _allWavelengths.IsCheckedChanged += (_, _) => UpdateModeAndPreview();
        _ignoreLateralColor.IsCheckedChanged += (_, _) => UpdatePreview();
        _includeCommon.IsCheckedChanged += (_, _) => UpdatePreview();
        _spatialFrequency.ValueChanged += (_, _) => UpdateModeAndPreview();
        _xWeight.ValueChanged += (_, _) => UpdateModeAndPreview();
        _yWeight.ValueChanged += (_, _) => UpdateModeAndPreview();
        UpdateModeAndPreview();
    }

    private void Generate()
    {
        var imageQuality = _quality.SelectedIndex switch
        {
            0 => OptimizationImageQuality.RmsWavefront,
            1 => OptimizationImageQuality.Contrast,
            2 => OptimizationImageQuality.RmsSpot,
            3 => OptimizationImageQuality.Angular,
            _ => OptimizationImageQuality.RmsSpot
        };

        _optimization.GenerateMeritFunction(new OptimizationWizardSettingsDto(
            imageQuality,
            _sampling.SelectedIndex == 1
                ? OptimizationPupilSampling.RectangularArray
                : OptimizationPupilSampling.GaussianQuadrature,
            IntegerValue(_rings, 3),
            IntegerValue(_arms, 6),
            DoubleValue(_obscuration, 0),
            IntegerValue(_startRow, 1),
            DoubleValue(_weightScale, 1),
            _allWavelengths.IsChecked == true,
            _includeCommon.IsChecked == true,
            _replaceExisting.IsChecked == true,
            _reference.SelectedIndex == 1
                ? OptimizationSpotReference.ChiefRay
                : _reference.SelectedIndex == 2
                    ? OptimizationSpotReference.Unreferenced
                    : OptimizationSpotReference.Centroid,
            DoubleValue(_spatialFrequency, 30),
            DoubleValue(_xWeight, 1),
            DoubleValue(_yWeight, 1),
            _ignoreLateralColor.IsChecked == true));
        Close(true);
    }

    private void UpdateModeAndPreview()
    {
        var isWavefront = _quality.SelectedIndex == 0;
        var isContrast = _quality.SelectedIndex == 1;
        var isSpot = _quality.SelectedIndex == 2;
        var isAngular = _quality.SelectedIndex == 3;
        _spatialFrequency.IsEnabled = isContrast;
        _xWeight.IsEnabled = isContrast || isSpot || isAngular;
        _yWeight.IsEnabled = isContrast || isSpot || isAngular;
        _reference.IsEnabled = isWavefront || isSpot || isAngular;
        if (!isWavefront && _reference.SelectedIndex == 2)
        {
            _reference.SelectedIndex = 0;
        }

        _ignoreLateralColor.IsEnabled = !isContrast && _allWavelengths.IsChecked == true;
        var contrastSettingsAreValid = DoubleValue(_spatialFrequency, 0) > 0
            && (DoubleValue(_xWeight, 0) > 0 || DoubleValue(_yWeight, 0) > 0);
        _generate.IsEnabled = !isContrast || contrastSettingsAreValid;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var fieldRows = _prescription.GetFields();
        var fields = Math.Max(1, fieldRows.Count);
        var wavelengths = _allWavelengths.IsChecked == true
            ? Math.Max(1, _prescription.GetWavelengths().Count)
            : 1;
        var common = _includeCommon.IsChecked == true ? 2 : 0;
        var variables = _prescription.GetSurfaces().Sum(surface =>
            (surface.RadiusVariable ? 1 : 0) + (surface.ThicknessVariable ? 1 : 0));
        var rings = IntegerValue(_rings, 3);
        var arms = IntegerValue(_arms, 6);
        var obscuration = DoubleValue(_obscuration, 0);
        var rectangularSamples = CountRectangularPupilSamples(rings, obscuration);
        var samplesByField = fieldRows.Count == 0
            ? new[] { _sampling.SelectedIndex == 1 ? rectangularSamples : rings }
            : fieldRows.Select(field => _sampling.SelectedIndex == 1
                ? rectangularSamples
                : (Math.Abs(field.X) <= 1e-12 && Math.Abs(field.Y) <= 1e-12
                    ? rings
                    : rings * Math.Max(1, arms / 2))).ToArray();
        var xWeight = DoubleValue(_xWeight, 0);
        var yWeight = DoubleValue(_yWeight, 0);
        var directionCount = xWeight <= 0 && yWeight <= 0
            ? 1
            : (xWeight > 0 ? 1 : 0) + (yWeight > 0 ? 1 : 0);
        var qualityOperands = samplesByField.Sum() * wavelengths * directionCount;
        var operands = 3 + fields + qualityOperands + common;
        var operandNames = _quality.SelectedIndex switch
        {
            0 => _reference.SelectedIndex switch
            {
                1 => "OPDM（主光线参考波前）",
                2 => "OPDC（无参考波前）",
                _ => "OPDX（质心参考波前）"
            },
            1 => "MECS / MECT（Moore-Elliott 对比度）",
            2 when directionCount == 1 && xWeight <= 0 && yWeight <= 0 =>
                _reference.SelectedIndex == 1 ? "TRAR（主光线参考径向像差）" : "TRAC（质心参考径向像差）",
            2 => _reference.SelectedIndex == 1 ? "TRAX / TRAY" : "TRCX / TRCY",
            3 when directionCount == 1 && xWeight <= 0 && yWeight <= 0 =>
                _reference.SelectedIndex == 1 ? "ANAR（主光线参考径向角差）" : "ANAC（质心参考径向角差）",
            3 => _reference.SelectedIndex == 1 ? "ANAX / ANAY" : "ANCX / ANCY",
            _ => string.Empty
        };
        var estimate = _quality.SelectedIndex == 1 ? "最多 " : string.Empty;
        _preview.Text = $"当前优化变量：{variables}。\n预计生成 {operands} 行评价函数。\n" +
                        $"当前组合：{QualityName()} · RMS · {ReferenceName()}。\n" +
                        $"操作数：{operandNames}。\n" +
                        $"每个视场/波长使用{estimate}{samplesByField.Min()}–{samplesByField.Max()} 个光瞳采样点。";
    }

    private void Reset()
    {
        _quality.SelectedIndex = 2;
        _criterion.SelectedIndex = 0;
        _reference.SelectedIndex = 0;
        _sampling.SelectedIndex = 0;
        _spatialFrequency.Value = 30;
        _xWeight.Value = 1;
        _yWeight.Value = 1;
        _rings.Value = 3;
        _arms.Value = 6;
        _obscuration.Value = 0;
        _startRow.Value = 1;
        _weightScale.Value = 1;
        _allWavelengths.IsChecked = true;
        _ignoreLateralColor.IsChecked = false;
        _includeCommon.IsChecked = true;
        _replaceExisting.IsChecked = true;
        UpdateModeAndPreview();
    }

    private string QualityName() => _quality.SelectedIndex switch
    {
        0 => "波前",
        1 => "对比度",
        2 => "点列图",
        3 => "角向",
        _ => "点列图"
    };

    private string ReferenceName() => _quality.SelectedIndex == 1
        ? "移位光线对"
        : _reference.SelectedIndex switch
        {
            1 => "主光线",
            2 => "无参考",
            _ => "质心"
        };

    private static Border Card(string title, Control content)
    {
        var card = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold },
                    content
                }
            }
        };
        card.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        card.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        return card;
    }

    private static StackPanel Labeled(string label, Control input)
    {
        input.HorizontalAlignment = HorizontalAlignment.Stretch;
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = label, FontWeight = FontWeight.SemiBold },
                input
            }
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

    private static int IntegerValue(NumericUpDown input, int fallback) => input.Value.HasValue
        ? decimal.ToInt32(input.Value.Value)
        : fallback;

    private static double DoubleValue(NumericUpDown input, double fallback) => input.Value.HasValue
        ? decimal.ToDouble(input.Value.Value)
        : fallback;

    private static int CountRectangularPupilSamples(int rings, double obscuration)
    {
        var side = (rings * 2) + 1;
        var count = 0;
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                var px = -1 + (2.0 * x / (side - 1));
                var py = -1 + (2.0 * y / (side - 1));
                var radiusSquared = (px * px) + (py * py);
                if (radiusSquared <= 1 + 1e-12
                    && radiusSquared >= (obscuration * obscuration) - 1e-12)
                {
                    count++;
                }
            }
        }

        return Math.Max(1, count);
    }
}
