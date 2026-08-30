using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.App.Panels;

internal sealed class NonSequentialStlImportWindow : Window
{
    private readonly ComboBox _unit = new() { ItemsSource = Enum.GetValues<NonSequentialMeshUnit>(), SelectedIndex = 0 };
    private readonly ComboBox _behavior = new() { ItemsSource = Enum.GetValues<NonSequentialSurfaceBehavior>(), SelectedItem = NonSequentialSurfaceBehavior.Absorbing };
    private readonly ComboBox _material;
    private readonly CheckBox _twoSided = new() { Content = "双面参与相交", IsChecked = true };

    public NonSequentialStlImportWindow(IReadOnlyList<string> materialNames)
    {
        Title = "STL 导入选项";
        Width = 430;
        Height = 330;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _material = new ComboBox { ItemsSource = materialNames, SelectedItem = "Air", MinWidth = 210 };
        var ok = new Button { Content = "导入", MinWidth = 88 };
        ok.Click += (_, _) => Close(new NonSequentialMeshImportOptionsDto(
            (NonSequentialMeshUnit)(_unit.SelectedItem ?? NonSequentialMeshUnit.Millimeter),
            (NonSequentialSurfaceBehavior)(_behavior.SelectedItem ?? NonSequentialSurfaceBehavior.Absorbing),
            Convert.ToString(_material.SelectedItem, CultureInfo.InvariantCulture) ?? "Air",
            _twoSided.IsChecked == true));
        var cancel = new Button { Content = "取消", MinWidth = 88 };
        cancel.Click += (_, _) => Close(null);
        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(18),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "STL 文件本身不保存单位，请明确选择源文件单位。导入后统一换算为毫米。", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                Row("源文件单位", _unit),
                Row("表面交互", _behavior),
                Row("材料", _material),
                _twoSided,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, ok } }
            }
        };
        ThemeChrome.ApplyDialogDecoration(this);
    }

    private static Control Row(string label, Control editor) => new Grid
    {
        ColumnDefinitions = new ColumnDefinitions("120,*"),
        Children =
        {
            new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center },
            Place(editor)
        }
    };

    private static Control Place(Control value)
    {
        Grid.SetColumn(value, 1);
        return value;
    }
}

internal sealed class NonSequentialTraceControlWindow : Window
{
    private readonly INonSequentialAnalysisService _service;
    private readonly ComboBox _source = new();
    private readonly CheckBox _analysis = new() { Content = "使用分析射线数量", IsChecked = true };
    private readonly ComboBox _splitting = new()
    {
        ItemsSource = Enum.GetValues<NonSequentialSplittingMode>(),
        SelectedItem = NonSequentialSplittingMode.FullFresnel
    };
    private readonly TextBox _rayCount = new() { PlaceholderText = "留空则使用光源对象设置" };
    private readonly TextBox _seed = new() { Text = "1" };
    private readonly TextBox _segments = new() { Text = "1000" };
    private readonly TextBox _branches = new() { Text = "1000000" };
    private readonly TextBox _minimumPower = new() { Text = "1e-9" };
    private readonly TextBox _retained = new() { Text = "2000" };
    private readonly TextBox _filter = new() { PlaceholderText = "例如 SEQ(Q1,H3,R3,D8)" };
    private readonly TextBox _path = new() { PlaceholderText = "选择 .starrdb 保存位置" };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private CancellationTokenSource? _cancellation;

