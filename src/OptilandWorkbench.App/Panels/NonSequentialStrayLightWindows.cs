using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using OptilandWorkbench.Application.Contracts;
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
    private readonly INonSequentialDocumentService _service;
    private readonly ComboBox _mode = new() { ItemsSource = Enum.GetValues<NonSequentialTraceOutputMode>(), SelectedItem = NonSequentialTraceOutputMode.RayDatabase };
    private readonly CheckBox _analysis = new() { Content = "使用分析射线数量", IsChecked = true };
    private readonly CheckBox _split = new() { Content = "启用 Fresnel 反射/透射分支", IsChecked = true };
    private readonly TextBox _retained = new() { Text = "2000" };
    private readonly TextBox _filter = new() { PlaceholderText = "例如 SEQ(Q1,H3,R3,D8)" };
    private readonly TextBox _path = new() { PlaceholderText = "选择 .starrdb 保存位置" };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };

    public NonSequentialTraceControlWindow(INonSequentialDocumentService service)
    {
        _service = service;
        Title = "非序列追迹控制";
        Width = 600;
        Height = 470;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var browse = new Button { Content = "浏览…" };
        browse.Click += async (_, _) => await BrowseAsync();
        var pathGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { _path, browse } };
        Grid.SetColumn(browse, 1);
        var run = new Button { Content = "开始追迹", MinWidth = 100 };
        run.Click += async (_, _) => await RunAsync(run);
        var close = new Button { Content = "关闭", MinWidth = 88 };
        close.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(18),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "分析光线可以直接写入独立数据库；3D 布局应使用 LayoutSample，避免保留全部光线。", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                Row("输出模式", _mode),
                _analysis,
                _split,
                Row("最多保留分支", _retained),
                Row("路径筛选", _filter),
                Row("数据库路径", pathGrid),
                _status,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { close, run } }
            }
        };
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

    private async Task RunAsync(Button run)
    {
        if (!int.TryParse(_retained.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var retained))
        {
            _status.Text = "最多保留分支不是有效整数。";
            return;
        }
        var mode = (NonSequentialTraceOutputMode)(_mode.SelectedItem ?? NonSequentialTraceOutputMode.InMemory);
        if (mode == NonSequentialTraceOutputMode.RayDatabase && string.IsNullOrWhiteSpace(_path.Text))
        {
            await BrowseAsync();
            if (string.IsNullOrWhiteSpace(_path.Text)) return;
        }
        run.IsEnabled = false;
        _status.Text = "正在追迹…";
        try
        {
            var result = await _service.TraceAsync(new NonSequentialTraceRunRequestDto(
                mode,
                AnalysisRays: _analysis.IsChecked == true,
                SplitFresnelRays: _split.IsChecked == true,
                MaximumRetainedBranches: retained,
                PathFilterExpression: string.IsNullOrWhiteSpace(_filter.Text) ? null : _filter.Text,
                RayDatabasePath: mode == NonSequentialTraceOutputMode.RayDatabase ? _path.Text : null));
            _status.Text = $"完成：{result.TotalBranchCount} 个分支，筛选命中 {result.MatchedBranchCount}，{result.SegmentCount} 个内存段。\n"
                + $"探测 {result.DetectorPowerWatts:G6} W，吸收 {result.AbsorbedPowerWatts:G6} W，逃逸 {result.EscapedPowerWatts:G6} W。"
                + (result.RayDatabasePath is null ? string.Empty : $"\n数据库 {result.RayDatabaseBytes:N0} 字节：{result.RayDatabasePath}");
        }
        catch (Exception exception)
        {
            _status.Text = $"追迹失败：{exception.Message}";
        }
        finally
        {
            run.IsEnabled = true;
        }
    }

    private static Control Row(string label, Control editor)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*"), ColumnSpacing = 8 };
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        return grid;
    }
}

internal sealed class NonSequentialRayDatabaseWindow : Window
{
    private readonly INonSequentialDocumentService _service;
    private readonly string _path;
    private readonly TextBox _filter = new() { PlaceholderText = "Q/H/R/T/D/M/W、A/E/X、! & |、SEQ(...)" };
    private readonly TextBlock _header = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly DataGrid _grid = new() { AutoGenerateColumns = false, IsReadOnly = true };

    public NonSequentialRayDatabaseWindow(INonSequentialDocumentService service, string path)
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
        Content = new DockPanel
        {
            Margin = new Avalonia.Thickness(12),
            Children = { Dock(_header, Avalonia.Controls.Dock.Top), Dock(top, Avalonia.Controls.Dock.Top), _grid }
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
        }
        catch (Exception exception)
        {
            _header.Text = $"数据库读取失败：{exception.Message}";
            _grid.ItemsSource = Array.Empty<PathRow>();
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
}
