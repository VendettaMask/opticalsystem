using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Panels;

public sealed partial class MultiConfigurationPanel
{
    private sealed record ParameterChoice(MultiConfigurationParameterKind Kind, string Label)
    { public override string ToString() => Label; }
    private sealed record OperandDisplay(MultiConfigurationOperandRowDto Row, string Code, string[] Values)
    { public int Number => Row.Number; public int Surface => Row.SurfaceNumber; }
    private readonly DataGrid _operandGrid = new()
    {
        Name = "MultiConfigurationOperands",
        AutoGenerateColumns = false,
        IsReadOnly = true,
        GridLinesVisibility = DataGridGridLinesVisibility.All,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        CanUserResizeColumns = true,
        MinHeight = 180
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
        _operandGrid.SelectionChanged += (_, _) => LoadOperandValue();
        _operandConfiguration.TextChanged += (_, _) => LoadOperandValue();
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
        }));
        toolbar.Children.Add(ActionButton("上移", "MoveMultiConfigurationOperandUp", () => MoveOperand(-1)));
        toolbar.Children.Add(ActionButton("下移", "MoveMultiConfigurationOperandDown", () => MoveOperand(1)));
        toolbar.Children.Add(ActionButton("删除行", "RemoveMultiConfigurationOperand", () =>
        {
            var selected = SelectedOperand(); var rows = Bindings(); rows.RemoveAt(selected.Number - 1);
            _configurations.ReplaceOperandRows(rows, _operandRevision);
        }));
        var editor = new WrapPanel { Orientation = Orientation.Horizontal };
        editor.Children.Add(Label("配置号")); editor.Children.Add(_operandConfiguration);
        editor.Children.Add(_operandValueLabel); editor.Children.Add(_operandValue);
        var apply = ActionButton("应用数值", "ApplyMultiConfigurationValue", () =>
        {
            if (!double.TryParse(_operandValue.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                throw new ArgumentException("请输入有限数值；曲率单位为 mm⁻¹，厚度和半口径为 mm。");
            var selected = SelectedOperand(); var configuration = PositiveInteger(_operandConfiguration.Text, "配置号");
            if (_editingBinding != new MultiConfigurationOperandBindingDto(selected.Row.Kind, selected.Row.SurfaceNumber)
                || configuration != _editingConfiguration)
                throw new InvalidOperationException("输入仍属于之前选择的单元格；请先还原输入，再编辑当前单元格。");
            _configurations.SetOperandValue(selected.Number, configuration - 1, value, _operandRevision);
        });
        apply.Classes.Add("accent"); editor.Children.Add(apply);
        editor.Children.Add(ActionButton("还原输入", "ReloadMultiConfigurationValue", () => { _operandDraft = false; _pickupDraft = false; LoadOperandValue(); }));
        var variableBar = new WrapPanel { Orientation = Orientation.Horizontal };
        variableBar.Children.Add(ActionButton("设为变量", "SetMultiConfigurationVariable", () => SetCellVariable(true)));
        variableBar.Children.Add(ActionButton("取消变量", "ClearMultiConfigurationVariable", () => SetCellVariable(false)));
        variableBar.Children.Add(new TextBlock { Text = "V = 联合变量；P = 拾取；其他配置无需先激活", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        var footer = new StackPanel { Children = { editor, variableBar, BuildPickupEditor(), _operandMessage } };
        var hint = new TextBlock { Text = "行号 Op# 与表面号不同。数值读取当前配置参数；编辑非基准配置会断开该参数的基准链接。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
        var header = new StackPanel { Children = { hint, toolbar } };
        var root = new DockPanel(); DockPanel.SetDock(header, Avalonia.Controls.Dock.Top); DockPanel.SetDock(footer, Avalonia.Controls.Dock.Bottom);
        root.Children.Add(header); root.Children.Add(footer); root.Children.Add(_operandGrid);
        return root;
    }

    private Button ActionButton(string text, string name, Action action)
    {
        var button = new Button { Content = text, Name = name, Margin = new Thickness(4, 2), MinWidth = 72 };
        button.Click += (_, _) =>
        {
            try
            {
                if (_pickupDraft && name is not ("ApplyMultiConfigurationPickup" or "ReloadMultiConfigurationValue"))
                    throw new InvalidOperationException("请先应用拾取设置或还原输入。");
                if (_operandDraft && name is not ("ApplyMultiConfigurationValue" or "ReloadMultiConfigurationValue"))
                    throw new InvalidOperationException("请先应用或还原当前单元格输入。");
                action(); _operandDraft = false; _pickupDraft = false; RefreshOperandTable();
                _operandMessage.Text = name == "ReloadMultiConfigurationValue" ? "已重新读取当前单元格。" : "多配置行表已更新。";
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
            { _operandMessage.Text = exception.Message; }
        };
        return button;
    }

    private List<MultiConfigurationOperandBindingDto> Bindings() => _configurations.GetOperandRows()
        .Select(row => new MultiConfigurationOperandBindingDto(row.Kind, row.SurfaceNumber)).ToList();
    private OperandDisplay SelectedOperand() => _operandGrid.SelectedItem as OperandDisplay
        ?? throw new InvalidOperationException("请先选择多配置操作数行。");
    private static int PositiveInteger(string? text, string label) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value : throw new ArgumentException($"{label}必须是从 1 开始的整数。");
    private void MoveOperand(int delta)
    {
        var selected = SelectedOperand(); var rows = Bindings(); var index = selected.Number - 1; var next = index + delta;
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
        var selected = (_operandGrid.SelectedItem as OperandDisplay)?.Row;
        var rows = _configurations.GetOperandRows();
        _operandGrid.Columns.Clear();
        _operandGrid.Columns.Add(new DataGridTextColumn { Header = "Op#", Binding = new Binding(nameof(OperandDisplay.Number)), Width = new DataGridLength(58) });
        _operandGrid.Columns.Add(new DataGridTextColumn { Header = "类型", Binding = new Binding(nameof(OperandDisplay.Code)), Width = new DataGridLength(85) });
        _operandGrid.Columns.Add(new DataGridTextColumn { Header = "表面", Binding = new Binding(nameof(OperandDisplay.Surface)), Width = new DataGridLength(60) });
        var count = _configurations.GetRows().Count;
        for (var i = 0; i < count; i++)
            _operandGrid.Columns.Add(new DataGridTextColumn { Header = $"配置 {i + 1}", Binding = new Binding($"Values[{i}]"), Width = new DataGridLength(120) });
        var choices = (ParameterChoice[])_operandKind.ItemsSource!;
        var display = rows.Select(row => new OperandDisplay(row, choices.Single(choice => choice.Kind == row.Kind).Label.Split(' ')[0],
            row.Values.Select((value, index) => value.ToString("G12", CultureInfo.InvariantCulture) + (row.Pickups[index] is not null ? "  P" : row.Variables[index] ? "  V" : string.Empty)).ToArray())).ToArray();
        _operandRevision = _events.Revision;
        _operandGrid.ItemsSource = display;
        _operandGrid.SelectedItem = display.FirstOrDefault(row => row.Row.Kind == selected?.Kind && row.Row.SurfaceNumber == selected?.SurfaceNumber) ?? display.FirstOrDefault();
        LoadOperandValue();
    }

    private void SetCellVariable(bool enabled)
    {
        var selected = SelectedOperand();
        var configuration = PositiveInteger(_operandConfiguration.Text, "配置号");
        if (_editingBinding != new MultiConfigurationOperandBindingDto(selected.Row.Kind, selected.Row.SurfaceNumber)
            || configuration != _editingConfiguration)
            throw new InvalidOperationException("请先选择有效单元格并还原输入，再设置变量。");
        _configurations.SetOperandVariable(selected.Number, configuration - 1, enabled, _operandRevision);
    }

    private void LoadOperandValue()
    {
        if (_operandDraft || _pickupDraft) return;
        if (_operandGrid.SelectedItem is not OperandDisplay selected) return;
        if (!int.TryParse(_operandConfiguration.Text, out var number) || number < 1 || number > selected.Row.Values.Count) return;
        _operandValueLabel.Text = selected.Row.Kind switch
        {
            MultiConfigurationParameterKind.Thickness => "厚度（mm）",
            MultiConfigurationParameterKind.Curvature => "曲率（mm⁻¹）",
            MultiConfigurationParameterKind.Conic => "圆锥系数",
            _ => "半口径（mm）"
        };
        _editingBinding = new(selected.Row.Kind, selected.Row.SurfaceNumber); _editingConfiguration = number;
        _loadingOperand = true;
        _operandValue.Text = selected.Row.Values[number - 1].ToString("G17", CultureInfo.InvariantCulture);
        LoadPickupFields(selected.Row.Pickups[number - 1]);
        _loadingOperand = false;
    }
}
