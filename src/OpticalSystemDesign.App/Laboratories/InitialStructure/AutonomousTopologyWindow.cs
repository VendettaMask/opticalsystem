using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

namespace OpticalSystemDesign.App.Laboratories.InitialStructure;

internal sealed record AutonomousDesktopSettings
{
    public string RunDirectory { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpticalSystemDesign", "Labs", "InitialStructure", "autonomous", "runs");
    public AutonomousSearchPlan? InitialPlan { get; init; }
    public Func<Window, string, Task<string?>>? OpenPicker { get; init; }
    public Func<Window, string, Task<string?>>? SavePicker { get; init; }
}

public sealed partial class AutonomousTopologyWindow : Window
{
    private readonly AutonomousDesktopSettings _settings;
    private readonly AutonomousDesktopSession _session;
    private AutonomousSearchPlan _draft;
    private readonly ObservableCollection<TopologyCandidateRow> _candidateRows = [];
    private readonly ObservableCollection<TopologyEventRow> _eventRows = [];
    private readonly ObservableCollection<TopologyPrescriptionRow> _prescriptionRows = [];
    private readonly ObservableCollection<TopologyFieldRow> _fieldRows = [];
    private readonly Dictionary<string, NumericUpDown> _numbers = [];
    private readonly Dictionary<string, CheckBox> _choices = [];
    private readonly Dictionary<string, Button> _commands = [];
    private readonly Dictionary<DataGridColumn, string> _columnTitles = [];
    private readonly TextBlock _source = Text("尚未载入起点");
    private readonly TextBlock _status = Named(Text("就绪"), "AutonomousStatus");
    private readonly TextBlock _budget = Named(Text("尚未消耗预算"), "AutonomousBudget");
    private readonly TextBlock _details = Named(Text("选择候选查看独立验收结果。"), "AutonomousDetails");
    private readonly TextBlock _error = Named(Text(""), "AutonomousError");
    private readonly TextBlock _record = Named(Text(""), "AutonomousRecordPath");
    private readonly StackPanel _inputs = new() { Spacing = 10, Margin = new Thickness(12) };
    private readonly Grid _body = new() { ColumnDefinitions = new("320,*"), ColumnSpacing = 12, RowSpacing = 12 };
    private readonly ScrollViewer _form = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    private readonly Grid _results = new() { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 8 };
    private readonly ScrollViewer _page = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    private readonly DataGrid _candidates;
    private ScrollViewer? _detailsHost;
    private bool _uiOperation, _closed, _allowClose, _closeRequested, _applyingPlan, _planEdited, _operationFailed;
    private bool? _narrow;
    private int _generation;
    private long _displayRevision = -1;
    private string? _displayRun;

    public AutonomousTopologyWindow() : this(new AutonomousDesktopSettings()) { }
    internal AutonomousTopologyWindow(AutonomousDesktopSettings settings)
    {
        _settings = settings; _session = new(settings.RunDirectory); _draft = settings.InitialPlan ?? AutonomousDesignExamples.ThreeElement();
        Title = "自主拓扑设计"; Width = 1240; Height = 850; MinWidth = 480; MinHeight = 620;
        Icon = BrandAssets.LoadWindowIcon();
        _candidates = Table("AutonomousCandidates", ("候选", "Id", 170), ("来源", "Parent", 125), ("验收", "Status", 115),
            ("片数", "Elements", 60), ("焦距 mm", "Focal", 100), ("F 数", "FNumber", 80), ("最差 RMS µm", "Rms", 115), ("最低通光", "Throughput", 100));
        _candidates.ItemsSource = _candidateRows;
        _candidates.SelectionChanged += (_, _) => ShowCandidate();
        Content = Build();
        ApplyPlan(_draft);
        SizeChanged += (_, _) => Responsive();
        Closing += (_, args) =>
        {
            if (_allowClose || !IsBusy) return;
            args.Cancel = true;
            _closeRequested = true; Cancel();
        };
        Closed += (_, _) => { _closed = true; _generation++; CancelStructureLoad(); };
        UiLocalization.LanguageChanged += LanguageChanged;
        Closed += (_, _) => UiLocalization.LanguageChanged -= LanguageChanged;
        DisplayTypography.Apply(this); UiLocalization.Attach(this);
        Responsive(); Refresh();
    }

