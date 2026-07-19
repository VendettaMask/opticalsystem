using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;

namespace OptilandWorkbench.App.Panels;

public sealed class OptimizationWizardWindow : Window
{
    private readonly IPrescriptionService _prescription;
    private readonly IOptimizationService _optimization;
    private readonly ComboBox _quality = Picker("RMS 点列半径", "RMS 波前差");
    private readonly ComboBox _sampling = Picker("高斯求积", "矩形阵列");
    private readonly NumericUpDown _rings = Number(3, 1, 20, 1);
    private readonly NumericUpDown _arms = Number(6, 3, 36, 1);
    private readonly NumericUpDown _obscuration = Number(0, 0, 0.95m, 0.05m);
    private readonly NumericUpDown _startRow = Number(1, 1, 100_000, 1);
    private readonly NumericUpDown _weightScale = Number(1, 0, 1_000_000, 0.1m);
    private readonly CheckBox _allWavelengths = new() { Content = "使用所有波长", IsChecked = true };
    private readonly CheckBox _includeCommon = new() { Content = "加入常用约束", IsChecked = true };
    private readonly CheckBox _replaceExisting = new() { Content = "替换当前评价函数", IsChecked = true };
    private readonly TextBlock _preview = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(Color.FromRgb(99, 99, 102))
    };

    public OptimizationWizardWindow(
        IPrescriptionService prescription,
        IOptimizationService optimization)
    {
        _prescription = prescription;
        _optimization = optimization;
        Title = "优化向导";
        Width = 860;
        Height = 570;
        MinWidth = 720;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(245, 245, 247));

        var generate = new Button
        {
            Content = new LocalIconLabel("sparkles", "生成评价函数"),
            MinWidth = 142,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        generate.Click += (_, _) => Generate();
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
                Labeled("权重缩放", _weightScale),
                _allWavelengths,
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
            BorderBrush = new SolidColorBrush(Color.FromRgb(209, 209, 214)),
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

        _quality.SelectionChanged += (_, _) => UpdatePreview();
        _sampling.SelectionChanged += (_, _) =>
        {
            _arms.IsEnabled = _sampling.SelectedIndex == 0;
            UpdatePreview();
        };
        _rings.ValueChanged += (_, _) => UpdatePreview();
        _arms.ValueChanged += (_, _) => UpdatePreview();
        _obscuration.ValueChanged += (_, _) => UpdatePreview();
        _allWavelengths.IsCheckedChanged += (_, _) => UpdatePreview();
        _includeCommon.IsCheckedChanged += (_, _) => UpdatePreview();
        UpdatePreview();
    }

    private void Generate()
    {
        _optimization.GenerateMeritFunction(new OptimizationWizardSettingsDto(
            _quality.SelectedIndex == 1
                ? OptimizationImageQuality.RmsWavefront
                : OptimizationImageQuality.RmsSpot,
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
            _replaceExisting.IsChecked == true));
        Close(true);
    }

    private void UpdatePreview()
    {
        var fields = Math.Max(1, _prescription.GetFields().Count);
        var wavelengths = _allWavelengths.IsChecked == true
            ? Math.Max(1, _prescription.GetWavelengths().Count)
            : 1;
        var common = _includeCommon.IsChecked == true ? 2 : 0;
        var operands = 1 + (fields * wavelengths) + common;
        var variables = _prescription.GetSurfaces().Sum(surface =>
            (surface.RadiusVariable ? 1 : 0) + (surface.ThicknessVariable ? 1 : 0));
        var rings = IntegerValue(_rings, 3);
        var arms = IntegerValue(_arms, 6);
        var samples = _sampling.SelectedIndex == 1
            ? ((rings * 2) + 1) * ((rings * 2) + 1)
            : 1 + (arms * rings * (rings + 1) / 2);
        _preview.Text = $"当前优化变量：{variables}。\n预计生成 {operands} 行评价函数。\n" +
                        $"每个成像质量操作数最多使用约 {samples} 条光瞳采样光线。";
    }

    private void Reset()
    {
        _quality.SelectedIndex = 0;
        _sampling.SelectedIndex = 0;
        _rings.Value = 3;
        _arms.Value = 6;
        _obscuration.Value = 0;
        _startRow.Value = 1;
        _weightScale.Value = 1;
        _allWavelengths.IsChecked = true;
        _includeCommon.IsChecked = true;
        _replaceExisting.IsChecked = true;
        UpdatePreview();
    }

    private static Border Card(string title, Control content) => new()
    {
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(209, 209, 214)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
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

    private static ComboBox Picker(params string[] values) => new()
    {
        ItemsSource = values,
        SelectedIndex = 0,
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
}
