using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.App.Controls;

public enum ViewCubeFace
{
    Isometric,
    Front,
    Back,
    Left,
    Right,
    Top,
    Bottom
}

public sealed class ViewCubeIcon : Control
{
    public ViewCubeIcon(ViewCubeFace face)
    {
        Face = face;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public ViewCubeFace Face { get; }

    public bool EmphasizeFace { get; init; }

    protected override Size MeasureOverride(Size availableSize) => new(26, 24);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var theme = ThemeRegistry.FromActualVariant(ActualThemeVariant);
        var palette = theme.IconPalette;
        if (!EmphasizeFace)
            palette = new ScientificIconPalette(palette.Structure, palette.Secondary, palette.Secondary) { Finish = palette.Finish };
        ScientificIconLibrary.Resolve($"view-{Face.ToString().ToLowerInvariant()}", theme.SettingsValue).Artwork!
            .Draw(context, Bounds, palette);
    }
}
