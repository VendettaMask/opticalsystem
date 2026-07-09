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

        var addButton = new Button { Content = "Add", MinWidth = 74 };
        addButton.Click += (_, _) => _connector.AddSurface();

        var removeButton = new Button { Content = "Remove", MinWidth = 74 };
        removeButton.Click += (_, _) => _connector.RemoveSurface(_grid.SelectedItem as OpticalSurface);

        var applyComponentsButton = new Button { Content = "Apply components", MinWidth = 132 };
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
                new TextBlock { Text = "Geometry", VerticalAlignment = VerticalAlignment.Center },
                _geometryPicker,
                new TextBlock { Text = "Material", VerticalAlignment = VerticalAlignment.Center },
                _materialPicker,
                new TextBlock { Text = "Coating", VerticalAlignment = VerticalAlignment.Center },
                _coatingPicker,
                new TextBlock { Text = "Interaction", VerticalAlignment = VerticalAlignment.Center },
                _interactionPicker,
                new TextBlock { Text = "Aperture", VerticalAlignment = VerticalAlignment.Center },
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
        grid.Columns.Add(new DataGridTextColumn { Header = "Label", Binding = new Binding(nameof(OpticalSurface.Label)), Width = new DataGridLength(140) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Radius", Binding = new Binding(nameof(OpticalSurface.Radius)), Width = new DataGridLength(92) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Thickness", Binding = new Binding(nameof(OpticalSurface.Thickness)), Width = new DataGridLength(92) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Material", Binding = new Binding(nameof(OpticalSurface.Material)), Width = new DataGridLength(96) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Coating", Binding = new Binding(nameof(OpticalSurface.Coating)), Width = new DataGridLength(90) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Semi Dia.", Binding = new Binding(nameof(OpticalSurface.SemiDiameter)), Width = new DataGridLength(92) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Conic", Binding = new Binding(nameof(OpticalSurface.Conic)), Width = new DataGridLength(78) });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Stop", Binding = new Binding(nameof(OpticalSurface.IsStop)), Width = new DataGridLength(64) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Geometry Kind", Binding = new Binding("Geometry.Kind"), IsReadOnly = true, Width = new DataGridLength(120) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Coating Kind", Binding = new Binding("CoatingModel.Kind"), IsReadOnly = true, Width = new DataGridLength(120) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Interaction Kind", Binding = new Binding("InteractionModel.Kind"), IsReadOnly = true, Width = new DataGridLength(132) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Aperture Kind", Binding = new Binding("PhysicalAperture.Kind"), IsReadOnly = true, Width = new DataGridLength(120) });

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
            _componentSummary.Text = "No surface selected";
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
            _componentSummary.Text = $"S{surface.Number}: {surface.Geometry.Kind}, {surface.MaterialAfterName}";
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
            _geometryPicker.SelectedItem as string ?? "Standard",
            _materialPicker.SelectedItem as string ?? surface.Material,
            _coatingPicker.SelectedItem as string ?? "None",
            _interactionPicker.SelectedItem as string ?? "Refractive",
            _aperturePicker.SelectedItem as string ?? "Circular");
    }

    private static string GeometryKindFor(OpticalSurface surface)
    {
        return surface.Geometry switch
        {
            PlaneGeometry => "Plane",
            EvenAsphereGeometry => "Even Asphere",
            OddAsphereGeometry => "Odd Asphere",
            BiconicGeometry => "Biconic",
            ToroidalGeometry => "Toroidal",
            PolynomialGeometry => "Polynomial",
            _ => "Standard"
        };
    }

    private static string CoatingKindFor(OpticalSurface surface)
    {
        if (surface.CoatingModel is ThinFilmStackCoating stack)
        {
            return stack.Layers.Count > 1 ? "Quarter-wave Stack" : "MgF2";
        }

        return "None";
    }

    private static string InteractionKindFor(OpticalSurface surface)
    {
        return surface.InteractionModel switch
        {
            ThinLensInteractionModel => "Thin Lens",
            DiffractiveInteractionModel => "Diffractive",
            PhaseInteractionModel => "Phase",
            RefractiveReflectiveInteractionModel model when model.IsReflective => "Reflective",
            _ => "Refractive"
        };
    }

    private static string ApertureKindFor(OpticalSurface surface)
    {
        return surface.PhysicalAperture switch
        {
            RectangularAperture => "Rectangular",
            null => "None",
            _ => "Circular"
        };
    }
}
