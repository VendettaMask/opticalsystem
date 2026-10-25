using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.App.Laboratories.InitialStructure;

public sealed partial class MainWindow : Window
{
    private readonly LabDesktopSettings _settings;
    private readonly FlatStartRunLibrary _library;
    private readonly ObservableCollection<CandidateRow> _rows = [];
    private readonly Grid _main = new() { ColumnSpacing = 14, RowSpacing = 12 };
    private readonly Grid _workspace = new() { RowDefinitions = new("228,*"), RowSpacing = 12 };
    private readonly ScrollViewer _mainScroll = new();
    private readonly ScrollViewer _form = new();
    private readonly CandidatePreviewControl _preview = Named(new CandidatePreviewControl(), "Preview", "候选镜头实际光线比较图");
    private readonly TextBlock _previewCaption = Text("选择方案查看实际布局与光线。");
    private readonly TextBlock _summary = Text("尚未生成方案。输入设计目标后开始。");
    private readonly TextBlock _status = Named(Text("就绪"), "Status", "实验室状态");
    private readonly TextBlock _messages = Named(Text(""), "Messages", "材料与运行提示");
    private readonly TextBlock _selectionDetails = Text("");
    private readonly TextBlock _comparisonDetails = Text("A 为蓝色，B 为橙色；共用毫米比例。");
    private Border _candidateEmpty = null!;
    private Border _previewEmpty = null!;
    private readonly OperationStatusBar _operationStatus = new();
    private readonly Button _validateButton = Command("预检查", "Validate");
    private readonly Button _runButton = Command("开始形式搜索", "Generate");
    private readonly Button _resumeButton = Command("恢复上次", "Resume");
    private readonly Button _openButton = Command("打开记录", "OpenRun");
    private readonly Button _cancelButton = Command("停止并保存", "Cancel");
    private readonly Button _refineButton = Command("细化选中方案", "Refine");
    private readonly Button _exportButton = Command("导出结构", "Export");
    private readonly Button _compareAButton = Command("设为 A", "CompareA");
    private readonly Button _compareBButton = Command("设为 B", "CompareB");
    private readonly Button _clearComparisonButton = Command("清除比较", "ClearComparison");
    private readonly DataGrid _candidateGrid;
    private readonly TextBlock _searchPlan = Named(Text("预检查可查看正负形式覆盖范围和每个起始试验的预算。"), "SearchPlan", "形式搜索计划");
    private readonly DataGrid _searchHistory = GridTable("SearchHistory", ("试验", "Trial", 92), ("起始形式", "Form", 95), ("步骤", "Operation", 180), ("状态", "State", 90), ("评价次数", "Evaluations", 85));
    private readonly DataGrid _targetGrid = GridTable("TargetTable", ("指标", "Name", 175), ("目标", "Target", 125), ("实际", "Actual", 115), ("结果", "State", 90));
    private readonly DataGrid _prescriptionGrid = GridTable("PrescriptionTable", ("面", "Surface", 45), ("曲率半径 mm", "Radius", 120),
        ("厚度 mm", "Thickness", 120), ("玻璃", "Material", 100), ("净半口径 mm", "SemiDiameter", 120), ("机械半径 mm", "Mechanical", 120));
    private readonly DataGrid _fieldGrid = GridTable("FieldTable", ("半视场 °", "Field", 85), ("RMS mm", "Rms", 110),
        ("最大半径 mm", "Maximum", 115), ("通光", "Transmission", 85), ("逐波长有效/总数", "Wavelengths", 300));