    internal bool IsBusy => _uiOperation || _session.IsBusy;
    internal Task PendingOperation { get; private set; } = Task.CompletedTask;
    internal AutonomousSearchCheckpoint? CurrentCheckpoint => _session.Checkpoint;

    private Control Build()
    {
        var root = Named(new Grid { RowDefinitions = new("Auto,Auto,*,Auto"), RowSpacing = 10, Margin = new Thickness(16) }, "AutonomousWorkspace");
        root.BindThemeResource(Panel.BackgroundProperty, ThemeResourceBindings.Workspace);
        var heading = new StackPanel { Spacing = 4 };
        heading.Children.Add(new TextBlock { Text = "自主拓扑设计", FontSize = DisplayTypography.PageTitle, FontWeight = FontWeight.SemiBold });
        heading.Children.Add(Text("无限远处方 → 设置目标与预算 → 运行并保存 → 检查候选与失败原因"));
        root.Children.Add(heading);
        var bar = new WrapPanel { Orientation = Orientation.Horizontal };
        Add("AutonomousNew", "新建示例任务", NewDraft);
        Add("AutonomousImport", "导入起点", ImportAsync);
        Add("AutonomousPrepareSeeds", "生成一阶起点", PrepareSeedsAsync);
        Add("AutonomousOpenPlan", "打开设计计划", OpenPlanAsync);
        Add("AutonomousSavePlan", "保存设计计划", SavePlanAsync);
        Add("AutonomousOpenRecord", "打开自主记录", OpenRecordAsync);
        Add("AutonomousCheck", "检查输入", CheckAsync);
        Add("AutonomousRun", "开始自主设计", () => RunAsync(false), accent: true);
        Add("AutonomousResume", "继续自主任务", () => RunAsync(true), accent: true);
        Add("AutonomousCancel", "停止并保存", () => { Cancel(); return Task.CompletedTask; });
        Add("AutonomousCopy", "复制运行记录", CopyRecordAsync);
        Add("AutonomousExport", "导出结构", ExportAsync);
        Grid.SetRow(bar, 1); root.Children.Add(bar);
        _inputs.Children.Add(Text("当前自主流程使用 CPU。起点与记录的载入不会运行。"));
        _inputs.Children.Add(_source);
        Number("AutonomousFocal", "目标焦距 (mm)", 50, .001m, 100000);
        Number("AutonomousFNumber", "目标 F 数", 12.5m, .01m, 1000);
        Number("AutonomousField", "最大半视场 (°)", 5, 0, 125);
        Number("AutonomousStop", "光阑净半口径 (mm)", 2, .001m, 10000);
        Number("AutonomousRms", "RMS 上限 (µm)", 50, .000001m, 1_000_000);
        Number("AutonomousMaximum", "最大光斑半径 (µm)", 150, .000001m, 1_000_000);
        Choice("AutonomousImage", "允许移动像面", false);
        Number("AutonomousMinElements", "最少镜片数", 1, 1, 16);
        Number("AutonomousMaxElements", "最多镜片数", 8, 1, 16);
        Number("AutonomousTrials", "结构决策上限", 12, 1, 128);
        Number("AutonomousFunctions", "评价尝试上限", 1000, 2, 100000);
        Number("AutonomousGradients", "光线梯度尝试上限", 200, 0, 100000);
        Number("AutonomousRays", "总追迹上限", 2_000_000, 1, 1_000_000_000);
        Number("AutonomousReserve", "终验预留追迹", 200000, 1, 100_000_000);
        Number("AutonomousMinutes", "运行时限 (分钟)", 5, .01m, 240);
        Number("AutonomousSeed", "搜索种子", 42, 0, int.MaxValue);
        Choice("AutonomousDiversity", "保留不同结构与玻璃组合", false);
        Choice("AutonomousPopulation", "群体探索与局部优化交替", false);
        Choice("AutonomousBounded", "筛检候选并有界停止", false);
        Number("AutonomousLayerLimit", "每片数层尝试上限", 3, 1, 128);
        Number("AutonomousStagnation", "连续无改进尝试上限", 2, 1, 128);
        Choice("AutonomousGeometryScreen", "重生候选几何筛检", false);
        Number("AutonomousPoolSize", "每种片数保留候选数", 4, 1, 8);
        _inputs.Children.Add(Text("一阶起点准备（独立预算）"));
        Choice("AutonomousDescriptor", "按结构描述生成新处方", false);
        Choice("AutonomousDelano", "使用 Delano 一阶搜索", false);
        Choice("AutonomousThickDelano", "按实际厚度与固定光阑构造 Delano 起点", false);
        Choice("AutonomousFixedStopRecovery", "独立恢复固定光阑入瞳", false);
        Choice("AutonomousImageScreening", "按真实像差筛选并排序起点", false);
        Number("AutonomousScreenRays", "每候选像差筛选追迹上限", 2500, 1, 1000000);
        Number("AutonomousGeneratedElements", "生成镜片数", 3, 2, 16);
        Number("AutonomousGeneratedStop", "光阑前镜片数", 0, 0, 16);
        Number("AutonomousGeneratedRadius", "生成净半口径 (mm)", 5, .001m, 1000);
        Number("AutonomousGeneratedMinThickness", "生成最小中心厚度 (mm)", 1, .001m, 1000);
        Number("AutonomousGeneratedMaxThickness", "生成最大中心厚度 (mm)", 4, .001m, 1000);
        Number("AutonomousGeneratedBackFocus", "生成最小后焦 (mm)", 5, .001m, 10000);
        Number("AutonomousAnnealing", "Delano 退火提议上限", 8, 0, 100);
        Number("AutonomousSeedStarts", "一阶起点尝试上限", 12, 1, 64);
        Number("AutonomousSeedRetained", "一阶保留起点数", 4, 1, 8);
        Number("AutonomousSeedFunctions", "一阶评价尝试上限", 300, 2, 10000);
        Number("AutonomousSeedPerStart", "每起点评价上限", 40, 2, 200);
        Number("AutonomousSeedSeconds", "一阶准备时限 (秒)", 15, .01m, 120);
        _inputs.Children.Add(_seedSummary);
        Choice("AutonomousStructure", "允许改变片数", true);
        Choice("AutonomousReductionOnly", "仅从达标处方减片", false);
        Choice("AutonomousGradual", "逐步移除难删镜片", true);
        Choice("AutonomousSplit", "允许拆片", true);
        Choice("AutonomousMerge", "允许并片", true);
        Choice("AutonomousMaterial", "允许目录材料分支", true);
        Choice("AutonomousProfile", "允许偶次面型分支", true);
        Choice("AutonomousPrecision", "高精度训练采样", true);
        Choice("AutonomousAxial", "允许镜面轴向微调", true);
        Number("AutonomousAxialWindow", "每次试验轴向移动上限 (mm)", .25m, .000001m, 10);
        _inputs.Children.Add(Text("恢复与连续补偿"));
        Choice("AutonomousMatrixDerivatives", "恢复矩阵自动求导", false);
        Choice("AutonomousGeometryRecovery", "沿制造约束恢复", false);
        Choice("AutonomousPupilRecovery", "恢复固定光阑瞳倍率", false);
        Choice("AutonomousRecoveryAxial", "恢复阶段允许轴向微调", false);
        Choice("AutonomousConic", "独立锥常数补偿", false);
        Choice("AutonomousStageChecks", "阶段间独立验收", false);
        Choice("AutonomousProtectLocalBudget", "恢复后保留局部补偿额度", false);
        Choice("AutonomousProtectFullBudget", "优先保留全目标补偿额度", false);
        Number("AutonomousLocalUpdates", "局部补偿更新上限", 8, 0, 400);
        Number("AutonomousFullUpdates", "全目标更新上限", 12, 0, 400);
        _form.Content = _inputs; _body.Children.Add(_form);
        _results.Children.Add(_budget);
        var tabs = Named(new TabControl
        {
            ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel()),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        }, "AutonomousTabs");
        tabs.Items.Add(new TabItem { Header = "候选与验收", Content = _candidates });
        var history = Table("AutonomousEvents", ("序号", "Sequence", 60), ("试验", "Trial", 110), ("父方案", "Parent", 130),
            ("操作", "Operation", 110), ("阶段", "Phase", 100), ("结果", "Outcome", 170), ("评价 / 光线梯度 / 追迹", "Work", 210),
            ("恢复矩阵 / Jacobian / 差分", "RecoveryWork", 220), ("瞳查询", "PupilWork", 90), ("原因", "Detail", 340));
        history.ItemsSource = _eventRows; tabs.Items.Add(new TabItem { Header = "谱系与失败", Content = history });
        var prescription = Table("AutonomousPrescription", ("镜片 / 面", "Id", 230), ("轴向位置 mm", "Position", 130),
            ("材料", "Material", 120), ("面型", "Profile", 130), ("净半口径 mm", "Clear", 130), ("保护", "Protected", 90));
        prescription.Columns.Add(Column("曲率半径 mm", "Radius", 130));
        prescription.Columns.Add(Column("二次曲面常数", "Conic", 130));
        prescription.Columns.Add(Column("偶次系数", "Terms", 300));
        prescription.ItemsSource = _prescriptionRows; tabs.Items.Add(new TabItem { Header = "实际处方", Content = prescription });
        var fields = Table("AutonomousFields", ("视场 X / Y", "Field", 170), ("RMS µm", "Rms", 130), ("最大半径 µm", "Maximum", 150),
            ("有效 / 尝试", "Valid", 140), ("边界 / 中心", "Checks", 140), ("畸变 %", "Distortion", 130));
        fields.ItemsSource = _fieldRows; tabs.Items.Add(new TabItem { Header = "独立视场验收", Content = fields });
        var structureTab = Named(new TabItem { Header = "镜头结构", Content = BuildStructureView() }, "AutonomousStructureTab");
        tabs.Items.Add(structureTab);
        tabs.SelectionChanged += (_, args) =>
        {
            if (!ReferenceEquals(args.Source, tabs)) return;
            _structureSelected = ReferenceEquals(tabs.SelectedItem, structureTab);
            _budget.IsVisible = !_structureSelected;
            if (_detailsHost is { } host) host.IsVisible = !_structureSelected;
            if (_narrow == true) _results.Height = _structureSelected ? 700 : 550;
            if (_structureSelected) StructurePendingLoad = RequestStructureDisplayAsync();
        };
        Grid.SetRow(tabs, 1); _results.Children.Add(tabs);
        var details = new ScrollViewer { Content = _details, MaxHeight = 160, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _detailsHost = details;
        Grid.SetRow(details, 2); _results.Children.Add(details);
        Grid.SetColumn(_results, 1); _body.Children.Add(_results);
        _page.Content = _body; Grid.SetRow(_page, 2); root.Children.Add(_page);
        var footer = new StackPanel { Spacing = 3 }; footer.Children.Add(_status); footer.Children.Add(_record); footer.Children.Add(_error);
        _error.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextWarning);
        Grid.SetRow(footer, 3); root.Children.Add(footer);
        return root;

