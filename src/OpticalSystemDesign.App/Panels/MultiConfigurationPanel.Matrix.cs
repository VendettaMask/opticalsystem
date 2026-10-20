using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class MultiConfigurationPanel
{
    private Control BuildMatrixHeader()
    {
        var toggle = MatrixButton("ToggleMultiConfigurationProperties", "chevron-down", "展开操作数属性", () => ToggleOperandProperties());
        _previousMatrixOperand = MatrixButton("PreviousMultiConfigurationOperand", "chevron-left", "上一个操作数", () => NavigateOperand(-1));
        _nextMatrixOperand = MatrixButton("NextMultiConfigurationOperand", "chevron-right", "下一个操作数", () => NavigateOperand(1));
        _previousMatrixConfiguration = MatrixButton("PreviousMatrixConfiguration", "chevron-left", "上一个结构", () => ActivateMatrixConfiguration(-1));
        _nextMatrixConfiguration = MatrixButton("NextMatrixConfiguration", "chevron-right", "下一个结构", () => ActivateMatrixConfiguration(1));
        var properties = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0),
            Children = { toggle, _operandPosition, _previousMatrixOperand, _nextMatrixOperand }
        };
        var configurations = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _matrixPosition, _previousMatrixConfiguration, _nextMatrixConfiguration }
        };
        return new WrapPanel
        {
            Name = "MultiConfigurationMatrixHeader",
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 6),
            Children = { properties, configurations, ActionButton("新增配置", "AddMatrixConfiguration", () => _configurations.Add()) }
        };
    }

    private Button MatrixButton(string name, string icon, string caption, Action action)
    {
        var button = ActionButton(caption, name, action);
        button.Content = new LocalIcon { IconName = icon, Width = 14, Height = 14 };
        button.Width = button.Height = 28;
        button.MinWidth = button.MinHeight = 0;
        button.Padding = new Thickness(0);
        button.CornerRadius = new CornerRadius(14);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(button, caption);
        AutomationProperties.SetName(button, caption);
        return button;
    }

    private void ToggleOperandProperties(bool? expanded = null)
    {
        _operandProperties.IsVisible = expanded ?? !_operandProperties.IsVisible;
        var toggle = this.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "ToggleMultiConfigurationProperties");
        ((LocalIcon)toggle.Content!).IconName = _operandProperties.IsVisible ? "chevron-up" : "chevron-down";
        var caption = _operandProperties.IsVisible ? "收起操作数属性" : "展开操作数属性";
        ToolTip.SetTip(toggle, caption);
        AutomationProperties.SetName(toggle, caption);
    }

    private void NavigateOperand(int delta)
    {
        var index = _operandGrid.SelectedIndex + delta;
        if (index < 0 || index >= _operandGrid.ItemsSource!.Cast<object>().Count()) return;
        _operandGrid.SelectedIndex = index;
        _operandGrid.ScrollIntoView(_operandGrid.SelectedItem, null);
    }

    private void ActivateMatrixConfiguration(int delta)
    {
        var state = _configurations.GetNavigationState();
        var index = state.ActiveIndex + delta;
        if (index < 0 || index >= state.Count) return;
        _operandConfiguration.Text = (index + 1).ToString(CultureInfo.InvariantCulture);
        _configurations.Activate(index);
    }

    private void RefreshMatrixHeader()
    {
        var state = _configurations.GetNavigationState();
        _matrixPosition.Text = $"结构 {state.ActiveIndex + 1}/{state.Count}";
        _operandPosition.Text = _operandGrid.SelectedItem is OperandDisplay { Row: { } row }
            ? $"操作数 {row.Number} 属性" : _operandGrid.SelectedItem is OperandDisplay { ThicknessSurface: not null } ? "厚度差异属性" : "系统孔径属性";
        SetMatrixButton(_previousMatrixConfiguration!, state.ActiveIndex > 0,
            state.Count == 1 ? "只有一个结构，无需切换" : "已是第一个结构", "上一个结构");
        SetMatrixButton(_nextMatrixConfiguration!, state.ActiveIndex + 1 < state.Count,
            state.Count == 1 ? "只有一个结构，无需切换" : "已是最后一个结构", "下一个结构");
        SetMatrixButton(_previousMatrixOperand!, _operandGrid.SelectedIndex > 0, "已是第一行", "上一个操作数");
        SetMatrixButton(_nextMatrixOperand!, _operandGrid.SelectedIndex + 1 < _operandGrid.ItemsSource!.Cast<object>().Count(), "已是最后一行", "下一个操作数");
        RefreshMatrixSelection();
    }

    private static void SetMatrixButton(Button button, bool enabled, string reason, string caption)
    {
        ControlAvailability.Set(button, enabled, reason);
        if (enabled) ToolTip.SetTip(button, caption);
    }

    private DataGridColumn MatrixColumn(int index, int activeIndex)
    {
        var header = new TextBlock { Text = $"结构 {index + 1}{(index == activeIndex ? "*" : "")}" };
        ToolTip.SetTip(header, "双击列标题激活结构");
        return new DataGridTemplateColumn
        {
            Header = header,
            Tag = index,
            Width = new DataGridLength(132),
            IsReadOnly = true,
            CellTemplate = new FuncDataTemplate<OperandDisplay>((row, _) =>
            {
                if (row is null) return new Border();
                var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
                text.Bind(TextBlock.TextProperty, new Binding($"Values[{index}]"));
                var cell = new Border
                {
                    Name = $"MultiConfigurationCell-{row.Code}-{row.Surface}-{index + 1}",
                    DataContext = row,
                    Child = text,
                    Padding = new Thickness(6, 2),
                    BorderThickness = new Thickness(2),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                cell.AttachedToVisualTree += (_, _) =>
                {
                    _matrixCells.Add((cell, row, index));
                    RefreshMatrixSelection();
                };
                cell.DetachedFromVisualTree += (_, _) => _matrixCells.RemoveAll(item => ReferenceEquals(item.Cell, cell));
                cell.PointerPressed += (_, e) =>
                {
                    if (_operandDraft || _pickupDraft)
                    {
                        _operandMessage.Text = "请先应用或还原当前单元格输入。";
                        e.Handled = true;
                        return;
                    }
                    _operandGrid.SelectedItem = cell.DataContext;
                    _operandConfiguration.Text = (index + 1).ToString(CultureInfo.InvariantCulture);
                    RefreshMatrixSelection();
                    if (e.ClickCount == 2) ToggleOperandProperties(true);
                };
                AutomationProperties.SetHelpText(cell, "单击选择单元格，双击展开属性编辑。");
                return cell;
            })
        };
    }

    private void OnMatrixPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (args.ClickCount != 2 || !args.GetCurrentPoint(_operandGrid).Properties.IsLeftButtonPressed
            || args.Source is not Visual source) return;
        var header = source as DataGridColumnHeader ?? source.GetVisualAncestors().OfType<DataGridColumnHeader>().FirstOrDefault();
        if (header is null || _operandGrid.Columns.FirstOrDefault(column => ReferenceEquals(column.Header, header.Content))?.Tag is not int index) return;
        var point = args.GetPosition(header);
        // Keep native double-click autosizing on the header's resize edges.
        if (point.X < 6 || point.X > header.Bounds.Width - 6) return;
        ExecuteOperandAction("ActivateMatrixColumn", () =>
        {
            _operandConfiguration.Text = (index + 1).ToString(CultureInfo.InvariantCulture);
            _configurations.Activate(index);
        });
        args.Handled = true;
    }

    private void RefreshMatrixSelection()
    {
        var active = _configurations.GetNavigationState().ActiveIndex;
        int.TryParse(_operandConfiguration.Text, out var editing);
        foreach (var (cell, _, index) in _matrixCells)
        {
            var row = cell.DataContext as OperandDisplay;
            var selected = ReferenceEquals(row, _operandGrid.SelectedItem) && index + 1 == editing;
            cell.BindThemeResource(Border.BackgroundProperty, selected ? ThemeResourceBindings.SelectionBackground
                : index == active ? ThemeResourceBindings.RibbonTabHover
                : row?.Binding is null ? ThemeResourceBindings.SubtleSurface : ThemeResourceBindings.WarningSurface);
            cell.BindThemeResource(Border.BorderBrushProperty, selected ? ThemeResourceBindings.TextAccent : ThemeResourceBindings.Border);
            ((TextBlock)cell.Child!).BindThemeResource(TextBlock.ForegroundProperty, selected ? ThemeResourceBindings.SelectionForeground : ThemeResourceBindings.TextPrimary);
        }
    }
}
