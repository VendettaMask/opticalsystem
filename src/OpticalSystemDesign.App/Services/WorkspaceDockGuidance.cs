using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Dock.Settings;

namespace OpticalSystemDesign.App.Services;

/// <summary>Keep Dock's drag/drop validation and geometry; make its guidance visible for every page.</summary>
internal static class WorkspaceDockGuidance
{
    private const string PreviewClass = "workspace-dock-preview-frame";
    private const string SelectorClass = "workspace-dock-selector-frame";
    private static bool _installed;
    private sealed class DragPointer { public IPointer? Pointer { get; set; } }
    private static readonly ConditionalWeakTable<DockControl, DragPointer> DragPointers = new();

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        Control.PointerPressedEvent.AddClassHandler<DockControl>((dock, args) =>
        {
            if (dock.Factory is WorkspaceDockFactory)
                DragPointers.GetOrCreateValue(dock).Pointer = args.Pointer;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        Window.KeyDownEvent.AddClassHandler<Window>((window, args) =>
        {
            if (args.Key != Key.Escape) return;
            foreach (var dock in window.GetVisualDescendants().OfType<DockControl>().Where(dock => dock.IsDraggingDock))
            {
                if (!DragPointers.TryGetValue(dock, out var state) || state.Pointer is not { } pointer) continue;
                // Releasing capture uses Dock's native CaptureLost cancellation:
                // no drop, floating window, content replacement or layout mutation.
                pointer.Capture(null);
                state.Pointer = null;
                args.Handled = true;
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        Control.LoadedEvent.AddClassHandler<MdiDocumentWindow>((page, _) =>
        {
            if (page.DataContext is not IDockable { Factory: WorkspaceDockFactory }) return;
            // Dock's MDI item template locally forces indicator-only/Fill. Keep
            // its group-sized host (the actual split target), but expose all valid
            // directions without replacing the page/content template.
            page.SetCurrentValue(DockProperties.ShowDockIndicatorOnlyProperty, false);
        }, handledEventsToo: true);
        Control.LoadedEvent.AddClassHandler<DockTargetBase>((target, _) => Decorate(target), handledEventsToo: true);
    }

    private static void Decorate(DockTargetBase target)
    {
        foreach (var operation in new[] { "Top", "Bottom", "Left", "Right", "Center" })
        {
            var descendants = target.GetVisualDescendants().OfType<Control>().ToArray();
            if (descendants.SingleOrDefault(control => control.Name == "PART_" + operation + "Indicator") is Panel indicator
                && !indicator.Children.Any(child => child.Classes.Contains(PreviewClass)))
            {
                // Native Dock still controls visibility, opacity and the exact
                // preview rectangle; this only replaces its accent-colored fill.
                indicator.Background = Brushes.Transparent;
                var frame = new Border
                {
                    BorderThickness = new Thickness(1),
                    IsHitTestVisible = false
                };
                frame.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.DockPreviewFill);
                frame.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.DockPreviewBorder);
                frame.Classes.Add(PreviewClass);
                indicator.Children.Add(frame);
            }
            if (descendants.SingleOrDefault(control => control.Name == "PART_" + operation + "Selector") is not Image selector
                || selector.GetVisualParent() is not Grid grid
                || grid.Children.Any(child => child.Classes.Contains(SelectorClass)
                    && Grid.GetRow(child) == Grid.GetRow(selector) && Grid.GetColumn(child) == Grid.GetColumn(selector))) continue;
            var plate = new Border
            {
                Width = selector.Width + 4,
                Height = selector.Height + 4,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            plate.Classes.Add(SelectorClass);
            plate.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
            plate.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
            plate.Bind(Visual.OpacityProperty, selector.GetObservable(Visual.OpacityProperty));
            plate.Bind(Visual.IsVisibleProperty, selector.GetObservable(Visual.IsVisibleProperty));
            Grid.SetRow(plate, Grid.GetRow(selector));
            Grid.SetColumn(plate, Grid.GetColumn(selector));
            grid.Children.Insert(grid.Children.IndexOf(selector), plate);
            // Supply canonical text to the shared live-localization hook, so an
            // already-visible compass also follows a language change.
            var hint = operation switch
            {
                "Left" => "停靠到左侧",
                "Right" => "停靠到右侧",
                "Top" => "停靠到上方",
                "Bottom" => "停靠到下方",
                _ => "合并为标签页"
            };
            ToolTip.SetTip(selector, hint);
            AutomationProperties.SetName(selector, hint);
        }
    }
}
