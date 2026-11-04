using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.LogicalTree;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Optimization;
using NscDomain = OpticalSystemDesign.Core.NonSequential.NonSequentialVariableDomain;
using DockSide = Avalonia.Controls.Dock;

namespace OpticalSystemDesign.App.Panels;

/// <summary>Desktop entry to shared, immutable NSC design and manufacturing evaluations.</summary>
internal sealed class NonSequentialDesignWindow : Window
{
    private readonly INonSequentialDocumentService _service;
    private readonly NonSequentialDocumentDto _document;
    private readonly string _identity;
    private readonly StackPanel _variables = new() { Spacing = 8 }, _targets = new() { Spacing = 8 };
    private readonly List<VariableRow> _variableRows = [];
    private readonly List<TargetRow> _targetRows = [];
    private readonly ComboBox _optimizer = new() { ItemsSource = OptimizerCatalog.Names, SelectedIndex = 0, MinWidth = 210 };
    private readonly TextBox _iterations = Box("50"), _evaluations = Box("200"), _budget = Box("1000000"),
        _rays = Box("1000"), _training = Box("1"), _validation = Box("104730"), _samples = Box("100"), _manufacturing = Box("42"),
        _compensation = Box("8"), _truncated = Box("0.001");
    private readonly TextBox _results = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Button _apply = new() { Content = "应用通过验证的候选", IsEnabled = false };
    private readonly List<Control> _editable = [];
    private CancellationTokenSource? _cancellation;
    private NonSequentialDesignResult? _candidate;
    private object? _lastResult;
    private NonSequentialDesignExperiment? _lastPlan;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    internal NonSequentialDesignWindow(INonSequentialDocumentService service)
    {
        _service = service; _document = service.GetDocument(); _identity = service.GetDesignSceneIdentity();
        Title = "非序列优化与公差"; Width = 1220; Height = 820; MinWidth = 800; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var optimize = Command("运行优化", () => RunAsync(false)); var tolerance = Command("运行公差", () => RunAsync(true));
        var save = Command("保存方案", SavePlanAsync); var open = Command("读取方案", OpenPlanAsync);
        var export = Command("导出结果", ExportAsync);
        var archive = Command("保存完整实验", SaveExperimentAsync);
        var replay = Command("重放实验", ReplayExperimentAsync);
        var cancel = new Button { Content = "取消" }; cancel.Click += (_, _) => _cancellation?.Cancel();
        _apply.Click += (_, _) =>
        {
            try
            {
                if (_candidate is not null)
                {
                    if (_lastPlan is null || JsonSerializer.Serialize(ReadPlan()) != JsonSerializer.Serialize(_lastPlan))
                        throw new InvalidOperationException("设计输入已改变，请重新运行后再应用候选。");
                    _service.ApplyDesignResult(_candidate); _apply.IsEnabled = false;
                    _status.Text = "候选已应用，可在主窗口撤销。重新打开设计页可使用更新后的场景。";
                }
            }
            catch (Exception e) { _status.Text = e.Message; }
        };
        var bar = new WrapPanel { Orientation = Orientation.Horizontal, Children = { optimize, tolerance, cancel, save, open, export, archive, replay, _apply } };
        var addVariable = Command("添加变量", () => { AddVariable(); return Task.CompletedTask; });
        var addTarget = Command("添加目标", () => { AddTarget(); return Task.CompletedTask; });
        var settings = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var pair in new (string, Control)[] { ("算法", _optimizer), ("最大迭代", _iterations), ("总追迹次数", _evaluations),
            ("总源光线预算", _budget), ("每源射线数", _rays), ("训练种子", _training), ("验证种子", _validation),
            ("截断功率上限", _truncated), ("公差样本数", _samples), ("制造种子", _manufacturing), ("每样本补偿追迹上限", _compensation) })
        { settings.Children.Add(Label(pair.Item1, pair.Item2)); _editable.Add(pair.Item2); }
        var tabs = new TabControl { ItemsSource = new[]
        {
            new TabItem { Header = "变量与补偿", Content = new DockPanel { Children =
                { Top(addVariable), new ScrollViewer { Content = _variables, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto } } } },
            new TabItem { Header = "探测器目标", Content = new DockPanel { Children =
                { Top(addTarget), new ScrollViewer { Content = _targets, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto } } } },
            new TabItem { Header = "结果", Content = _results }
        } };
        var root = new DockPanel { Margin = new Thickness(12), LastChildFill = true };
        root.Children.Add(Top(new StackPanel { Spacing = 8, Children = { bar, settings,
            new TextBlock { Text = "位置与光斑尺寸使用实际光线落点。优化后用独立种子验证；公差保留全部制造抽样和失败原因。所有追迹与补偿共用总预算。", TextWrapping = Avalonia.Media.TextWrapping.Wrap } } }));
        DockPanel.SetDock(_status, DockSide.Bottom); root.Children.Add(_status); root.Children.Add(tabs); Content = root;
        _editable.Add(addVariable); _editable.Add(addTarget); _editable.Add(optimize); _editable.Add(tolerance); _editable.Add(open);
        _editable.Add(archive); _editable.Add(replay);
        AddVariable(); AddTarget();
        Closed += (_, _) => _cancellation?.Cancel(); ThemeChrome.ApplyDialogDecoration(this);
    }

    private void AddVariable(NonSequentialDesignVariable? variable = null, NonSequentialToleranceVariable? tolerance = null, bool compensator = false)
    {
        if (_variableRows.Count >= 64) throw new InvalidOperationException("最多 64 个变量。");
        var row = new VariableRow(_document, variable, tolerance, compensator);
        var remove = new Button { Content = "删除" }; var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        line.Children.Add(row.View); line.Children.Add(remove); _variableRows.Add(row); _variables.Children.Add(line);
        remove.Click += (_, _) => { _variableRows.Remove(row); _variables.Children.Remove(line); };
    }
    private void AddTarget(NonSequentialDesignTarget? target = null)
    {
        if (_targetRows.Count >= 64) throw new InvalidOperationException("最多 64 个目标。");
        var row = new TargetRow(_document, target); var remove = new Button { Content = "删除" };
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { row.View, remove } };
        _targetRows.Add(row); _targets.Children.Add(line);
        remove.Click += (_, _) => { _targetRows.Remove(row); _targets.Children.Remove(line); };
    }
    private NonSequentialDesignExperiment ReadPlan()
    {
        if (_service.GetDesignSceneIdentity() != _identity) throw new InvalidOperationException("场景或材料已改变，请重新打开设计页。");
        var settings = new NonSequentialDesignSettings((string)_optimizer.SelectedItem!, Int(_iterations), Int(_evaluations),
            long.Parse(_budget.Text!, CultureInfo.InvariantCulture), string.IsNullOrWhiteSpace(_rays.Text) ? null : Int(_rays), Int(_training), Int(_validation), Number(_truncated));
        return new(1, _identity, _variableRows.Where(r => !r.Compensator).Select(r => r.Read()).ToArray(),
            _targetRows.Select(r => r.Read()).ToArray(), settings,
            _variableRows.Where(r => r.Toleranced).Select(r => r.ReadTolerance()).ToArray(),
            new(Int(_samples), Int(_manufacturing), settings, Int(_compensation)),
            _variableRows.Where(r => r.Compensator).Select(r => r.Read()).ToArray());
    }
    private async Task RunAsync(bool tolerance)
    {
        if (_cancellation is not null) return;
        try
        {
            var plan = ReadPlan(); using var cancel = new CancellationTokenSource(); _cancellation = cancel;
            SetBusy(true); _apply.IsEnabled = false;
            _candidate = null; _lastResult = null; _lastPlan = plan;
            var progress = new Progress<NonSequentialDesignProgress>(p => _status.Text = $"{p.Phase} · {p.TraceEvaluations} 次追迹 · {p.LaunchedSourceRays:N0} 条源光线");
            if (tolerance)
            {
                var result = await _service.ToleranceAsync(plan.Tolerances!, plan.Targets, plan.ToleranceSettings!, plan.Compensators, progress, cancel.Token);
                _lastResult = result; _results.Text = JsonSerializer.Serialize(result, Json);
                _status.Text = $"公差完成：{result.Samples.Count} 个样本，合格率 {(result.Yield is { } y ? y.ToString("P2", CultureInfo.InvariantCulture) : "未抽样")}；失败样本计入分母。";
            }
            else
            {
                var result = await _service.OptimizeAsync(plan.Variables, plan.Targets, plan.Settings, progress, cancel.Token);
                _lastResult = result; _candidate = result; _apply.IsEnabled = result.Passed;
                _results.Text = string.Join(Environment.NewLine, result.ValidationMetrics.Select(m => $"{m.Definition.Metric}: {m.Value:G10} {m.Unit}，目标 {m.Definition.Target:G10} ± {m.Definition.Tolerance:G6}，{(m.Passed ? "通过" : "未通过")}"))
                    + $"\n停止原因：{result.Optimizer.StopReason} {result.Optimizer.Message}\n" + JsonSerializer.Serialize(result.Cost, Json);
                _status.Text = result.Passed ? "候选通过训练和独立种子验证，可以应用。" : "候选未通过全部目标；已保留实际结果。";
            }
        }
        catch (OperationCanceledException) { _status.Text = "计算已取消，当前场景未改变。"; }
        catch (Exception e) { _status.Text = e.Message; }
        finally { _cancellation = null; SetBusy(false); }
    }
    private void SetBusy(bool busy)
    {
        foreach (var c in _editable) c.IsEnabled = !busy;
        foreach (var c in _variables.GetLogicalDescendants().Concat(_targets.GetLogicalDescendants()).OfType<Control>())
            if (c is TextBox or ComboBox or CheckBox or Button) c.IsEnabled = !busy;
    }
    private async Task SaveExperimentAsync()
    {
        if (_lastPlan is null || _lastResult is null) throw new InvalidOperationException("请先完成一次优化或公差计算。");
        var file = await StorageProvider.SaveFilePickerAsync(new() { Title = "保存完整非序列实验", SuggestedFileName = "nsc-design.nsc-experiment.zip", DefaultExtension = "zip" });
        if (file is null) return;
        await _service.SaveDesignExperimentAsync(file.Path.LocalPath, _lastPlan, _lastResult as NonSequentialDesignResult, _lastResult as NonSequentialToleranceResult);
        _status.Text = "完整实验已保存，包含源场景、材料、几何、设置和结果，可独立重放。";
    }
    internal async Task ReplayExperimentFromPathAsync(string path)
    {
        if (_cancellation is not null) return;
        using var cancel = new CancellationTokenSource(); _cancellation = cancel; SetBusy(true); _apply.IsEnabled = false;
        try
        {
            var progress = new Progress<NonSequentialDesignProgress>(p => _status.Text = $"实验重放：{p.Phase} · {p.TraceEvaluations} 次追迹");
            var replay = await _service.ReplayDesignExperimentAsync(path, progress, cancel.Token);
            _candidate = null; _lastPlan = replay.Archive.Plan; _lastResult = (object?)replay.Optimization ?? replay.Tolerance;
            _results.Text = JsonSerializer.Serialize(replay.Optimization is { } o ? (object)NonSequentialOptimizationRecord.From(o) : replay.Tolerance, Json);
            _status.Text = replay.MatchesRecorded switch { true => "重放完成：结果与归档逐值一致。当前场景未改变。", false => "重放完成：结果与归档存在差异，未改写原记录。", _ => "重放完成：归档没有旧结果可对照。当前场景未改变。" };
        }
        catch (OperationCanceledException) { _status.Text = "实验重放已取消，当前场景未改变。"; }
        finally { _cancellation = null; SetBusy(false); }
    }
    private async Task ReplayExperimentAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "重放完整非序列实验", AllowMultiple = false });
        if (files.Count > 0) await ReplayExperimentFromPathAsync(files[0].Path.LocalPath);
    }
    private async Task SavePlanAsync()
    {
        var plan = ReadPlan(); var file = await SaveAsync("非序列设计方案", "nsc-design.json");
        if (file is not null) await File.WriteAllTextAsync(file, JsonSerializer.Serialize(plan, Json));
    }
    private async Task OpenPlanAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "读取非序列设计方案", AllowMultiple = false });
        if (files.Count == 0) return; var path = files[0].Path.LocalPath;
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("方案文件过大。");
        var p = JsonSerializer.Deserialize<NonSequentialDesignExperiment>(await File.ReadAllTextAsync(path)) ?? throw new InvalidDataException("方案为空。");
        if (p.FormatVersion != 1 || p.SourceSceneSha256 != _identity || _service.GetDesignSceneIdentity() != _identity)
            throw new InvalidDataException("方案版本不受支持或原始场景/材料不一致；请先打开对应项目。");
        if (p.Variables.Count + (p.Compensators?.Count ?? 0) > 64 || p.Targets.Count > 64) throw new InvalidDataException("方案超过变量/目标数量限制。");
        var rows = p.Variables.Select(v => (v, t: p.Tolerances?.FirstOrDefault(t => t.Variable == v), comp: false))
            .Concat((p.Compensators ?? []).Select(v => (v, t: p.Tolerances?.FirstOrDefault(t => t.Variable == v), comp: true))).ToArray();
        _variableRows.Clear(); _variables.Children.Clear(); _targetRows.Clear(); _targets.Children.Clear();
        foreach (var r in rows) AddVariable(r.v, r.t, r.comp); foreach (var t in p.Targets) AddTarget(t);
        _optimizer.SelectedItem = p.Settings.Optimizer; _iterations.Text = p.Settings.MaximumIterations.ToString(); _evaluations.Text = p.Settings.MaximumTraceEvaluations.ToString();
        _budget.Text = p.Settings.MaximumLaunchedRays.ToString(); _rays.Text = p.Settings.RayCountOverride?.ToString() ?? "";
        _training.Text = p.Settings.TrainingSeed.ToString(); _validation.Text = p.Settings.ValidationSeed.ToString(); _truncated.Text = F(p.Settings.MaximumTruncatedFraction);
        _samples.Text = (p.ToleranceSettings?.Samples ?? 100).ToString(); _manufacturing.Text = (p.ToleranceSettings?.ManufacturingSeed ?? 1).ToString();
        _compensation.Text = (p.ToleranceSettings?.CompensationTraceEvaluations ?? 8).ToString();
        _candidate = null; _lastResult = null; _lastPlan = null; _apply.IsEnabled = false; _results.Text = "";
        _status.Text = "方案已读取，场景与材料身份一致。";
    }
    private async Task ExportAsync()
    {
        if (_lastResult is null) throw new InvalidOperationException("请先完成计算。");
        var file = await SaveAsync("导出非序列结果", "nsc-results.json");
        if (file is not null) await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new { Plan = _lastPlan, Result = _lastResult }, Json));
    }
    private async Task<string?> SaveAsync(string title, string name) => (await StorageProvider.SaveFilePickerAsync(new()
        { Title = title, SuggestedFileName = name, DefaultExtension = "json" }))?.Path.LocalPath;
    private Button Command(string text, Func<Task> action)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 4) };
        b.Click += async (_, _) => { try { await action(); } catch (Exception e) { _status.Text = e.Message; } }; return b;
    }
    private static TextBox Box(string text) => new() { Text = text, Width = 104 };
    private static string F(double v) => v.ToString("G17", CultureInfo.InvariantCulture);
    private static double Number(TextBox box) => double.Parse(box.Text!, CultureInfo.InvariantCulture);
    private static int Int(TextBox box) => int.Parse(box.Text!, CultureInfo.InvariantCulture);
    private static Control Top(Control c) { DockPanel.SetDock(c, DockSide.Top); return c; }
    private static Control Label(string title, Control c) => new StackPanel { Spacing = 3, Margin = new Thickness(0, 0, 8, 8), Children = { new TextBlock { Text = title }, c } };
    private sealed record Choice(Guid? Id, string Label) { public override string ToString() => Label; }

    private sealed class VariableRow
    {
        private readonly ComboBox _object, _domain = new() { ItemsSource = Enum.GetValues<NscDomain>(), SelectedIndex = 0, Width = 135 }, _parameter = new() { Width = 175 };
        private readonly TextBox _low = Box("-1"), _high = Box("1"), _step = Box("0.01"), _scale = Box("0.01");
        private readonly CheckBox _tolerance = new() { Content = "公差" }, _compensator = new() { Content = "补偿" };
        private readonly ComboBox _distribution = new() { ItemsSource = Enum.GetValues<NonSequentialToleranceDistribution>(), SelectedIndex = 1, Width = 110 };
        public Control View { get; } public bool Compensator => _compensator.IsChecked == true; public bool Toleranced => _tolerance.IsChecked == true;
        public VariableRow(NonSequentialDocumentDto doc, NonSequentialDesignVariable? v, NonSequentialToleranceVariable? t, bool comp)
        {
            var objects = doc.Objects.Select(o => new Choice(o.Id, $"{o.ObjectNumber} {o.Name}")).ToArray();
            _object = new() { ItemsSource = objects, SelectedItem = objects.FirstOrDefault(o => o.Id == v?.ObjectId) ?? objects.FirstOrDefault(), Width = 180 };
            void Parameters()
            {
                var item = doc.Objects.FirstOrDefault(o => o.Id == (_object.SelectedItem as Choice)?.Id);
                _parameter.ItemsSource = _domain.SelectedItem is NscDomain.ObjectParameter
                    ? item?.Parameters.GetType().GetProperties().Where(p => p.PropertyType == typeof(double) && p.SetMethod?.IsPublic == true).Select(p => p.Name).ToArray() ?? []
                    : _domain.SelectedItem is NscDomain.SourcePhase ? new[] { "PhaseDegrees" } : new[] { "X", "Y", "Z" };
                _parameter.SelectedIndex = 0;
            }
            _object.SelectionChanged += (_, _) => Parameters(); _domain.SelectionChanged += (_, _) => Parameters(); Parameters();
            if (v is not null) { _domain.SelectedItem = v.Domain; Parameters(); _parameter.SelectedItem = v.Parameter; _low.Text = F(v.LowerBound); _high.Text = F(v.UpperBound); _step.Text = F(v.Step); }
            _tolerance.IsChecked = t is not null; _compensator.IsChecked = comp;
            if (t is not null) { _scale.Text = F(t.Scale); _distribution.SelectedItem = t.Distribution; }
            View = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { Label("对象", _object), Label("变量类型", _domain), Label("参数 / 轴", _parameter),
                Label("下限", _low), Label("上限", _high), Label("步长", _step), Label("", _compensator), Label("", _tolerance), Label("分布", _distribution), Label("σ / 均匀半宽", _scale) } };
        }
        public NonSequentialDesignVariable Read() => new((_object.SelectedItem as Choice)?.Id ?? throw new ArgumentException("请选择变量对象。"),
            (NscDomain)_domain.SelectedItem!, (string?)_parameter.SelectedItem ?? throw new ArgumentException("请选择参数。"), Number(_low), Number(_high), Number(_step));
        public NonSequentialToleranceVariable ReadTolerance() => new(Read(), Number(_scale), (NonSequentialToleranceDistribution)_distribution.SelectedItem!);
    }
    private sealed class TargetRow
    {
        private readonly ComboBox _detector, _source, _metric = new() { ItemsSource = Enum.GetValues<NonSequentialMetric>(), SelectedIndex = 0, Width = 170 };
        private readonly TextBox _target = Box("1"), _tolerance = Box("0.01"), _weight = Box("1"), _wave = Box("0"), _pixel = Box("0"), _path = Box("");
        public Control View { get; }
        public TargetRow(NonSequentialDocumentDto doc, NonSequentialDesignTarget? t)
        {
            var detectors = doc.Objects.Where(o => o.Parameters is Application.Contracts.DetectorRectangleParameters).Select(o => new Choice(o.Id, $"{o.ObjectNumber} {o.Name}")).ToArray();
            _detector = new() { ItemsSource = detectors, SelectedItem = detectors.FirstOrDefault(o => o.Id == t?.DetectorId) ?? detectors.FirstOrDefault(), Width = 180 };
            var sources = doc.Objects.Where(o => o.Parameters is Application.Contracts.SourceParameters).Select(o => new Choice(o.Id, $"{o.ObjectNumber} {o.Name}")).Prepend(new(null, "全部光源")).ToArray();
            _source = new() { ItemsSource = sources, SelectedItem = sources.FirstOrDefault(o => o.Id == t?.SourceObjectId) ?? sources[0], Width = 160 };
            if (t is not null) { _metric.SelectedItem = t.Metric; _target.Text = F(t.Target); _tolerance.Text = F(t.Tolerance); _weight.Text = F(t.Weight); _wave.Text = t.WavelengthNumber.ToString(); _pixel.Text = t.PixelIndex.ToString(); _path.Text = t.PathFilter; }
            View = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { Label("探测器", _detector), Label("指标", _metric), Label("目标", _target), Label("允许偏差", _tolerance),
                Label("权重", _weight), Label("波长（0=全部）", _wave), Label("像素（从0起）", _pixel), Label("光源筛选", _source), Label("路径筛选", _path) } };
        }
        public NonSequentialDesignTarget Read() => new((_detector.SelectedItem as Choice)?.Id ?? throw new ArgumentException("请选择探测器。"),
            (NonSequentialMetric)_metric.SelectedItem!, Number(_target), Number(_tolerance), Number(_weight), Int(_wave), Int(_pixel),
            string.IsNullOrWhiteSpace(_path.Text) ? null : _path.Text, (_source.SelectedItem as Choice)?.Id);
    }
}
