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

        var addButton = new Button { Content = "添加", MinWidth = 74 };
        addButton.Click += (_, _) => _connector.AddSurface();

        var removeButton = new Button { Content = "删除", MinWidth = 74 };
        removeButton.Click += (_, _) => _connector.RemoveSurface(_grid.SelectedItem as OpticalSurface);

        var applyComponentsButton = new Button { Content = "应用组件", MinWidth = 112 };
        applyComponentsButton.Click += (_, _) => ApplySelectedComponents();

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
            Children = { addButton, removeButton }
        };

        var componentEditor = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
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
                applyComponentsButton,
                _componentSummary
            }
        };

        var root = new DockPanel { Margin = new Avalonia.Thickness(12) };
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        DockPanel.SetDock(componentEditor, Dock.Top);
        root.Children.Add(componentEditor);
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
            RowBackground = Brushes.White
        };

        grid.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new Binding(nameof(OpticalSurface.Number)), IsReadOnly = true, Width = new DataGridLength(52) });
        grid.Columns.Add(new DataGridTextColumn { Header = "标签", Binding = new Binding(nameof(OpticalSurface.Label)), Width = new DataGridLength(140) });
        grid.Columns.Add(new DataGridTextColumn { Header = "半径", Binding = new Binding(nameof(OpticalSurface.Radius)), Width = new DataGridLength(92) });
        grid.Columns.Add(new DataGridTextColumn { Header = "厚度", Binding = new Binding(nameof(OpticalSurface.Thickness)), Width = new DataGridLength(92) });
        grid.Columns.Add(new DataGridTextColumn { Header = "材料", Binding = new Binding(nameof(OpticalSurface.Material)), Width = new DataGridLength(96) });
        grid.Columns.Add(new DataGridTextColumn { Header = "镀膜", Binding = new Binding(nameof(OpticalSurface.Coating)), Width = new DataGridLength(90) });
        grid.Columns.Add(new DataGridTextColumn { Header = "半口径", Binding = new Binding(nameof(OpticalSurface.SemiDiameter)), Width = new DataGridLength(92) });
        grid.Columns.Add(new DataGridTextColumn { Header = "圆锥系数", Binding = new Binding(nameof(OpticalSurface.Conic)), Width = new DataGridLength(86) });
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
            _aperturePicker.SelectedItem as string ?? "圆形");
    }

    private static string GeometryKindFor(OpticalSurface surface)
    {
        return surface.Geometry switch
        {
            PlaneGeometry => "平面",
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
            ThinLensInteractionModel => "薄透镜",
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
            null => "无",
            _ => "圆形"
        };
    }
}