    public NonSequentialTraceControlWindow(
        INonSequentialDocumentService documentService,
        INonSequentialAnalysisService service)
    {
        _service = service;
        Title = "非序列追迹控制";
        Width = 600;
        Height = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var sources = documentService.GetDocument().Objects
            .Where(item => item.Enabled && item.Parameters is SourceParameters)
            .Select(item => new SourceChoice(item.Id, $"{item.ObjectNumber} - {item.Name}"))
            .Prepend(new SourceChoice(null, "全部启用光源"))
            .ToArray();
        _source.ItemsSource = sources;
        _source.SelectedIndex = 0;
        var browse = new Button { Content = "浏览…" };
        browse.Click += async (_, _) => await BrowseAsync();
        var pathGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { _path, browse } };
        Grid.SetColumn(browse, 1);
        var clearAndTrace = new Button { Content = "清空并追迹", MinWidth = 100 };
        var traceOnly = new Button { Content = "仅追迹", MinWidth = 88 };
        var clear = new Button { Content = "仅清空", MinWidth = 88 };
        var traceCommands = new[] { clearAndTrace, traceOnly, clear };
        clearAndTrace.Click += async (_, _) => await RunAsync(NonSequentialTraceCommand.ClearAndTrace, traceCommands);
        traceOnly.Click += async (_, _) => await RunAsync(NonSequentialTraceCommand.TraceOnly, traceCommands);
        clear.Click += async (_, _) => await RunAsync(NonSequentialTraceCommand.ClearOnly, traceCommands);
        var cancel = new Button { Content = "取消", MinWidth = 88 };
        cancel.Click += (_, _) => _cancellation?.Cancel();
        var close = new Button { Content = "关闭", MinWidth = 88 };
        close.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(18),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "追迹结果自动保存到受管理的临时数据库，探测器、路径分析和3D布局将读取同一批光线。数据库路径留空即可。", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                Row("光源", _source),
                _analysis,
                Row("分裂模式", _splitting),
                Row("临时覆盖射线数", _rayCount),
                Row("随机种子", _seed),
                Row("每条最大段数", _segments),
                Row("最大活动分支", _branches),
                Row("最小相对能量", _minimumPower),
                Row("最多保留分支", _retained),
                Row("路径筛选", _filter),
                Row("另存数据库", pathGrid),
                _status,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, clear, traceOnly, clearAndTrace, close } }
            }
        };
        ThemeChrome.ApplyDialogDecoration(this);
        ShowSession();
    }

    private async Task BrowseAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存非序列光线数据库",
            SuggestedFileName = "non-sequential-rays.starrdb",
            DefaultExtension = "starrdb",
            FileTypeChoices = new[] { new FilePickerFileType("STAR 光线数据库") { Patterns = new[] { "*.starrdb" } } }
        });
        if (file is not null) _path.Text = file.Path.LocalPath;
    }

    private async Task RunAsync(NonSequentialTraceCommand command, IReadOnlyList<Button> commandButtons)
    {
        if (_cancellation is not null)
        {
            return;
        }
        var retained = 2_000;
        var seed = 1;
        var segments = 1_000;
        var branches = 1_000_000;
        var minimumPower = 1e-9;
        int? rayCount = null;
        if (command != NonSequentialTraceCommand.ClearOnly
            && (!int.TryParse(_retained.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out retained)
                || !int.TryParse(_seed.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed)
                || !int.TryParse(_segments.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out segments)
                || !int.TryParse(_branches.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out branches)
                || !double.TryParse(_minimumPower.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out minimumPower)))
        {
            _status.Text = "射线数、随机种子、限制参数或最小能量格式无效。";
            return;
        }
        if (command != NonSequentialTraceCommand.ClearOnly
            && !string.IsNullOrWhiteSpace(_rayCount.Text))
        {
            if (!int.TryParse(_rayCount.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRayCount))
            {
                _status.Text = "临时覆盖射线数不是有效整数。";
                return;
            }
            rayCount = parsedRayCount;
        }
        foreach (var button in commandButtons) button.IsEnabled = false;
        _status.Text = "正在追迹…";
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        try
        {
            var result = await _service.TraceAsync(new NonSequentialTraceRunRequestDto(
                NonSequentialTraceOutputMode.RayDatabase,
                SourceObjectId: (_source.SelectedItem as SourceChoice)?.Id,
                AnalysisRays: _analysis.IsChecked == true,
                MaximumRetainedBranches: retained,
                PathFilterExpression: string.IsNullOrWhiteSpace(_filter.Text) ? null : _filter.Text,
                RayDatabasePath: string.IsNullOrWhiteSpace(_path.Text) ? null : _path.Text,
                Command: command,
                SplittingMode: (NonSequentialSplittingMode)(_splitting.SelectedItem ?? NonSequentialSplittingMode.FullFresnel),
                RandomSeed: seed,
                MaximumSegmentsPerRay: segments,
                MaximumActiveBranches: branches,
                MinimumRelativeIntensity: minimumPower,
                RayCountOverride: rayCount), cancellation.Token);
            _status.Text = command == NonSequentialTraceCommand.ClearOnly
                ? "探测器和当前追迹结果已清空。"
                : $"{result.SessionState}：本次 {result.TotalBranchCount} 个分支，筛选命中 {result.MatchedBranchCount}，耗时 {result.Elapsed:g}。\n"
                    + $"探测 {result.DetectorPowerWatts:G6} W，吸收 {result.AbsorbedPowerWatts:G6} W，逃逸 {result.EscapedPowerWatts:G6} W。"
                    + (result.RayDatabasePath is null ? string.Empty : $"\n数据库 {result.RayDatabaseBytes:N0} 字节：{result.RayDatabasePath}")
                    + (result.Warnings is { Count: > 0 } ? $"\n{string.Join(" ", result.Warnings)}" : string.Empty);
        }
        catch (OperationCanceledException) { _status.Text = "追迹已取消，上一份有效结果保持不变。"; }
        catch (Exception exception)
        {
            _status.Text = $"追迹失败：{exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
            cancellation.Dispose();
            foreach (var button in commandButtons) button.IsEnabled = true;
        }
    }

    private void ShowSession()
    {
        var session = _service.GetCurrentSession();
        _status.Text = session is null
            ? "尚未生成非序列追迹结果。"
            : $"当前会话：{session.BranchCount:N0} 个分支，{session.TracePassCount} 次追迹"
                + (session.IsStale ? "（结果已过期）" : string.Empty);
    }

    private static Control Row(string label, Control editor)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*"), ColumnSpacing = 8 };
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        return grid;
    }

    private sealed record SourceChoice(Guid? Id, string Label)
    {
        public override string ToString() => Label;
    }
}

