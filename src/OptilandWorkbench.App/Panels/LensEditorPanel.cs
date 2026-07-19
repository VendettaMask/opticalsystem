using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.ViewModels;

namespace OptilandWorkbench.App.Panels;

public sealed class LensEditorPanel : UserControl, IDisposable
{
    private readonly IPrescriptionService _prescription;
    private readonly IWorkspaceEventStream _events;
    private readonly DataGrid _grid;
    private readonly ComboBox _geometryPicker = new() { MinWidth = 130 };
    private readonly ComboBox _materialPicker = new() { MinWidth = 120 };
    private readonly ComboBox _coatingPicker = new() { MinWidth = 140 };
    private readonly ComboBox _interactionPicker = new() { MinWidth = 120 };
    private readonly ComboBox _aperturePicker = new() { MinWidth = 110 };
    private readonly NumericUpDown _gratingOrder = Number(72, -100, 100, 1, 1);
    private readonly NumericUpDown _gratingPeriod = Number(94, 0.000001m, 1_000_000, 0.1m, 1);
    private readonly NumericUpDown _gratingAngle = Number(88, -360, 360, 1, 0);
    private readonly NumericUpDown _thinLensFocalLength = Number(92, -1_000_000, 1_000_000, 1, 50);
    private readonly CheckBox _infiniteGratingPeriod = new() { Content = "∞", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _infiniteThinLensFocalLength = new() { Content = "∞", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _componentSummary = new()
    {
        MinWidth = 160,
        MaxWidth = 260,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };
    private bool _disposed;

    public LensEditorPanel(IPrescriptionService prescription, IWorkspaceEventStream events)
    {
        _prescription = prescription;
        _events = events;
        _grid = CreateGrid();
        var options = prescription.GetOptions();
        _geometryPicker.ItemsSource = options.GeometryKinds;
        _materialPicker.ItemsSource = options.Materials;
        _coatingPicker.ItemsSource = options.CoatingKinds;
        _interactionPicker.ItemsSource = options.InteractionKinds;
        _aperturePicker.ItemsSource = options.PhysicalApertureKinds;
        _infiniteGratingPeriod.IsCheckedChanged += (_, _) =>
            _gratingPeriod.IsEnabled = _infiniteGratingPeriod.IsChecked != true;
        _infiniteThinLensFocalLength.IsCheckedChanged += (_, _) =>
            _thinLensFocalLength.IsEnabled = _infiniteThinLensFocalLength.IsChecked != true;

        var addButton = CommandButton("plus", "添加", 74);
        addButton.Click += (_, _) => _prescription.AddSurface();
        var removeButton = CommandButton("trash-2", "删除", 74);
        removeButton.Click += (_, _) =>
        {
            if (_grid.SelectedItem is SurfaceEditorRow row)
            {
                _prescription.RemoveSurface(row.Number);
            }
        };
        var applyComponentsButton = CommandButton("check", "应用组件", 112);
        applyComponentsButton.Click += (_, _) => ApplySelectedComponents();

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { addButton, removeButton }
        };
        var componentEditor = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Avalonia.Thickness(8),
            Children =
            {
                Label("几何"), _geometryPicker,
                Label("材料"), _materialPicker,
                Label("镀膜"), _coatingPicker,
                Label("相互作用"), _interactionPicker,
                Label("物理孔径"), _aperturePicker,
                Label("级次"), _gratingOrder,
                Label("周期 (μm)"), _gratingPeriod, _infiniteGratingPeriod,
                Label("槽角 (°)"), _gratingAngle,
                Label("焦距 (mm)"), _thinLensFocalLength, _infiniteThinLensFocalLength,
                applyComponentsButton,
                _componentSummary
            }
        };
        var commandBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(250, 250, 252)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(209, 209, 214)),
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
            Padding = new Avalonia.Thickness(10, 5),
            BoxShadow = BoxShadows.Parse("0 2 6 0 #12000000"),
            Child = toolbar
        };
        var componentExpander = new Expander
        {
            Header = "表面属性与组件",
            IsExpanded = false,
            Background = new SolidColorBrush(Color.FromRgb(242, 242, 247)),
            Content = componentEditor
        };
        var root = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(245, 245, 247)) };
        DockPanel.SetDock(commandBar, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(componentExpander, Avalonia.Controls.Dock.Top);
        root.Children.Add(commandBar);
        root.Children.Add(componentExpander);
        root.Children.Add(_grid);
        Content = root;

        _grid.SelectionChanged += (_, _) => LoadComponentSelection();
        _events.Changed += OnWorkspaceChanged;
        Refresh();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _events.Changed -= OnWorkspaceChanged;
    }

    private DataGrid CreateGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserReorderColumns = true,
            CanUserResizeColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            IsReadOnly = false,
            RowBackground = new SolidColorBrush(Color.FromRgb(250, 250, 252)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(199, 199, 204)),
            BorderThickness = new Avalonia.Thickness(1, 0, 1, 1),
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(229, 229, 234)),
            VerticalGridLinesBrush = new SolidColorBrush(Color.FromRgb(218, 218, 223)),
            RowHeight = 28,
            ColumnHeaderHeight = 30,
            FrozenColumnCount = 2
        };
        grid.Styles.Add(new Style(selector => selector
            .OfType<DataGridRow>()
            .Class("glass-material-row"))
        {
            Setters =
            {
                new Setter(DataGridRow.BackgroundProperty, new SolidColorBrush(Color.FromRgb(205, 220, 250)))
            }
        });
        grid.LoadingRow += (_, eventArgs) =>
        {
            if (eventArgs.Row.DataContext is SurfaceEditorRow row)
            {
                ApplyMaterialRowClass(eventArgs.Row, row);
            }
        };
        grid.Columns.Add(Column("#", nameof(SurfaceEditorRow.Number), 44, true));
        grid.Columns.Add(SurfaceTypeColumn());
        grid.Columns.Add(Column("标注", nameof(SurfaceEditorRow.Label), 88));
        grid.Columns.Add(Column("曲率半径", nameof(SurfaceEditorRow.Radius), 112));
        grid.Columns.Add(OptimizationVariableColumn("R 变量", radius: true));
        grid.Columns.Add(Column("厚度", nameof(SurfaceEditorRow.Thickness), 96));
        grid.Columns.Add(OptimizationVariableColumn("T 变量", radius: false));
        grid.Columns.Add(Column("材料", nameof(SurfaceEditorRow.MaterialDisplay), 122));
        grid.Columns.Add(Column("膜层", nameof(SurfaceEditorRow.Coating), 92));
        grid.Columns.Add(Column("净口径", nameof(SurfaceEditorRow.SemiDiameter), 106));
        grid.Columns.Add(Column("延伸区", nameof(SurfaceEditorRow.ExtensionZone), 102, true));
        grid.Columns.Add(Column("机械半直径", nameof(SurfaceEditorRow.MechanicalSemiDiameter), 132, true));
        grid.Columns.Add(Column("圆锥系数", nameof(SurfaceEditorRow.Conic), 100));
        grid.Columns.Add(Column("TCE x 1E-6", nameof(SurfaceEditorRow.ThermalExpansionDisplay), 112, true));
        grid.CellEditEnded += (_, eventArgs) =>
        {
            if (eventArgs.EditAction == DataGridEditAction.Commit
                && eventArgs.Row.DataContext is SurfaceEditorRow row)
            {
                ApplyMaterialRowClass(eventArgs.Row, row);
                _prescription.UpdateSurface(row.ToDto());
            }
        };
        return grid;
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args)
    {
        Dispatcher.UIThread.Post(Refresh);
    }

    private void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        var selectedNumber = (_grid.SelectedItem as SurfaceEditorRow)?.Number;
        var surfaces = _prescription.GetSurfaces();
        var lastSurfaceNumber = surfaces.Count == 0 ? -1 : surfaces.Max(surface => surface.Number);
        var rows = surfaces
            .Select(surface => new SurfaceEditorRow(surface, surface.Number == lastSurfaceNumber))
            .ToArray();
        ApplyMechanicalSemiDiameters(rows);
        _grid.ItemsSource = rows;
        _grid.SelectedItem = rows.FirstOrDefault(row => row.Number == selectedNumber)
            ?? rows.ElementAtOrDefault(Math.Min(1, Math.Max(0, rows.Length - 1)));
        _materialPicker.ItemsSource = _prescription.GetOptions().Materials;
        LoadComponentSelection();
    }

    private void LoadComponentSelection()
    {
        if (_grid.SelectedItem is not SurfaceEditorRow row)
        {
            _componentSummary.Text = "未选择表面";
            return;
        }

        _geometryPicker.SelectedItem = row.GeometryKind;
        _materialPicker.SelectedItem = row.Material;
        _coatingPicker.SelectedItem = row.CoatingKind;
        _interactionPicker.SelectedItem = row.InteractionKind;
        _aperturePicker.SelectedItem = row.ApertureKind;
        _gratingOrder.Value = row.GratingOrder;
        _infiniteGratingPeriod.IsChecked = double.IsPositiveInfinity(row.GratingPeriodMicrometers);
        if (double.IsFinite(row.GratingPeriodMicrometers))
        {
            _gratingPeriod.Value = (decimal)Math.Clamp(row.GratingPeriodMicrometers, 0.000001, 1_000_000);
        }

        _gratingAngle.Value = (decimal)row.GrooveOrientationAngleDegrees;
        _infiniteThinLensFocalLength.IsChecked = double.IsInfinity(row.ThinLensFocalLength);
        if (double.IsFinite(row.ThinLensFocalLength))
        {
            _thinLensFocalLength.Value = (decimal)Math.Clamp(row.ThinLensFocalLength, -1_000_000, 1_000_000);
        }

        _componentSummary.Text = $"表面 {row.Number}: {row.GeometryKind}, {row.Material}";
    }

    private void ApplySelectedComponents()
    {
        if (_grid.SelectedItem is not SurfaceEditorRow row)
        {
            return;
        }

        _prescription.UpdateSurfaceComponents(row.Number, new SurfaceComponentUpdateDto(
            _geometryPicker.SelectedItem as string ?? row.GeometryKind,
            _materialPicker.SelectedItem as string ?? row.Material,
            _coatingPicker.SelectedItem as string ?? row.CoatingKind,
            _interactionPicker.SelectedItem as string ?? row.InteractionKind,
            _aperturePicker.SelectedItem as string ?? row.ApertureKind,
            (int)(_gratingOrder.Value ?? row.GratingOrder),
            _infiniteGratingPeriod.IsChecked == true
                ? double.PositiveInfinity
                : (double)(_gratingPeriod.Value ?? 1),
            (double)(_gratingAngle.Value ?? 0),
            _infiniteThinLensFocalLength.IsChecked == true
                ? Math.CopySign(double.PositiveInfinity, (double)(_thinLensFocalLength.Value ?? 1))
                : (double)(_thinLensFocalLength.Value ?? 50)));
    }

    private static DataGridTextColumn Column(string header, string property, double width, bool readOnly = false) => new()
    {
        Header = header,
        Binding = new Binding(property),
        IsReadOnly = readOnly,
        Width = new DataGridLength(width)
    };

    private static DataGridTemplateColumn SurfaceTypeColumn() => new()
    {
        Header = "表面类型",
        IsReadOnly = true,
        Width = new DataGridLength(240),
        CellTemplate = new FuncDataTemplate<SurfaceEditorRow>((row, _) =>
        {
            var content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("100,*")
            };
            var role = new TextBlock
            {
                Text = row?.SurfaceRole ?? string.Empty,
                Margin = new Avalonia.Thickness(8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var type = new TextBlock
            {
                Text = row?.SurfaceType ?? string.Empty,
                Margin = new Avalonia.Thickness(8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(type, 1);
            content.Children.Add(role);
            content.Children.Add(type);
            return content;
        }, supportsRecycling: true)
    };

    private DataGridTemplateColumn OptimizationVariableColumn(string header, bool radius) => new()
    {
        Header = header,
        IsReadOnly = true,
        Width = new DataGridLength(68),
        CellTemplate = new FuncDataTemplate<SurfaceEditorRow>((row, _) =>
        {
            var checkBox = new CheckBox
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = row?.CanOptimize == true,
                IsChecked = radius ? row?.RadiusVariable : row?.ThicknessVariable
            };
            checkBox.IsCheckedChanged += (_, _) =>
            {
                if (row is null || !row.CanOptimize)
                {
                    return;
                }

                if (radius)
                {
                    row.RadiusVariable = checkBox.IsChecked == true;
                }
                else
                {
                    row.ThicknessVariable = checkBox.IsChecked == true;
                }

                _prescription.UpdateSurface(row.ToDto());
            };
            return checkBox;
        })
    };

    private static void ApplyMechanicalSemiDiameters(IReadOnlyList<SurfaceEditorRow> rows)
    {
        for (var index = 1; index < rows.Count - 1; index++)
        {
            if (!HasOpticalMaterial(rows[index]))
            {
                continue;
            }

            var end = index;
            while (end < rows.Count - 1 && HasOpticalMaterial(rows[end]))
            {
                end++;
            }

            var mechanicalSemiDiameter = rows
                .Skip(index)
                .Take((end - index) + 1)
                .Max(row => row.SemiDiameter);
            for (var surfaceIndex = index; surfaceIndex <= end; surfaceIndex++)
            {
                rows[surfaceIndex].MechanicalSemiDiameter = mechanicalSemiDiameter;
            }

            index = end;
        }
    }

    private static bool HasOpticalMaterial(SurfaceEditorRow row) =>
        row.HasOpticalMaterial;

    private static void ApplyMaterialRowClass(DataGridRow dataGridRow, SurfaceEditorRow row) =>
        dataGridRow.Classes.Set("glass-material-row", row.HasOpticalMaterial);

    private static NumericUpDown Number(double width, decimal minimum, decimal maximum, decimal increment, decimal value) => new()
    {
        Width = width,
        Minimum = minimum,
        Maximum = maximum,
        Increment = increment,
        Value = value,
        ShowButtonSpinner = false
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Avalonia.Thickness(7, 0, 4, 0)
    };

    private static Button CommandButton(string iconName, string text, double minWidth) => new()
    {
        Content = new LocalIconLabel(iconName, text),
        MinWidth = minWidth
    };
}
