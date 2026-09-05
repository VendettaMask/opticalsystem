using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.ViewModels;

namespace OptilandWorkbench.App.Panels;

public sealed partial class LensEditorPanel : UserControl, IDisposable, IDisplaySettingsAware
{
    private const string NumericColumnTag = "numeric";

    private readonly IPrescriptionService _prescription;
    private readonly IWorkspaceEventStream _events;
    private readonly SurfaceSelectionService _surfaceSelection;
    private readonly DataGrid _grid;
    private readonly ComboBox _geometryPicker = new() { MinWidth = 130 };
    private readonly ComboBox _aperturePicker = new() { MinWidth = 110 };
    private readonly NumericUpDown _gratingOrder = Number(72, -100, 100, 1, 1);
    private readonly NumericUpDown _gratingPeriod = Number(94, 0.000001m, 1_000_000, 0.1m, 1);
    private readonly NumericUpDown _gratingAngle = Number(88, -360, 360, 1, 0);
    private readonly NumericUpDown _thinLensFocalLength = Number(92, -1_000_000, 1_000_000, 1, 50);
    private readonly CheckBox _infiniteGratingPeriod = new() { Content = "∞", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _infiniteThinLensFocalLength = new() { Content = "∞", VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _applyComponentsButton;
    private readonly TextBlock _componentSummary = new()
    {
        MinWidth = 160,
        MaxWidth = 260,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };
    private bool _disposed;

    public LensEditorPanel(
        IPrescriptionService prescription,
        IWorkspaceEventStream events,
        SurfaceSelectionService surfaceSelection)
    {
        _prescription = prescription;
        _events = events;
        _surfaceSelection = surfaceSelection;
        _grid = CreateGrid();
        var options = prescription.GetOptions();
        _geometryPicker.ItemsSource = options.GeometryKinds;
        _aperturePicker.ItemsSource = options.PhysicalApertureKinds;
        _infiniteGratingPeriod.IsCheckedChanged += (_, _) =>
            _gratingPeriod.IsEnabled = _infiniteGratingPeriod.IsChecked != true;
        _infiniteThinLensFocalLength.IsCheckedChanged += (_, _) =>
            _thinLensFocalLength.IsEnabled = _infiniteThinLensFocalLength.IsChecked != true;

        ConfigureSurfaceContextMenu();
        _applyComponentsButton = CommandButton("check", "应用", 72);
        _applyComponentsButton.Click += (_, _) => ApplySelectedComponents();

        var componentSection = BuildSurfacePropertiesSection();
        var root = new DockPanel();
        root.BindThemeResource(Panel.BackgroundProperty, ThemeResourceBindings.Workspace);
        DockPanel.SetDock(componentSection, Avalonia.Controls.Dock.Top);
        root.Children.Add(componentSection);
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
        ClearSurfaceContext();
        CloseRadiusSolve();
        _events.Changed -= OnWorkspaceChanged;
    }

    public void RefreshDisplaySettings() => Refresh();

    private DataGrid CreateGrid()
    {
        var grid = new DataGrid
        {
            Name = "LensSurfaceGrid",
            AutoGenerateColumns = false,
            CanUserReorderColumns = true,
            CanUserResizeColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            IsReadOnly = false,
            BorderThickness = new Avalonia.Thickness(1, 0, 1, 1),
            RowHeight = UiDensity.CompactTableRowHeight,
            ColumnHeaderHeight = UiDensity.TableHeaderHeight,
            FrozenColumnCount = 2
        };
        grid.BindThemeResource(DataGrid.RowBackgroundProperty, ThemeResourceBindings.Surface);
        grid.BindThemeResource(DataGrid.BorderBrushProperty, ThemeResourceBindings.Border);
        grid.Styles.Add(new Style(selector => selector
            .OfType<DataGridRow>()
            .Class("glass-material-row"))
        {
            Setters =
            {
                new Setter(
                    DataGridRow.BackgroundProperty,
                    new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(
                        ThemeResourceBindings.RibbonTabHover))
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
        grid.Columns.Add(RadiusColumn());
        grid.Columns.Add(ThicknessColumn());
        grid.Columns.Add(ThicknessVariableColumn());
        grid.Columns.Add(Column("材料", nameof(SurfaceEditorRow.MaterialDisplay), 122));
        grid.Columns.Add(Column("膜层", nameof(SurfaceEditorRow.Coating), 92));
        grid.Columns.Add(SemiDiameterColumn());
        grid.Columns.Add(SemiDiameterFixedColumn());
        grid.Columns.Add(NumericColumn("延伸区", nameof(SurfaceEditorRow.ExtensionZone), 102, true));
        grid.Columns.Add(NumericColumn("机械半直径", nameof(SurfaceEditorRow.MechanicalSemiDiameter), 132, true));
        grid.Columns.Add(NumericColumn("圆锥系数", nameof(SurfaceEditorRow.Conic), 100));
        grid.Columns.Add(NumericColumn("TCE x 1E-6", nameof(SurfaceEditorRow.ThermalExpansionDisplay), 112, true));
        grid.PreparingCellForEdit += (_, eventArgs) =>
        {
            if (Equals(eventArgs.Column.Tag, NumericColumnTag)
                && eventArgs.EditingElement is TextBox editor)
            {
                editor.TextAlignment = TextAlignment.Right;
                editor.HorizontalContentAlignment = HorizontalAlignment.Right;
            }
        };
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
        Dispatcher.UIThread.Post(() => Refresh(preserveSelection: !args.FileSwitched));
    }

    private void Refresh(bool preserveSelection = true)
    {
        if (_disposed)
        {
            return;
        }

        if (_surfaceContextRevision != _events.Revision)
        {
            ClearSurfaceContext();
        }
        if (_radiusSolveRevision != _events.Revision) CloseRadiusSolve();

        var selectedNumber = preserveSelection
            ? (_grid.SelectedItem as SurfaceEditorRow)?.Number
            : null;
        var surfaces = _prescription.GetSurfaces();
        var lastSurfaceNumber = surfaces.Count == 0 ? -1 : surfaces.Max(surface => surface.Number);
        var rows = surfaces
            .Select(surface => new SurfaceEditorRow(surface, surface.Number == lastSurfaceNumber))
            .ToArray();
        ApplyMechanicalSemiDiameters(rows);
        _grid.ItemsSource = rows;
        _grid.SelectedItem = rows.FirstOrDefault(row => row.Number == selectedNumber)
            ?? rows.ElementAtOrDefault(Math.Min(1, Math.Max(0, rows.Length - 1)));
        LoadComponentSelection();
    }

    private void LoadComponentSelection()
    {
        UpdateSurfacePropertiesHeader();
        if (_grid.SelectedItem is not SurfaceEditorRow row)
        {
            _componentSummary.Text = "未选择表面";
            _propertyBody.IsEnabled = false;
            _applyComponentsButton.IsEnabled = false;
            _surfaceSelection.Select(null);
            return;
        }

        _surfaceSelection.Select(row.Number);
        _propertyBody.IsEnabled = true;

        var geometryKinds = _prescription.GetOptions().GeometryKinds;
        _geometryPicker.ItemsSource = geometryKinds.Contains(row.GeometryKind)
            ? geometryKinds
            : geometryKinds.Append(row.GeometryKind).ToArray();
        _geometryPicker.SelectedItem = row.GeometryKind;
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

        _componentSummary.Text = $"表面 {row.Number}: {row.GeometryKind}";
        var canEditComponents = row.GeometryComputable;
        _geometryPicker.IsEnabled = canEditComponents;
        _aperturePicker.IsEnabled = canEditComponents;
        _gratingOrder.IsEnabled = canEditComponents;
        _gratingPeriod.IsEnabled = canEditComponents && _infiniteGratingPeriod.IsChecked != true;
        _infiniteGratingPeriod.IsEnabled = canEditComponents;
        _gratingAngle.IsEnabled = canEditComponents;
        _thinLensFocalLength.IsEnabled = canEditComponents && _infiniteThinLensFocalLength.IsChecked != true;
        _infiniteThinLensFocalLength.IsEnabled = canEditComponents;
        _applyComponentsButton.IsEnabled = canEditComponents;
        LoadSurfaceProperties(row);
        if (!canEditComponents)
        {
            _componentSummary.Text += "（只读：暂不支持计算/编辑该 Zemax 面型）";
        }
    }

    private void ApplySelectedComponents()
    {
        if (_grid.SelectedItem is not SurfaceEditorRow row)
        {
            return;
        }

        if (!row.GeometryComputable)
        {
            _componentSummary.Text = $"表面 {row.Number}: {row.GeometryKind}（只读：暂不支持计算/编辑该 Zemax 面型）";
            return;
        }

        try
        {
            _prescription.UpdateSurfaceComponents(row.Number, new SurfaceComponentUpdateDto(
            _geometryPicker.SelectedItem as string ?? row.GeometryKind,
            _aperturePicker.SelectedItem as string ?? row.ApertureKind,
            (int)(_gratingOrder.Value ?? row.GratingOrder),
            _infiniteGratingPeriod.IsChecked == true
                ? double.PositiveInfinity
                : (double)(_gratingPeriod.Value ?? 1),
            (double)(_gratingAngle.Value ?? 0),
            _infiniteThinLensFocalLength.IsChecked == true
                ? Math.CopySign(double.PositiveInfinity, (double)(_thinLensFocalLength.Value ?? 1))
                : (double)(_thinLensFocalLength.Value ?? 50),
            _stopSurface.IsChecked == true,
            _surfaceCoating.Text,
            _fixedSemiDiameter.IsChecked == true,
            (double)(_surfaceSemiDiameter.Value ?? (decimal)row.SemiDiameter)));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            _componentSummary.Text = $"未应用：{exception.Message}";
        }
    }

    private static DataGridTextColumn Column(string header, string property, double width, bool readOnly = false) => new()
    {
        Header = header,
        Binding = new Binding(property),
        IsReadOnly = readOnly,
        Width = new DataGridLength(width)
    };

    private static DataGridTextColumn NumericColumn(
        string header,
        string property,
        double width,
        bool readOnly = false) => new()
        {
            Header = NumericHeader(header),
            Binding = new Binding(property),
            IsReadOnly = readOnly,
            Width = new DataGridLength(width),
            Tag = NumericColumnTag,
            CellTheme = new ControlTheme(typeof(DataGridCell))
            {
                Setters =
            {
                new Setter(
                    DataGridCell.HorizontalContentAlignmentProperty,
                    HorizontalAlignment.Right)
            }
            }
        };

    private DataGridTemplateColumn RadiusColumn() => new()
    {
        Header = NumericHeader("曲率半径"),
        Tag = NumericColumnTag,
        Width = new DataGridLength(136),
        CellTemplate = new FuncDataTemplate<SurfaceEditorRow>((row, _) => CreateRadiusCell(row))
    };

    private DataGridTemplateColumn ThicknessColumn() => new()
    {
        Header = NumericHeader("厚度"),
        Tag = NumericColumnTag,
        Width = new DataGridLength(96),
        CellTemplate = new FuncDataTemplate<SurfaceEditorRow>((row, _) =>
        {
            if (row?.IsLastSurface != false)
            {
                return new TextBlock
                {
                    Text = "-",
                    Margin = new Avalonia.Thickness(8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    TextAlignment = TextAlignment.Right
                };
            }

            return CreateNumericEditor(row.ThicknessDisplay, text =>
            {
                row.ThicknessDisplay = text;
                _prescription.UpdateSurface(row.ToDto());
            });
        })
    };

    private DataGridTemplateColumn SemiDiameterColumn() => new()
    {
        Header = NumericHeader("净口径"),
        Tag = NumericColumnTag,
        Width = new DataGridLength(106),
        CellTemplate = new FuncDataTemplate<SurfaceEditorRow>((row, _) =>
        {
            if (row is null)
            {
                return new TextBlock();
            }

            if (!row.SemiDiameterFixed)
            {
                return new TextBlock
                {
                    Text = row.SemiDiameterDisplay,
                    Margin = new Avalonia.Thickness(8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    TextAlignment = TextAlignment.Right
                };
            }

            return CreateNumericEditor(row.SemiDiameterDisplay, text =>
            {
                row.SemiDiameterDisplay = text;
                _prescription.UpdateSurface(row.ToDto());
            });
        })
    };

    private DataGridTemplateColumn SemiDiameterFixedColumn() => new()
    {
        Header = "固定",
        IsReadOnly = true,
        Width = new DataGridLength(64),
        CellTemplate = new FuncDataTemplate<SurfaceEditorRow>((row, _) =>
        {
            var checkBox = new CheckBox
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsChecked = row?.SemiDiameterFixed
            };
            checkBox.IsCheckedChanged += (_, _) =>
            {
                if (row is null)
                {
                    return;
                }

                row.SemiDiameterFixed = checkBox.IsChecked == true;
                _prescription.UpdateSurface(row.ToDto());
            };
            return checkBox;
        })
    };

    private static TextBox CreateNumericEditor(string value, Action<string> commit)
    {
        var editor = new TextBox
        {
            Text = value,
            Background = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            Padding = new Avalonia.Thickness(8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextAlignment = TextAlignment.Right
        };
        var committedText = value;
        editor.LostFocus += (_, _) =>
        {
            var text = editor.Text ?? string.Empty;
            if (text == committedText) return;
            commit(text);
            committedText = text;
        };
        return editor;
    }

    private static TextBlock NumericHeader(string text) => new()
    {
        Text = text,
        HorizontalAlignment = HorizontalAlignment.Right,
        TextAlignment = TextAlignment.Right
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

    private DataGridTemplateColumn ThicknessVariableColumn() => new()
    {
        Header = "T 变量",
        IsReadOnly = true,
        Width = new DataGridLength(68),
        CellTemplate = new FuncDataTemplate<SurfaceEditorRow>((row, _) =>
        {
            var checkBox = new CheckBox
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = row?.CanOptimize == true,
                IsChecked = row?.ThicknessVariable
            };
            checkBox.IsCheckedChanged += (_, _) =>
            {
                if (row is null || !row.CanOptimize)
                {
                    return;
                }

                row.ThicknessVariable = checkBox.IsChecked == true;
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