    public MainWindow() : this(new LabDesktopSettings()) { }
    internal MainWindow(LabDesktopSettings settings)
    {
        _settings = settings;
        _library = new(settings.RunDirectory);
        Title = "智能初始结构实验室";
        Width = 1240; Height = 850; MinWidth = 480; MinHeight = 620;
        _candidateGrid = GridTable("Candidates", ("方案", "Name", 82), ("起始形式", "InitialForm", 104), ("状态", "Status", 88), ("片数", "Elements", 64),
            ("焦距 mm", "FocalLength", 100), ("最差 RMS mm", "Rms", 120), ("最低通光", "Transmission", 96));
        _candidateGrid.ItemsSource = _rows;
        Content = BuildContent();
        _runButton.Classes.Add("accent");
        _refineButton.Classes.Add("accent");
        WireEvents();
        if (settings.InitialSpecification is { } specification) ApplySpecification(specification, settings.InitialOptions ?? new());
        else
        {
            var reference = AutoLensReferenceExamples.Mobile();
            ApplySpecification(reference.Specification, reference.Options);
        }
        ApplyResponsiveLayout(Width);
        UpdateCommands();
        Ready = Task.WhenAll(RefreshResumeAvailabilityAsync(), RefreshGpuStatusAsync());
        Icon = BrandAssets.LoadWindowIcon();
        DisplayTypography.Apply(this);
    }

    internal Task Ready { get; }
    internal Task PendingOperation { get; private set; } = Task.CompletedTask;
    internal FlatStartSearchCheckpoint? CurrentCheckpoint => _current;

