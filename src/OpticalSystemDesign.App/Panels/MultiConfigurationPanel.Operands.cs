using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Controls;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class MultiConfigurationPanel
{
    private sealed record ParameterChoice(MultiConfigurationParameterKind Kind, string Label)
    { public override string ToString() => Label; }
    private sealed record OperandDisplay(MultiConfigurationOperandRowDto? Row, string Code, string[] Values, IReadOnlyList<double> NumericValues, int? ThicknessSurface = null)
    {
        public string Number => Row?.Number.ToString(CultureInfo.InvariantCulture) ?? "—";
        public string Surface => (Row?.SurfaceNumber ?? ThicknessSurface)?.ToString(CultureInfo.InvariantCulture) ?? "—";
        public MultiConfigurationOperandBindingDto? Binding => Row is not null ? new(Row.Kind, Row.SurfaceNumber)
            : ThicknessSurface.HasValue ? new(MultiConfigurationParameterKind.Thickness, ThicknessSurface.Value) : null;
    }
    private readonly DataGrid _operandGrid = new()
    {
        Name = "MultiConfigurationOperands",
        AutoGenerateColumns = false,
        IsReadOnly = true,
        GridLinesVisibility = DataGridGridLinesVisibility.All,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        CanUserResizeColumns = true,
        FrozenColumnCount = 3,
        RowHeight = UiDensity.CompactTableRowHeight,
        ColumnHeaderHeight = UiDensity.TableHeaderHeight
    };
    private readonly ComboBox _operandKind = new() { MinWidth = 150 };
    private readonly TextBox _operandSurface = new() { Text = "1", Width = 64 };
    private readonly TextBox _operandConfiguration = new() { Text = "1", Width = 64 };
    private readonly TextBox _operandValue = new() { Width = 130 };
    private readonly TextBlock _operandValueLabel = Label("值");
    private readonly TextBlock _operandMessage = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
    private long _operandRevision;
    private bool _operandDraft;
    private bool _loadingOperand;
    private MultiConfigurationOperandBindingDto? _editingBinding;
    private int _editingConfiguration;
    private readonly TextBlock _matrixPosition = new() { Name = "MultiConfigurationPosition", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _operandPosition = new() { Name = "MultiConfigurationOperandPosition", VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _operandProperties = new() { Name = "MultiConfigurationOperandProperties", IsVisible = false };
    private readonly List<Control> _operandEditControls = new();
    private readonly List<Control> _operandBindingControls = new();
    private readonly List<(Border Cell, OperandDisplay Row, int Configuration)> _matrixCells = new();
    private Button? _previousMatrixConfiguration;
    private Button? _nextMatrixConfiguration;
    private Button? _previousMatrixOperand;
    private Button? _nextMatrixOperand;
    private bool _refreshingOperandTable;

    private Control BuildOperandTable()
    {
        _operandKind.ItemsSource = new[]
        {
            new ParameterChoice(MultiConfigurationParameterKind.Thickness, "THIC · 厚度"),
            new ParameterChoice(MultiConfigurationParameterKind.Curvature, "CRVT · 曲率"),
            new ParameterChoice(MultiConfigurationParameterKind.Conic, "CONN · 圆锥系数"),
            new ParameterChoice(MultiConfigurationParameterKind.SemiDiameter, "SDIA · 半口径")
        };
        _operandKind.SelectedIndex = 0;
        AutomationProperties.SetName(_operandKind, "多配置行类型");
        AutomationProperties.SetName(_operandSurface, "多配置行表面号");
        AutomationProperties.SetName(_operandConfiguration, "编辑配置号（从 1 开始）");
        AutomationProperties.SetName(_operandValue, "多配置单元格值");
        AutomationProperties.SetHelpText(_operandGrid, "Op# 是这里的行号；每个配置列显示该绑定参数的实际数值。");
        _operandGrid.BindThemeResource(DataGrid.RowBackgroundProperty, ThemeResourceBindings.Surface);
        _operandGrid.Styles.Add(new Style(s => s.OfType<DataGridColumnHeader>())
        {
            Setters = { new Setter(TemplatedControl.FontWeightProperty, FontWeight.Bold) }
        });
        _operandGrid.SelectionChanged += (_, _) => { if (!_refreshingOperandTable) { LoadOperandValue(); RefreshMatrixHeader(); } };
        _operandGrid.AddHandler(InputElement.PointerPressedEvent, OnMatrixPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        _operandGrid.CurrentCellChanged += (_, _) =>
        {
            if (!_refreshingOperandTable && !_operandDraft && !_pickupDraft && _operandGrid.CurrentColumn?.Tag is int index)
                _operandConfiguration.Text = (index + 1).ToString(CultureInfo.InvariantCulture);
        };
        _operandConfiguration.TextChanged += (_, _) => { if (!_refreshingOperandTable) { LoadOperandValue(); RefreshMatrixSelection(); } };
        _operandValue.PropertyChanged += (_, args) =>
        { if (args.Property == TextBox.TextProperty && !_loadingOperand) _operandDraft = true; };
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(Label("类型")); toolbar.Children.Add(_operandKind);
        toolbar.Children.Add(Label("表面")); toolbar.Children.Add(_operandSurface);
        toolbar.Children.Add(ActionButton("新增行", "AddMultiConfigurationOperand", () =>
        {
            if (_operandKind.SelectedItem is not ParameterChoice choice) throw new InvalidOperationException("请选择行类型。");
            var rows = Bindings(); rows.Add(new(choice.Kind, PositiveInteger(_operandSurface.Text, "表面号")));
            _configurations.ReplaceOperandRows(rows, _events.Revision);
            _operandGrid.SelectedItem = null;
        }));
        toolbar.Children.Add(ActionButton("上移", "MoveMultiConfigurationOperandUp", () => MoveOperand(-1)));
        toolbar.Children.Add(ActionButton("下移", "MoveMultiConfigurationOperandDown", () => MoveOperand(1)));
        toolbar.Children.Add(ActionButton("删除行", "RemoveMultiConfigurationOperand", () =>
        {
            var selected = SelectedOperand(); var rows = Bindings(); rows.RemoveAt(selected.Row!.Number - 1);
            _configurations.ReplaceOperandRows(rows, _operandRevision);
        }));
        _operandBindingControls.AddRange(toolbar.Children.OfType<Button>().Where(b => b.Name != "AddMultiConfigurationOperand"));
        var editor = new WrapPanel { Orientation = Orientation.Horizontal };
        editor.Children.Add(Label("配置号")); editor.Children.Add(_operandConfiguration);
        editor.Children.Add(_operandValueLabel); editor.Children.Add(_operandValue);
        var apply = ActionButton("应用数值", "ApplyMultiConfigurationValue", () =>
        {
            if (!double.TryParse(_operandValue.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                throw new ArgumentException("请输入有限数值；曲率单位为 mm⁻¹，厚度和半口径为 mm。");
            var selected = _operandGrid.SelectedItem as OperandDisplay ?? throw new InvalidOperationException("请先选择多配置操作数行。");
            var configuration = PositiveInteger(_operandConfiguration.Text, "配置号");
            if (_editingBinding is null || _editingBinding != selected.Binding
                || configuration != _editingConfiguration)
                throw new InvalidOperationException("输入仍属于之前选择的单元格；请先还原输入，再编辑当前单元格。");
            if (selected.Row is not null) _configurations.SetOperandValue(selected.Row.Number, configuration - 1, value, _operandRevision);
            else if (selected.ThicknessSurface.HasValue) _configurations.SetThickness(configuration - 1, selected.ThicknessSurface.Value, value, _operandRevision);
        });
        apply.Classes.Add("accent"); editor.Children.Add(apply);
        editor.Children.Add(ActionButton("还原输入", "ReloadMultiConfigurationValue", () => { _operandDraft = false; _pickupDraft = false; LoadOperandValue(); }));
        var variableBar = new WrapPanel { Orientation = Orientation.Horizontal };
        variableBar.Children.Add(ActionButton("设为变量", "SetMultiConfigurationVariable", () => SetCellVariable(true)));
        variableBar.Children.Add(ActionButton("取消变量", "ClearMultiConfigurationVariable", () => SetCellVariable(false)));
        variableBar.Children.Add(new TextBlock { Text = "V = 联合变量；P = 拾取；其他配置无需先激活", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        _operandEditControls.AddRange(new Control[] { _operandConfiguration, _operandValue, apply });
        _operandBindingControls.AddRange(variableBar.Children.OfType<Button>());
        var pickup = BuildPickupEditor();
        _operandBindingControls.AddRange(pickup.GetLogicalDescendants().OfType<Control>().Where(c => c is TextBox or Button));
        var hint = new TextBlock
        {
            Text = "Op# 为可编辑操作数行号。APER 为只读系统孔径，请在系统选项中修改；它不占用 Op#，不改变拾取或评价函数行引用。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 4)
        };
        var differencesHint = new TextBlock
        {
            Text = "未登记的有限厚度差异也会显示为 THIC 行，可编辑厚度但不占用 Op#；设置变量或拾取前需新增对应操作数行。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 0, 4, 4)
        };
        _operandProperties.Child = new StackPanel { Children = { toolbar, editor, variableBar, pickup, hint, differencesHint } };
        var header = new StackPanel { Children = { BuildMatrixHeader(), _operandProperties } };
        var root = new DockPanel(); DockPanel.SetDock(header, Avalonia.Controls.Dock.Top); DockPanel.SetDock(_operandMessage, Avalonia.Controls.Dock.Bottom);
        root.Children.Add(header); root.Children.Add(_operandMessage); root.Children.Add(_operandGrid);
        return root;
    }

    private Button ActionButton(string text, string name, Action action)
    {
        var button = new Button { Content = text, Name = name, Margin = new Thickness(4, 2), MinWidth = 72 };
        button.Click += (_, _) => ExecuteOperandAction(name, action);
        return button;
    }

    private void ExecuteOperandAction(string name, Action action)
    {
        if (_disposed) return;
        try
        {
            if (name == "ToggleMultiConfigurationProperties") { action(); return; }
            if (_pickupDraft && name is not ("ApplyMultiConfigurationPickup" or "ReloadMultiConfigurationValue"))
                throw new InvalidOperationException("请先应用拾取设置或还原输入。");
            if (_operandDraft && name is not ("ApplyMultiConfigurationValue" or "ReloadMultiConfigurationValue"))
                throw new InvalidOperationException("请先应用或还原当前单元格输入。");
            action(); _operandDraft = false; _pickupDraft = false; RefreshOperandTable();
            _operandMessage.Text = name == "ReloadMultiConfigurationValue" ? "已重新读取当前单元格。" : "多配置行表已更新。";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        { _operandMessage.Text = exception.Message; }
    }

    private List<MultiConfigurationOperandBindingDto> Bindings() => _configurations.GetOperandRows()
        .Select(row => new MultiConfigurationOperandBindingDto(row.Kind, row.SurfaceNumber)).ToList();
    private OperandDisplay SelectedOperand() => _operandGrid.SelectedItem is OperandDisplay { Row: not null } selected
        ? selected : throw new InvalidOperationException("请选择可编辑操作数；APER 请在系统选项中修改。");
    private static int PositiveInteger(string? text, string label) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value : throw new ArgumentException($"{label}必须是从 1 开始的整数。");
    private void MoveOperand(int delta)
    {
        var selected = SelectedOperand(); var rows = Bindings(); var index = selected.Row!.Number - 1; var next = index + delta;
        if (next < 0 || next >= rows.Count) throw new InvalidOperationException("该行已到达列表边界。");
        (rows[index], rows[next]) = (rows[next], rows[index]);
        _configurations.ReplaceOperandRows(rows, _operandRevision);
    }

    private void RefreshOperandTable()
    {
        if ((_operandDraft || _pickupDraft) && _operandRevision != _events.Revision)
        {
            _operandMessage.Text = "工程已变化；当前输入已保留，请还原输入后重新编辑。";
            return;
        }
        var selectedDisplay = _operandGrid.SelectedItem as OperandDisplay;
        var rows = _configurations.GetOperandRows();
        var state = _configurations.GetNavigationState();
        _refreshingOperandTable = true;
        _matrixCells.Clear();
        _operandGrid.Columns.Clear();
        _operandGrid.Columns.Add(new DataGridTextColumn { Header = "Op#", Binding = new Binding(nameof(OperandDisplay.Number)), Width = new DataGridLength(52) });
        _operandGrid.Columns.Add(new DataGridTextColumn { Header = new TextBlock { Text = $"活动: {state.ActiveIndex + 1}/{state.Count}" }, Binding = new Binding(nameof(OperandDisplay.Code)), Width = new DataGridLength(110) });
        _operandGrid.Columns.Add(new DataGridTextColumn { Header = "表面", Binding = new Binding(nameof(OperandDisplay.Surface)), Width = new DataGridLength(74) });
        for (var i = 0; i < state.Count; i++) _operandGrid.Columns.Add(MatrixColumn(i, state.ActiveIndex));
        var choices = (ParameterChoice[])_operandKind.ItemsSource!;
        var aperture = _configurations.GetApertureValues();
        var differences = _configurations.GetThicknessDifferences()
            .Where(row => !rows.Any(binding => binding.Kind == MultiConfigurationParameterKind.Thickness && binding.SurfaceNumber == row.SurfaceNumber))
            .Select(row => new OperandDisplay(null, "THIC", row.Values.Select(v => v.ToString("G12", CultureInfo.InvariantCulture)).ToArray(), row.Values, row.SurfaceNumber));
        var display = rows.Select(row => new OperandDisplay(row, choices.Single(choice => choice.Kind == row.Kind).Label.Split(' ')[0],
            row.Values.Select((value, index) => value.ToString("G12", CultureInfo.InvariantCulture) + (row.Pickups[index] is not null ? "  P" : row.Variables[index] ? "  V" : string.Empty)).ToArray(), row.Values))
            .Concat(differences)
            .Prepend(new OperandDisplay(null, "APER", aperture.Select(v => v.ToString("G12", CultureInfo.InvariantCulture)).ToArray(), aperture)).ToArray();
        _operandRevision = _events.Revision;
        _operandGrid.ItemsSource = display;
        _operandGrid.SelectedItem = selectedDisplay is null ? display.FirstOrDefault(row => row.Binding is not null) ?? display[0]
            : display.FirstOrDefault(row => row.Binding == selectedDisplay.Binding) ?? display.FirstOrDefault(row => row.Binding is not null) ?? display[0];
        if (!int.TryParse(_operandConfiguration.Text, out var editing) || editing < 1 || editing > state.Count)
            _operandConfiguration.Text = (state.ActiveIndex + 1).ToString(CultureInfo.InvariantCulture);
        _refreshingOperandTable = false;
        LoadOperandValue();
        RefreshMatrixHeader();
    }

    private void SetCellVariable(bool enabled)
    {
        var selected = SelectedOperand();
        var configuration = PositiveInteger(_operandConfiguration.Text, "配置号");
        if (_editingBinding != new MultiConfigurationOperandBindingDto(selected.Row!.Kind, selected.Row.SurfaceNumber)
            || configuration != _editingConfiguration)
            throw new InvalidOperationException("请先选择有效单元格并还原输入，再设置变量。");
        _configurations.SetOperandVariable(selected.Row!.Number, configuration - 1, enabled, _operandRevision);
    }

    private void LoadOperandValue()
    {
        if (_operandDraft || _pickupDraft) return;
        if (_operandGrid.SelectedItem is not OperandDisplay selected) return;
        var editable = selected.Binding is not null;
        foreach (var control in _operandEditControls) ControlAvailability.Set(control, editable, "APER 为只读系统孔径，请在系统选项中修改。");
        foreach (var control in _operandBindingControls) ControlAvailability.Set(control, selected.Row is not null,
            editable ? "厚度差异尚未登记为操作数行；可新增相同类型和表面号后设置变量或拾取。" : "APER 为只读系统孔径，请在系统选项中修改。");
        if (!int.TryParse(_operandConfiguration.Text, out var number) || number < 1 || number > selected.NumericValues.Count)
        {
            _editingBinding = null;
            _loadingOperand = true;
            _operandValue.Text = string.Empty;
            _loadingOperand = false;
            return;
        }
        if (selected.Binding is null)
        {
            _editingBinding = null;
            _loadingOperand = true;
            _operandValueLabel.Text = "系统孔径（只读）";
            _operandValue.Text = selected.NumericValues[number - 1].ToString("G17", CultureInfo.InvariantCulture);
            _loadingOperand = false;
            return;
        }
        _operandValueLabel.Text = selected.Binding.Kind switch
        {
            MultiConfigurationParameterKind.Thickness => "厚度（mm）",
            MultiConfigurationParameterKind.Curvature => "曲率（mm⁻¹）",
            MultiConfigurationParameterKind.Conic => "圆锥系数",
            _ => "半口径（mm）"
        };
        _editingBinding = selected.Binding; _editingConfiguration = number;
        _loadingOperand = true;
        _operandValue.Text = selected.NumericValues[number - 1].ToString("G17", CultureInfo.InvariantCulture);
        LoadPickupFields(selected.Row?.Pickups[number - 1]);
        _loadingOperand = false;
    }
}