        void Add(string id, string label, Func<Task> action, bool accent = false)
        {
            var button = Named(new Button { Content = label, Margin = new Thickness(0, 0, 6, 6) }, id);
            AutomationProperties.SetName(button, label);
            if (accent) button.Classes.Add("accent");
            button.Click += (_, _) =>
            {
                if (id == "AutonomousCancel") { Cancel(); return; }
                if (IsBusy) return;
                PendingOperation = PerformAsync(action);
            };
            _commands.Add(id, button); bar.Children.Add(button);
        }
    }

    private void Number(string id, string label, decimal value, decimal minimum, decimal maximum)
    {
        var panel = new Grid { ColumnDefinitions = new("*,130"), ColumnSpacing = 8 };
        panel.Children.Add(Text(label));
        var number = Named(new NumericUpDown { Value = value, Minimum = minimum, Maximum = maximum, FormatString = "0.########", Increment = value >= 100 ? 10 : .1m }, id);
        AutomationProperties.SetName(number, label);
        Grid.SetColumn(number, 1); panel.Children.Add(number); _inputs.Children.Add(panel); _numbers.Add(id, number); number.ValueChanged += (_, _) => { if (!_applyingPlan) MarkPlanEdited(); };
        number.PropertyChanged += (_, args) =>
        {
            if (args.Property == NumericUpDown.TextProperty && !_applyingPlan
                && (!decimal.TryParse(number.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed) || number.Value != parsed)) MarkPlanEdited();
        };
    }
    private void Choice(string id, string label, bool value)
    {
        var control = Named(new CheckBox { Content = label, IsChecked = value }, id); _choices.Add(id, control);
        AutomationProperties.SetName(control, label);
        control.IsCheckedChanged += (_, _) =>
        {
            if (_applyingPlan) return;
            MarkPlanEdited();
            if (id == "AutonomousGeometryScreen" && control.IsChecked == true) _choices["AutonomousPopulation"].IsChecked = true;
            if (id == "AutonomousDelano" && control.IsChecked == true) _choices["AutonomousDescriptor"].IsChecked = true;
            if (id == "AutonomousThickDelano" && control.IsChecked == true)
            {
                _choices["AutonomousDelano"].IsChecked = true;
                _choices["AutonomousFixedStopRecovery"].IsChecked = true;
            }
            if (id == "AutonomousDelano" && control.IsChecked != true) _choices["AutonomousThickDelano"].IsChecked = false;
            if (id == "AutonomousDescriptor" && control.IsChecked != true) _choices["AutonomousDelano"].IsChecked = false;
            if (id == "AutonomousReductionOnly" && control.IsChecked == true)
            {
                _choices["AutonomousPopulation"].IsChecked = false;
                _choices["AutonomousStructure"].IsChecked = true;
                foreach (var branch in new[] { "AutonomousSplit", "AutonomousMerge", "AutonomousMaterial", "AutonomousProfile" })
                    _choices[branch].IsChecked = false;
            }
            if (id is "AutonomousPupilRecovery" or "AutonomousGeometryRecovery" && control.IsChecked == true)
                _choices["AutonomousMatrixDerivatives"].IsChecked = true;
            else if (id == "AutonomousMatrixDerivatives" && control.IsChecked != true)
            {
                _choices["AutonomousPupilRecovery"].IsChecked = false;
                _choices["AutonomousGeometryRecovery"].IsChecked = false;
            }
            Refresh();
        };
        _inputs.Children.Add(control);
    }
    private void Responsive()
    {
        var narrow = Bounds.Width > 0 && Bounds.Width < 850;
        if (_narrow == narrow) return;
        _narrow = narrow;
        _body.ColumnDefinitions = new(narrow ? "*" : "320,*"); _body.RowDefinitions = new(narrow ? "Auto,Auto" : "*");
        _form.Height = narrow ? 300 : double.NaN; _results.Height = narrow ? _structureSelected ? 700 : 550 : double.NaN;
        Grid.SetColumn(_results, narrow ? 0 : 1); Grid.SetRow(_results, narrow ? 1 : 0);
        _page.VerticalScrollBarVisibility = narrow ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
    }
    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap };
    private static T Named<T>(T control, string id) where T : Control
    { control.Name = id; AutomationProperties.SetName(control, id); return control; }
    private DataGrid Table(string id, params (string Title, string Path, double Width)[] columns)
    {
        var grid = Named(new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, MinHeight = 180 }, id);
        foreach (var column in columns) grid.Columns.Add(Column(column.Title, column.Path, column.Width));
        return grid;
    }
    private DataGridTextColumn Column(string title, string path, double width)
    {
        var column = new DataGridTextColumn { Header = UiLocalization.Text(title), Binding = new Binding(path), Width = new DataGridLength(width) };
        _columnTitles.Add(column, title);
        return column;
    }
    private static string Value(double? value, string format = "G8") => value is { } actual && double.IsFinite(actual) ? actual.ToString(format, CultureInfo.CurrentCulture) : UiLocalization.Text("不可用");
    private void LanguageChanged(object? sender, EventArgs args)
    {
        if (_closed) return;
        RefreshSeedSummary();
        foreach (var (column, title) in _columnTitles) column.Header = UiLocalization.Text(title);
        if (_session.Checkpoint is { } state) Publish(state); else Refresh();
    }
}

internal sealed record TopologyCandidateRow(string Id, string Parent, string Status, int Elements, string Focal, string FNumber, string Rms, string Throughput, AutonomousSearchCandidate Candidate);
internal sealed record TopologyEventRow(int Sequence, string Trial, string Parent, string Operation, string Phase, string Outcome, string Work,
    string RecoveryWork, string PupilWork, string Detail);
internal sealed record TopologyPrescriptionRow(string Id, string Position, string Material, string Profile, string Clear, string Protected, string Radius, string Conic, string Terms);
internal sealed record TopologyFieldRow(string Field, string Rms, string Maximum, string Valid, string Checks, string Distortion);
