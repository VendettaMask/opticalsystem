using System.ComponentModel;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.App;

public sealed partial class MainWindow
{
    private readonly NumericUpDown _effectiveFocalLength = Number("FocalLength", "焦距 mm", 50, .1m, 100000, 1);
    private readonly NumericUpDown _fNumber = Number("FNumber", "F/#", 8, .1m, 1000, .1m);
    private readonly NumericUpDown _epd = Number("EntrancePupil", "入瞳直径 mm", 6.25m, .001m, 100000, .1m);
    private readonly ComboBox _apertureMode = Named(new ComboBox { ItemsSource = new[] { "F/#", "入瞳直径" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch }, "ApertureMode", "孔径输入方式");
    private readonly NumericUpDown _fieldAngle = Number("HalfField", "最大半视场 °", 5, 0, 89, .5m);
    private readonly NumericUpDown _minimumElements = Number("MinimumElements", "最少片数", 3, 1, 8, 1);
    private readonly NumericUpDown _maximumElements = Number("MaximumElements", "最多片数", 3, 1, 8, 1);
    private readonly NumericUpDown _maximumTrack = Number("MaximumTrack", "最大总长 mm", 100, .01m, 100000, 1);
    private readonly NumericUpDown _minimumCenter = Number("MinimumCenter", "最小中心厚度 mm", 2, .001m, 10000, .1m);
    private readonly NumericUpDown _minimumAir = Number("MinimumAir", "最小空气隙 mm", 1, .001m, 10000, .1m);
    private readonly NumericUpDown _minimumBack = Number("MinimumBack", "最小后焦 mm", 5, .001m, 100000, 1);
    private readonly NumericUpDown _minimumEdge = Number("MinimumEdge", "最小边厚 mm", .5m, 0, 10000, .1m);
    private readonly CheckBox _fixedBack = Named(new CheckBox { Content = "固定后焦距" }, "FixedBack", "固定后焦距");
    private readonly NumericUpDown _fixedBackValue = Number("FixedBackValue", "固定后焦 mm", 25, .001m, 100000, 1);
    private readonly NumericUpDown _rmsLimit = Number("RmsLimit", "最差 RMS 上限 mm", .05m, .000001m, 100, .01m);
    private readonly NumericUpDown _maximumSpotLimit = Number("SpotLimit", "最大光斑半径 mm", .15m, .000001m, 200, .01m);
    private readonly NumericUpDown _focalTolerance = Number("FocalTolerance", "焦距容差 %", 2, .0001m, 99, .1m);
    private readonly NumericUpDown _fNumberTolerance = Number("FNumberTolerance", "F/# 容差 %", 5, .0001m, 99, .1m);
    private readonly NumericUpDown _transmission = Number("TransmissionLimit", "最低通光 %", 98, .01m, 100, .1m);
    private readonly CheckBox _automaticDiameters = Named(new CheckBox { Content = "镜片直径随光线自动调整", IsChecked = true }, "AutomaticDiameters", "镜片直径随光线自动调整");
    private readonly NumericUpDown _apertureMargin = Number("ApertureMargin", "半口径裕量", 1.25m, 1, 100, .05m);
    private readonly NumericUpDown _seedCount = Number("RootCount", "起始试验上限", 24, 1, 128, 1);
    private readonly NumericUpDown _maximumEvaluations = Number("EvaluationBudget", "评价次数上限", 10000, 1, 100000, 100);
    private readonly ComboBox _searchMode = Named(new ComboBox { ItemsSource = new[] { "QUICK · 筛选后择优优化", "FULL · 逐形式完整优化" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch }, "SearchMode", "形式搜索方式");
    private readonly NumericUpDown _startingRadius = Number("StartingRadius", "起始半径 mm（0 自动）", 0, 0, 10000000, 10);
    private readonly NumericUpDown _startingCenter = Number("StartingCenter", "起始厚度 mm（0 最小值）", 0, 0, 100000, 1);
    private readonly NumericUpDown _startingAir = Number("StartingAir", "起始空气隙 mm（0 最小值）", 0, 0, 100000, 1);
    private readonly TextBox _signFilter = Named(new TextBox { Text = "", PlaceholderText = "如 +*-；空白搜索全部组合" }, "SignFilter", "起始形式筛选");
    private readonly NumericUpDown _stopSurface = Number("SearchStop", "起始光阑面（0 中间）", 0, 0, 16, 1);
    private readonly CheckBox _freeStop = Named(new CheckBox { Content = "继续探索其他光阑位置", IsChecked = true }, "FreeStop", "探索其他光阑位置");
    private readonly NumericUpDown _retainedForms = Number("RetainedForms", "保留方案数", 10, 1, 64, 1);
    private readonly NumericUpDown _parallelism = Number("Workers", "并行任务数", 2, 1, 4, 1);
    private readonly NumericUpDown _randomSeed = Number("RandomSeed", "随机种子", 101, 0, 1000000000, 1);
    private readonly NumericUpDown _minutes = Number("TimeLimit", "时间上限 min", 10, .01m, 1440, 1);
    private readonly NumericUpDown _refineBudget = Number("RefinementBudget", "追加细化次数", 500, 1, 100000, 100);
    private readonly TextBox _name = Named(new TextBox { Text = "定焦实验" }, "ExperimentName", "实验名称");
    private readonly TextBox _glasses = Named(new TextBox { Text = "N-BK7, N-F2, N-SF6", TextWrapping = Avalonia.Media.TextWrapping.Wrap }, "AllowedGlasses", "允许的目录玻璃");
    private readonly TextBox _initialGlass = Named(new TextBox { Text = "N-BK7" }, "InitialGlass", "起始玻璃");
    private readonly TextBox _catalogs = Named(new TextBox { PlaceholderText = "使用默认目录" }, "Catalogs", "优先玻璃目录");
    private readonly ObservableCollection<WavelengthRow> _wavelengths = [];
    private readonly DataGrid _wavelengthGrid = Named(new DataGrid
    {
        AutoGenerateColumns = false,
        IsReadOnly = false,
        Height = 145,
        CanUserSortColumns = false,
        SelectionMode = DataGridSelectionMode.Single
    }, "Wavelengths", "波长和权重");
    private InitialStructureSpecification _inputTemplate = new() { FlatStart = new() };
    private FlatStartSearchOptions _inputOptions = new();

    private Control BuildInputs()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 8, 0) };
        var systemPanel = panel;
        var goalsPanel = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 8, 0) };
        var searchPanel = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 8, 0) };
        panel.Children.Add(new TextBlock { Text = "SYSTEM · 系统定义", FontSize = 17, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        panel.Children.Add(Field("实验名称", _name, 276));
        panel.Children.Add(Pairs(Field("焦距 mm", _effectiveFocalLength), Field("最大半视场 °", _fieldAngle)));
        panel.Children.Add(Field("孔径定义", _apertureMode, 276));
        var fInput = Field("F/#", _fNumber, 276);
        var epdInput = Field("入瞳直径 mm", _epd, 276);
        epdInput.IsVisible = false;
        _apertureMode.SelectionChanged += (_, _) => { fInput.IsVisible = _apertureMode.SelectedIndex == 0; epdInput.IsVisible = !fInput.IsVisible; };
        panel.Children.Add(fInput); panel.Children.Add(epdInput);
        panel.Children.Add(Text("无限远物方 · 球面定焦系统；半视场为光轴到边缘的角度。"));

        _wavelengthGrid.ItemsSource = _wavelengths;
        _wavelengthGrid.Columns.Add(new DataGridTextColumn { Header = "波长 nm", Binding = new Binding(nameof(WavelengthRow.Nanometers)) { Mode = BindingMode.TwoWay }, Width = new DataGridLength(96) });
        _wavelengthGrid.Columns.Add(new DataGridTextColumn { Header = "权重", Binding = new Binding(nameof(WavelengthRow.Weight)) { Mode = BindingMode.TwoWay }, Width = new DataGridLength(70) });
        _wavelengthGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "主波长", Binding = new Binding(nameof(WavelengthRow.IsPrimary)) { Mode = BindingMode.TwoWay }, Width = new DataGridLength(84) });
        panel.Children.Add(Text("波长与权重")); panel.Children.Add(_wavelengthGrid);
        var add = Command("添加", "AddWave");
        var remove = Command("移除", "RemoveWave");
        var visible = Command("F / d / C", "VisibleSpectrum");
        add.Click += (_, _) => { if (_wavelengths.Count < 64) AddWave(new() { Nanometers = 550, Weight = 1 }); };
        remove.Click += (_, _) => { if (_wavelengthGrid.SelectedItem is WavelengthRow row) { _wavelengths.Remove(row); ParametersChanged(); } };
        visible.Click += (_, _) => SetWaves(new InitialStructureSpecification().Wavelengths);
        panel.Children.Add(Buttons(add, remove, visible));
        SetWaves([new() { Label = "d", Nanometers = 587.6, Weight = 1, IsPrimary = true }]);

        panel = goalsPanel;
        panel.Children.Add(new TextBlock { Text = "GOALS · 设计约束", FontSize = 17, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        panel.Children.Add(Pairs(Field("最少片数", _minimumElements), Field("最多片数", _maximumElements)));
        panel.Children.Add(Field("最大总长 mm", _maximumTrack, 276));
        panel = searchPanel;
        panel.Children.Add(new TextBlock { Text = "SEARCH · 形式搜索", FontSize = 17, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        panel.Children.Add(Field("搜索方式", _searchMode, 276));
        panel.Children.Add(Field("起始半径 mm（0 = 10 倍目标焦距）", _startingRadius, 276));
        panel.Children.Add(Pairs(Field("起始厚度 mm（0 自动）", _startingCenter), Field("起始空气隙 mm（0 自动）", _startingAir)));
        panel.Children.Add(Field("起始形式 + / - / *（固定片数时）", _signFilter, 276));
        panel.Children.Add(Field("起始光阑面（0 = 中间面）", _stopSurface, 276));
        panel.Children.Add(_freeStop);
        panel.Children.Add(Pairs(Field("起始试验上限", _seedCount), Field("保留方案数", _retainedForms)));
        panel.Children.Add(Pairs(Field("评价次数上限", _maximumEvaluations), Field("时间上限 min", _minutes)));
        panel.Children.Add(Text("枚举正负初始形式，再比较与优化候选。+ / - 指起始方向，优化中可改变。自动厚度与空气隙使用目标页中的最小值。"));

        var advanced = new StackPanel { Spacing = 10 };
        advanced.Children.Add(Pairs(Field("最小中心厚度 mm", _minimumCenter), Field("最小空气隙 mm", _minimumAir)));
        advanced.Children.Add(Pairs(Field("最小后焦 mm", _minimumBack), Field("最小边厚 mm", _minimumEdge)));
        advanced.Children.Add(_fixedBack);
        advanced.Children.Add(Field("固定后焦 mm", _fixedBackValue, 276));
        _fixedBackValue.IsEnabled = false;
        _fixedBack.IsCheckedChanged += (_, _) => _fixedBackValue.IsEnabled = _fixedBack.IsChecked == true;
        advanced.Children.Add(Field("允许的目录玻璃（逗号分隔）", _glasses, 276));
        advanced.Children.Add(Field("起始玻璃", _initialGlass, 276));
        advanced.Children.Add(Field("优先玻璃目录（逗号分隔）", _catalogs, 276));
        advanced.Children.Add(Pairs(Field("最差 RMS 上限 mm", _rmsLimit), Field("最大光斑半径 mm", _maximumSpotLimit)));
        advanced.Children.Add(Text("新实验按均匀照明圆瞳的面积 RMS 评价像质，独立加密验收；边缘光线必须全部通过。"));
        advanced.Children.Add(Pairs(Field("焦距容差 %", _focalTolerance), Field("F/# 容差 %", _fNumberTolerance)));
        advanced.Children.Add(_automaticDiameters);
        advanced.Children.Add(Text("自动口径按当前视场、光瞳和波长的真实光束包络计算，可增大也可缩小；同片前后面使用共同直径。"));
        advanced.Children.Add(Pairs(Field("最低通光 %", _transmission), Field("半口径裕量", _apertureMargin)));
        panel.Children.Add(Pairs(Field("并行任务数", _parallelism), Field("随机种子", _randomSeed)));
        panel.Children.Add(Field("追加细化次数", _refineBudget, 276));
        panel.Children.Add(Text("追加细化使用选中方案的原目标和新预算；不改变原实验。"));
        goalsPanel.Children.Add(Named(new Expander { Header = "几何、材料与验收", IsExpanded = true, Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch }, "Advanced", "几何、材料与验收"));
        var tabs = Named(new TabControl(), "InputTabs", "系统、目标与搜索设置");
        tabs.Items.Add(new TabItem { Header = "系统", Content = Scroll(systemPanel) });
        tabs.Items.Add(new TabItem { Header = "目标", Content = Scroll(goalsPanel) });
        tabs.Items.Add(new TabItem { Header = "搜索", Content = Scroll(searchPanel) });
        return tabs;

        static ScrollViewer Scroll(Control content) => new() { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    }

    private InitialStructureSpecification BuildSpecification()
    {
        if (!_wavelengthGrid.CommitEdit()) throw new ArgumentException("请修正波长或权重中的无效数值。");
        if (_wavelengths.Count == 0 || _wavelengths.Count(wave => wave.IsPrimary) != 1)
            throw new ArgumentException("请添加波长，并且只选择一个主波长。");
        var focal = Value(_effectiveFocalLength);
        if (Value(_minimumElements) > Value(_maximumElements)) throw new ArgumentException("最少片数不能大于最多片数。");
        return _inputTemplate with
        {
            Name = _name.Text?.Trim() ?? "",
            EffectiveFocalLengthMillimeters = focal,
            FNumber = _apertureMode.SelectedIndex == 1 ? focal / Value(_epd) : Value(_fNumber),
            MaximumFieldAngleDegrees = Value(_fieldAngle),
            MinimumElementCount = Integer(_minimumElements),
            MaximumElementCount = Integer(_maximumElements),
            MaximumTrackLengthMillimeters = Value(_maximumTrack),
            MinimumCenterThicknessMillimeters = Value(_minimumCenter),
            MinimumAirGapMillimeters = Value(_minimumAir),
            MinimumBackFocusMillimeters = Value(_minimumBack),
            MaximumRmsSpotRadiusMillimeters = Value(_rmsLimit),
            MaximumSpotRadiusMillimeters = Value(_maximumSpotLimit),
            SemiDiameterMarginFactor = Value(_apertureMargin),
            InitialGlass = _initialGlass.Text?.Trim() ?? "",
            GlassCatalogs = Names(_catalogs.Text),
            Wavelengths = _wavelengths.Select(wave => new WavelengthSpecification
            {
                Label = wave.Label,
                Nanometers = wave.Nanometers,
                Weight = wave.Weight,
                IsPrimary = wave.IsPrimary
            }).ToArray(),
            FlatStart = new()
            {
                SamplingPolicy = FlatStartSamplingPolicy.UniformAreaGaussianV1,
                AutomaticLensDiameters = _automaticDiameters.IsChecked == true,
                FixedBackFocusMillimeters = _fixedBack.IsChecked == true ? Value(_fixedBackValue) : null,
                MinimumEdgeThicknessMillimeters = Value(_minimumEdge),
                EffectiveFocalLengthRelativeTolerance = Value(_focalTolerance) / 100,
                FNumberRelativeTolerance = Value(_fNumberTolerance) / 100,
                MinimumValidRayFraction = Value(_transmission) / 100
            },
            Budget = _inputTemplate.Budget with
            {
                InitialSeedCount = Integer(_seedCount),
                MaximumEvaluations = Integer(_maximumEvaluations),
                MaximumParallelism = Integer(_parallelism),
                RandomSeed = Integer(_randomSeed),
                TimeLimit = TimeSpan.FromMinutes(Value(_minutes))
            }
        };
    }
    private FlatStartSearchOptions BuildOptions() => _inputOptions with
    {
        AllowedGlassNames = Names(_glasses.Text),
        MaximumDisplayedCandidates = Integer(_retainedForms),
        DesignSearch = new()
        {
            Mode = _searchMode.SelectedIndex == 1 ? DesignSearchMode.Full : DesignSearchMode.Quick,
            StartingRadiusMillimeters = Optional(_startingRadius),
            StartingCenterThicknessMillimeters = Optional(_startingCenter),
            StartingAirGapMillimeters = Optional(_startingAir),
            SignFilter = _signFilter.Text?.Trim() ?? "",
            StopSurfaceIndex = Integer(_stopSurface) is var stop && stop > 0 ? stop : null,
            ExploreStopPositions = _freeStop.IsChecked == true
        }
    };
    private static double? Optional(NumericUpDown control) => Value(control) is var value && value > 0 ? value : null;

    private void ApplySpecification(InitialStructureSpecification spec, FlatStartSearchOptions options)
    {
        if (spec.FlatStart is null) throw new ArgumentException("这份记录不是严格平板生成实验，请选择新的实验记录。");
        _inputTemplate = spec; _inputOptions = options;
        var search = options.DesignSearch ?? new();
        _searchMode.SelectedIndex = search.Mode == DesignSearchMode.Full ? 1 : 0;
        Set(_startingRadius, search.StartingRadiusMillimeters ?? 0);
        Set(_startingCenter, search.StartingCenterThicknessMillimeters ?? 0);
        Set(_startingAir, search.StartingAirGapMillimeters ?? 0);
        Set(_stopSurface, search.StopSurfaceIndex ?? 0);
        _freeStop.IsChecked = search.ExploreStopPositions;
        _signFilter.Text = search.SignFilter;
        Set(_retainedForms, options.MaximumDisplayedCandidates);
        _name.Text = spec.Name;
        Set(_effectiveFocalLength, spec.EffectiveFocalLengthMillimeters); Set(_fNumber, spec.FNumber);
        Set(_epd, spec.EffectiveFocalLengthMillimeters / spec.FNumber); _apertureMode.SelectedIndex = 0;
        Set(_fieldAngle, spec.MaximumFieldAngleDegrees); Set(_minimumElements, spec.MinimumElementCount); Set(_maximumElements, spec.MaximumElementCount);
        Set(_maximumTrack, spec.MaximumTrackLengthMillimeters); Set(_minimumCenter, spec.MinimumCenterThicknessMillimeters);
        Set(_minimumAir, spec.MinimumAirGapMillimeters); Set(_minimumBack, spec.MinimumBackFocusMillimeters);
        Set(_minimumEdge, spec.FlatStart.MinimumEdgeThicknessMillimeters);
        _automaticDiameters.IsChecked = spec.FlatStart.AutomaticLensDiameters;
        _fixedBack.IsChecked = spec.FlatStart.FixedBackFocusMillimeters.HasValue;
        Set(_fixedBackValue, spec.FlatStart.FixedBackFocusMillimeters ?? spec.MinimumBackFocusMillimeters);
        Set(_rmsLimit, spec.MaximumRmsSpotRadiusMillimeters); Set(_maximumSpotLimit, spec.MaximumSpotRadiusMillimeters);
        Set(_focalTolerance, spec.FlatStart.EffectiveFocalLengthRelativeTolerance * 100); Set(_fNumberTolerance, spec.FlatStart.FNumberRelativeTolerance * 100);
        Set(_transmission, spec.FlatStart.MinimumValidRayFraction * 100); Set(_apertureMargin, spec.SemiDiameterMarginFactor);
        _glasses.Text = string.Join(", ", options.AllowedGlassNames); _initialGlass.Text = spec.InitialGlass; _catalogs.Text = string.Join(", ", spec.GlassCatalogs);
        Set(_seedCount, spec.Budget.InitialSeedCount); Set(_maximumEvaluations, spec.Budget.MaximumEvaluations); Set(_parallelism, spec.Budget.MaximumParallelism);
        Set(_randomSeed, spec.Budget.RandomSeed); Set(_minutes, spec.Budget.TimeLimit.TotalMinutes);
        SetWaves(spec.Wavelengths);
    }
    private void SetWaves(IEnumerable<WavelengthSpecification> waves)
    {
        _wavelengths.Clear();
        foreach (var wave in waves) AddWave(new() { Label = wave.Label, Nanometers = wave.Nanometers, Weight = wave.Weight, IsPrimary = wave.IsPrimary });
    }
    private void AddWave(WavelengthRow row)
    {
        row.PropertyChanged += (_, args) =>
        {
            ParametersChanged();
            if (args.PropertyName == nameof(WavelengthRow.IsPrimary) && row.IsPrimary)
                foreach (var other in _wavelengths.Where(other => other != row)) other.IsPrimary = false;
        };
        _wavelengths.Add(row);
        ParametersChanged();
    }
    private static string[] Names(string? text) => (text ?? "").Split([',', ';', '，', '；', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static double Value(NumericUpDown input)
    {
        // Collapsed, untemplated advanced controls have a Value but no Text yet.
        if (input.Value is not { } value || input.Text is { } text
            && (!decimal.TryParse(text, input.ParsingNumberStyle, input.NumberFormat, out var parsed)
                || parsed < input.Minimum || parsed > input.Maximum))
            throw new ArgumentException($"请填写有效的{Avalonia.Automation.AutomationProperties.GetName(input)}数值。");
        return (double)value;
    }
    private static bool TextRepresentsCurrentValue(NumericUpDown input) => input.Text is null
        || input.Value is { } value && (input.Text == value.ToString(input.FormatString, input.NumberFormat)
            || decimal.TryParse(input.Text, input.ParsingNumberStyle, input.NumberFormat, out var parsed) && parsed == value);
    private static int Integer(NumericUpDown input)
    {
        var value = Value(input);
        if (value != Math.Truncate(value)) throw new ArgumentException("片数、结构数量、次数和随机种子必须是整数。");
        return checked((int)value);
    }
    private static void Set(NumericUpDown input, double value)
    {
        var number = checked((decimal)value);
        input.Minimum = Math.Min(input.Minimum, number); input.Maximum = Math.Max(input.Maximum, number);
        input.Value = number;
    }
    private static NumericUpDown Number(string id, string label, decimal value, decimal minimum, decimal maximum, decimal increment) =>
        Named(new SpecificationNumberInput
        {
            Value = value,
            Minimum = minimum,
            Maximum = maximum,
            Increment = increment,
            FormatString = "0.######",
            HorizontalAlignment = HorizontalAlignment.Stretch
        }, id, label);
    private static StackPanel Field(string label, Control control, double width = 132) => new()
    {
        Width = width,
        Spacing = 4,
        Margin = new Thickness(0, 0, 8, 4),
        Children = { Text(label), control }
    };
    private static WrapPanel Pairs(params Control[] controls)
    {
        var panel = new WrapPanel(); foreach (var control in controls) panel.Children.Add(control); return panel;
    }
}

internal sealed class WavelengthRow : INotifyPropertyChanged
{
    private double _nanometers = 587.6;
    private double _weight = 1;
    private bool _primary;
    public string Label { get; init; } = "";
    public double Nanometers { get => _nanometers; set { _nanometers = value; PropertyChanged?.Invoke(this, new(nameof(Nanometers))); } }
    public double Weight { get => _weight; set { _weight = value; PropertyChanged?.Invoke(this, new(nameof(Weight))); } }
    public bool IsPrimary { get => _primary; set { if (_primary == value) return; _primary = value; PropertyChanged?.Invoke(this, new(nameof(IsPrimary))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}