internal sealed class NonSequentialRayDatabaseWindow : Window
{
    private readonly INonSequentialAnalysisService _service;
    private readonly string _path;
    private readonly TextBox _filter = new() { PlaceholderText = "Q/H/R/T/D/M/W、A/E/X、! & |、SEQ(...)" };
    private readonly TextBlock _header = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly DataGrid _grid = new() { AutoGenerateColumns = false, IsReadOnly = true };
    private readonly DataGrid _branchGrid = new() { AutoGenerateColumns = false, IsReadOnly = true };
    private readonly TextBox _page = new() { Text = "1", Width = 64 };
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _pageCancellation;
    private bool _closed;
    private int _loadGeneration;
    private int _pageGeneration;

    public NonSequentialRayDatabaseWindow(
        INonSequentialAnalysisService service,
        string path,
        bool showPathAnalysis = false)
    {
        _service = service;
        _path = path;
        Title = "光线数据库与路径分析";
        Width = 1100;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _grid.Columns.Add(Column("射线数", nameof(PathRow.RayCount), 80));
        _grid.Columns.Add(Column("功率 (W)", nameof(PathRow.Power), 110));
        _grid.Columns.Add(Column("占比", nameof(PathRow.Fraction), 80));
        _grid.Columns.Add(Column("终止", nameof(PathRow.Termination), 120));
        _grid.Columns.Add(Column("路径", nameof(PathRow.Path), 420));
        _grid.Columns.Add(Column("筛选表达式", nameof(PathRow.Filter), 300));
        _grid.SelectionChanged += (_, _) =>
        {
            if (_grid.SelectedItem is PathRow row) _filter.Text = row.Filter;
        };
        var apply = new Button { Content = "应用筛选" };
        apply.Click += async (_, _) => await LoadAsync();
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { _filter, apply } };
        Grid.SetColumn(apply, 1);
        _branchGrid.Columns.Add(Column("分支", nameof(BranchRow.Id), 75));
        _branchGrid.Columns.Add(Column("父分支", nameof(BranchRow.ParentId), 75));
        _branchGrid.Columns.Add(Column("层级", nameof(BranchRow.Level), 55));
        _branchGrid.Columns.Add(Column("终止", nameof(BranchRow.Termination), 120));
        _branchGrid.Columns.Add(Column("波长 (nm)", nameof(BranchRow.Wavelength), 100));
        _branchGrid.Columns.Add(Column("功率 (W)", nameof(BranchRow.Power), 100));
        _branchGrid.Columns.Add(Column("逐段对象/交互", nameof(BranchRow.Segments), 600));
        var previous = new Button { Content = "上一页" };
        previous.Click += (_, _) => ChangePage(-1);
        var next = new Button { Content = "下一页" };
        next.Click += (_, _) => ChangePage(1);
        var pageBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
            Children = { previous, new TextBlock { Text = "页码", VerticalAlignment = VerticalAlignment.Center }, _page, next }
        };
        var tabs = new TabControl
        {
            SelectedIndex = showPathAnalysis ? 1 : 0,
            ItemsSource = new object[]
            {
                new TabItem { Header = "光线数据库", Content = new DockPanel { Children = { Dock(pageBar, Avalonia.Controls.Dock.Top), _branchGrid } } },
                new TabItem { Header = "路径分析", Content = _grid }
            }
        };
        Content = new DockPanel
        {
            Margin = new Avalonia.Thickness(12),
            Children = { Dock(_header, Avalonia.Controls.Dock.Top), Dock(top, Avalonia.Controls.Dock.Top), tabs }
        };
        ThemeChrome.ApplyDialogDecoration(this);
        Closed += (_, _) =>
        {
            _closed = true;
            _loadCancellation?.Cancel();
            _pageCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _pageCancellation?.Dispose();
        };
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var generation = ++_loadGeneration;
        _pageGeneration++;
        _pageCancellation?.Cancel();
        _pageCancellation?.Dispose();
        _pageCancellation = null;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        var cancellation = _loadCancellation = new CancellationTokenSource();
        try
        {
            _header.Text = "正在读取光线数据库…";
            var filter = _filter.Text;
            var database = await Task.Run(
                () => _service.InspectRayDatabase(_path, filter, cancellation.Token),
                cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested || generation != _loadGeneration) return;
            _service.SelectRayDatabase(_path, filter);
            _header.Text = $"{database.Path}\n{database.BranchCount:N0} 个分支 · {database.CreatedUtc.LocalDateTime:G}"
                + (database.IsStale ? " · 结果已过期：场景与当前工程不一致" : " · 与当前场景一致")
                + "\n当前筛选已作为非序列 3D 布局和探测器查看器的数据源；刷新对应页面即可联动。";
            _grid.ItemsSource = database.Paths.Select(item => new PathRow(item)).ToArray();
            await LoadPageAsync();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (_closed || generation != _loadGeneration) return;
            _header.Text = $"数据库读取失败：{exception.Message}";
            _grid.ItemsSource = Array.Empty<PathRow>();
            _branchGrid.ItemsSource = Array.Empty<BranchRow>();
        }
    }

    private void ChangePage(int delta)
    {
        var value = int.TryParse(_page.Text, out var parsed) ? parsed : 1;
        _page.Text = Math.Max(1, value + delta).ToString(CultureInfo.InvariantCulture);
        _ = LoadPageAsync();
    }

    private async Task LoadPageAsync()
    {
        var generation = ++_pageGeneration;
        _pageCancellation?.Cancel();
        _pageCancellation?.Dispose();
        var cancellation = _pageCancellation = new CancellationTokenSource();
        try
        {
            var pageNumber = int.TryParse(_page.Text, out var parsed) ? Math.Max(1, parsed) : 1;
            var filter = _filter.Text;
            var page = await Task.Run(() =>
                _service.GetRayDatabasePage(_path, pageNumber - 1, 100, filter, cancellation.Token),
                cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested || generation != _pageGeneration) return;
            _branchGrid.ItemsSource = page.Branches.Select(item => new BranchRow(item)).ToArray();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (_closed || generation != _pageGeneration) return;
            _header.Text += $"\n分页读取失败：{exception.Message}";
            _branchGrid.ItemsSource = Array.Empty<BranchRow>();
        }
    }

    private static Control Dock(Control control, Avalonia.Controls.Dock side)
    {
        control.Margin = new Avalonia.Thickness(0, 0, 0, 8);
        DockPanel.SetDock(control, side);
        return control;
    }

    private static DataGridTextColumn Column(string header, string property, double width) => new()
    {
        Header = header,
        Binding = new Avalonia.Data.Binding(property),
        Width = new DataGridLength(width)
    };

    private sealed class PathRow
    {
        public PathRow(NonSequentialPathSummaryDto value)
        {
            RayCount = value.RayCount;
            Power = value.TotalPowerWatts.ToString("G6", CultureInfo.InvariantCulture);
            Fraction = value.PowerFraction.ToString("P2", CultureInfo.CurrentCulture);
            Termination = value.TerminationReason;
            Path = value.Path;
            Filter = value.FilterExpression;
        }
        public int RayCount { get; }
        public string Power { get; }
        public string Fraction { get; }
        public string Termination { get; }
        public string Path { get; }
        public string Filter { get; }
    }

    private sealed class BranchRow
    {
        public BranchRow(NonSequentialRayBranchDto value)
        {
            Id = value.Id;
            ParentId = value.ParentId?.ToString(CultureInfo.InvariantCulture) ?? "-";
            Level = value.Level;
            Termination = value.TerminationReason;
            Wavelength = value.WavelengthNanometers.ToString("G7", CultureInfo.InvariantCulture);
            Power = value.FinalPowerWatts.ToString("G6", CultureInfo.InvariantCulture);
            Segments = string.Join(" → ", value.Segments.Select(segment =>
                $"O{segment.ObjectNumber}:{segment.ObjectName}/{segment.Interaction}"));
        }
        public long Id { get; }
        public string ParentId { get; }
        public int Level { get; }
        public string Termination { get; }
        public string Wavelength { get; }
        public string Power { get; }
        public string Segments { get; }
    }
}

