using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.CoatingDesign.Engine;
using OptilandWorkbench.Core.Coatings;

namespace OptilandWorkbench.CoatingDesign.App;

public sealed class MainWindow : Window
{
    private readonly MaterialLibrary _materials = new();
    private readonly DesignService _service = new();
    private readonly ExperimentSession _session = new(Examples.Create(DesignKind.Antireflection));
    private readonly Dictionary<string, TextBox> _numbers = new();
    private readonly ObservableCollection<LayerRow> _rows = [];
    private readonly ComboBox _kind = new() { ItemsSource = new[] { "减反膜", "高反膜", "窄带滤光片" }, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _polarization = new() { ItemsSource = new[] { "S 偏振", "P 偏振", "非偏振（S/P 等权）" }, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static readonly string[] AlgorithmNames = ["Damped Least Squares", "Nelder-Mead", "Coordinate Pattern Search"];
    private readonly ComboBox _algorithm = new() { ItemsSource = new[] { "阻尼最小二乘 (DLS)", "单纯形 (Nelder-Mead)", "坐标模式搜索" }, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _candidates = new() { MinWidth = 180 };
    private readonly AutoCompleteBox _substrate;
    private readonly AutoCompleteBox _low;
    private readonly AutoCompleteBox _high;
    private readonly AutoCompleteBox _layerMaterial;
    private readonly TextBox _name = new();
    private readonly TextBox _stopBands = new();
    private readonly DataGrid _table;
    private readonly SpectrumPlot _plot = new() { MinHeight = 240 };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { Text = "选择示例，或输入指标后生成膜系。", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _materialSource = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _progress = new() { IsVisible = false, IsIndeterminate = true, Height = 4 };
    private readonly StackPanel _narrowFields = new() { Spacing = 8 };
    private Control? _reflectanceField;
    private bool _loading, _busy;
    private long _uiRevision;
    private readonly List<Button> _computeButtons = [];
    public Task PendingOperation { get; private set; } = Task.CompletedTask;
    public Experiment CurrentExperiment => _session.Current;

    private readonly LaboratoryFileDialogs? _dialogs;
    public MainWindow(LaboratoryFileDialogs? dialogs = null)
    {
        _dialogs = dialogs;
        Title = "光学镀膜设计实验室"; Width = 1200; Height = 900; MinWidth = 820; MinHeight = 640;
        FontSize = LaboratoryTypography.Body;
        Background = (IBrush)Avalonia.Application.Current!.Resources["CoatingBackground"]!;
        Foreground = (IBrush)Avalonia.Application.Current.Resources["CoatingText"]!;
        _substrate = MaterialPicker(); _low = MaterialPicker(); _high = MaterialPicker(); _layerMaterial = MaterialPicker();
        _table = new DataGrid { ItemsSource = _rows, AutoGenerateColumns = false, CanUserSortColumns = false,
            CanUserReorderColumns = false, SelectionMode = DataGridSelectionMode.Single, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            MinHeight = 180 };
        _table.Columns.Add(new DataGridTextColumn { Header = "层序", Binding = new Binding(nameof(LayerRow.Number)), IsReadOnly = true, Width = new DataGridLength(52) });
        _table.Columns.Add(new DataGridTextColumn { Header = "材料（入射侧 → 基板）", Binding = new Binding(nameof(LayerRow.MaterialId)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _table.Columns.Add(new DataGridTextColumn { Header = "厚度 (nm)", Binding = new Binding(nameof(LayerRow.ThicknessText)) { Mode = BindingMode.TwoWay }, Width = new DataGridLength(120) });
        _table.Columns.Add(new DataGridCheckBoxColumn { Header = "变量", Binding = new Binding(nameof(LayerRow.Variable)) { Mode = BindingMode.TwoWay }, Width = new DataGridLength(64) });
        _table.SelectionChanged += (_, _) => { if (_table.SelectedItem is LayerRow row) _layerMaterial.Text = row.MaterialId; };
        _table.CellEditEnded += (_, _) => Changed();
        _table.KeyDown += (_, e) => { if (e.Key == Key.Delete && _table.SelectedItem is LayerRow row) { _rows.Remove(row); Renumber(); Changed(); e.Handled = true; } };

        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(ActionButton("打开实验", OpenAsync));
        toolbar.Children.Add(ActionButton("保存实验", SaveAsync));
        toolbar.Children.Add(ActionButton("导出结果", ExportAsync));
        foreach (var (label, kind) in new[] { ("示例：减反", DesignKind.Antireflection), ("示例：高反", DesignKind.HighReflector), ("示例：窄带", DesignKind.NarrowBand) })
            toolbar.Children.Add(ActionButton(label, () => { Load(Examples.Create(kind)); return Task.CompletedTask; }));
        var input = BuildInputs();
        var layerTools = new WrapPanel();
        _layerMaterial.Width = 260;
        layerTools.Children.Add(_layerMaterial);
        layerTools.Children.Add(ActionButton("添加层", () => EditLayer("add")));
        layerTools.Children.Add(ActionButton("替换材料", () => EditLayer("material")));
        layerTools.Children.Add(ActionButton("复制", () => EditLayer("copy")));
        layerTools.Children.Add(ActionButton("删除", () => EditLayer("remove")));
        layerTools.Children.Add(ActionButton("上移", () => EditLayer("up")));
        layerTools.Children.Add(ActionButton("下移", () => EditLayer("down")));
        var actions = new WrapPanel();
        AddCompute("1 生成膜系", () => RunAsync("generate"), actions);
        AddCompute("2 优化", () => RunAsync("optimize"), actions);
        AddCompute("计算 / 复验", () => RunAsync("evaluate"), actions);
        actions.Children.Add(ActionButton("取消", () => { _uiRevision++; _session.Cancel(); _status.Text = "已请求取消；当前已完成结果保留。"; return Task.CompletedTask; }));
        var candidateBar = new WrapPanel(); candidateBar.Children.Add(_candidates);
        candidateBar.Children.Add(ActionButton("采用候选", SelectCandidateAsync));
        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,2*,Auto,3*,Auto"), RowSpacing = 8 };
        Put(right, actions, 0); Put(right, layerTools, 1); Put(right, _table, 2); Put(right, candidateBar, 3); Put(right, _plot, 4);
        Put(right, new ScrollViewer { Content = _summary, MaxHeight = 220 }, 5);
        var main = new Grid { ColumnDefinitions = new ColumnDefinitions("300,*"), ColumnSpacing = 16 };
        main.Children.Add(new ScrollViewer { Content = input }); Grid.SetColumn(right, 1); main.Children.Add(right);
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(16), RowSpacing = 12 };
        Put(root, toolbar, 0); Put(root, main, 1); Put(root, _progress, 2); Put(root, _status, 3);
        Content = root;
        KeyDown += async (_, e) =>
        {
            var command = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
            if (command && e.Key == Key.S) { e.Handled = true; await Guard(SaveAsync); }
            if (command && e.Key == Key.O) { e.Handled = true; await Guard(OpenAsync); }
            if (e.Key == Key.Escape) { _uiRevision++; _session.Cancel(); }
            if (e.Key == Key.F5 && !_busy) { e.Handled = true; await Guard(() => RunAsync("evaluate")); }
        };
        Closed += (_, _) => _session.Dispose();
        Load(_session.Current);
    }

    private StackPanel BuildInputs()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "指标与材料", FontSize = LaboratoryTypography.Section });
        panel.Children.Add(Field("实验名称", _name));
        panel.Children.Add(Field("设计类型", _kind));
        panel.Children.Add(Field("基板（搜索选择）", _substrate));
        panel.Children.Add(Field("低折射率 / 第一膜料", _low));
        panel.Children.Add(Field("高折射率 / 第二膜料", _high));
        panel.Children.Add(Number("center", "中心波长 (nm)"));
        panel.Children.Add(Number("min", "目标 / 计算范围下限 (nm)"));
        panel.Children.Add(Number("max", "目标 / 计算范围上限 (nm)"));
        panel.Children.Add(_reflectanceField = Number("r", "目标反射率 (%)"));
        panel.Children.Add(Number("layers", "层数上限"));
        panel.Children.Add(Number("angle", "入射角 (°)，入射介质为空气"));
        panel.Children.Add(Field("偏振", _polarization));
        _narrowFields.Children.Add(Number("fwhm", "目标 FWHM (nm)"));
        _narrowFields.Children.Add(Number("peak", "最低峰值透过率 (%)"));
        _narrowFields.Children.Add(Number("od", "截止深度 OD（0–12）"));
        _narrowFields.Children.Add(Field("截止波段 (nm)，例 490-510;590-610", _stopBands));
        panel.Children.Add(_narrowFields);
        var advanced = new StackPanel { Spacing = 8 };
        advanced.Children.Add(Number("dmin", "最小膜厚 (nm)")); advanced.Children.Add(Number("dmax", "最大膜厚 (nm)"));
        advanced.Children.Add(Number("preview", "预览基础间隔数")); advanced.Children.Add(Number("samples", "优化基础间隔数"));
        advanced.Children.Add(Number("iterations", "最大迭代次数")); advanced.Children.Add(Field("实际优化算法", _algorithm));
        advanced.Children.Add(Number("ctol", "峰值位置容差 (nm)")); advanced.Children.Add(Number("ftol", "FWHM 容差 (nm)"));
        advanced.Children.Add(Number("tolerance", "厚度均匀扰动 ± (%)")); advanced.Children.Add(Number("trials", "公差样本数"));
        advanced.Children.Add(Number("seed", "公差随机种子"));
        AddCompute("公差复验", () => RunAsync("tolerance"), advanced);
        panel.Children.Add(new Expander { Header = "高级：约束、采样、算法与公差", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch });
        panel.Children.Add(new Expander { Header = "材料来源与计算边界", Content = _materialSource, HorizontalAlignment = HorizontalAlignment.Stretch });
        Watch(_name, TextBox.TextProperty); Watch(_stopBands, TextBox.TextProperty);
        foreach (var picker in new[] { _substrate, _low, _high }) Watch(picker, AutoCompleteBox.TextProperty);
        _kind.SelectionChanged += (_, _) => { Visibility(); Changed(); };
        _polarization.SelectionChanged += (_, _) => Changed(); _algorithm.SelectionChanged += (_, _) => Changed();
        return panel;
    }

    private AutoCompleteBox MaterialPicker() => new()
    {
        ItemsSource = _materials.Names.ToArray(), FilterMode = AutoCompleteFilterMode.Contains,
        MinimumPrefixLength = 0, IsTextCompletionEnabled = true, HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private Control Number(string key, string label)
    {
        var box = new TextBox(); _numbers.Add(key, box); Watch(box, TextBox.TextProperty);
        return Field(label, box);
    }
    private void Watch(Control control, AvaloniaProperty property) => control.PropertyChanged += (_, e) =>
    {
        // TextChanged is queued by Avalonia; invalidate synchronously before a completed task can write back.
        if (e.Property == property) Changed();
    };
    private static Control Field(string label, Control control)
    {
        AutomationProperties.SetName(control, label);
        return new StackPanel { Spacing = 4, Children = { new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, control } };
    }
    private Button ActionButton(string label, Func<Task> action)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 8, 4) };
        AutomationProperties.SetName(button, label); ToolTip.SetTip(button, label);
        button.Click += (_, _) => PendingOperation = Guard(action);
        return button;
    }
    private void AddCompute(string label, Func<Task> action, Panel panel)
    {
        var button = ActionButton(label, action);
        if (label is "1 生成膜系" or "2 优化") button.Classes.Add("accent");
        _computeButtons.Add(button); panel.Children.Add(button);
    }
    private static void Put(Grid grid, Control control, int row) { Grid.SetRow(control, row); grid.Children.Add(control); }
    private async Task Guard(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { _status.Text = "操作已取消。"; }
        catch (Exception error) { _status.Text = "未完成：" + error.Message; }
    }

    private void Changed()
    {
        if (_loading) return;
        _uiRevision++; _session.Invalidate(); _plot.SetExperiment(null); _summary.Text = "参数或膜层已修改，请重新计算。";
        _candidates.ItemsSource = null; _status.Text = "输入已修改，旧结果已失效。";
    }
    private void Visibility()
    {
        _narrowFields.IsVisible = _kind.SelectedIndex == 2;
        if (_reflectanceField is not null) _reflectanceField.IsVisible = _kind.SelectedIndex != 2;
    }
    private double Value(string key)
    {
        if (double.TryParse(_numbers[key].Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) && double.IsFinite(v)) return v;
        throw new ArgumentException($"“{AutomationProperties.GetName(_numbers[key])}”请输入有效数值。");
    }
    private int Integer(string key)
    {
        var v = Value(key);
        if (v != Math.Truncate(v) || v < int.MinValue || v > int.MaxValue) throw new ArgumentException($"“{AutomationProperties.GetName(_numbers[key])}”必须为整数。");
        return (int)v;
    }
    private Experiment ReadInputs()
    {
        _table.CommitEdit();
        var current = _session.Current;
        MaterialSnapshot Select(AutoCompleteBox box)
        {
            var name = box.Text?.Trim() ?? "";
            return current.Materials.FirstOrDefault(m => m.Name == name || m.Id == name) ?? _materials.Select(name);
        }
        var substrate = Select(_substrate); var low = Select(_low); var high = Select(_high);
        var target = new DesignTarget((DesignKind)_kind.SelectedIndex, Value("center"), Value("min"), Value("max"), Value("r") / 100,
            Integer("layers"), Value("angle"), (ThinFilmPolarization)_polarization.SelectedIndex, Value("fwhm"), Value("peak") / 100,
            Value("od"), _kind.SelectedIndex == 2 ? ParseBands(_stopBands.Text) : null, Value("ctol"), Value("ftol"));
        var settings = new CalculationSettings(Integer("preview"), Integer("samples"), Integer("iterations"),
            _algorithm.SelectedIndex >= 0 ? AlgorithmNames[_algorithm.SelectedIndex] : "", Value("dmin"), Value("dmax"), Value("tolerance"), Integer("trials"), Integer("seed"));
        var materials = current.Materials.Concat([substrate, low, high]).GroupBy(m => m.Id).Select(g => g.First()).ToArray();
        var d = current with { Name = _name.Text?.Trim() ?? "", Target = target, Settings = settings, SubstrateId = substrate.Id,
            LowId = low.Id, HighId = high.Id, Materials = materials, Layers = _rows.Select(r => r.ToLayer()).ToArray() };
        if (d.Result?.Fingerprint != d.Fingerprint()) d = d with { Result = null };
        if (d.Tolerance?.Fingerprint != d.Fingerprint()) d = d with { Tolerance = null };
        d.Validate(); return d;
    }
    private static SpectralBand[] ParseBands(string? text)
    {
        var result = new List<SpectralBand>();
        foreach (var part in (text ?? "").Split(';', '；'))
        {
            var numbers = part.Trim().Split('-', '–');
            if (numbers.Length != 2 || !double.TryParse(numbers[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var low)
                || !double.TryParse(numbers[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var high))
                throw new ArgumentException("截止波段格式应为 490-510;590-610（nm）。");
            result.Add(new(low, high));
        }
        return result.ToArray();
    }

    private void Load(Experiment d)
    {
        _uiRevision++; _session.Replace(d); _loading = true;
        try
        {
            _name.Text = d.Name; _kind.SelectedIndex = (int)d.Target.Kind; _polarization.SelectedIndex = (int)d.Target.Polarization;
            _substrate.Text = d.SubstrateId; _low.Text = d.LowId; _high.Text = d.HighId; _layerMaterial.Text = d.LowId;
            var t = d.Target; var s = d.Settings;
            foreach (var (key, value) in new (string, double)[] { ("center", t.CenterNm), ("min", t.MinimumNm), ("max", t.MaximumNm),
                ("r", t.Reflectance * 100), ("layers", t.MaximumLayers), ("angle", t.AngleDegrees), ("fwhm", t.FwhmNm), ("peak", t.PeakTransmittance * 100),
                ("od", t.BlockingOd), ("ctol", t.CenterToleranceNm), ("ftol", t.FwhmToleranceNm), ("preview", s.PreviewIntervals), ("samples", s.OptimizationIntervals),
                ("iterations", s.MaximumIterations), ("dmin", s.MinimumThicknessNm), ("dmax", s.MaximumThicknessNm), ("tolerance", s.TolerancePercent),
                ("trials", s.ToleranceTrials), ("seed", s.RandomSeed) }) _numbers[key].Text = value.ToString("G17", CultureInfo.CurrentCulture);
            _algorithm.SelectedIndex = Array.IndexOf(AlgorithmNames, s.Optimizer);
            _stopBands.Text = string.Join(';', (t.StopBands ?? [new SpectralBand(490, 510), new SpectralBand(590, 610)])
                .Select(b => $"{b.MinimumNm.ToString("G17", CultureInfo.InvariantCulture)}-{b.MaximumNm.ToString("G17", CultureInfo.InvariantCulture)}"));
            _rows.Clear(); foreach (var layer in d.Layers) AddRow(layer); Renumber();
            _candidates.ItemsSource = (d.Candidates ?? []).Select(c => c.Name + $" · merit {NumericDisplayFormatter.Format(c.Merit)}").ToArray();
            _candidates.SelectedIndex = d.Candidates is { Length: > 0 } ? 0 : -1;
            _plot.SetExperiment(d);
            _materialSource.Text = string.Join("\n\n", d.Materials.Select(m => m.Name + "\n" + m.Source))
                + "\n\n相干、各向同性膜层；半无限基板。T 为进入基板的功率。OD 显示上限 12，真实数值不截断。";
            _summary.Text = Summary(d);
            Visibility(); _status.Text = d.Result is null ? "示例 / 实验已打开，请生成膜系或计算。" : "计算已完成，请查看逐项指标。";
        }
        finally { _loading = false; }
    }

    private static string Summary(Experiment d)
    {
        if (d.Result is not { } r) return "尚无当前输入的计算结果。";
        var text = (r.Passed ? "✓ 全部目标达标" : "未全部达标 · 保留当前有效结果") + $"    复验采样 {r.VerificationSamples} 点 / {(r.SamplingConverged ? "收敛" : "未收敛")}\n";
        foreach (var check in r.Checks)
        {
            var value = check.Actual.HasValue ? (check.Name.Contains("OD", StringComparison.Ordinal) && check.Actual.Value > 12
                ? "≥12（显示饱和）" : NumericDisplayFormatter.Format(check.Actual.Value)) : "—";
            text += $"{(check.Passed ? "✓" : "✗")} {check.Name}：{value}；要求 {check.Requirement}\n";
        }
        text += $"算法：{r.Run.Algorithm} / {r.Run.AlgorithmVersion}；停止：{StopReason(r.Run.StopReason)}；评估 {r.Run.Evaluations}\n"
            + string.Join("\n", r.Warnings);
        if (d.Tolerance is { } tolerance) text += $"\n公差：±{tolerance.Percent}% 均匀独立厚度扰动，{tolerance.Passed}/{tolerance.Trials} 达标；种子 {tolerance.Seed}。";
        return text;
    }
    private static string StopReason(string reason) => reason switch
    {
        "MaximumIterations" => "达到迭代上限", "GradientTolerance" => "梯度达到停止阈值", "StepOrMeritStagnation" => "步长或评价函数停滞",
        "SimplexTolerance" => "单纯形达到停止阈值", _ => reason
    };
    private void AddRow(FilmLayer layer)
    {
        var row = new LayerRow(layer); row.PropertyChanged += (_, _) => Changed(); _rows.Add(row);
    }
    private void Renumber() { for (var i = 0; i < _rows.Count; i++) _rows[i].Number = i + 1; }
    private Task EditLayer(string action)
    {
        _table.CommitEdit();
        var selected = _table.SelectedItem as LayerRow;
        var index = selected is null ? -1 : _rows.IndexOf(selected);
        if (action is "add" or "material")
        {
            var d = _session.Current;
            var material = d.Materials.FirstOrDefault(m => m.Id == _layerMaterial.Text) ?? _materials.Select(_layerMaterial.Text ?? "");
            _session.Replace(d with { Materials = d.Materials.Append(material).DistinctBy(m => m.Id).ToArray(), Result = null });
            if (action == "add") AddRow(new(material.Id, 100));
            else if (selected is not null) selected.MaterialId = material.Id;
        }
        else if (selected is not null)
        {
            if (action == "copy") AddRow(selected.ToLayer());
            if (action == "remove") _rows.Remove(selected);
            if (action == "up" && index > 0) _rows.Move(index, index - 1);
            if (action == "down" && index < _rows.Count - 1) _rows.Move(index, index + 1);
        }
        Renumber(); Changed(); return Task.CompletedTask;
    }
    private Task SelectCandidateAsync()
    {
        var d = ReadInputs(); var index = _candidates.SelectedIndex;
        if (index < 0 || d.Candidates is null || index >= d.Candidates.Length) throw new ArgumentException("请先生成并选择候选。");
        Load(d with { Layers = d.Candidates[index].Layers, Result = null, Tolerance = null });
        return Task.CompletedTask;
    }

    private async Task RunAsync(string action)
    {
        if (_busy) return;
        var d = ReadInputs(); _session.Replace(d);
        var revision = ++_uiRevision;
        SetBusy(true); _status.Text = "正在计算；可取消或修改输入。";
        var progress = new Progress<DesignProgress>(p =>
        {
            if (revision == _uiRevision) _status.Text = $"{p.Stage}：{p.Evaluations}，最佳评价值 {NumericDisplayFormatter.Format(p.BestMerit ?? 0)}";
        });
        try
        {
            var applied = await _session.RunAsync((snapshot, token) => Task.Run(() => action switch
            {
                "generate" => _service.Generate(snapshot, progress, token),
                "optimize" => _service.Optimize(snapshot, progress, token),
                "tolerance" => RunTolerance(snapshot, token),
                _ => _service.Evaluate(snapshot, progress: progress, token: token)
            }, token));
            if (applied && revision == _uiRevision)
            {
                Load(_session.Current);
            }
            else if (revision == _uiRevision) _status.Text = "已取消或输入已变更，本次结果未写回。";
        }
        finally { SetBusy(false); }
        Experiment RunTolerance(Experiment snapshot, CancellationToken token)
        {
            var evaluated = snapshot.Result is null ? _service.Evaluate(snapshot, progress: progress, token: token) : snapshot;
            return evaluated with { Tolerance = _service.Tolerance(evaluated, progress, token) };
        }
    }
    private void SetBusy(bool value)
    {
        _busy = value; _progress.IsVisible = value;
        foreach (var button in _computeButtons) button.IsEnabled = !value;
    }
    private async Task SaveAsync()
    {
        var d = ReadInputs();
        string? path;
        if (_dialogs is not null) path = await _dialogs.Save();
        else
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "保存镀膜实验", SuggestedFileName = d.Name + ".coating.json", DefaultExtension = "coating.json" });
            path = file?.TryGetLocalPath();
        }
        if (path is null) return;
        await ExperimentStore.SaveAsync(d, path); _status.Text = "已保存：" + path;
    }
    private async Task OpenAsync()
    {
        string? path;
        if (_dialogs is not null) path = await _dialogs.Open();
        else
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "打开镀膜实验", AllowMultiple = false });
            path = files.FirstOrDefault()?.TryGetLocalPath();
        }
        if (path is null) return;
        _session.Invalidate(); _plot.SetExperiment(null); _summary.Text = "正在打开并重算材料快照…";
        var revision = ++_uiRevision;
        var applied = await _session.RunAsync((_, token) => ExperimentStore.OpenAsync(path, token));
        if (applied && revision == _uiRevision) Load(_session.Current);
    }
    private async Task ExportAsync()
    {
        var d = ReadInputs();
        string? path;
        if (_dialogs is not null) path = await _dialogs.ExportDirectory();
        else
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择导出目录" });
            path = folders.FirstOrDefault()?.TryGetLocalPath();
        }
        if (path is null) return;
        var destination = Path.Combine(path, "coating-export-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        await ExperimentStore.ExportAsync(d, destination); _status.Text = "膜层、光谱、指标和实验已导出：" + destination;
    }
}

