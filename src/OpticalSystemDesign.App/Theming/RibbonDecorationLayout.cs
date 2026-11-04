using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace OpticalSystemDesign.App.Theming;

/// <summary>Paint-only slots derived from the native Ribbon controls, in the overlay's coordinates.</summary>
internal readonly record struct RibbonDecorationLayout(Rect MenuBand, Rect SelectedTab, Rect SwordBand,
    Rect Commands, Rect VacantEnd, double FooterGap)
{
    internal bool IsReady => MenuBand.Height > 0 && Commands.Height > 0;

    internal static RibbonDecorationLayout Measure(TabControl tabs, Control overlay)
    {
        Rect menus = default, selected = default, commands = default;
        foreach (var control in tabs.GetVisualDescendants().OfType<Control>())
        {
            if (!control.IsEffectivelyVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0
                || control.TransformToVisual(overlay) is not { } transform) continue;
            var rect = new Rect(control.Bounds.Size).TransformToAABB(transform);
            if (control is TabItem tab && tab.Classes.Contains("ribbon-tab"))
            {
                menus = menus.Width > 0 ? menus.Union(rect) : rect;
                if (tab.IsSelected) selected = rect;
            }
            else if (control is Button && control.Classes.Contains("ribbon-command")
                || control.Name == "RibbonGroupDivider")
                commands = commands.Width > 0 ? commands.Union(rect) : rect;
        }
        if (menus.Height <= 0 || commands.Height <= 0) return default;
        var width = overlay.Bounds.Width;
        var sword = new Rect(6, menus.Bottom + .5, Math.Max(0, width - 12),
            Math.Max(0, commands.Top - menus.Bottom - 1));
        var left = Math.Max(8, commands.Right + 12);
        var vacant = new Rect(left, commands.Top + 2, Math.Max(0, width - left - 8), Math.Max(0, commands.Height - 4));
        return new(menus, selected, sword, commands, vacant, Math.Max(0, overlay.Bounds.Height - commands.Bottom));
    }
}

internal interface ILayoutAwareRibbonDecoration
{
    void RenderRibbon(DrawingContext context, Rect bounds, RibbonDecorationLayout layout, bool foreground);
}
