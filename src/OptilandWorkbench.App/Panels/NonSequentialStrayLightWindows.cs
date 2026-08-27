using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;

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
        clearAndTrace.Click += async (_, _) => await RunAsync(NonSequentialTraceCommand.ClearAndTrace, clearAndTrace);
        var traceOnly = new Button { Content = "仅追迹", MinWidth = 88 };
        traceOnly.Click += async (_, _) => await RunAsync(NonSequentialTraceCommand.TraceOnly, traceOnly);
        var clear = new Button { Content = "仅清空", MinWidth = 88 };
        clear.Click += async (_, _) => await RunAsync(NonSequentialTraceCommand.ClearOnly, clear);
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

    private async Task RunAsync(NonSequentialTraceCommand command, Button run)
    {
        if (command == NonSequentialTraceCommand.ClearOnly)
        {
            _service.ClearDetectors();
            _status.Text = "探测器和当前追迹结果已清空。";
            return;
        }
        if (!int.TryParse(_retained.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var retained)
            || !int.TryParse(_seed.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed)
            || !int.TryParse(_segments.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var segments)
            || !int.TryParse(_branches.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var branches)
            || !double.TryParse(_minimumPower.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var minimumPower))
        {
            _status.Text = "射线数、随机种子、限制参数或最小能量格式无效。";
            return;
        }
        int? rayCount = null;
        if (!string.IsNullOrWhiteSpace(_rayCount.Text))
        {
            if (!int.TryParse(_rayCount.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRayCount))
            {
                _status.Text = "临时覆盖射线数不是有效整数。";
                return;
            }
            rayCount = parsedRayCount;
        }
        run.IsEnabled = false;
        _status.Text = "正在追迹…";
        _cancellation = new CancellationTokenSource();
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
                RayCountOverride: rayCount), _cancellation.Token);
            _status.Text = $"{result.SessionState}：本次 {result.TotalBranchCount} 个分支，筛选命中 {result.MatchedBranchCount}，耗时 {result.Elapsed:g}。\n"
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
            _cancellation?.Dispose();
            _cancellation = null;
            run.IsEnabled = true;
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
        apply.Click += (_, _) => Load();
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
        Load();
    }

    private void Load()
    {
        try
        {
            var database = _service.OpenRayDatabase(_path, _filter.Text);
            _header.Text = $"{database.Path}\n{database.BranchCount:N0} 个分支 · {database.CreatedUtc.LocalDateTime:G}"
                + (database.IsStale ? " · 结果已过期：场景与当前工程不一致" : " · 与当前场景一致")
                + "\n当前筛选已作为非序列 3D 布局和探测器查看器的数据源；刷新对应页面即可联动。";
            _grid.ItemsSource = database.Paths.Select(item => new PathRow(item)).ToArray();
            LoadPage();
        }
        catch (Exception exception)
        {
            _header.Text = $"数据库读取失败：{exception.Message}";
            _grid.ItemsSource = Array.Empty<PathRow>();
            _branchGrid.ItemsSource = Array.Empty<BranchRow>();
        }
    }

    private void ChangePage(int delta)
    {
        var value = int.TryParse(_page.Text, out var parsed) ? parsed : 1;
        _page.Text = Math.Max(1, value + delta).ToString(CultureInfo.InvariantCulture);
        LoadPage();
    }

    private void LoadPage()
    {
        try
        {
            var pageNumber = int.TryParse(_page.Text, out var parsed) ? Math.Max(1, parsed) : 1;
            var page = _service.GetRayDatabasePage(_path, pageNumber - 1, 100, _filter.Text);
            _branchGrid.ItemsSource = page.Branches.Select(item => new BranchRow(item)).ToArray();
        }
        catch (Exception exception)
        {
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
    private readonly TextBlock _statistics = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly AnalysisPlotControl _plot = new();
    private NonSequentialDetectorViewDto? _lastView;
    private bool _disposed;

    public NonSequentialDetectorViewerPanel(
        INonSequentialDocumentService documentService,
        INonSequentialAnalysisService service)
    {
        _documentService = documentService;
        _service = service;
        ReloadChoices();
        var refresh = new Button { Content = "刷新结果", MinWidth = 90 };
        refresh.Click += (_, _) => RefreshView();
        var export = new Button { Content = "导出 CSV", MinWidth = 90 };
        export.Click += async (_, _) => await ExportCsvAsync();
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
                _logarithmic, refresh, export
            }
        };
        Content = new DockPanel
        {
            Margin = new Avalonia.Thickness(12),
            Children =
            {
                Dock(controls, Avalonia.Controls.Dock.Top),
                Dock(_filter, Avalonia.Controls.Dock.Top),
                Dock(_statistics, Avalonia.Controls.Dock.Bottom),
                _plot
            }
        };
        _service.SessionChanged += OnSessionChanged;
        RefreshView();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _service.SessionChanged -= OnSessionChanged;
    }

    private void OnSessionChanged(object? sender, NonSequentialTraceSessionDto? session)
    {
        _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!_disposed)
            {
                RefreshView();
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

    private void RefreshView()
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
            var view = _service.GetDetectorView(new NonSequentialDetectorViewRequestDto(
                detector.Id,
                (NonSequentialDetectorSpace)(_space.SelectedItem ?? NonSequentialDetectorSpace.Position),
                (NonSequentialDetectorDataType)(_dataType.SelectedItem ?? NonSequentialDetectorDataType.IncoherentIrradiance),
                (_wavelength.SelectedItem as WavelengthChoice)?.Number ?? 0,
                string.IsNullOrWhiteSpace(_filter.Text) ? null : _filter.Text));
            _lastView = view;
            var values = _logarithmic.IsChecked == true
                ? view.Values.Select(value => value > 0 ? Math.Log10(value) : double.NaN).ToArray()
                : view.Values;
            var points = new AnalysisPointDto[view.PixelsX * view.PixelsY];
            for (var y = 0; y < view.PixelsY; y++)
                for (var x = 0; x < view.PixelsX; x++)
                {
                    var index = y * view.PixelsX + x;
                    points[index] = new AnalysisPointDto(
                        view.XMinimum + (x + 0.5) * (view.XMaximum - view.XMinimum) / view.PixelsX,
                        view.YMinimum + (y + 0.5) * (view.YMaximum - view.YMinimum) / view.PixelsY,
                        Value: values[index]);
                }
            var angle = view.XUnit == "deg";
            var dataType = (NonSequentialDetectorDataType)(_dataType.SelectedItem ?? NonSequentialDetectorDataType.IncoherentIrradiance);
            var series = new AnalysisSeriesDto(
                $"X ({view.XUnit})", $"Y ({view.YUnit})", points,
                AnalysisSeriesKind.Heatmap, view.DetectorName,
                ValueLabel: (_logarithmic.IsChecked == true ? "log10 " : string.Empty) + view.ValueUnit,
                ColorMap: AnalysisColorMap.Inferno,
                XQuantity: angle ? AnalysisAxisQuantity.IncidentAngle : AnalysisAxisQuantity.Coordinate,
                XUnit: angle ? AnalysisAxisUnit.Degree : AnalysisAxisUnit.Millimeter,
                YQuantity: angle ? AnalysisAxisQuantity.IncidentAngle : AnalysisAxisQuantity.Coordinate,
                YUnit: angle ? AnalysisAxisUnit.Degree : AnalysisAxisUnit.Millimeter,
                ValueQuantity: dataType == NonSequentialDetectorDataType.IncoherentIrradiance
                    ? AnalysisAxisQuantity.Irradiance
                    : AnalysisAxisQuantity.Intensity,
                ValueUnit: dataType switch
                {
                    NonSequentialDetectorDataType.IncoherentIrradiance => AnalysisAxisUnit.WattsPerSquareMillimeter,
                    NonSequentialDetectorDataType.RadiantIntensity => AnalysisAxisUnit.WattsPerSteradian,
                    NonSequentialDetectorDataType.HitCount => AnalysisAxisUnit.Dimensionless,
                    _ => AnalysisAxisUnit.Unspecified
                });
            _plot.PlotOptions = new AnalysisPlotOptionsDto(
                $"探测器：{view.DetectorName}", EqualAspect: true,
                XMinimum: view.XMinimum, XMaximum: view.XMaximum,
                YMinimum: view.YMinimum, YMaximum: view.YMaximum,
                DefaultSquareViewport: true);
            _plot.Series = new[] { series };
            var stats = view.Statistics;
            _statistics.Text = $"总功率 {stats.TotalPowerWatts:G6} W · 命中 {stats.TotalHits:N0} · 峰值 {stats.PeakValue:G6} {view.ValueUnit} · "
                + $"质心 ({stats.CentroidX:G5}, {stats.CentroidY:G5}) {view.XUnit} · RMS ({stats.RmsX:G5}, {stats.RmsY:G5}) {view.XUnit} · 均匀度 {stats.Uniformity:P2}"
                + (view.IsStale ? " · 结果已过期" : string.Empty);
        }
        catch (Exception exception)
        {
            _statistics.Text = $"探测器结果不可用：{exception.Message}";
            _plot.Series = Array.Empty<AnalysisSeriesDto>();
        }
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
        await File.WriteAllLinesAsync(file.Path.LocalPath, lines);
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Margin = new Avalonia.Thickness(10, 0, 4, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

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
