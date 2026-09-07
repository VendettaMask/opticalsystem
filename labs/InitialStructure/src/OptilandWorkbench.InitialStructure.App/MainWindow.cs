using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Persistence;

namespace OptilandWorkbench.InitialStructure.App;

public sealed partial class MainWindow : Window
{
    private readonly LabDesktopSettings _settings;
    private readonly FlatStartRunLibrary _library;
    private readonly ObservableCollection<CandidateRow> _rows = [];
    private readonly Grid _main = new() { ColumnSpacing = 14, RowSpacing = 12 };
    private readonly Grid _workspace = new() { RowDefinitions = new("Auto,190,*"), RowSpacing = 8 };
    private readonly ScrollViewer _mainScroll = new();
    private readonly ScrollViewer _form = new();
    private readonly CandidatePreviewControl _preview = Named(new CandidatePreviewControl(), "Preview", "候选镜头实际光线比较图");
    private readonly TextBlock _previewCaption = Text("选择方案查看实际布局与光线。");
    private readonly TextBlock _summary = Text("尚未生成方案。输入设计目标后开始。");
    private readonly TextBlock _status = Named(Text("就绪"), "Status", "实验室状态");
    private readonly TextBlock _messages = Named(Text(""), "Messages", "材料与运行提示");
    private readonly TextBlock _selectionDetails = Text("");
    private readonly TextBlock _comparisonDetails = Text("A 为蓝色，B 为橙色；共用毫米比例。");
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Height = 4 };
    private readonly Button _validateButton = Command("预检查", "Validate");
    private readonly Button _runButton = Command("从平板生成", "Generate");
    private readonly Button _resumeButton = Command("恢复上次", "Resume");
    private readonly Button _openButton = Command("打开记录", "OpenRun");
    private readonly Button _cancelButton = Command("停止并保存", "Cancel");
    private readonly Button _refineButton = Command("细化选中方案", "Refine");
    private readonly Button _exportButton = Command("导出结构", "Export");
    private readonly Button _compareAButton = Command("设为 A", "CompareA");
    private readonly Button _compareBButton = Command("设为 B", "CompareB");
    private readonly Button _clearComparisonButton = Command("清除比较", "ClearComparison");
    private readonly DataGrid _candidateGrid;
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
        _candidateGrid = GridTable("Candidates", ("方案", "Name", 82), ("状态", "Status", 88), ("片数", "Elements", 64),
            ("焦距 mm", "FocalLength", 100), ("最差 RMS mm", "Rms", 120), ("最低通光", "Transmission", 96));
        _candidateGrid.ItemsSource = _rows;
        Content = BuildContent();
        WireEvents();
        if (settings.InitialSpecification is { } specification) ApplySpecification(specification, settings.InitialOptions ?? new());
        ApplyResponsiveLayout(Width);
        UpdateCommands();
        Ready = RefreshResumeAvailabilityAsync();
    }

    internal Task Ready { get; }
    internal Task PendingOperation { get; private set; } = Task.CompletedTask;
    internal FlatStartSearchCheckpoint? CurrentCheckpoint => _current;

    private Control BuildContent()
    {
        var root = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new Thickness(16, 12), RowSpacing = 12 };
        var header = new StackPanel { Spacing = 9 };
        header.Children.Add(new TextBlock { Text = "从平板生成定焦镜头", FontSize = 23, FontWeight = FontWeight.SemiBold });
        header.Children.Add(Text("设置设计目标，生成并验证可继续设计的球面结构。"));
        header.Children.Add(Buttons(_validateButton, _runButton, _resumeButton, _openButton, _cancelButton, _refineButton, _exportButton));
        root.Children.Add(header);

        _form.Content = BuildInputs();
        _form.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _form.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _main.Children.Add(_form);
        _workspace.Children.Add(_summary);
        Grid.SetRow(_candidateGrid, 1);
        _workspace.Children.Add(_candidateGrid);
        var tabs = Named(new TabControl(), "DetailsTabs", "候选详情");
        var drawing = new Grid { RowDefinitions = new("Auto,*,Auto,Auto"), RowSpacing = 6, MinHeight = 240 };
        drawing.Children.Add(Buttons(_compareAButton, _compareBButton, _clearComparisonButton));
        Grid.SetRow(_preview, 1); drawing.Children.Add(_preview);
        Grid.SetRow(_previewCaption, 2); drawing.Children.Add(_previewCaption);
        Grid.SetRow(_comparisonDetails, 3); drawing.Children.Add(_comparisonDetails);
        tabs.Items.Add(new TabItem { Header = "结构与光线", Content = drawing });
        tabs.Items.Add(new TabItem { Header = "目标对照", Content = _targetGrid });
        tabs.Items.Add(new TabItem { Header = "处方", Content = _prescriptionGrid });
        tabs.Items.Add(new TabItem { Header = "视场明细", Content = _fieldGrid });
        tabs.Items.Add(new TabItem { Header = "来源与差距", Content = new ScrollViewer { Content = _selectionDetails } });
        Grid.SetRow(tabs, 2); _workspace.Children.Add(tabs);
        _main.Children.Add(_workspace);
        _mainScroll.Content = _main;
        _mainScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Grid.SetRow(_mainScroll, 1); root.Children.Add(_mainScroll);
        var footer = new StackPanel { Spacing = 5 };
        footer.Children.Add(_status);
        footer.Children.Add(new ScrollViewer { Content = _messages, MaxHeight = 70, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        footer.Children.Add(_progress);
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        return root;
    }

    private void ApplyResponsiveLayout(double width)
    {
        var narrow = width < 820;
        _main.ColumnDefinitions = new(narrow ? "*" : "300,*");
        _main.RowDefinitions = new(narrow ? "Auto,Auto" : "*");
        Grid.SetColumn(_workspace, narrow ? 0 : 1);
        Grid.SetRow(_workspace, narrow ? 1 : 0);
        _form.Height = narrow ? 270 : double.NaN;
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
    private static Button Command(string label, string id) => Named(new Button { Content = label, Padding = new Thickness(12, 7), Margin = new Thickness(0, 0, 6, 4) }, id, label);
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
            SelectionMode = DataGridSelectionMode.Single
        }, id, id);
        foreach (var column in columns) grid.Columns.Add(new DataGridTextColumn { Header = column.Header, Binding = new Binding(column.Property), Width = new DataGridLength(column.Width) });
        return grid;
    }
}
