using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.App.Connectors;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;

namespace OptilandWorkbench.App.Panels;

public sealed class LensEditorPanel : UserControl
{
    private readonly OptilandConnector _connector;
    private readonly DataGrid _grid;
    private readonly ComboBox _geometryPicker = new() { MinWidth = 130 };
    private readonly ComboBox _materialPicker = new() { MinWidth = 120 };
    private readonly ComboBox _coatingPicker = new() { MinWidth = 140 };
    private readonly ComboBox _interactionPicker = new() { MinWidth = 120 };
    private readonly ComboBox _aperturePicker = new() { MinWidth = 110 };
    private readonly NumericUpDown _gratingOrder = new()
    {
        Width = 72,
        Minimum = -100,
        Maximum = 100,
        Value = 1
    };
    private readonly NumericUpDown _gratingPeriod = new()
    {
        Width = 94,
        Minimum = 0.000001m,
        Maximum = 1000000,
        Increment = 0.1m,
        Value = 1
    };
    private readonly CheckBox _infiniteGratingPeriod = new()
    {
        Content = "∞",
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly NumericUpDown _gratingAngle = new()
    {
        Width = 88,
        Minimum = -360,
        Maximum = 360,
        Increment = 1,
        Value = 0
    };
    private readonly NumericUpDown _thinLensFocalLength = new()
    {
        Width = 92,
        Minimum = -1000000,
        Maximum = 1000000,
        Increment = 1,
        Value = 50
    };
    private readonly CheckBox _infiniteThinLensFocalLength = new()
    {
        Content = "∞",
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _componentSummary = new()
    {
        MinWidth = 160,
        MaxWidth = 260,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };
    private bool _loadingComponentSelection;

    public LensEditorPanel(OptilandConnector connector)
    {
        _connector = connector;
        _grid = CreateGrid();
        _geometryPicker.ItemsSource = _connector.GeometryKinds;
        _coatingPicker.ItemsSource = _connector.CoatingKinds;
        _interactionPicker.ItemsSource = _connector.InteractionKinds;
        _aperturePicker.ItemsSource = _connector.PhysicalApertureKinds;
        _infiniteGratingPeriod.IsCheckedChanged += (_, _) =>
            _gratingPeriod.IsEnabled = _infiniteGratingPeriod.IsChecked != true;
        _infiniteThinLensFocalLength.IsCheckedChanged += (_, _) =>
            _thinLensFocalLength.IsEnabled = _infiniteThinLensFocalLength.IsChecked != true;

        var addButton = new Button { Content = "添加", MinWidth = 74 };
        addButton.Click += (_, _) => _connector.AddSurface();

        var removeButton = new Button { Content = "删除", MinWidth = 74 };
        removeButton.Click += (_, _) => _connector.RemoveSurface(_grid.SelectedItem as OpticalSurface);

        var applyComponentsButton = new Button { Content = "应用组件", MinWidth = 112 };
        applyComponentsButton.Click += (_, _) => ApplySelectedComponents();

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "更新: 所有窗口  ▾",
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Avalonia.Thickness(0, 0, 8, 0)
                },
                addButton,
                removeButton,
                new TextBlock
                {
                    Text = "  ◀   ▶   ⟳   ⊕   ⊖   ⇄   ?",
                    FontSize = 15,
                    Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 255)),
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };

