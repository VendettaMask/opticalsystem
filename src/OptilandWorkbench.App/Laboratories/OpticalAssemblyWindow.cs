using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Laboratories;

/// <summary>Explicit laboratory window; never part of the default document layout.</summary>
public sealed class OpticalAssemblyWindow : Window
{
    private readonly IWorkbenchApplication _application;
    private readonly ComboBox _probeMode = new()
    {
        Name = "assembly-measurement-mode",
        Width = 190,
        SelectedIndex = 0,
        ItemsSource = new[] { new ModeOption(AssemblyMeasurementMode.Focused, "附加测量物镜"),
            new ModeOption(AssemblyMeasurementMode.Infinity, "无穷远（平行光）") }
    };
    private readonly ComboBox _direction = new() { ItemsSource = new[] { "从前端观察", "从后端观察" }, SelectedIndex = 0, Width = 148 };
    private readonly NumericUpDown _wavelength = Number("assembly-wavelength", 587.6m, 200, 20000, 1);
    private readonly NumericUpDown _head = Number("assembly-head-focal", 100, 1, 10000, 10);
    private readonly NumericUpDown _collimator = Number("assembly-collimator-focal", 200, 1, 10000, 10);
    private readonly NumericUpDown _na = Number("assembly-na", 0.02m, 0.001m, 0.2m, 0.005m);
    private readonly NumericUpDown _length = Number("assembly-reticle-length", 1, 0.01m, 20, 0.1m);
    private readonly NumericUpDown _lineWidth = Number("assembly-reticle-width", 0.02m, 0.001m, 20, 0.005m);
    private readonly NumericUpDown _offset = Number("assembly-focus-offset", 0, -10000, 10000, 0.1m);
    private readonly NumericUpDown _sensor = Number("assembly-sensor-width", 4, 0.1m, 100, 1);
    private readonly NumericUpDown _pupil = Number("assembly-pupil-diameter", 4, 0.1m, 200, 1);
    private readonly NumericUpDown _distance = Number("assembly-instrument-distance", 100, 0.1m, 10000, 10);
    private readonly List<Control> _focusedFields = new();
    private readonly List<Control> _infinityFields = new();
    private readonly TextBlock _modeInfo = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _calculate = new() { Name = "assembly-calculate", Content = "计算全部表面", Classes = { "accent" }, Height = 32 };
    private readonly Button _simulate = new() { Name = "assembly-simulate", Content = "更新叉丝图像", Classes = { "accent" }, Height = 32 };
    private readonly Button _cancel = new() { Content = "取消计算", Height = 32 };
    private readonly CheckBox _sort = new() { Content = "按找像位置排序", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _source = new() { FontSize = DisplayTypography.SectionTitle, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _imageInfo = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 0, 12, 12) };
    private readonly DataGrid _grid = new() { Name = "assembly-conjugates", IsReadOnly = true, AutoGenerateColumns = false, CanUserSortColumns = false, SelectionMode = DataGridSelectionMode.Single };
    private readonly AssemblyReticleView _image = new() { MinHeight = 250 };
    private CancellationTokenSource? _operation;
    private int _generation;
    private bool _closed;
    private bool _busy;
    private bool _bindingRows;
    private OpticalAssemblyResult? _result;
    private OpticalAssemblyRow? _imageRow;

    internal OpticalAssemblyResult? Result => _result;
    internal AssemblyReticleResult? Preview => _image.Result;

