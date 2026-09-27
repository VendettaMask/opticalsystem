using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Path = Avalonia.Controls.Shapes.Path;

namespace OptilandWorkbench.App.Controls;

internal sealed class DataGridHeaderLayout
{
    internal static readonly AttachedProperty<bool> CompactProperty =
        AvaloniaProperty.RegisterAttached<DataGridHeaderLayout, DataGridColumnHeader, bool>("Compact");

    static DataGridHeaderLayout()
    {
        CompactProperty.Changed.AddClassHandler<DataGridColumnHeader>((header, change) =>
        {
            header.TemplateApplied -= OnTemplateApplied;
            if (change.NewValue is true) header.TemplateApplied += OnTemplateApplied;
        });
    }

    private static void OnTemplateApplied(object? sender, TemplateAppliedEventArgs args)
    {
        // Fluent reserves 32 DIP even while SortIcon is hidden. Keep the original
        // template, sort states, focus and resize grips; only measure that slot by content.
        if (args.NameScope.Find<Path>("SortIcon") is { } icon
            && icon.GetVisualParent() is Grid grid
            && grid.ColumnDefinitions.Count == 2)
        {
            grid.ColumnDefinitions = new ColumnDefinitions("*,Auto");
            icon.Margin = new Thickness(4, 0, 0, 0);
        }
    }
}
