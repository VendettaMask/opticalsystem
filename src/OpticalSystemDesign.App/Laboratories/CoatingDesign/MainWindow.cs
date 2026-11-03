using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Formatting;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Modules;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

public sealed partial class MainWindow : Window
{
    private readonly MaterialLibrary _materials = new();
    private readonly DesignService _service = new();
    private readonly ExperimentSession _session = new(WorkspaceExample(Examples.Create(DesignKind.Antireflection)));
    private readonly Dictionary<string, TextBox> _numbers = new();
    private readonly ObservableCollection<LayerRow> _rows = [];
    private readonly ObservableCollection<LayerRow> _displayRows = [];
    private readonly ComboBox _kind = new() { ItemsSource = new[] { "减反膜", "高反膜", "窄带滤光片" }, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _polarization = new() { ItemsSource = new[] { "S 偏振", "P 偏振", "非偏振（S/P 等权）" }, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static readonly string[] AlgorithmNames = ["Damped Least Squares", "Nelder-Mead", "Coordinate Pattern Search"];
    private readonly ComboBox _algorithm = new() { ItemsSource = new[] { "阻尼最小二乘 (DLS)", "单纯形 (Nelder-Mead)", "坐标模式搜索" }, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _candidates = new() { MinWidth = 180 };
    private readonly AutoCompleteBox _substrate;
    private readonly AutoCompleteBox _incident;
    private readonly AutoCompleteBox _low;
    private readonly AutoCompleteBox _high;
    private readonly AutoCompleteBox _layerMaterial;
    private readonly TextBox _name = new();
    private readonly TextBox _stopBands = new();
    private readonly DataGrid _table;
    private readonly TabControl _settingsContent;
    private readonly Control _designResultsContent;
    private readonly CoatingDockWorkspace _workspace;
    internal CoatingDockWorkspace DockWorkspace => _workspace;
    internal TabControl SettingsContent => _settingsContent;
    private readonly SpectrumPlot _plot = new() { MinHeight = 240 };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { Text = "选择示例，或输入指标后生成膜系。", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _materialSource = new() { TextWrapping = TextWrapping.Wrap };
    private readonly OperationStatusBar _operationStatus = new() { Name = "CoatingOperationStatus" };
    private readonly StackPanel _narrowFields = new() { Spacing = 8 };
    private readonly ComboBox _narrowProtocol = new() { ItemsSource = new[] { "原始设计流程", "候选竞争（兼容）", "通用约束优化（试验）" } };
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
        Title = "光学镀膜设计"; Width = 1200; Height = 900; MinWidth = 820; MinHeight = 640;
        Icon = BrandAssets.LoadWindowIcon();
        DisplayTypography.Apply(this);
        try { CoatingPlotDefaults.Load(CoatingPlotDefaults.DefaultPath).Apply(_plot); }
        catch (Exception error) when (error is System.IO.IOException or System.Text.Json.JsonException or ArgumentException or UnauthorizedAccessException)
        { CoatingPlotDefaults.Factory.Apply(_plot); _plotDefaultsWarning = "默认设置未载入：" + error.Message; }
        this.BindThemeResource(BackgroundProperty, ThemeResourceBindings.Workspace);
        this.BindThemeResource(ForegroundProperty, ThemeResourceBindings.TextPrimary);
        _incident = MaterialPicker(); _substrate = MaterialPicker(); _low = MaterialPicker(); _high = MaterialPicker(); _layerMaterial = MaterialPicker();
        _table = BuildLayerTable();
        _rows.CollectionChanged += (_, _) => Renumber();
        _table.SelectionChanged += (_, _) => { if (_table.SelectedItem is LayerRow row) _layerMaterial.Text = row.MaterialId; };
        _table.CellEditEnded += (_, _) => Changed();
        _table.KeyDown += (_, e) => { if (e.Key == Key.Delete && e.Source is not TextBox) { TrackOperation(() => EditLayer("remove")); e.Handled = true; } };

        var commandBar = BuildCommandBar();
        _settingsContent = BuildInputs();
        var actions = new WrapPanel();
        AddCompute("1 生成膜系", () => RunAsync("generate"), actions);
        AddCompute("2 优化", () => RunAsync("optimize"), actions);
        AddCompute("结构搜索", () => RunAsync("search"), actions);
        AddCompute("计算 / 复验", () => RunAsync("evaluate"), actions);
        _cancel = ActionButton("取消", () => { _uiRevision++; _session.Cancel(); _plot.CancelPendingEvaluation(); _status.Text = "已请求取消；当前已完成结果保留。"; return Task.CompletedTask; });
        actions.Children.Add(_cancel);
        var layerPanel = new Grid { RowDefinitions = new("Auto,*,Auto") };
        var layerHeading = BuildCompactEditorHeader();
        var editorControls = new ScrollViewer
        {
            Name = "CoatingEditorControlsScroll", MaxHeight = 200,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = layerHeading
        };
        var geometryFooter = new ScrollViewer
        {
            Name = "CoatingEditorGeometryFooter", MaxHeight = 220, Content = BuildStackGeometryPanel(),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        void SizeEditorControls()
        {
            var height = layerPanel.Bounds.Height;
            geometryFooter.MaxHeight = Math.Max(44, Math.Min(220, height * .3));
            editorControls.MaxHeight = Math.Max(44, Math.Min(200, height - Math.Max(44, geometryFooter.Bounds.Height) - 120));
        }
        layerPanel.SizeChanged += (_, _) => SizeEditorControls();
        geometryFooter.SizeChanged += (_, _) => SizeEditorControls();
        layerPanel.Children.Add(editorControls);
        _table.BorderThickness = new Thickness(0);
        Put(layerPanel, BuildLayerViewport(), 1);
        Put(layerPanel, geometryFooter, 2);
        var plotPanel = new Grid { RowDefinitions = new("Auto,*") };
        plotPanel.Children.Add(new StackPanel
        {
            Margin = new Thickness(8),
            Spacing = 6,
            Children =
            { BuildSpectrumToolbar() }
        });
        Put(plotPanel, new AnalysisImageFrame { Margin = new Thickness(4, 0, 4, 8), Child = _plot }, 1);
        _summary.FontSize = DisplayTypography.BodySmall;
        _designResultsContent = BuildDesignResultsContent();
        var evaluation = new Grid { RowDefinitions = new("*,Auto,Auto") };
        evaluation.Children.Add(plotPanel); _plotStatus.Margin = new Thickness(8, 3); Put(evaluation, _plotStatus, 1);
        Put(evaluation, BuildResultsPanel(), 2);
        _workspace = new CoatingDockWorkspace(WorkspacePane(layerPanel, "CoatingLayerCard"), WorkspacePane(evaluation, "CoatingSpectrumCard"), this);
        _workspace.RegisterPageFactory(CoatingDockWorkspace.ResultsId, "镀膜设计结果", () => _designResultsContent);
        _workspace.RegisterPageFactory(CoatingDockWorkspace.StackId, "堆栈几何", () =>
        {
            _stackAnalysisView ??= new CoatingStackAnalysisView { FontFamily = FontFamily, FontSize = FontSize };
            RefreshStackGeometry(); return _stackAnalysisView;
        });
        _workspace.HostCreated += host =>
        {
            LaboratoryPresentation.Apply(host);
            host.KeyDown += HandleWorkspaceKey;
            host.AddHandler(KeyDownEvent, HandleEditorHistoryKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            host.AddHandler(KeyDownEvent, HandleEditorClipboardKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        };
        var main = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 6, Name = "CoatingResults" };
        main.Children.Add(actions); Put(main, _workspace, 1);
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        main.Margin = new Thickness(8, 8, 8, 0);
        _operationStatus.Margin = new Thickness(12, 8, 12, 8);
        Put(root, commandBar, 0); Put(root, main, 1); Put(root, _operationStatus, 2);
        Content = MainWindowTitleBar.Wrap(this, root);
        KeyDown += HandleWorkspaceKey;
        AddHandler(KeyDownEvent, HandleEditorHistoryKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, HandleEditorClipboardKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        UiLocalization.LanguageChanged += OnEditorLanguageChanged;
        Closed += (_, _) => { DisposeRecovery(); UiLocalization.LanguageChanged -= OnEditorLanguageChanged; _workspace.Dispose(); _stackAnalysisView?.Dispose(); _plot.Dispose(); _session.Dispose(); _operationStatus.Dispose(); };
        Closing += OnClosing;
        Load(_session.Current, markDirty: false); SetBusy(false);
        InitializeRecovery();
        Opened += (_, _) =>
        {
            if (File.Exists(_layoutPath)) TrackOperation(RestoreWorkspaceLayoutAsync);
            TrackOperation(OfferRecoveryAsync);
        };
        LaboratoryPresentation.Apply(this);
    }

    private async void HandleWorkspaceKey(object? sender, KeyEventArgs e)
    {
        var command = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        if (command && e.Key == Key.S) { e.Handled = true; await TrackOperation(SaveAsync); }
        if (command && e.Key == Key.O) { e.Handled = true; await TrackOperation(OpenAsync); }
        if (e.Key == Key.Escape) { _uiRevision++; _session.Cancel(); _plot.CancelPendingEvaluation(); }
        if (e.Key == Key.F5 && !_busy) { e.Handled = true; await TrackOperation(() => RunAsync("evaluate")); }
    }

    private TabControl BuildInputs()
    {
        var panel = new TabControl { Name = "CoatingSettingsTabs", HorizontalContentAlignment = HorizontalAlignment.Stretch };
        void Page(string title, Control content) => panel.Items.Add(new TabItem
        {
            Header = title, FontSize = DisplayTypography.Body, FontWeight = FontWeight.Medium, Padding = new Thickness(12, 8), Content = new ScrollViewer
            {
                Content = new Border { Padding = new Thickness(16), Child = content },
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            }
        });
        var targets = new StackPanel { Spacing = 8 };
        targets.Children.Add(Field("实验名称", _name));
        targets.Children.Add(Field("设计类型", _kind));
        targets.Children.Add(Number("center", "中心波长 (nm)"));
        targets.Children.Add(Number("min", "目标 / 计算范围下限 (nm)"));
        targets.Children.Add(Number("max", "目标 / 计算范围上限 (nm)"));
        targets.Children.Add(_reflectanceField = Number("r", "目标反射率 (%)"));
        targets.Children.Add(Number("layers", "层数上限"));
        targets.Children.Add(Number("angle", "入射角 (°)"));
        targets.Children.Add(Field("偏振", _polarization));
        _narrowFields.Children.Add(Number("fwhm", "目标 FWHM (nm)"));
        _narrowFields.Children.Add(Number("peak", "最低峰值透过率 (%)"));
        _narrowFields.Children.Add(Number("od", "截止深度 OD（0–12）"));
        targets.Children.Add(Field("设计流程", _narrowProtocol));
        _narrowFields.Children.Add(Field("截止波段 (nm)，例 490-510;590-610", _stopBands));
        targets.Children.Add(_narrowFields);
        Page("设计目标", targets);
        var media = new StackPanel { Spacing = 10, Children =
        {
            Field("入射介质（透明）", _incident), Field("基板（搜索选择）", _substrate),
            Field("低折射率 / 第一膜料", _low), Field("高折射率 / 第二膜料", _high),
            new Expander { Header = "材料来源与计算边界", Content = _materialSource, HorizontalAlignment = HorizontalAlignment.Stretch }
        } };
        Page("介质与膜料", media);
        var advanced = new StackPanel { Spacing = 8 };
        advanced.Children.Add(Number("dmin", "最小膜厚 (nm)")); advanced.Children.Add(Number("dmax", "最大膜厚 (nm)"));
        advanced.Children.Add(Number("preview", "预览基础间隔数")); advanced.Children.Add(Number("samples", "优化基础间隔数"));
        advanced.Children.Add(Number("iterations", "最大迭代次数")); advanced.Children.Add(Field("实际优化算法", _algorithm));
        advanced.Children.Add(Number("ctol", "峰值位置容差 (nm)")); advanced.Children.Add(Number("ftol", "FWHM 容差 (nm)"));
        var tolerances = new StackPanel { Spacing = 8 };
        tolerances.Children.Add(Number("tolerance", "厚度公差 (%)")); tolerances.Children.Add(Number("trials", "公差样本数"));
        tolerances.Children.Add(Number("seed", "随机种子"));
        tolerances.Children.Add(Field("公差分布", _distribution));
        tolerances.Children.Add(Field("厚度误差关联", _correlation));
        tolerances.Children.Add(Number("angleTolerance", "角度公差 (°)"));
        tolerances.Children.Add(new TextBlock { Text = "均匀：±范围；正态：1σ。所有膜层参与制造公差，固定设置仅限制优化。", TextWrapping = TextWrapping.Wrap });
        advanced.Children.Add(Number("starts", "搜索起点预算"));
        advanced.Children.Add(Number("jitter", "搜索厚度扰动 (%)"));
        advanced.Children.Add(Field("搜索结构变化", _varyStructure));
        advanced.Children.Add(Number("cavities", "窄带腔数 (1–5)"));
        _distribution.SelectionChanged += (_, _) => Changed(); _correlation.SelectionChanged += (_, _) => Changed();
        _varyStructure.IsCheckedChanged += (_, _) => Changed();
        _narrowProtocol.SelectionChanged += (_, _) => Changed();
        AddCompute("公差复验", () => RunAsync("tolerance"), tolerances);
        Page("优化与采样", advanced); Page("公差", tolerances);
        Page("表面与评价", BuildDuplexControls());
        Watch(_name, TextBox.TextProperty); Watch(_stopBands, TextBox.TextProperty);
        foreach (var picker in new[] { _incident, _substrate, _low, _high }) Watch(picker, AutoCompleteBox.TextProperty);
        _kind.SelectionChanged += (_, _) =>
        {
            Visibility(); Changed();
        };
        _polarization.SelectionChanged += (_, _) => Changed(); _algorithm.SelectionChanged += (_, _) => Changed();
        return panel;
    }

    private AutoCompleteBox MaterialPicker() => new()
    {
        ItemsSource = _materials.Names.ToArray(),
        FilterMode = AutoCompleteFilterMode.Contains,
        MinimumPrefixLength = 0,
        IsTextCompletionEnabled = true,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private Control Number(string key, string label)
    {
        var box = new TextBox(); _numbers.Add(key, box); Watch(box, TextBox.TextProperty);
        return Field(label, box);
    }
    private void Watch(Control control, AvaloniaProperty property) => control.PropertyChanged += (_, e) =>
    {
        // TextChanged is queued by Avalonia; invalidate synchronously before a completed task can write back.
        if (e.Property == property) { if (control is AutoCompleteBox picker) ToolTip.SetTip(picker, picker.Text); Changed($"field:{control.GetHashCode()}"); }
    };
    private static Control Field(string label, Control control)
    {
        AutomationProperties.SetName(control, label);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*"), ColumnSpacing = 12, MinHeight = UiDensity.StandardControlHeight };
        var caption = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        caption.FontSize = DisplayTypography.BodySmall;
        caption.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextSecondary);
        ToolTip.SetTip(caption, label); row.Children.Add(caption); Grid.SetColumn(control, 1); row.Children.Add(control);
        control.MinWidth = 0; control.HorizontalAlignment = HorizontalAlignment.Stretch;
        return row;
    }
    private Button ActionButton(string label, Func<Task> action)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 8, 4) };
        button.Classes.Add("lab-command");
        AutomationProperties.SetName(button, label); ToolTip.SetTip(button, label);
        button.Click += (_, _) => TrackOperation(action);
        return button;
    }
    private void AddCompute(string label, Func<Task> action, Panel panel)
    {
        var button = ActionButton(label, action);
        if (label is "1 生成膜系" or "2 优化" or "结构搜索" or "计算 / 复验" or "公差复验") button.Classes.Add("accent");
        _computeButtons.Add(button); panel.Children.Add(button);
    }
    private static void Put(Grid grid, Control control, int row) { Grid.SetRow(control, row); grid.Children.Add(control); }
    private async Task Guard(Func<Task> action)
    {
        if (!_busy) _operationStatus.IsVisible = false;
        try { await action(); }
        catch (OperationCanceledException) { _status.Text = "操作已取消。"; }
        catch (Exception error) { ShowFailure("未完成：" + error.Message); }
    }

    private void ShowFailure(string message)
    {
        _status.Text = message;
        _operationStatus.MarkFailed(message);
        _operationStatus.IsVisible = true;
    }

    private void Changed(string? historyKey = null)
    {
        if (_loading) return;
        _hasUnsavedChanges = true;
        _uiRevision++; _session.Invalidate(); _plot.SetExperiment(null); _summary.Text = "参数或膜层已修改，请重新计算。";
        _candidates.ItemsSource = null; _status.Text = "输入已修改，旧结果已失效。";
        RefreshStackGeometry();
        RefreshLayerOpticalThickness();
        RefreshLayerEditorPresentation();
        RecordEditorHistory(historyKey);
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
        var incident = SelectMaterial(_incident); var substrate = SelectMaterial(_substrate); var low = SelectMaterial(_low); var high = SelectMaterial(_high);
        var exit = SelectMaterial(_exit);
        var cavityCount = Integer("cavities");
        var structure = current.Target.Structure is { } originalStructure ? originalStructure with { Cavities = cavityCount }
            : cavityCount == 1 ? null : new StructureOptions(cavityCount);
        var protocol = _narrowProtocol.SelectedIndex switch
        { 2 => 4, 1 => current.Settings.DesignProtocolVersion == 2 ? 2 : _kind.SelectedIndex == (int)DesignKind.NarrowBand ? 3 : 2, _ => (int?)null };
        if (protocol != 2 && structure is not null) structure = structure with { PrototypeFamilyVersion = null };
        var target = CoatingPresentation.Target((DesignKind)_kind.SelectedIndex, Value("center"), Value("min"), Value("max"), Value("r") / 100,
            Integer("layers"), Value("angle"), _polarization.SelectedIndex, Value("fwhm"), Value("peak") / 100,
            Value("od"), _kind.SelectedIndex == 2 ? ParseBands(_stopBands.Text) : null, Value("ctol"), Value("ftol"), structure);
        var settings = new CalculationSettings(Integer("preview"), Integer("samples"), Integer("iterations"),
            _algorithm.SelectedIndex >= 0 ? AlgorithmNames[_algorithm.SelectedIndex] : "", Value("dmin"), Value("dmax"), Value("tolerance"), Integer("trials"), Integer("seed"),
            new SearchOptions(Integer("starts"), Value("jitter"), _varyStructure.IsChecked == true),
            new ToleranceOptions((ToleranceDistribution)_distribution.SelectedIndex, (ThicknessCorrelation)_correlation.SelectedIndex, Value("angleTolerance")))
        { DesignProtocolVersion = protocol };
        if (current.Settings.Search is null && settings.Search == new SearchOptions()) settings = settings with { Search = null };
        if (current.Settings.Tolerancing is null && settings.Tolerancing == new ToleranceOptions()) settings = settings with { Tolerancing = null };
        CoatingStackGeometry? geometry = new(exit.Id, Value("substrateThickness"), _includeBackSurface.IsChecked == true);
        if (_duplexEnabled.IsChecked != true && current.StackGeometry is null && !geometry.IncludeBackSurface && geometry.ExitMediumId == (current.ReferenceExample?.ExitMediumId ?? current.IncidentId)
            && geometry.SubstrateThicknessMm == (current.ReferenceExample?.SubstrateThicknessMm ?? 1)) geometry = null;
        var materials = current.Materials.Concat([incident, substrate, low, high, exit]).GroupBy(m => m.Id).Select(g => g.First()).ToArray();
        var d = current with
        {
            Name = _name.Text?.Trim() ?? "",
            Target = target,
            Settings = settings,
            IncidentId = incident.Id,
            SubstrateId = substrate.Id,
            LowId = low.Id,
            HighId = high.Id,
            Materials = materials,
            ReferenceWavelengthNm = current.ReferenceWavelengthNm is null && Value("reference") == target.CenterNm ? null : Value("reference"),
            StackGeometry = geometry,
            Layers = FrontEditorRows.Select(r => r.ToLayer()).ToArray()
        };
        d = ReadFilmEditors(d);
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

    private void Load(Experiment d, bool markDirty = true)
    {
        _uiRevision++; _session.Replace(d); _loading = true;
        try
        {
            RefreshMaterials(d);
            _name.Text = d.Name; _kind.SelectedIndex = (int)d.Target.Kind; _polarization.SelectedIndex = CoatingPresentation.PolarizationIndex(d.Target);
            _incident.Text = d.IncidentId; _substrate.Text = d.SubstrateId; _low.Text = d.LowId; _high.Text = d.HighId; _layerMaterial.Text = d.LowId;
            _exit.Text = d.StackGeometry?.ExitMediumId ?? d.ReferenceExample?.ExitMediumId ?? d.IncidentId;
            _numbers["substrateThickness"].Text = (d.StackGeometry?.SubstrateThicknessMm ?? d.ReferenceExample?.SubstrateThicknessMm ?? 1).ToString("G17", CultureInfo.CurrentCulture);
            _numbers["reference"].Text = d.EffectiveReferenceWavelengthNm.ToString("G17", CultureInfo.CurrentCulture);
            _includeBackSurface.IsChecked = d.StackGeometry?.IncludeBackSurface ?? false;
            var t = d.Target; var s = d.Settings;
            foreach (var (key, value) in new (string, double)[] { ("center", t.CenterNm), ("min", t.MinimumNm), ("max", t.MaximumNm),
                ("r", t.Reflectance * 100), ("layers", t.MaximumLayers), ("angle", t.AngleDegrees), ("fwhm", t.FwhmNm), ("peak", t.PeakTransmittance * 100),
                ("od", t.BlockingOd), ("ctol", t.CenterToleranceNm), ("ftol", t.FwhmToleranceNm), ("preview", s.PreviewIntervals), ("samples", s.OptimizationIntervals),
                ("iterations", s.MaximumIterations), ("dmin", s.MinimumThicknessNm), ("dmax", s.MaximumThicknessNm), ("tolerance", s.TolerancePercent),
                ("trials", s.ToleranceTrials), ("seed", s.RandomSeed), ("cavities", t.Structure?.Cavities ?? 1),
                ("starts", s.Search?.Starts ?? 6), ("jitter", s.Search?.JitterPercent ?? 12), ("angleTolerance", s.Tolerancing?.AngleDegrees ?? 0) }) _numbers[key].Text = value.ToString("G17", CultureInfo.CurrentCulture);
            _algorithm.SelectedIndex = Array.IndexOf(AlgorithmNames, s.Optimizer);
            _distribution.SelectedIndex = (int)(s.Tolerancing?.Distribution ?? ToleranceDistribution.Uniform);
            _correlation.SelectedIndex = (int)(s.Tolerancing?.Correlation ?? ThicknessCorrelation.Independent);
            _varyStructure.IsChecked = s.Search?.VaryStructure ?? true;
            _narrowProtocol.SelectedIndex = s.DesignProtocolVersion switch { 4 => 2, 2 or 3 => 1, _ => 0 };
            _stopBands.Text = string.Join(';', (t.StopBands ?? [new SpectralBand(490, 510), new SpectralBand(590, 610)])
                .Select(b => $"{b.MinimumNm.ToString("G17", CultureInfo.InvariantCulture)}-{b.MaximumNm.ToString("G17", CultureInfo.InvariantCulture)}"));
            LoadFilmEditors(d);
            _candidates.ItemsSource = (d.Candidates ?? []).Select(c => c.Name + $" · merit {NumericDisplayFormatter.Format(c.Merit)}").ToArray();
            _candidates.SelectedIndex = d.Candidates is { Length: > 0 } ? 0 : -1;
            _plot.SetExperiment(d);
            _materialSource.Text = string.Join("\n\n", d.Materials.Select(m => m.Name + "\n" + m.Source))
                + (d.Duplex is not null ? $"\n\n相干、各向同性膜层。评价对象：{DesignService.EvaluationLabel(d)}；优化对象：{DesignService.OptimizationLabel(d)}。整片评价含两面膜系、有限基板吸收及非相干内部多次反射。"
                    : "\n\n相干、各向同性膜层。正式设计指标按前表面、半无限基板验收，T 为进入基板的功率。整片显示可计入有限基板吸收、裸背面及非相干内部多次反射，T 为进入出射介质的功率。")
                + "OD 显示上限 12，真实数值不截断。";
            _summary.Text = Summary(d);
            RefreshStackGeometry();
            RefreshLayerOpticalThickness();
            RefreshLayerEditorPresentation();
            Visibility(); _status.Text = d.Result is null ? "示例 / 实验已打开，请生成膜系或计算。" : "计算已完成，请查看逐项指标。";
        }
        finally { _loading = false; _hasUnsavedChanges = markDirty; }
        RecordEditorHistory(null);
    }

    private static string Summary(Experiment d)
    {
        if (d.Result is not { } r)
        {
            try { return "尚无当前输入的计算结果。\n材料适用性诊断：\n" + string.Join("\n", new DesignService().AssessMaterials(d).Messages); }
            catch (ArgumentException error) { return "材料适用性诊断：" + error.Message; }
        }
        var text = (d.Duplex is null ? "" : $"优化：{DesignService.OptimizationLabel(d)}；评价：{DesignService.EvaluationLabel(d)}\n")
            + (r.Passed ? "✓ 全部目标达标" : "未全部达标 · 保留当前有效结果") + $"    复验采样 {r.VerificationSamples} 点 / {(r.SamplingConverged ? "收敛" : "未收敛")}\n";
        if (d.ReferenceExample is { } example && d.Duplex is null)
            text = $"参考原处方：{example.OriginalName}\n基板 {example.SubstrateThicknessMm:G7} mm；出射介质 {example.ExitMediumId}；前面单独评价（忽略背面）。\n"
                + "参考未定义优化验收目标；以下按本软件原默认指标比较。\n" + text;
        foreach (var check in r.Checks)
        {
            var value = check.Actual.HasValue ? (check.Name.Contains("OD", StringComparison.Ordinal) && check.Actual.Value > 12
                ? "≥12（显示饱和）" : NumericDisplayFormatter.Format(check.Actual.Value)) : "—";
            text += $"{(check.Passed ? "✓" : "✗")} {check.Name}：{value}；要求 {check.Requirement}\n";
        }
        if (r.Run.DesignProtocol == DesignService.TargetBalancedProtocol)
            text += "评价：按目标分组平衡（与旧版评价值不直接比较）\n";
        if (r.Run.DesignProtocol == DesignService.NarrowBandCompetitionProtocol)
            text += "评价：实际峰位/FWHM/峰值与截止约束；原密集验收保持（与旧版评价值不直接比较）\n";
        if (r.Run.DesignProtocol == DesignService.GeneralCoatingProtocol)
            text += "评价：可行性优先的通用约束；多个匹配解参与整谱竞争，原验收保持\n";
        if (r.Run.GeneralWork is { } work)
            text += $"指导计算：光谱点 {work.SpectrumEvaluations}，灵敏度点 {work.SensitivityEvaluations}，解析导数批次 {work.AnalyticJacobians}；约束更新 {work.ConstraintPasses} 次，完整复验 {work.DenseValidations} 次\n";
        if (r.Run.NarrowBandCompetition is { } competition)
            text += $"镜段/腔阶：初始 {competition.InitialStructures} 组，实际比较 {competition.DiscreteEvaluations}/{competition.DiscreteEvaluationLimit}；入射侧匹配 {competition.MatchingTrials.Length} 组\n";
        if (r.Run.Screening is { } screens)
            text += $"短程候选 {screens.Length} 组；模板比较 {r.Run.TemplateEvaluations ?? 0} 次\n";
        text += $"算法：{r.Run.Algorithm} / {r.Run.AlgorithmVersion}；停止：{StopReason(r.Run.StopReason)}；评估 {r.Run.Evaluations}\n"
            + string.Join("\n", r.Warnings);
        if (d.Tolerance is { } tolerance) text += $"\n公差：{tolerance.Percent}% {((tolerance.Options?.Distribution ?? ToleranceDistribution.Uniform) == ToleranceDistribution.Normal ? "正态 1σ" : "均匀 ±")}厚度扰动（{tolerance.Options?.Correlation ?? ThicknessCorrelation.Independent}），{tolerance.Passed}/{tolerance.Trials} 达标；种子 {tolerance.Seed}。";
        return text;
    }
    private static string StopReason(string reason) => reason switch
    {
        "MaximumIterations" => "达到迭代上限",
        "GradientTolerance" => "梯度达到停止阈值",
        "StepOrMeritStagnation" => "步长或评价函数停滞",
        "SimplexTolerance" => "单纯形达到停止阈值",
        "ConstraintGuidanceSatisfied" => "指导约束已满足，仍以完整复验为准",
        "DenseValidationRetainedInput" => "完整复验后保留原结构",
        _ => reason
    };
    private void AddRow(FilmLayer layer)
        => _rows.Add(CreateRow(layer));

    private LayerRow CreateRow(FilmLayer layer)
    {
        var row = new LayerRow(layer, id => _session.Current.Materials.FirstOrDefault(m => m.Id == id)?.Name ?? id, MaterialBrushFor);
        row.InputChanged += key => Changed($"row:{row.GetHashCode()}:{key}");
        return row;
    }
    private void Renumber()
    {
        var wanted = _rows.Reverse().ToArray();
        for (var i = 0; i < wanted.Length; i++)
        {
            wanted[i].Number = i + 1;
            if (i < _displayRows.Count && ReferenceEquals(_displayRows[i], wanted[i])) continue;
            var old = _displayRows.IndexOf(wanted[i]);
            if (old >= 0) _displayRows.Move(old, i); else _displayRows.Insert(i, wanted[i]);
        }
        while (_displayRows.Count > wanted.Length) _displayRows.RemoveAt(_displayRows.Count - 1);
        RefreshLayerEditorPresentation();
    }
    private Task EditLayer(string action)
    {
        EditSelectedLayers(action); return Task.CompletedTask;
    }
    private Task SelectCandidateAsync()
    {
        var d = ReadInputs(); var index = _candidates.SelectedIndex;
        if (index < 0 || d.Candidates is null || index >= d.Candidates.Length) throw new ArgumentException("请先生成并选择候选。");
        Load(d with { Layers = d.Candidates[index].Layers, BackLayers = d.Candidates[index].BackLayers, Result = null, Tolerance = null });
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
            if (revision == _uiRevision) _status.Text = $"{p.Stage}：{p.Evaluations}" + (p.BestMerit.HasValue ? $"，评价值 {NumericDisplayFormatter.Format(p.BestMerit.Value)}" : "");
        });
        try
        {
            var applied = await _session.RunAsync((snapshot, token) => Task.Run(() => action switch
            {
                "generate" => _service.Generate(snapshot, progress, token),
                "optimize" => _service.Optimize(snapshot, progress, token),
                "search" => _service.Search(snapshot, progress, token),
                "tolerance" => RunTolerance(snapshot, token),
                _ => _service.Evaluate(snapshot, progress: progress, token: token)
            }, token));
            if (applied && revision == _uiRevision)
            {
                Load(_session.Current);
                await _plot.PendingEvaluation;
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
        _busy = value;
        UpdatePlotState();
        _operationStatus.IsVisible = value;
        if (value) _operationStatus.Start("正在计算镀膜设计…", () => _session.Cancel());
        else _operationStatus.MarkIdle(_status.Text ?? "计算已结束");
        foreach (var button in _computeButtons) ControlAvailability.Set(button, !value, "已有后台计算，请等待完成或先取消。");
        ControlAvailability.Set(_cancel!, value, "当前没有正在运行的计算。");
    }
    private async Task SaveAsync() => await TrySaveAsync();

    private async Task<bool> TrySaveAsync()
    {
        var d = ReadInputs();
        var revision = _uiRevision;
        string? path;
        if (_dialogs is not null) path = await _dialogs.Save();
        else
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "保存镀膜实验", SuggestedFileName = d.Name + ".coating.json", DefaultExtension = "coating.json" });
            path = file?.TryGetLocalPath();
        }
        if (path is null) return false;
        await ExperimentStore.SaveAsync(d, path);
        if (revision != _uiRevision)
        {
            _status.Text = "已保存所选快照，但保存期间有新的修改，请再次保存。";
            return false;
        }
        _hasUnsavedChanges = false;
        ScheduleRecovery();
        _status.Text = "已保存：" + path;
        return true;
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
        if (!await ConfirmUnsavedChangesAsync("打开另一份镀膜实验")) return;
        _session.Invalidate(); _plot.SetExperiment(null); _summary.Text = "正在打开并重算材料快照…";
        var revision = ++_uiRevision;
        var applied = await _session.RunAsync((_, token) => ExperimentStore.OpenAsync(path, token));
        if (applied && revision == _uiRevision)
        { _historyCurrent = null; _undo.Clear(); _redo.Clear(); _historyKey = null; Load(_session.Current, markDirty: false); await _plot.PendingEvaluation; }
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
