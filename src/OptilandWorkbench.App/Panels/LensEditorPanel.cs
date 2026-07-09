using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.App.Connectors;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.App.Panels;

public sealed class LensEditorPanel : UserControl
{
    private readonly OptilandConnector _connector;
    private readonly DataGrid _grid;

    public LensEditorPanel(OptilandConnector connector)
    {
        _connector = connector;
        _grid = CreateGrid();

        var addButton = new Button { Content = "Add", MinWidth = 74 };
        addButton.Click += (_, _) => _connector.AddSurface();

        var removeButton = new Button { Content = "Remove", MinWidth = 74 };
        removeButton.Click += (_, _) => _connector.RemoveSurface(_grid.SelectedItem as OpticalSurface);

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
            Children = { addButton, removeButton }
        };

        var root = new DockPanel { Margin = new Avalonia.Thickness(12) };
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(_grid);
        Content = root;

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

        grid.BeginningEdit += (_, _) => _connector.CaptureCurrentState();
        grid.CellEditEnded += (_, _) => _connector.CommitSurfaceEdit();

        return grid;
    }

    private void Refresh()
    {
        _grid.ItemsSource = _connector.Surfaces;
    }
}