    public OpticalAssemblyWindow(IWorkbenchApplication application)
    {
        _application = application;
        Title = "光学装调 · 实验室";
        Width = 1260; Height = 850; MinWidth = 920; MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var wavelength = application.Prescription.GetWavelengths().FirstOrDefault(x => x.IsPrimary)
            ?? application.Prescription.GetWavelengths().FirstOrDefault();
        if (wavelength is { Nanometers: >= 200 and <= 20000 }) _wavelength.Value = (decimal)wavelength.Nanometers;

        var toolbar = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        toolbar.Children.Add(_direction);
        toolbar.Children.Add(Field("测量模式", _probeMode));
        toolbar.Children.Add(Field("测量波长 (nm)", _wavelength));
        toolbar.Children.Add(_calculate);
        toolbar.Children.Add(_cancel);
        toolbar.Children.Add(_sort);
        foreach (var child in toolbar.Children) child.Margin = new Thickness(0, 0, 12, 6);
        var header = new StackPanel
        {
            Margin = new Thickness(16, 12),
            Spacing = 5,
            Children = { _source, new TextBlock { Text = "基于当前打开的镜头（含未保存修改） · 位置以第一实体表面顶点为零，沿设计 +Z 为正", TextWrapping = TextWrapping.Wrap }, toolbar }
        };
        _grid.Columns.Add(Column("面", nameof(TableRow.Surface), 50));
        _grid.Columns.Add(Column("表面名称", nameof(TableRow.Label), 95));
        _grid.Columns.Add(Column("像类型", nameof(TableRow.Kind), 90));
        _grid.Columns.Add(Column("位置 (mm)", nameof(TableRow.Position), 130));
        _grid.Columns.Add(new DataGridTextColumn { Header = "计算说明", Binding = new Binding(nameof(TableRow.Message)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var preview = new DockPanel();
        var previewTitle = new TextBlock { Text = "返回叉丝像 · 探测面物理尺寸", FontWeight = FontWeight.SemiBold, Margin = new Thickness(16, 8) };
        DockPanel.SetDock(previewTitle, Avalonia.Controls.Dock.Top); preview.Children.Add(previewTitle);
        DockPanel.SetDock(_imageInfo, Avalonia.Controls.Dock.Bottom); preview.Children.Add(_imageInfo);
        preview.Children.Add(_image);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,5,420"), Margin = new Thickness(12, 0) };
        body.Children.Add(_grid);
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(splitter, 1); body.Children.Add(splitter);
        var previewScroll = new ScrollViewer
        {
            Content = preview,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        Grid.SetColumn(previewScroll, 2); body.Children.Add(previewScroll);

        var inputs = new WrapPanel();
        inputs.Children.Add(Field("自准直仪焦距 (mm)", _collimator));
        foreach (var pair in new[] { ("附加物镜焦距 (mm)", _head), ("测量 NA（近轴）", _na), ("找像位置偏移 (mm)", _offset) })
        {
            var field = Field(pair.Item1, pair.Item2); inputs.Children.Add(field); _focusedFields.Add(field);
        }
        foreach (var pair in new[] { ("仪器通光直径 (mm)", _pupil), ("物镜到入口顶点距离 (mm)", _distance) })
        {
            var field = Field(pair.Item1, pair.Item2); inputs.Children.Add(field); _infinityFields.Add(field);
        }
        foreach (var pair in new[] { ("叉丝总长 (mm)", _length), ("线宽 (mm)", _lineWidth), ("探测面宽度 (mm)", _sensor) })
            inputs.Children.Add(Field(pair.Item1, pair.Item2));
        inputs.Children.Add(_simulate);
        var footer = new StackPanel
        {
            Margin = new Thickness(16, 10),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "模拟测量头参数（可修改）", FontWeight = FontWeight.SemiBold },
                inputs,
                _modeInfo,
                new TextBlock { Text = "理想薄透镜测量头；镜组使用真实曲面往返追迹。显示几何像与归一化亮度，不含衍射、镀膜强度及实际仪器标定。", TextWrapping = TextWrapping.Wrap },
                _status
            }
        };
        var root = new DockPanel();
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top); root.Children.Add(header);
        DockPanel.SetDock(footer, Avalonia.Controls.Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(body); Content = root;
        this.BindThemeResource(BackgroundProperty, ThemeResourceBindings.Surface);

        _calculate.Click += async (_, _) => await CalculateAsync();
        _simulate.Click += async (_, _) => await SimulateAsync();
        _cancel.Click += (_, _) => { CancelOperation(); _status.Text = "已取消计算"; };
        // DataGrid can publish a deferred selection notification after binding.
        // Do not restart the same image and cancel the calculation being awaited.
        _grid.SelectionChanged += async (_, _) =>
        {
            if (!_bindingRows && (_grid.SelectedItem as TableRow)?.Row != _imageRow)
                await SimulateAsync();
        };
        _sort.IsCheckedChanged += (_, _) => BindRows();
        _direction.SelectionChanged += (_, _) => InvalidatePrescription("观察方向已改变，请重新计算");
        _wavelength.ValueChanged += (_, _) => InvalidatePrescription("测量波长已改变，请重新计算");
        _probeMode.SelectionChanged += (_, _) =>
        {
            CancelOperation(); ClearImage(); UpdateModeFields(); UpdateAvailability();
            _status.Text = "测量模式已切换，找像位置保持不变；点击“更新叉丝图像”。";
        };
        foreach (var input in new[] { _head, _collimator, _na, _length, _lineWidth, _offset, _sensor, _pupil, _distance })
            input.ValueChanged += (_, _) =>
            {
                CancelOperation(); ClearImage(); _status.Text = "测量头参数已改变，点击“更新叉丝图像”"; UpdateAvailability();
            };
        application.Events.Changed += OnWorkspaceChanged;
        application.Modes.ModeChanged += OnModeChanged;
        Opened += async (_, _) => await CalculateAsync();
        Closed += (_, _) =>
        {
            _closed = true; CancelOperation();
            application.Events.Changed -= OnWorkspaceChanged;
            application.Modes.ModeChanged -= OnModeChanged;
            _image.Dispose();
        };
        UpdateModeFields(); UpdateSource(); UpdateAvailability();
    }

    internal async Task CalculateAsync()
    {
        if (_closed) return;
        InvalidatePrescription("正在计算各面顶点像与球心像…");
        var (generation, token) = BeginOperation();
        var succeeded = false;
        try
        {
            var request = new OpticalAssemblyRequest(Value(_wavelength), _direction.SelectedIndex == 1);
            var result = await _application.OpticalAssembly.CalculateAsync(request, token);
            if (!IsCurrent(generation, result.SourceRevision)) return;
            _result = result; BindRows();
            _status.Text = $"{result.Rows.Count / 2} 个实体表面；{result.Rows.Count(x => x.Status == AssemblyConjugateStatus.Finite)} 个有限共轭。平面经前置镜片后也可能存在有限的平行光共轭。";
            succeeded = true;
        }
        catch (OperationCanceledException) { if (generation == _generation) _status.Text = "计算已取消"; }
        catch (Exception ex) { if (generation == _generation) _status.Text = ex.Message; }
        finally { EndOperation(generation); }
        if (succeeded) await SimulateAsync();
    }

    internal async Task SimulateAsync()
    {
        if (_closed) return;
        CancelOperation(); ClearImage();
        if (_result is null || _grid.SelectedItem is not TableRow selected) { UpdateAvailability(); return; }
        if (!CanPreview(selected.Row))
        {
            _imageInfo.Text = $"表面 {selected.Row.SurfaceNumber} · {selected.Row.SurfaceLabel}\n{selected.Kind}：{UnavailableReason(selected.Row)}";
            UpdateAvailability(); return;
        }
        var result = _result;
        var row = selected.Row;
        _imageRow = row;
        var (generation, token) = BeginOperation();
        _imageInfo.Text = $"表面 {row.SurfaceNumber} · {selected.Kind}：正在追踪叉丝往返光束…";
        try
        {
            var focused = MeasurementMode == AssemblyMeasurementMode.Focused;
            var request = new AssemblyReticleRequest(result.Request, result.SourceRevision, row.SurfaceNumber,
                row.Kind, focused ? Value(_head) : 100, Value(_collimator), focused ? Value(_na) : 0.02,
                Value(_length), Value(_lineWidth), focused ? Value(_offset) : 0, Value(_sensor), MeasurementMode,
                focused ? 4 : Value(_pupil), focused ? 100 : Value(_distance));
            var image = await _application.OpticalAssembly.SimulateAsync(request, token);
            if (!IsCurrent(generation, image.SourceRevision)) return;
            _image.SetResult(image);
            var positionInfo = image.HeadPosition is { } headPosition
                ? $"附加物镜位置 {Format(headPosition)} mm（同一基准）"
                : $"自准直仪物镜位置 {Format(image.CollimatorPosition)} mm（同一基准）";
            var scaleLabel = image.MatchesSelectedConjugate ? "近轴倍率" : "主光线比例";
            _imageInfo.Text = $"表面 {row.SurfaceNumber} · {row.SurfaceLabel} · {selected.Kind}\n" +
                $"{scaleLabel} {Format(image.Magnification)}×；名义像长 {Format(image.ImageLength)} mm\n" +
                positionInfo + "\n" +
                $"返回 {image.ReturnedRays:N0} / {image.LaunchedRays:N0}，落入探测面 {image.RecordedRays:N0}\n{image.Message}";
            _status.Text = image.RecordedRays > 0 ? "叉丝图像已更新；可选择其他表面，或修改当前模式的仪器参数。" : image.Message;
        }
        catch (OperationCanceledException) { if (generation == _generation) _imageInfo.Text = "图像计算已取消"; }
        catch (Exception ex) { if (generation == _generation) _imageInfo.Text = ex.Message; }
        finally { EndOperation(generation); }
    }

    private void BindRows()
    {
        _bindingRows = true;
        try
        {
            var previous = (_grid.SelectedItem as TableRow)?.Row;
            IEnumerable<OpticalAssemblyRow> rows = _result?.Rows ?? Array.Empty<OpticalAssemblyRow>();
            if (_sort.IsChecked == true) rows = rows.OrderBy(x => x.PositionMillimeters ?? double.PositiveInfinity);
            var items = rows.Select(x => new TableRow(x)).ToArray();
            _grid.ItemsSource = items;
            _grid.SelectedItem = items.FirstOrDefault(x => x.Row == previous)
                ?? items.FirstOrDefault(x => MeasurementMode == AssemblyMeasurementMode.Infinity && x.Row.Status == AssemblyConjugateStatus.Infinity)
                ?? items.FirstOrDefault(x => CanPreview(x.Row));
        }
        finally { _bindingRows = false; }
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args) => OnUi(() =>
    {
        InvalidatePrescription("镜头已改变，旧结果已清除；请重新计算"); UpdateSource();
    });
    private void OnModeChanged(object? sender, WorkbenchModeChangedEventArgs args) => OnUi(() =>
        InvalidatePrescription("工作模式已改变；光学装调仅适用于顺序镜头"));
    private void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) { if (!_closed) action(); }
        else Dispatcher.UIThread.Post(() => { if (!_closed) action(); });
    }
    private void UpdateSource() => _source.Text = $"光学装调 · {_application.Documents.GetSnapshot().Name}";
    private void InvalidatePrescription(string message)
    {
        CancelOperation(); _result = null; BindRows(); ClearImage(); _status.Text = message; UpdateAvailability();
    }
    private void ClearImage() { _imageRow = null; _image.SetResult(null); _imageInfo.Text = "选择顶点像或球心像；坐标刻度为探测面毫米，不随叉丝自动缩放。"; }
    private (int Generation, CancellationToken Token) BeginOperation()
    {
        CancelOperation(); _operation = new CancellationTokenSource(); _busy = true; UpdateAvailability();
        return (_generation, _operation.Token);
    }
    private void CancelOperation()
    {
        _generation++; _operation?.Cancel(); _operation?.Dispose(); _operation = null; _busy = false; UpdateAvailability();
    }
    private void EndOperation(int generation)
    {
        if (generation != _generation) return;
        _operation?.Dispose(); _operation = null; _busy = false; UpdateAvailability();
    }
    private bool IsCurrent(int generation, long revision) => !_closed && generation == _generation
        && revision == _application.Events.Revision;
    private void UpdateAvailability()
    {
        var sequential = _application.Modes.CurrentMode == OpticalWorkbenchMode.Sequential;
        var selected = (_grid.SelectedItem as TableRow)?.Row;
        ControlAvailability.Set(_calculate, sequential && !_busy, _busy ? "正在计算；可先取消" : "请切换到顺序模式并打开镜头");
        ControlAvailability.Set(_simulate, sequential && !_busy && selected is not null && CanPreview(selected),
            !sequential ? "请切换到顺序模式并打开镜头" : _busy ? "正在计算；可先取消"
                : selected is null ? "请先计算并选择一个表面共轭" : UnavailableReason(selected));
        ControlAvailability.Set(_cancel, _busy, "当前没有进行中的计算");
    }
    private AssemblyMeasurementMode MeasurementMode => (_probeMode.SelectedItem as ModeOption)?.Mode ?? AssemblyMeasurementMode.Focused;
    private bool CanPreview(OpticalAssemblyRow row) => row.Status == AssemblyConjugateStatus.Finite
        || (row.Status == AssemblyConjugateStatus.Infinity && MeasurementMode == AssemblyMeasurementMode.Infinity);
    private static string UnavailableReason(OpticalAssemblyRow row) => row.Status == AssemblyConjugateStatus.Infinity
        ? "所选共轭在无穷远，请切换到无穷远（平行光）模式。" : row.Message;
    private void UpdateModeFields()
    {
        var focused = MeasurementMode == AssemblyMeasurementMode.Focused;
        foreach (var field in _focusedFields) field.IsVisible = focused;
        foreach (var field in _infinityFields) field.IsVisible = !focused;
        _modeInfo.Text = focused ? "附加物镜将平行光汇聚到有限找像位置；∞ 共轭请切换为无穷远模式。"
            : "无穷远模式不加测量物镜；距离只改变仪器位置，不能对焦有限共轭。自准直仪焦距仍为有限值。";
    }
    private static double Value(NumericUpDown input) => input.Value is { } value ? (double)value
        : throw new ArgumentException("请填写所有数值参数。");
    private static string Format(double? value) => value?.ToString("0.######", CultureInfo.InvariantCulture) ?? "—";
    private static NumericUpDown Number(string name, decimal value, decimal minimum, decimal maximum, decimal increment) => new()
    {
        Name = name,
        Value = value,
        Minimum = minimum,
        Maximum = maximum,
        Increment = increment,
        Width = 106,
        Height = 32,
        FormatString = "0.###"
    };
    private static Control Field(string label, Control input)
    {
        AutomationProperties.SetName(input, label);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 0, 12, 8),
            Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, input }
        };
    }
    private static DataGridTextColumn Column(string label, string property, double width) => new()
    { Header = label, Binding = new Binding(property), Width = new DataGridLength(width) };

    private sealed record TableRow(OpticalAssemblyRow Row)
    {
        public int Surface => Row.SurfaceNumber;
        public string Label => Row.SurfaceLabel;
        public string Kind => Row.Kind == AssemblyConjugateKind.Vertex ? "顶点像" : "球心像";
        public string Position => Row.Status == AssemblyConjugateStatus.Infinity ? "∞" : Format(Row.PositionMillimeters);
        public string Message => Row.Message;
    }
    private sealed record ModeOption(AssemblyMeasurementMode Mode, string Label)
    {
        public override string ToString() => Label;
    }
}