        var componentEditor = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Avalonia.Thickness(8),
            Children =
            {
                new TextBlock { Text = "几何", VerticalAlignment = VerticalAlignment.Center },
                _geometryPicker,
                new TextBlock { Text = "材料", VerticalAlignment = VerticalAlignment.Center },
                _materialPicker,
                new TextBlock { Text = "镀膜", VerticalAlignment = VerticalAlignment.Center },
                _coatingPicker,
                new TextBlock { Text = "相互作用", VerticalAlignment = VerticalAlignment.Center },
                _interactionPicker,
                new TextBlock { Text = "物理孔径", VerticalAlignment = VerticalAlignment.Center },
                _aperturePicker,
                new TextBlock { Text = "级次", VerticalAlignment = VerticalAlignment.Center },
                _gratingOrder,
                new TextBlock { Text = "周期 (μm)", VerticalAlignment = VerticalAlignment.Center },
                _gratingPeriod,
                _infiniteGratingPeriod,
                new TextBlock { Text = "槽角 (°)", VerticalAlignment = VerticalAlignment.Center },
                _gratingAngle,
                new TextBlock { Text = "焦距 (mm)", VerticalAlignment = VerticalAlignment.Center },
                _thinLensFocalLength,
                _infiniteThinLensFocalLength,
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

        var root = new DockPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(245, 245, 247))
        };
        DockPanel.SetDock(commandBar, Dock.Top);
        root.Children.Add(commandBar);
        DockPanel.SetDock(componentExpander, Dock.Top);
        root.Children.Add(componentExpander);
        root.Children.Add(_grid);
        Content = root;

        _grid.SelectionChanged += (_, _) => LoadComponentSelection();
        _connector.OpticLoaded += (_, _) => Refresh();
        _connector.SurfaceDataChanged += (_, _) => Refresh();
        Refresh();
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

        grid.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new Binding(nameof(OpticalSurface.Number)), IsReadOnly = true, Width = new DataGridLength(44) });
        grid.Columns.Add(new DataGridTextColumn { Header = "表面类型", Binding = new Binding(nameof(OpticalSurface.Label)), Width = new DataGridLength(132) });
        grid.Columns.Add(new DataGridTextColumn { Header = "曲率半径", Binding = new Binding(nameof(OpticalSurface.Radius)), Width = new DataGridLength(102) });
        grid.Columns.Add(new DataGridTextColumn { Header = "厚度", Binding = new Binding(nameof(OpticalSurface.Thickness)), Width = new DataGridLength(92) });
        grid.Columns.Add(new DataGridTextColumn { Header = "材料", Binding = new Binding(nameof(OpticalSurface.Material)), Width = new DataGridLength(96) });
        grid.Columns.Add(new DataGridTextColumn { Header = "膜层", Binding = new Binding(nameof(OpticalSurface.Coating)), Width = new DataGridLength(90) });
        grid.Columns.Add(new DataGridTextColumn { Header = "净口径", Binding = new Binding(nameof(OpticalSurface.SemiDiameter)), Width = new DataGridLength(92) });
        grid.Columns.Add(new DataGridTextColumn { Header = "圆锥系数", Binding = new Binding(nameof(OpticalSurface.Conic)), Width = new DataGridLength(92) });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "光阑", Binding = new Binding(nameof(OpticalSurface.IsStop)), Width = new DataGridLength(64) });
        grid.Columns.Add(new DataGridTextColumn { Header = "几何类型", Binding = new Binding("Geometry.Kind"), IsReadOnly = true, Width = new DataGridLength(120) });
        grid.Columns.Add(new DataGridTextColumn { Header = "镀膜类型", Binding = new Binding("CoatingModel.Kind"), IsReadOnly = true, Width = new DataGridLength(120) });
        grid.Columns.Add(new DataGridTextColumn { Header = "作用类型", Binding = new Binding("InteractionModel.Kind"), IsReadOnly = true, Width = new DataGridLength(132) });
        grid.Columns.Add(new DataGridTextColumn { Header = "孔径类型", Binding = new Binding("PhysicalAperture.Kind"), IsReadOnly = true, Width = new DataGridLength(120) });

        grid.BeginningEdit += (_, _) => _connector.CaptureCurrentState();
        grid.CellEditEnded += (_, _) => _connector.CommitSurfaceEdit();

        return grid;
    }

    private void Refresh()
    {
        _materialPicker.ItemsSource = _connector.MaterialNames;
        _grid.ItemsSource = _connector.Surfaces;
        if (_grid.SelectedItem is null && _connector.Surfaces.Count > 0)
        {
            _grid.SelectedIndex = Math.Min(1, _connector.Surfaces.Count - 1);
        }

        LoadComponentSelection();
    }

    private void LoadComponentSelection()
    {
        if (_grid.SelectedItem is not OpticalSurface surface)
        {
            _componentSummary.Text = "未选择表面";
            return;
        }

        _loadingComponentSelection = true;
        try
        {
            _geometryPicker.SelectedItem = GeometryKindFor(surface);
            _materialPicker.SelectedItem = _connector.MaterialNames.Contains(surface.Material)
                ? surface.Material
                : "Air";
            _coatingPicker.SelectedItem = CoatingKindFor(surface);
            _interactionPicker.SelectedItem = InteractionKindFor(surface);
            _aperturePicker.SelectedItem = ApertureKindFor(surface);
            if (surface.Geometry is IGratingGeometry grating)
            {
                _gratingOrder.Value = grating.GratingOrder;
                _infiniteGratingPeriod.IsChecked = double.IsPositiveInfinity(grating.GratingPeriodMicrometers);
                if (double.IsFinite(grating.GratingPeriodMicrometers))
                {
                    _gratingPeriod.Value = (decimal)Math.Clamp(
                        grating.GratingPeriodMicrometers,
                        (double)_gratingPeriod.Minimum,
                        (double)_gratingPeriod.Maximum);
                }
                _gratingAngle.Value = (decimal)(grating.GrooveOrientationAngleRadians * 180.0 / Math.PI);
            }
            else
            {
                _infiniteGratingPeriod.IsChecked = false;
            }
            if (surface.InteractionModel is ThinLensInteractionModel thinLens
                && double.IsFinite(thinLens.FocalLength))
            {
                _infiniteThinLensFocalLength.IsChecked = false;
                _thinLensFocalLength.Value = (decimal)Math.Clamp(
                    thinLens.FocalLength,
                    (double)_thinLensFocalLength.Minimum,
                    (double)_thinLensFocalLength.Maximum);
            }
            else if (surface.InteractionModel is ThinLensInteractionModel infiniteThinLens)
            {
                _infiniteThinLensFocalLength.IsChecked = true;
                _thinLensFocalLength.Value = double.IsNegativeInfinity(infiniteThinLens.FocalLength) ? -1 : 1;
            }
            else
            {
                _infiniteThinLensFocalLength.IsChecked = false;
            }
            _componentSummary.Text = $"表面 {surface.Number}: {surface.Geometry.Kind}, {surface.MaterialAfterName}";
        }
        finally
        {
            _loadingComponentSelection = false;
        }
    }

    private void ApplySelectedComponents()
    {
        if (_loadingComponentSelection || _grid.SelectedItem is not OpticalSurface surface)
        {
            return;
        }

        _connector.ApplySurfaceComponents(
            surface,
            _geometryPicker.SelectedItem as string ?? "标准球面/圆锥",
            _materialPicker.SelectedItem as string ?? surface.Material,
            _coatingPicker.SelectedItem as string ?? "无镀膜",
            _interactionPicker.SelectedItem as string ?? "折射",
            _aperturePicker.SelectedItem as string ?? "圆形",
            (int)(_gratingOrder.Value ?? 1),
            _infiniteGratingPeriod.IsChecked == true
                ? double.PositiveInfinity
                : (double)(_gratingPeriod.Value ?? 1),
            (double)(_gratingAngle.Value ?? 0),
            _infiniteThinLensFocalLength.IsChecked == true
                ? Math.CopySign(
                    double.PositiveInfinity,
                    (double)(_thinLensFocalLength.Value ?? 1))
                : (double)(_thinLensFocalLength.Value ?? 50));
    }

    private static string GeometryKindFor(OpticalSurface surface)
    {
        return surface.Geometry switch
        {
            PlaneGeometry => "平面",
            PlaneGratingGeometry => "平面光栅",
            StandardGratingGeometry => "标准曲面光栅",
            EvenAsphereGeometry => "偶次非球面",
            OddAsphereGeometry => "奇次非球面",
            BiconicGeometry => "双圆锥",
            ToroidalGeometry => "环形面",
            PolynomialGeometry => "XY 多项式",
            ChebyshevGeometry => "Chebyshev 曲面",
            ZernikeGeometry => "Zernike 曲面",
            ForbesQGeometry => "Forbes Q 曲面",
            _ => "标准球面/圆锥"
        };
    }

    private static string CoatingKindFor(OpticalSurface surface)
    {
        if (surface.CoatingModel is ThinFilmStackCoating stack)
        {
            return stack.Layers.Count > 1 ? "四分之一波堆栈" : "MgF2 单层";
        }

        return "无镀膜";
    }

    private static string InteractionKindFor(OpticalSurface surface)
    {
        return surface.InteractionModel switch
        {
            ThinLensInteractionModel model when model.IsReflective => "反射薄透镜",
            ThinLensInteractionModel => "薄透镜",
            DiffractiveInteractionModel model when model.IsReflective => "反射衍射",
            DiffractiveInteractionModel => "衍射",
            PhaseInteractionModel => "相位",
            RefractiveReflectiveInteractionModel model when model.IsReflective => "反射",
            _ => "折射"
        };
    }

    private static string ApertureKindFor(OpticalSurface surface)
    {
        return surface.PhysicalAperture switch
        {
            AnnularAperture => "环形",
            OffsetRadialAperture => "偏心圆",
            RectangularAperture => "矩形",
            EllipticalAperture => "椭圆",
            FileAperture => "多边形",
            PolygonAperture => "多边形",
            BooleanAperture => "组合孔径",
            null => "无",
            _ => "圆形"
        };
    }
}
