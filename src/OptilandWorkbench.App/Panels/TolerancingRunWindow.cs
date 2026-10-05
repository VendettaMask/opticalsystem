using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.App.Panels;

public sealed record TolerancingRunOptions(
    ToleranceAnalysisMode Mode,
    ToleranceCriterion Criterion,
    int MonteCarloRuns,
    int Seed,
    int CompensationIterations,
    int MaxDegreeOfParallelism,
    double YieldLimit,
    ToleranceDistribution? DistributionOverride,
    int WorstSensitivityCount,
    bool ShowMonteCarloTrials,
    double InverseValue,
    ToleranceMtfSettingsDto? MtfSettings = null,
    ToleranceCompensationAlgorithm CompensationAlgorithm = ToleranceCompensationAlgorithm.DampedLeastSquares,
    IReadOnlyList<ToleranceCompensatorDto>? AdditionalCompensators = null);

public sealed partial class TolerancingRunWindow : Window
{
    private readonly ComboBox _mode = Picker(
        0,
        "灵敏度",
        "反向极限",
        "反向增量",
        "跳过灵敏度（仅 Monte Carlo）");
    private readonly ComboBox _criterion = Picker(0, "RMS 点列半径", "RMS 波前误差", "指定频率 MTF");
    private readonly ComboBox _compensation = Picker(1, "无", "优化全部（DLS）", "优化全部（坐标模式搜索）");
    private readonly NumericUpDown _cycles = Number(3, 0, 500, 1);
    private readonly NumericUpDown _runs = Number(20, 0, 10_000, 20);
    private readonly NumericUpDown _seed = Number(1234, 0, 2_000_000_000, 1);
    private readonly NumericUpDown _cpuCount = Number(
        Math.Max(1, Environment.ProcessorCount),
        1,
        Math.Max(1, Environment.ProcessorCount),
        1);
    private readonly NumericUpDown _yieldLimit = Number(0, 0, 1_000_000, 0.01m);
    private readonly ComboBox _distribution = Picker(0, "使用公差数据编辑器定义", "正态（极限为 ±2σ）", "均匀");
    private readonly NumericUpDown _worstCount = Number(0, 0, 100_000, 1);
    private readonly CheckBox _showMonteCarlo = Check("在报告中列出 Monte Carlo 试验", true);
    private readonly NumericUpDown _inverseValue = Number(0.05m, 0.000000001m, 1_000_000, 0.001m);