internal sealed class NonSequentialDetectorViewerPanel : UserControl, IDisposable
{
    private readonly INonSequentialDocumentService _documentService;
    private readonly INonSequentialAnalysisService _service;
    private readonly ComboBox _detector = new();
    private readonly ComboBox _space = new()
    {
        ItemsSource = Enum.GetValues<NonSequentialDetectorSpace>(),
        SelectedItem = NonSequentialDetectorSpace.Position
    };
    private readonly ComboBox _dataType = new()
    {
        ItemsSource = Enum.GetValues<NonSequentialDetectorDataType>(),
        SelectedItem = NonSequentialDetectorDataType.IncoherentIrradiance
    };
    private readonly ComboBox _wavelength = new();
    private readonly TextBox _filter = new() { PlaceholderText = "可选路径筛选" };
    private readonly CheckBox _logarithmic = new() { Content = "对数显示" };
    private readonly ComboBox _colorMap = new()
    {
        ItemsSource = Enum.GetValues<AnalysisColorMap>(),
        SelectedItem = AnalysisColorMap.Inferno
    };
    private readonly ComboBox _normalization = new()
    {
        ItemsSource = Enum.GetValues<DetectorDisplayNormalization>(),
        SelectedItem = DetectorDisplayNormalization.Absolute
    };
    private readonly NumericUpDown _smoothing = new()
    {
        Minimum = 0,
        Maximum = NonSequentialDetectorDisplay.MaximumSmoothingRadius,
        Increment = 1,
        Value = 0,
        Width = 72
    };
    private readonly CheckBox _autoRange = new() { Content = "自动范围", IsChecked = true };
    private readonly NumericUpDown _rangeMinimum = new() { Value = 0, Width = 100, IsEnabled = false };
    private readonly NumericUpDown _rangeMaximum = new() { Value = 1, Width = 100, IsEnabled = false };
    private readonly ComboBox _profileAxis = new()
    {
        ItemsSource = Enum.GetValues<DetectorProfileAxis>(),
        SelectedItem = DetectorProfileAxis.X
    };
    private readonly NumericUpDown _profileIndex = new() { Minimum = 0, Maximum = 0, Value = 0, Width = 82 };
    private readonly NumericUpDown _cursorX = new() { Minimum = 0, Maximum = 0, Value = 0, Width = 82 };
    private readonly NumericUpDown _cursorY = new() { Minimum = 0, Maximum = 0, Value = 0, Width = 82 };
    private readonly TextBlock _cursorValue = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _statistics = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly AnalysisPlotControl _plot = new();
    private readonly AnalysisPlotControl _profilePlot = new();
    private DetectorDisplayFrame? _displayFrame;
    private NonSequentialDetectorViewDto? _lastView;
    private CancellationTokenSource? _refreshCancellation;
    private bool _disposed;

