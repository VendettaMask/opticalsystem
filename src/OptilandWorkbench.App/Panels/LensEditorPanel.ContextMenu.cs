using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OptilandWorkbench.App.ViewModels;

namespace OptilandWorkbench.App.Panels;

public sealed partial class LensEditorPanel
{
    private readonly ContextMenu _surfaceContextMenu = new();
    private readonly MenuItem _insertSurfaceBelow = new() { Header = "下插入" };
    private readonly MenuItem _insertSurfaceAbove = new() { Header = "上插入" };
    private readonly MenuItem _deleteSurface = new() { Header = "删除" };
    private int? _surfaceContextNumber;
    private long _surfaceContextRevision;

    private void ConfigureSurfaceContextMenu()
    {
        _surfaceContextMenu.ItemsSource = new[] { _insertSurfaceBelow, _insertSurfaceAbove, _deleteSurface };
        _insertSurfaceBelow.Click += (_, _) => EditContextSurface(after: true);
        _insertSurfaceAbove.Click += (_, _) => EditContextSurface(after: false);
        _deleteSurface.Click += (_, _) => EditContextSurface(after: null);
        // Tunnel before a cell's TextBox can open its own editing menu.
        _grid.AddHandler(InputElement.ContextRequestedEvent, OnSurfaceContextRequested, RoutingStrategies.Tunnel);
    }

    private void OnSurfaceContextRequested(object? sender, ContextRequestedEventArgs args)
    {
        args.Handled = true;
        ClearSurfaceContext();
        var source = args.Source as Control;
        var visualRow = source as DataGridRow ?? source?.GetVisualAncestors().OfType<DataGridRow>().FirstOrDefault();
        var row = visualRow?.DataContext as SurfaceEditorRow;
        if (row is null && !args.TryGetPosition(_grid, out _))
        {
            row = _grid.SelectedItem as SurfaceEditorRow; // Keyboard context-menu key.
        }
        if (row is null || _disposed) return;

        // Finish pending edits before capturing the row/revision, so a delayed LostFocus
        // cannot write an old row back after insertion or deletion has renumbered it.
        if (!_grid.CommitEdit(DataGridEditingUnit.Cell, true)
            || !_grid.CommitEdit(DataGridEditingUnit.Row, true)) return;
        _grid.Focus();
        _grid.SelectedItem = row;
        _surfaceContextNumber = row.Number;
        _surfaceContextRevision = _events.Revision;
        var imageNumber = _prescription.GetSurfaces().Count - 1;
        _insertSurfaceBelow.IsEnabled = row.Number >= 0 && row.Number < imageNumber;
        _insertSurfaceAbove.IsEnabled = row.Number > 0 && row.Number <= imageNumber;
        _deleteSurface.IsEnabled = row.Number > 0 && row.Number < imageNumber;
        _surfaceContextMenu.Open(_grid);
    }

    private void EditContextSurface(bool? after)
    {
        var number = _surfaceContextNumber;
        var current = _surfaceContextRevision == _events.Revision;
        ClearSurfaceContext();
        if (_disposed || !current || number is null) return;

        var imageNumber = _prescription.GetSurfaces().Count - 1;
        int selectedNumber;
        if (after.HasValue)
        {
            var insertion = number.Value + (after.Value ? 1 : 0);
            if (insertion <= 0 || insertion > imageNumber) return;
            selectedNumber = _prescription.InsertSurface(number.Value, after.Value);
        }
        else
        {
            if (number.Value <= 0 || number.Value >= imageNumber) return;
            _prescription.RemoveSurface(number.Value);
            selectedNumber = Math.Min(number.Value, Math.Max(1, imageNumber - 2));
        }

        Refresh();
        _grid.SelectedItem = _grid.ItemsSource!.Cast<SurfaceEditorRow>()
            .FirstOrDefault(row => row.Number == selectedNumber);
        _grid.ScrollIntoView(_grid.SelectedItem, null);
    }

    private void ClearSurfaceContext()
    {
        _surfaceContextMenu.Close();
        _surfaceContextNumber = null;
    }
}