    private Control BuildContent()
    {
        var root = Named(new Grid { RowDefinitions = new("Auto,Auto,*,Auto"), RowSpacing = 12, Background = Brushes.Transparent }, "InitialStructureDesign", "自动初始结构工作区");
        root.BindThemeResource(Panel.BackgroundProperty, ThemeResourceBindings.Workspace);
        ApplyPresentationStyles(root);
        var header = new StackPanel { Spacing = 5, Margin = new Thickness(18, 16, 18, 0) };
        header.Children.Add(SectionHeading("自动初始结构", "scan-line", DisplayTypography.PageTitle));
        header.Children.Add(Hint("定义系统与目标 → 正负形式搜索 → 比较候选 → 继续优化"));
        root.Children.Add(header);
        var commands = Buttons(_runButton, _validateButton, _cancelButton, _resumeButton, _openButton);
        commands.Margin = new Thickness(18, 0, 12, 0);
        Grid.SetRow(commands, 1); root.Children.Add(commands);

        _form.Content = BuildInputs();
        _form.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _form.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _main.Children.Add(Frame(_form));
        var candidates = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 8, Margin = new Thickness(12) };
        var candidateHeading = new WrapPanel();
        candidateHeading.Children.Add(SectionHeading("候选方案", "list-filter"));
        var resultActions = Buttons(_refineButton, _exportButton);
        resultActions.Margin = new Thickness(16, 0, 0, 0);
        candidateHeading.Children.Add(resultActions);
        candidates.Children.Add(candidateHeading);
        var candidateBody = new Grid();
        candidateBody.Children.Add(_candidateGrid);
        _candidateEmpty = EmptyState("CandidateEmpty", "等待生成候选", "完成左侧设置后，先预检查，再开始形式搜索。", "layers");
        _candidateEmpty.Margin = new Thickness(0, 34, 0, 0);
        candidateBody.Children.Add(_candidateEmpty);
        Grid.SetRow(candidateBody, 1); candidates.Children.Add(candidateBody);
        _summary.FontSize = DisplayTypography.BodySmall;
        _summary.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextSecondary);
        Grid.SetRow(_summary, 2); candidates.Children.Add(_summary);
        _workspace.Children.Add(Frame(candidates));
        var tabs = Named(CompactTabs(), "DetailsTabs", "候选详情");
        var drawing = new Grid { RowDefinitions = new("Auto,*,Auto,Auto"), RowSpacing = 6, Margin = new Thickness(12, 8, 12, 10) };
        drawing.Children.Add(Buttons(_compareAButton, _compareBButton, _clearComparisonButton));
        var previewBody = new Grid();
        previewBody.Children.Add(_preview);
        _previewEmpty = EmptyState("PreviewEmpty", "在这里查看实际光线", "选择候选方案查看结构；设置 A / B 可在同一比例下比较。", "scan-line");
        previewBody.Children.Add(_previewEmpty);
        Grid.SetRow(previewBody, 1); drawing.Children.Add(previewBody);
        foreach (var caption in new[] { _previewCaption, _comparisonDetails })
        {
            caption.FontSize = DisplayTypography.CompactBody;
            caption.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextMuted);
        }
        Grid.SetRow(_previewCaption, 2); drawing.Children.Add(_previewCaption);
        Grid.SetRow(_comparisonDetails, 3); drawing.Children.Add(_comparisonDetails);
        tabs.Items.Add(DetailTab("结构与光线", drawing));
        tabs.Items.Add(DetailTab("目标对照", _targetGrid));
        tabs.Items.Add(DetailTab("处方", _prescriptionGrid));
        tabs.Items.Add(DetailTab("视场明细", _fieldGrid));
        tabs.Items.Add(DetailTab("来源与差距", new ScrollViewer { Content = _selectionDetails, Margin = new Thickness(12), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }));
        var search = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 8, Margin = new Thickness(12) };
        _searchPlan.FontSize = DisplayTypography.BodySmall;
        search.Children.Add(_searchPlan);
        Grid.SetRow(_searchHistory, 1); search.Children.Add(_searchHistory);
        tabs.Items.Add(DetailTab("搜索计划与过程", search));
        var details = Frame(tabs);
        Grid.SetRow(details, 1); _workspace.Children.Add(details);
        _main.Children.Add(_workspace);
        _main.Margin = new Thickness(16, 0);
        _mainScroll.Content = _main;
        _mainScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Grid.SetRow(_mainScroll, 2); root.Children.Add(_mainScroll);
        var footer = new StackPanel { Spacing = 3, Margin = new Thickness(18, 0, 18, 10) };
        _status.FontSize = DisplayTypography.BodySmall;
        _messages.FontSize = DisplayTypography.CompactBody;
        _messages.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextWarning);
        _operationStatus.IsVisible = false;
        footer.Children.Add(_status);
        footer.Children.Add(new ScrollViewer { Content = _messages, MaxHeight = 70, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        footer.Children.Add(_operationStatus);
        Grid.SetRow(footer, 3); root.Children.Add(footer);
        return root;
    }

    private void ApplyResponsiveLayout(double width)
    {
        var narrow = width < 820;
        _main.ColumnDefinitions = new(narrow ? "*" : width < 1100 ? "328,*" : "356,*");
        _main.RowDefinitions = new(narrow ? "Auto,Auto" : "*");
        Grid.SetColumn(_workspace, narrow ? 0 : 1);
        Grid.SetRow(_workspace, narrow ? 1 : 0);
        // The frame, not the inner tab page, owns the available height.
        if (_form.Parent is Control frame) frame.Height = narrow ? 380 : double.NaN;
        _workspace.Height = narrow ? 640 : double.NaN;
        _mainScroll.VerticalScrollBarVisibility = narrow ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
    }

    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap };
    private static T Named<T>(T control, string id, string name) where T : Control
    {
        control.Name = id;
        control.SetValue(AutomationProperties.NameProperty, name);
        return control;
    }
    private static Button Command(string label, string id) => Named(new Button { Content = label, FontSize = DisplayTypography.BodySmall, MinHeight = 34, Padding = new Thickness(12, 6), Margin = new Thickness(0, 0, 6, 4) }, id, label);
    private static WrapPanel Buttons(params Button[] buttons)
    {
        var panel = new WrapPanel();
        foreach (var button in buttons) panel.Children.Add(button);
        return panel;
    }
    private static DataGrid GridTable(string id, params (string Header, string Property, int Width)[] columns)
    {
        var grid = Named(new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserResizeColumns = true,
            CanUserSortColumns = false,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            SelectionMode = DataGridSelectionMode.Single,
            FontSize = DisplayTypography.BodySmall, RowHeight = 34
        }, id, id);
        foreach (var column in columns) grid.Columns.Add(new DataGridTextColumn { Header = column.Header, Binding = new Binding(column.Property), Width = new DataGridLength(column.Width) });
        return grid;
    }
}
