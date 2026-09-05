using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.ViewModels;

namespace OptilandWorkbench.App.Panels;

public sealed partial class LensEditorPanel
{
    private Flyout? _radiusSolveFlyout;
    private long _radiusSolveRevision;

    private Control CreateRadiusCell(SurfaceEditorRow? row)
    {
        if (row is null) return new TextBlock();
        var editor = CreateNumericEditor(row.RadiusDisplay, text =>
        {
            row.RadiusDisplay = text;
            _prescription.UpdateSurface(row.ToDto());
        });
        editor.IsReadOnly = row.RadiusSolve.Kind == RadiusSolveKind.Pickup;
        var marker = new Button
        {
            Name = "RadiusSolveButton",
            // Opening is non-mutating; react on press before a pending numeric
            // commit refreshes this row and detaches the release target.
            ClickMode = ClickMode.Press,
            Content = row.RadiusSolve.Kind switch
            {
                RadiusSolveKind.Variable => "V",
                RadiusSolveKind.Pickup => "P",
                _ => string.Empty
            },
            Width = 24,
            MinWidth = 24,
            MinHeight = 0,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsEnabled = row.CanOptimize && row.GeometryComputable
        };
        marker.BindThemeResource(Button.BorderBrushProperty, ThemeResourceBindings.Border);
        ToolTip.SetTip(marker, $"表面 {row.Number} 的曲率求解：{SolveLabel(row.RadiusSolve.Kind)}");
        Avalonia.Automation.AutomationProperties.SetName(marker, $"表面 {row.Number} 曲率求解类型");
        marker.Click += (_, _) => BeginRadiusSolve(row.Number);
        var cell = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24") };
        Grid.SetColumn(marker, 1);
        cell.Children.Add(editor);
        cell.Children.Add(marker);
        return cell;
    }

    private void BeginRadiusSolve(int number)
    {
        if (!_grid.CommitEdit(DataGridEditingUnit.Cell, true)
            || !_grid.CommitEdit(DataGridEditingUnit.Row, true)) return;
        _grid.Focus();
        var revision = _events.Revision;
        // Numeric LostFocus may have queued a refresh; anchor to the replacement row.
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || revision != _events.Revision) return;
            var row = _grid.ItemsSource!.Cast<SurfaceEditorRow>().FirstOrDefault(item => item.Number == number);
            var visualRow = _grid.GetVisualDescendants().OfType<DataGridRow>()
                .FirstOrDefault(item => item.DataContext is SurfaceEditorRow data && data.Number == number);
            var marker = visualRow?.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.Name == "RadiusSolveButton");
            if (row is null || marker is null || !marker.IsEnabled) return;
            _grid.SelectedItem = row;
            ShowRadiusSolve(row, marker);
        });
    }

    private void ShowRadiusSolve(SurfaceEditorRow row, Control anchor)
    {
        CloseRadiusSolve();
        _radiusSolveRevision = _events.Revision;
        var revision = _radiusSolveRevision;
        var current = row.RadiusSolve;
        var kinds = new[] { RadiusSolveKind.Fixed, RadiusSolveKind.Variable, RadiusSolveKind.Pickup };
        var kind = new ComboBox
        {
            Name = "RadiusSolveKind",
            ItemsSource = kinds.Select(SolveLabel).ToArray(),
            SelectedIndex = current.Kind == RadiusSolveKind.Fixed ? 1 : Array.IndexOf(kinds, current.Kind),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var source = new ComboBox
        {
            Name = "RadiusPickupSource",
            ItemsSource = Enumerable.Range(0, row.Number).ToArray(),
            SelectedItem = current.Kind == RadiusSolveKind.Pickup ? current.SourceSurface : Math.Max(0, row.Number - 1),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var scale = new TextBox
        {
            Name = "RadiusPickupScale",
            Text = (current.PickupEditable ? current.ScaleFactor : 1).ToString("G17", CultureInfo.CurrentCulture)
        };
        var pickupFields = new StackPanel
        {
            Name = "RadiusPickupFields",
            Spacing = 8,
            Children =
            {
                SolveRow("拾取表面：", source),
                SolveRow("比例因子：", scale),
                SolveRow("拾取列：", new TextBlock { Text = "曲率半径", VerticalAlignment = VerticalAlignment.Center })
            }
        };
        var error = new TextBlock { Name = "RadiusSolveError", TextWrapping = TextWrapping.Wrap, IsVisible = false };
        error.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextError);
        var apply = new Button { Name = "ApplyRadiusSolve", Content = "确定", MinWidth = 64 };
        var cancel = new Button { Name = "CancelRadiusSolve", Content = "取消", MinWidth = 64 };
        var content = new StackPanel
        {
            Name = "RadiusSolveContent",
            Width = 340,
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = $"在面 {row.Number} 上的曲率求解", FontWeight = FontWeight.SemiBold },
                SolveRow("求解类型：", kind), pickupFields, error,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Children = { apply, cancel }
                }
            }
        };
        SettingsPanelChrome.ApplyInputStyles(content);
        void UpdateFields()
        {
            var pickup = kind.SelectedIndex == 2;
            pickupFields.IsVisible = pickup && current.PickupEditable;
            pickupFields.IsEnabled = current.PickupEditable;
            apply.IsEnabled = !pickup || current.PickupEditable;
            error.IsVisible = pickup && !current.PickupEditable;
            error.Text = "此旧版拾取含当前不支持的参数，保持原设置。可选择固定或变量后重新设置。";
        }
        kind.SelectionChanged += (_, _) => UpdateFields();
        scale.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty && current.PickupEditable) error.IsVisible = false;
        };
        source.PropertyChanged += (_, args) =>
        {
            if (args.Property == ComboBox.SelectedItemProperty && current.PickupEditable) error.IsVisible = false;
        };
        UpdateFields();
        cancel.Click += (_, _) => CloseRadiusSolve();
        apply.Click += (_, _) =>
        {
            try
            {
                var chosen = kinds[kind.SelectedIndex];
                var factor = 1.0;
                var sourceNumber = 0;
                if (chosen == RadiusSolveKind.Pickup)
                {
                    if (source.SelectedItem is not int selected) throw new ArgumentException("请选择拾取表面。");
                    sourceNumber = selected;
                    if (!double.TryParse(scale.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out factor)
                        && !double.TryParse(scale.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out factor))
                        throw new ArgumentException("请输入有效的比例因子。");
                }
                _prescription.SetRadiusSolve(row.Number, new RadiusSolveUpdateDto(chosen, sourceNumber, factor), revision);
                CloseRadiusSolve();
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
            {
                error.Text = exception.Message;
                error.IsVisible = true;
            }
        };
        _radiusSolveFlyout = new Flyout { Content = content, Placement = PlacementMode.BottomEdgeAlignedLeft };
        _radiusSolveFlyout.ShowAt(anchor);
    }

    private void CloseRadiusSolve()
    {
        _radiusSolveFlyout?.Hide();
        _radiusSolveFlyout = null;
    }

    private static Grid SolveRow(string label, Control input)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("100,*") };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(input, 1);
        row.Children.Add(input);
        return row;
    }

    private static string SolveLabel(RadiusSolveKind kind) => kind switch
    {
        RadiusSolveKind.Variable => "变量",
        RadiusSolveKind.Pickup => "拾取",
        _ => "固定"
    };
}