    public TolerancingRunWindow(TolerancingRunOptions defaults,
        IReadOnlyList<FieldRowDto>? fields = null,
        IReadOnlyList<SurfaceRowDto>? surfaces = null,
        IReadOnlyList<WavelengthRowDto>? wavelengths = null)
    {
        Title = "公差分析";
        Width = 720;
        Height = 560;
        MinWidth = 480;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.BindThemeResource(Window.BackgroundProperty, ThemeResourceBindings.Workspace);

        InitializeMtfControls(fields, surfaces, wavelengths);
        Apply(defaults);
        _criterion.SelectionChanged += (_, _) => UpdateMtfControls();
        _mode.SelectionChanged += (_, _) => UpdateModeControls();
        _compensation.SelectionChanged += (_, _) =>
            UpdateCompensationControls();
        UpdateModeControls();

        var run = Button("运行");
        run.Classes.Add("accent");
        run.Click += (_, _) => SubmitOptions();
        var cancel = Button("取消");
        cancel.Click += (_, _) => Close(null);
        var reset = Button("重置");
        reset.Click += (_, _) => Apply(DefaultOptions());

        var tabs = new TabControl
        {
            ItemsSource = new object[]
            {
                new TabItem { Header = "设置", Content = SetupTab() },
                new TabItem { Header = "评价标准", Content = CriterionTab() },
                new TabItem { Header = "补偿", Content = CompensationTab() },
                new TabItem { Header = "Monte Carlo", Content = MonteCarloTab() },
                new TabItem { Header = "显示", Content = DisplayTab() }
            }
        };

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(12, 10),
            Children = { _settingsError, reset, run, cancel }
        };
        Grid.SetColumn(reset, 1);
        Grid.SetColumn(run, 2);
        Grid.SetColumn(cancel, 3);

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children = { tabs, footer }
        };
        Grid.SetRow(footer, 1);
        ThemeChrome.ApplyDialogDecoration(this);
    }

    public static TolerancingRunOptions DefaultOptions() => new(
        ToleranceAnalysisMode.Sensitivity,
        ToleranceCriterion.RmsSpotRadius,
        20,
        1234,
        3,
        Math.Max(1, Environment.ProcessorCount),
        0,
        null,
        0,
        true,
        0.05);

    private Control SetupTab() => Page(
        Section("分析方式",
            Row("模式", _mode),
            Row("反求极限 / 增量", _inverseValue),
            Row("并行 CPU 数", _cpuCount)),
        Note("反向极限：RMS 为上限，MTF 为下限。反向增量：RMS 允许增加，MTF 允许下降；逐视场模式按每个视场的名义值反求。满足目标的公差不会放宽。"));

    private Control CriterionTab() => Page(
        Section("评价标准", Row("标准", _criterion), Row(_acceptanceLabel, _yieldLimit)),
        MtfSection(), FieldLimitsSection(),
        Note("RMS 按上限验收，MTF 按下限验收。逐视场模式要求同一次补偿后的所有定义视场同时合格，包含权重为 0 的视场。"));

    private Control MonteCarloTab() => Page(
        Section("Monte Carlo",
            Row("运行次数", _runs),
            Row("随机种子", _seed),
            Row("统计模型", _distribution)),
        Note("每次试验同时施加全部启用公差。Zemax 默认正态模型的最小值到最大值覆盖 4σ，即中心到极限为 ±2σ。次数为 0 时不执行 Monte Carlo。"));

    private Control DisplayTab() => Page(
        Section("报告内容",
            Row("仅显示最严重的 N 项（0=全部）", _worstCount),
            _showMonteCarlo),
        Note("显示选项只控制报告内容，不改变公差计算。"));

    private TolerancingRunOptions BuildOptions() => new(
        _mode.SelectedIndex switch
        {
            1 => ToleranceAnalysisMode.InverseLimit,
            2 => ToleranceAnalysisMode.InverseIncrement,
            3 => ToleranceAnalysisMode.SkipSensitivity,
            _ => ToleranceAnalysisMode.Sensitivity
        },
        (ToleranceCriterion)_criterion.SelectedIndex,
        IntValue(_runs, 20),
        IntValue(_seed, 1234),
        _compensation.SelectedIndex == 0 ? 0 : IntValue(_cycles, 3),
        IntValue(_cpuCount, Math.Max(1, Environment.ProcessorCount)),
        DoubleValue(_yieldLimit, 0),
        _distribution.SelectedIndex switch
        {
            1 => ToleranceDistribution.Normal,
            2 => ToleranceDistribution.Uniform,
            _ => null
        },
        IntValue(_worstCount, 0),
        _showMonteCarlo.IsChecked == true,
        DoubleValue(_inverseValue, 0.05), BuildMtfSettings(),
        _compensation.SelectedIndex == 2 ? ToleranceCompensationAlgorithm.CoordinatePatternSearch : ToleranceCompensationAlgorithm.DampedLeastSquares,
        BuildAdditionalCompensators());

    private void Apply(TolerancingRunOptions options)
    {
        _mode.SelectedIndex = options.Mode switch
        {
            ToleranceAnalysisMode.InverseLimit => 1,
            ToleranceAnalysisMode.InverseIncrement => 2,
            ToleranceAnalysisMode.SkipSensitivity => 3,
            _ => 0
        };
        _criterion.SelectedIndex = (int)options.Criterion;
        _compensation.SelectedIndex = options.CompensationIterations <= 0 ? 0 : options.CompensationAlgorithm == ToleranceCompensationAlgorithm.CoordinatePatternSearch ? 2 : 1;
        _cycles.Value = options.CompensationIterations;
        UpdateCompensationControls();
        _runs.Value = options.MonteCarloRuns;
        _seed.Value = options.Seed;
        _cpuCount.Value = Math.Clamp(options.MaxDegreeOfParallelism, 1, Math.Max(1, Environment.ProcessorCount));
        _yieldLimit.Value = ToDecimal(options.YieldLimit);
        _distribution.SelectedIndex = options.DistributionOverride switch
        {
            ToleranceDistribution.Normal => 1,
            ToleranceDistribution.Uniform => 2,
            _ => 0
        };
        _worstCount.Value = options.WorstSensitivityCount;
        _showMonteCarlo.IsChecked = options.ShowMonteCarloTrials;
        _inverseValue.Value = ToDecimal(options.InverseValue);
        ApplyMtfSettings(options.MtfSettings, options.AdditionalCompensators);
        UpdateModeControls();
        UpdateMtfControls();
    }

    private void UpdateModeControls()
    {
        ControlAvailability.Set(_inverseValue, _mode.SelectedIndex is 1 or 2,
            "此值仅用于反向极限或反向增量模式。");
    }

    private void UpdateCompensationControls() =>
        ControlAvailability.Set(_cycles, _compensation.SelectedIndex > 0,
            "选择优化补偿后可设置补偿循环。");

    private static Control Page(params Control[] children)
    {
        var panel = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(16)
        };
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    private static Border Section(string title, params Control[] children)
    {
        var panel = new StackPanel
        {
            Spacing = 10,
            Children = { new TextBlock { Text = title, FontWeight = FontWeight.SemiBold } }
        };
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        var border = new Border { Padding = new Thickness(14), Child = panel };
        SettingsPanelChrome.ApplySurfaceCardStyle(border, shadow: false);
        return border;
    }

    private static Grid Row(string label, Control input) => Row(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, input);

    private static Grid Row(Control text, Control input)
    {
        text.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("180,*"),
            ColumnSpacing = 12,
            Children = { text, input }
        };
        Grid.SetColumn(input, 1);
        return grid;
    }

    private static TextBlock Note(string text)
    {
        var note = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        note.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        return note;
    }

    private static ComboBox Picker(int selectedIndex, params string[] items) => new()
    {
        ItemsSource = items,
        SelectedIndex = selectedIndex,
        MinWidth = 160,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static NumericUpDown Number(decimal value, decimal minimum, decimal maximum, decimal increment) => new()
    {
        Value = value,
        Minimum = minimum,
        Maximum = maximum,
        Increment = increment,
        MinWidth = 180,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static CheckBox Check(string text, bool value) => new()
    {
        Content = text,
        IsChecked = value
    };

    private static Button Button(string text) => new()
    {
        Content = text,
        MinWidth = 96
    };

    private static decimal ToDecimal(double value) =>
        (decimal)Math.Clamp(value, -1_000_000, 1_000_000);

    private static double DoubleValue(NumericUpDown input, double fallback) =>
        input.Value.HasValue ? decimal.ToDouble(input.Value.Value) : fallback;

    private static int IntValue(NumericUpDown input, int fallback) =>
        input.Value.HasValue ? decimal.ToInt32(input.Value.Value) : fallback;
}
