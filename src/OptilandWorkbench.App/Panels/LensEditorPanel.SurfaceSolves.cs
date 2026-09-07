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
    private Flyout? _thicknessSolveFlyout;
    private Flyout? _semiDiameterSolveFlyout;
    private long _thicknessSolveRevision;
    private long _semiDiameterSolveRevision;

    private Control CreateThicknessCell(SurfaceEditorRow? row)
    {
        if (row is null) return new TextBlock();
        if (row.IsLastSurface)
        {
            return new TextBlock
            {
                Text = "-",
                Margin = new Thickness(8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                TextAlignment = TextAlignment.Right
            };
        }

        var editor = CreateNumericEditor(row.ThicknessDisplay, text =>
        {
            row.ThicknessDisplay = text;
            _prescription.UpdateSurface(row.ToDto());
        });
        editor.IsReadOnly = row.ThicknessSolve.Kind == ThicknessSolveKind.Pickup;
        var marker = CreateSolveMarker(
            "ThicknessSolveButton",
            row.ThicknessSolve.Kind switch
            {
                ThicknessSolveKind.Variable => "V",
                ThicknessSolveKind.Pickup => "P",
                _ => string.Empty
            },
            row.CanOptimize,
            $"表面 {row.Number} 的厚度求解：{ThicknessSolveLabel(row.ThicknessSolve.Kind)}",
            $"表面 {row.Number} 厚度求解类型",
            () => BeginThicknessSolve(row.Number));
        return SolveCell("ThicknessSolveCell", editor, marker);
    }

    private Control CreateSemiDiameterCell(SurfaceEditorRow? row)
    {
        if (row is null) return new TextBlock();
        var editor = CreateNumericEditor(row.SemiDiameterDisplay, text =>
        {
            row.SemiDiameterDisplay = text;
            _prescription.UpdateSurface(row.ToDto());
        });
        editor.IsReadOnly = row.SemiDiameterSolve.Kind != SemiDiameterSolveKind.Fixed;
        var marker = CreateSolveMarker(
            "SemiDiameterSolveButton",
            row.SemiDiameterSolve.Kind switch
            {
                SemiDiameterSolveKind.Fixed => "U",
                SemiDiameterSolveKind.Pickup => "P",
                _ => string.Empty
            },
            row.GeometryComputable,
            $"表面 {row.Number} 的净口径求解：{SemiDiameterSolveLabel(row.SemiDiameterSolve.Kind)}",
            $"表面 {row.Number} 净口径求解类型",
            () => BeginSemiDiameterSolve(row.Number));
        return SolveCell("SemiDiameterSolveCell", editor, marker);
    }

    private static Button CreateSolveMarker(
        string name,
        string content,
        bool enabled,
        string tooltip,
        string automationName,
        Action open)
    {
        var marker = new Button
        {
            Name = name,
            ClickMode = ClickMode.Press,
            Content = content,
            Width = 24,
            MinWidth = 24,
            MinHeight = 0,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsEnabled = enabled
        };
        marker.BindThemeResource(Button.BorderBrushProperty, ThemeResourceBindings.Border);
        ToolTip.SetTip(marker, tooltip);
        Avalonia.Automation.AutomationProperties.SetName(marker, automationName);
        marker.Click += (_, _) => open();
        return marker;
    }

    private static Grid SolveCell(string name, Control editor, Control marker)
    {
        var cell = new Grid { Name = name, ColumnDefinitions = new ColumnDefinitions("*,24") };
        Grid.SetColumn(marker, 1);
        cell.Children.Add(editor);
        cell.Children.Add(marker);
        return cell;
    }

    private void BeginThicknessSolve(int number) => BeginSurfaceSolve(
        number,
        "ThicknessSolveButton",
        row => row.CanOptimize,
        ShowThicknessSolve);

    private void BeginSemiDiameterSolve(int number) => BeginSurfaceSolve(
        number,
        "SemiDiameterSolveButton",
        row => row.GeometryComputable,
        ShowSemiDiameterSolve);

    private void BeginSurfaceSolve(
        int number,
        string markerName,
        Func<SurfaceEditorRow, bool> canOpen,
        Action<SurfaceEditorRow, Control> show)
    {
        if (!_grid.CommitEdit(DataGridEditingUnit.Cell, true)
            || !_grid.CommitEdit(DataGridEditingUnit.Row, true)) return;
        _grid.Focus();
        var revision = _events.Revision;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || revision != _events.Revision) return;
            var row = _grid.ItemsSource!.Cast<SurfaceEditorRow>().FirstOrDefault(item => item.Number == number);
            var visualRow = _grid.GetVisualDescendants().OfType<DataGridRow>()
                .FirstOrDefault(item => item.DataContext is SurfaceEditorRow data && data.Number == number);
            var marker = visualRow?.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.Name == markerName);
            if (row is null || marker is null || !marker.IsEnabled || !canOpen(row)) return;
            _grid.SelectedItem = row;
            show(row, marker);
        });
    }

    private void ShowThicknessSolve(SurfaceEditorRow row, Control anchor)
    {
        CloseThicknessSolve();
        _thicknessSolveRevision = _events.Revision;
        var revision = _thicknessSolveRevision;
        var current = row.ThicknessSolve;
        var kinds = new[] { ThicknessSolveKind.Fixed, ThicknessSolveKind.Variable, ThicknessSolveKind.Pickup };
        var kind = SolveKindPicker(
            "ThicknessSolveKind",
            kinds.Select(ThicknessSolveLabel).ToArray(),
            current.Kind == ThicknessSolveKind.Fixed ? 1 : Array.IndexOf(kinds, current.Kind));
        var source = PickupSource("ThicknessPickupSource", row.Number, current.SourceSurface, current.Kind == ThicknessSolveKind.Pickup);
        var scale = PickupNumber("ThicknessPickupScale", current.ScaleFactor);
        var offset = PickupNumber("ThicknessPickupOffset", current.Offset);
        var fields = new StackPanel
        {
            Name = "ThicknessPickupFields",
            Spacing = 8,
            Children =
            {
                SolveRow("拾取表面：", source),
                SolveRow("比例因子：", scale),
                SolveRow("偏移量：", offset),
                SolveRow("拾取列：", new TextBlock { Text = "厚度", VerticalAlignment = VerticalAlignment.Center })
            }
        };
        var (content, error, apply, cancel) = SolveContent(
            "ThicknessSolveContent",
            $"在面 {row.Number} 上的厚度求解",
            kind,
            fields);
        void UpdateFields() => fields.IsVisible = kind.SelectedIndex == 2;
        kind.SelectionChanged += (_, _) => UpdateFields();
        UpdateFields();
        cancel.Click += (_, _) => CloseThicknessSolve();
        apply.Click += (_, _) =>
        {
            try
            {
                var chosen = kinds[kind.SelectedIndex];
                var sourceNumber = chosen == ThicknessSolveKind.Pickup ? RequireSource(source) : 0;
                var factor = chosen == ThicknessSolveKind.Pickup ? ParseNumber(scale, "比例因子") : 1;
                var additive = chosen == ThicknessSolveKind.Pickup ? ParseNumber(offset, "偏移量") : 0;
                _prescription.SetThicknessSolve(
                    row.Number,
                    new ThicknessSolveUpdateDto(chosen, sourceNumber, factor, additive),
                    revision);
                CloseThicknessSolve();
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
            {
                ShowSolveError(error, exception);
            }
        };
        _thicknessSolveFlyout = new Flyout { Content = content, Placement = PlacementMode.BottomEdgeAlignedLeft };
        _thicknessSolveFlyout.ShowAt(anchor);
    }

    private void ShowSemiDiameterSolve(SurfaceEditorRow row, Control anchor)
    {
        CloseSemiDiameterSolve();
        _semiDiameterSolveRevision = _events.Revision;
        var revision = _semiDiameterSolveRevision;
        var current = row.SemiDiameterSolve;
        var kinds = new[] { SemiDiameterSolveKind.Automatic, SemiDiameterSolveKind.Fixed, SemiDiameterSolveKind.Pickup };
        var kind = SolveKindPicker(
            "SemiDiameterSolveKind",
            kinds.Select(SemiDiameterSolveLabel).ToArray(),
            current.Kind == SemiDiameterSolveKind.Automatic ? 1 : Array.IndexOf(kinds, current.Kind));
        var source = PickupSource("SemiDiameterPickupSource", row.Number, current.SourceSurface, current.Kind == SemiDiameterSolveKind.Pickup);
        var scale = PickupNumber("SemiDiameterPickupScale", current.ScaleFactor);
        var fields = new StackPanel
        {
            Name = "SemiDiameterPickupFields",
            Spacing = 8,
            Children =
            {
                SolveRow("拾取表面：", source),
                SolveRow("比例因子：", scale),
                SolveRow("拾取列：", new TextBlock { Text = "净口径", VerticalAlignment = VerticalAlignment.Center })
            }
        };
        var (content, error, apply, cancel) = SolveContent(
            "SemiDiameterSolveContent",
            $"在面 {row.Number} 上的净口径求解",
            kind,
            fields);
        void UpdateFields()
        {
            fields.IsVisible = kind.SelectedIndex == 2;
            apply.IsEnabled = kind.SelectedIndex != 2 || row.Number > 0;
        }
        kind.SelectionChanged += (_, _) => UpdateFields();
        UpdateFields();
        cancel.Click += (_, _) => CloseSemiDiameterSolve();
        apply.Click += (_, _) =>
        {
            try
            {
                var chosen = kinds[kind.SelectedIndex];
                var sourceNumber = chosen == SemiDiameterSolveKind.Pickup ? RequireSource(source) : 0;
                var factor = chosen == SemiDiameterSolveKind.Pickup ? ParseNumber(scale, "比例因子") : 1;
                _prescription.SetSemiDiameterSolve(
                    row.Number,
                    new SemiDiameterSolveUpdateDto(chosen, sourceNumber, factor),
                    revision);
                CloseSemiDiameterSolve();
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
            {
                ShowSolveError(error, exception);
            }
        };
        _semiDiameterSolveFlyout = new Flyout { Content = content, Placement = PlacementMode.BottomEdgeAlignedLeft };
        _semiDiameterSolveFlyout.ShowAt(anchor);
    }

    private static ComboBox SolveKindPicker(string name, string[] labels, int selectedIndex) => new()
    {
        Name = name,
        ItemsSource = labels,
        SelectedIndex = selectedIndex,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static ComboBox PickupSource(string name, int number, int currentSource, bool pickup) => new()
    {
        Name = name,
        ItemsSource = Enumerable.Range(0, Math.Max(0, number)).ToArray(),
        SelectedItem = pickup ? currentSource : Math.Max(0, number - 1),
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static TextBox PickupNumber(string name, double value) => new()
    {
        Name = name,
        Text = value.ToString("G17", CultureInfo.CurrentCulture)
    };

    private static (StackPanel Content, TextBlock Error, Button Apply, Button Cancel) SolveContent(
        string name,
        string title,
        ComboBox kind,
        StackPanel pickupFields)
    {
        var error = new TextBlock { Name = $"{name}Error", TextWrapping = TextWrapping.Wrap, IsVisible = false };
        error.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextError);
        var apply = new Button { Name = $"Apply{name}", Content = "确定", MinWidth = 64 };
        var cancel = new Button { Name = $"Cancel{name}", Content = "取消", MinWidth = 64 };
        var content = new StackPanel
        {
            Name = name,
            Width = 340,
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = title, FontWeight = FontWeight.SemiBold },
                SolveRow("求解类型：", kind),
                pickupFields,
                error,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { apply, cancel }
                }
            }
        };
        SettingsPanelChrome.ApplyInputStyles(content);
        return (content, error, apply, cancel);
    }

    private static int RequireSource(ComboBox source) =>
        source.SelectedItem is int selected
            ? selected
            : throw new ArgumentException("请选择拾取表面。");

    private static double ParseNumber(TextBox editor, string label)
    {
        if (double.TryParse(editor.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
            || double.TryParse(editor.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return value;
        throw new ArgumentException($"请输入有效的{label}。");
    }

    private static void ShowSolveError(TextBlock error, Exception exception)
    {
        error.Text = exception.Message;
        error.IsVisible = true;
    }

    private void CloseThicknessSolve()
    {
        _thicknessSolveFlyout?.Hide();
        _thicknessSolveFlyout = null;
    }

    private void CloseSemiDiameterSolve()
    {
        _semiDiameterSolveFlyout?.Hide();
        _semiDiameterSolveFlyout = null;
    }

    private static string ThicknessSolveLabel(ThicknessSolveKind kind) => kind switch
    {
        ThicknessSolveKind.Variable => "变量",
        ThicknessSolveKind.Pickup => "拾取",
        _ => "固定"
    };

    private static string SemiDiameterSolveLabel(SemiDiameterSolveKind kind) => kind switch
    {
        SemiDiameterSolveKind.Fixed => "固定",
        SemiDiameterSolveKind.Pickup => "拾取",
        _ => "自动"
    };
}