    public NonSequentialDetectorViewerPanel(
        INonSequentialDocumentService documentService,
        INonSequentialAnalysisService service)
    {
        _documentService = documentService;
        _service = service;
        SetAutomationNames();
        ReloadChoices();
        UpdateDataTypeChoices();
        _space.SelectionChanged += (_, _) => UpdateDataTypeChoices();
        var refresh = new Button { Content = "刷新结果", MinWidth = 90 };
        refresh.Click += async (_, _) => await RefreshViewAsync();
        var export = new Button { Content = "导出 CSV", MinWidth = 90 };
        export.Click += async (_, _) => await ExportCsvAsync();
        var exportPng = new Button { Content = "导出 PNG", MinWidth = 90 };
        exportPng.Click += async (_, _) => await ExportPngAsync();
        var controls = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
            Children =
            {
                Label("探测器"), _detector,
                Label("空间"), _space,
                Label("数据"), _dataType,
                Label("波长"), _wavelength,
                Label("色表"), _colorMap,
                Label("归一化"), _normalization,
                Label("平滑半径"), _smoothing,
                _logarithmic, _autoRange,
                Label("最小"), _rangeMinimum,
                Label("最大"), _rangeMaximum,
                refresh, export, exportPng
            }
        };
        var inspectionControls = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
            Children =
            {
                Label("剖面方向"), _profileAxis,
                Label("行/列"), _profileIndex,
                Label("像素 X"), _cursorX,
                Label("像素 Y"), _cursorY,
                _cursorValue
            }
        };
        var plots = new Grid
        {
            RowDefinitions = new RowDefinitions("3*,8,*"),
            Children = { _plot, _profilePlot }
        };
        Grid.SetRow(_profilePlot, 2);
        var topControls = new StackPanel
        {
            Children = { controls, inspectionControls, _filter }
        };
        Content = new DockPanel
        {
            Margin = new Avalonia.Thickness(12),
            Children =
            {
                Dock(topControls, Avalonia.Controls.Dock.Top),
                Dock(_statistics, Avalonia.Controls.Dock.Bottom),
                plots
            }
        };
        _colorMap.SelectionChanged += (_, _) => RenderLastView();
        _normalization.SelectionChanged += (_, _) => RenderLastView();
        _smoothing.ValueChanged += (_, _) => RenderLastView();
        _logarithmic.IsCheckedChanged += (_, _) => RenderLastView();
        _autoRange.IsCheckedChanged += (_, _) =>
        {
            _rangeMinimum.IsEnabled = _autoRange.IsChecked != true;
            _rangeMaximum.IsEnabled = _autoRange.IsChecked != true;
            RenderLastView();
        };
        _rangeMinimum.ValueChanged += (_, _) => RenderLastView();
        _rangeMaximum.ValueChanged += (_, _) => RenderLastView();
        _profileAxis.SelectionChanged += (_, _) =>
        {
            UpdateInspectionBounds();
            RenderLastView();
        };
        _profileIndex.ValueChanged += (_, _) => RenderLastView();
        _cursorX.ValueChanged += (_, _) => UpdateCursorValue();
        _cursorY.ValueChanged += (_, _) => UpdateCursorValue();
        _service.SessionChanged += OnSessionChanged;
        _ = RefreshViewAsync();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _service.SessionChanged -= OnSessionChanged;
    }

    private void OnSessionChanged(object? sender, NonSequentialTraceSessionDto? session)
    {
        _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!_disposed)
            {
                _ = RefreshViewAsync();
            }
        });
    }

    private void ReloadChoices()
    {
        var selectedDetector = (_detector.SelectedItem as DetectorChoice)?.Id;
        var selectedWavelength = (_wavelength.SelectedItem as WavelengthChoice)?.Number ?? 0;
        var document = _documentService.GetDocument();
        var detectors = document.Objects
            .Where(item => item.Enabled && item.Kind == NonSequentialObjectKind.DetectorRectangle)
            .Select(item => new DetectorChoice(item.Id, $"{item.ObjectNumber} - {item.Name}"))
            .ToArray();
        _detector.ItemsSource = detectors;
        _detector.SelectedItem = detectors.FirstOrDefault(item => item.Id == selectedDetector)
            ?? detectors.FirstOrDefault();
        var wavelengths = document.Wavelengths
            .Select(item => new WavelengthChoice(item.Index + 1, $"{item.Index + 1} - {item.Label} {item.Nanometers:G7} nm"))
            .Prepend(new WavelengthChoice(0, "全部波长"))
            .ToArray();
        _wavelength.ItemsSource = wavelengths;
        _wavelength.SelectedItem = wavelengths.FirstOrDefault(item => item.Number == selectedWavelength)
            ?? wavelengths[0];
    }

    private void UpdateDataTypeChoices()
    {
        var selected = _dataType.SelectedItem is NonSequentialDetectorDataType value ? value : (NonSequentialDetectorDataType?)null;
        var angle = _space.SelectedItem is NonSequentialDetectorSpace.Angle;
        var choices = angle
            ? new[]
            {
                NonSequentialDetectorDataType.PixelPower,
                NonSequentialDetectorDataType.RadiantIntensity,
                NonSequentialDetectorDataType.HitCount
            }
            : new[]
            {
                NonSequentialDetectorDataType.PixelPower,
                NonSequentialDetectorDataType.IncoherentIrradiance,
                NonSequentialDetectorDataType.HitCount
            };
        _dataType.ItemsSource = choices;
        _dataType.SelectedItem = selected is { } current && choices.Contains(current)
            ? current
            : angle
                ? NonSequentialDetectorDataType.RadiantIntensity
                : NonSequentialDetectorDataType.IncoherentIrradiance;
    }

    private async Task RefreshViewAsync()
    {
        try
        {
            ReloadChoices();
            if (_detector.SelectedItem is not DetectorChoice detector)
            {
                _statistics.Text = "场景没有启用的矩形探测器。";
                _plot.Series = Array.Empty<AnalysisSeriesDto>();
                return;
            }
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            _refreshCancellation = new CancellationTokenSource();
            var cancellationToken = _refreshCancellation.Token;
            _statistics.Text = "正在重建探测器结果…";
            var request = new NonSequentialDetectorViewRequestDto(
                detector.Id,
                (NonSequentialDetectorSpace)(_space.SelectedItem ?? NonSequentialDetectorSpace.Position),
                (NonSequentialDetectorDataType)(_dataType.SelectedItem ?? NonSequentialDetectorDataType.IncoherentIrradiance),
                (_wavelength.SelectedItem as WavelengthChoice)?.Number ?? 0,
                string.IsNullOrWhiteSpace(_filter.Text) ? null : _filter.Text);
            var view = await Task.Run(
                () => _service.GetDetectorView(request, cancellationToken),
                cancellationToken);
            if (_disposed || cancellationToken.IsCancellationRequested) return;
            _lastView = view;
            UpdateInspectionBounds();
            RenderView(view);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _statistics.Text = $"探测器结果不可用：{exception.Message}";
            _plot.Series = Array.Empty<AnalysisSeriesDto>();
        }
    }

    private void RenderLastView()
    {
        if (_lastView is null || _disposed)
        {
            return;
        }

        try
        {
            RenderView(_lastView);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            _statistics.Text = $"探测器显示设置无效：{exception.Message}";
        }
    }

    private void RenderView(NonSequentialDetectorViewDto view)
    {
        double? manualMinimum = _autoRange.IsChecked == true
            ? null
            : _rangeMinimum.Value is { } minimum ? decimal.ToDouble(minimum) : 0;
        double? manualMaximum = _autoRange.IsChecked == true
            ? null
            : _rangeMaximum.Value is { } maximum ? decimal.ToDouble(maximum) : 1;
        var normalization = _normalization.SelectedItem is DetectorDisplayNormalization selectedNormalization
            ? selectedNormalization
            : DetectorDisplayNormalization.Absolute;
        var display = NonSequentialDetectorDisplay.Transform(
            view.Values,
            view.PixelsX,
            view.PixelsY,
            view.ValueUnit,
            normalization,
            _smoothing.Value is { } smoothing ? decimal.ToInt32(smoothing) : 0,
            _logarithmic.IsChecked == true,
            manualMinimum,
            manualMaximum);
        _displayFrame = display;

        var points = new AnalysisPointDto[view.PixelsX * view.PixelsY];
        for (var y = 0; y < view.PixelsY; y++)
        {
            for (var x = 0; x < view.PixelsX; x++)
            {
                var index = y * view.PixelsX + x;
                points[index] = new AnalysisPointDto(
                    view.XMinimum + (x + 0.5) * (view.XMaximum - view.XMinimum) / view.PixelsX,
                    view.YMinimum + (y + 0.5) * (view.YMaximum - view.YMinimum) / view.PixelsY,
                    Value: display.Values[index]);
            }
        }

        var angle = view.XUnit == "deg";
        var dataType = (NonSequentialDetectorDataType)(
            _dataType.SelectedItem ?? NonSequentialDetectorDataType.IncoherentIrradiance);
        var transformed = normalization != DetectorDisplayNormalization.Absolute
            || _logarithmic.IsChecked == true;
        var (valueQuantity, valueUnit) = NonSequentialDetectorDisplay.ValueAxis(dataType, transformed);
        var axisQuantity = angle ? AnalysisAxisQuantity.IncidentAngle : AnalysisAxisQuantity.Coordinate;
        var axisUnit = angle ? AnalysisAxisUnit.Degree : AnalysisAxisUnit.Millimeter;
        var series = new AnalysisSeriesDto(
            $"X ({view.XUnit})",
            $"Y ({view.YUnit})",
            points,
            AnalysisSeriesKind.Heatmap,
            view.DetectorName,
            ValueLabel: display.ValueUnit,
            ColorMap: _colorMap.SelectedItem is AnalysisColorMap colorMap
                ? colorMap
                : AnalysisColorMap.Inferno,
            ValueMinimum: display.ValueMinimum,
            ValueMaximum: display.ValueMaximum,
            XQuantity: axisQuantity,
            XUnit: axisUnit,
            YQuantity: axisQuantity,
            YUnit: axisUnit,
            ValueQuantity: valueQuantity,
            ValueUnit: valueUnit);
        _plot.PlotOptions = new AnalysisPlotOptionsDto(
            $"探测器：{view.DetectorName}",
            EqualAspect: true,
            XMinimum: view.XMinimum,
            XMaximum: view.XMaximum,
            YMinimum: view.YMinimum,
            YMaximum: view.YMaximum,
            DefaultSquareViewport: true);
        _plot.Series = new[] { series };

        var profileAxis = _profileAxis.SelectedItem is DetectorProfileAxis selectedAxis
            ? selectedAxis
            : DetectorProfileAxis.X;
        var profileIndex = _profileIndex.Value is { } selectedIndex
            ? decimal.ToInt32(selectedIndex)
            : 0;
        var profile = NonSequentialDetectorDisplay.Profile(
            view,
            display.Values,
            profileAxis,
            profileIndex);
        _profilePlot.PlotOptions = new AnalysisPlotOptionsDto(
            profileAxis == DetectorProfileAxis.X
                ? $"X 剖面，行 {profileIndex}"
                : $"Y 剖面，列 {profileIndex}",
            XMinimum: profileAxis == DetectorProfileAxis.X ? view.XMinimum : view.YMinimum,
            XMaximum: profileAxis == DetectorProfileAxis.X ? view.XMaximum : view.YMaximum,
            YMinimum: display.ValueMinimum,
            YMaximum: display.ValueMaximum);
        _profilePlot.Series = new[]
        {
            new AnalysisSeriesDto(
                profileAxis == DetectorProfileAxis.X ? $"X ({view.XUnit})" : $"Y ({view.YUnit})",
                display.ValueUnit,
                profile,
                AnalysisSeriesKind.Line,
                "剖面",
                XQuantity: axisQuantity,
                XUnit: axisUnit,
                YQuantity: valueQuantity,
                YUnit: valueUnit)
        };

        var stats = view.Statistics;
        _statistics.Text = $"原始物理统计：总功率 {stats.TotalPowerWatts:G6} W · 命中 {stats.TotalHits:N0} · 峰值 {stats.PeakValue:G6} {view.ValueUnit} · "
            + $"质心 ({stats.CentroidX:G5}, {stats.CentroidY:G5}) {view.XUnit} · RMS ({stats.RmsX:G5}, {stats.RmsY:G5}) {view.XUnit} · 均匀度 {stats.Uniformity:P2}"
            + (view.IsStale ? " · 结果已过期" : string.Empty);
        UpdateCursorValue();
    }

    private void UpdateInspectionBounds()
    {
        if (_lastView is null)
        {
            return;
        }

        _cursorX.Maximum = Math.Max(0, _lastView.PixelsX - 1);
        _cursorY.Maximum = Math.Max(0, _lastView.PixelsY - 1);
        _profileIndex.Maximum = _profileAxis.SelectedItem is DetectorProfileAxis.Y
            ? Math.Max(0, _lastView.PixelsX - 1)
            : Math.Max(0, _lastView.PixelsY - 1);
    }

    private void UpdateCursorValue()
    {
        if (_lastView is null || _displayFrame is null)
        {
            _cursorValue.Text = string.Empty;
            return;
        }

        var x = Math.Clamp(
            _cursorX.Value is { } xValue ? decimal.ToInt32(xValue) : 0,
            0,
            _lastView.PixelsX - 1);
        var y = Math.Clamp(
            _cursorY.Value is { } yValue ? decimal.ToInt32(yValue) : 0,
            0,
            _lastView.PixelsY - 1);
        var value = _displayFrame.Values[(y * _lastView.PixelsX) + x];
        _cursorValue.Text = $"像素 ({x}, {y}) = {(double.IsFinite(value) ? value.ToString("G7", CultureInfo.InvariantCulture) : "无数据")} {_displayFrame.ValueUnit}";
    }

    private async Task ExportCsvAsync()
    {
        if (_lastView is null) return;
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
        {
            _statistics.Text = "当前工作区无法访问文件保存功能。";
            return;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出探测器数据",
            SuggestedFileName = "detector.csv",
            DefaultExtension = "csv",
            FileTypeChoices = new[] { new FilePickerFileType("CSV 数据") { Patterns = new[] { "*.csv" } } }
        });
        if (file is null) return;
        var lines = new List<string> { "x,y,value" };
        for (var y = 0; y < _lastView.PixelsY; y++)
            for (var x = 0; x < _lastView.PixelsX; x++)
            {
                var px = _lastView.XMinimum + (x + 0.5) * (_lastView.XMaximum - _lastView.XMinimum) / _lastView.PixelsX;
                var py = _lastView.YMinimum + (y + 0.5) * (_lastView.YMaximum - _lastView.YMinimum) / _lastView.PixelsY;
                lines.Add($"{px.ToString("G17", CultureInfo.InvariantCulture)},{py.ToString("G17", CultureInfo.InvariantCulture)},{_lastView.Values[y * _lastView.PixelsX + x].ToString("G17", CultureInfo.InvariantCulture)}");
            }
        await BoundedApplicationFile.WriteAllTextAtomicAsync(
            file.Path.LocalPath,
            string.Join(Environment.NewLine, lines) + Environment.NewLine,
            BoundedApplicationFile.MaximumExportBytes,
            "Non-sequential ray database export");
    }

    private async Task ExportPngAsync()
    {
        if (_lastView is null || _plot.Bounds.Width <= 0 || _plot.Bounds.Height <= 0)
        {
            _statistics.Text = "当前没有可导出的探测器图像。";
            return;
        }

        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
        {
            _statistics.Text = "当前工作区无法访问文件保存功能。";
            return;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出探测器图像",
            SuggestedFileName = "detector.png",
            DefaultExtension = "png",
            FileTypeChoices = new[] { new FilePickerFileType("PNG 图像") { Patterns = new[] { "*.png" } } }
        });
        if (file is null)
        {
            return;
        }

        var width = Math.Clamp((int)Math.Ceiling(_plot.Bounds.Width), 1, 4096);
        var height = Math.Clamp((int)Math.Ceiling(_plot.Bounds.Height), 1, 4096);
        using var bitmap = new RenderTargetBitmap(
            new Avalonia.PixelSize(width, height),
            new Avalonia.Vector(96, 96));
        bitmap.Render(_plot);
        var targetPath = file.Path.LocalPath;
        var temporaryPath = targetPath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                bitmap.Save(stream, PngBitmapEncoderOptions.Default);
                await stream.FlushAsync();
            }

            File.Move(temporaryPath, targetPath, overwrite: true);
            _statistics.Text = $"探测器 PNG 已导出：{targetPath}";
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Margin = new Avalonia.Thickness(10, 0, 4, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private void SetAutomationNames()
    {
        AutomationProperties.SetName(_detector, "探测器");
        AutomationProperties.SetName(_space, "探测器坐标空间");
        AutomationProperties.SetName(_dataType, "探测器数据类型");
        AutomationProperties.SetName(_wavelength, "探测器波长");
        AutomationProperties.SetName(_filter, "探测器路径筛选");
        AutomationProperties.SetName(_logarithmic, "对数显示");
        AutomationProperties.SetName(_colorMap, "热图颜色表");
        AutomationProperties.SetName(_normalization, "显示归一化");
        AutomationProperties.SetName(_smoothing, "平滑半径");
        AutomationProperties.SetName(_autoRange, "自动显示范围");
        AutomationProperties.SetName(_rangeMinimum, "显示范围最小值");
        AutomationProperties.SetName(_rangeMaximum, "显示范围最大值");
        AutomationProperties.SetName(_profileAxis, "剖面方向");
        AutomationProperties.SetName(_profileIndex, "剖面行列索引");
        AutomationProperties.SetName(_cursorX, "像素X索引");
        AutomationProperties.SetName(_cursorY, "像素Y索引");
        AutomationProperties.SetName(_plot, "非序列探测器热图");
        AutomationProperties.SetName(_profilePlot, "非序列探测器剖面图");
    }

    private static Control Dock(Control control, Avalonia.Controls.Dock side)
    {
        control.Margin = new Avalonia.Thickness(0, 0, 0, 8);
        DockPanel.SetDock(control, side);
        return control;
    }

    private sealed record DetectorChoice(Guid Id, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record WavelengthChoice(int Number, string Label)
    {
        public override string ToString() => Label;
    }
}