public sealed record LaboratoryFileDialogs(Func<Task<string?>> Save, Func<Task<string?>> Open, Func<Task<string?>> ExportDirectory);

public sealed class LayerRow : INotifyPropertyChanged
{
    private string _materialId, _thickness;
    private readonly string _originalText;
    private readonly double _originalThickness;
    private bool _variable;
    private int _number;
    public LayerRow(FilmLayer layer)
    {
        _materialId = layer.MaterialId; _originalThickness = layer.ThicknessNm;
        _thickness = _originalText = NumericDisplayFormatter.Format(layer.ThicknessNm); _variable = layer.Variable;
    }
    public int Number { get => _number; set { _number = value; Notify(); } }
    public string MaterialId { get => _materialId; set { _materialId = value; Notify(); } }
    public string ThicknessText { get => _thickness; set { _thickness = value; Notify(); } }
    public bool Variable { get => _variable; set { _variable = value; Notify(); } }
    public FilmLayer ToLayer()
    {
        if (ThicknessText == _originalText) return new(MaterialId, _originalThickness, Variable);
        if (!double.TryParse(ThicknessText, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) || !double.IsFinite(value) || value <= 0)
            throw new ArgumentException($"第 {Number} 层厚度请输入大于零的数值（nm）。");
        return new(MaterialId, value, Variable);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
