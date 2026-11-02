using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.App.ViewModels;

namespace OpticalSystemDesign.App.Controls;

internal sealed class SurfaceRoleIndicator : Control
{
    internal SurfaceRoleIndicator()
    {
        Width = Height = 8;
        IsHitTestVisible = false;
        DataContextChanged += (_, _) => InvalidateVisual();
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (DataContext is not SurfaceEditorRow row) return;
        var brush = SurfaceRolePalette.Resolve(row.Number, row.IsLastSurface, row.IsStop,
            ThemeRegistry.FromActualVariant(ActualThemeVariant).IsDarkLike);
        var radius = Math.Min(Bounds.Width, Bounds.Height) / 2;
        context.DrawEllipse(brush, null, new Point(Bounds.Width / 2, Bounds.Height / 2), radius, radius);
    }
}
