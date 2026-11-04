using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace OpticalSystemDesign.App.Theming;

/// <summary>Optional paint-only drawing material. Data, typography and layout remain with the source.</summary>
internal interface IThemeDrawingMaterial
{
    ResourceDictionary CreateResources(bool transparentPlot);
    void DrawPaper(DrawingContext context, Rect bounds, bool scene);
    void DrawFrame(DrawingContext context, Rect bounds);
    void DrawLegendOrnament(DrawingContext context, Rect bounds, AnalysisArtworkKind kind);
    void DrawCaptionOrnament(DrawingContext context, Rect bounds, AnalysisArtworkKind kind);
    double FrameInset { get; }
    IBrush Ink { get; }
    IBrush CaptionBackground { get; }
}

internal static class ThemeDrawingSurface
{
    internal static IThemeDrawingMaterial? Material(Control control) =>
        ThemeRegistry.FromActualVariant(control.ActualThemeVariant).DrawingMaterial;

    internal static double FrameInset(Control control) => Material(control)?.FrameInset ?? 1;

    internal static void ApplyResources(Control control, bool transparentPlot = false)
    {
        // Per-variant resources are local to the drawing. The leather UI and all
        // other theme dictionaries retain their original brushes and behaviour.
        foreach (var theme in ThemeRegistry.ConcreteThemes)
            if (theme.DrawingMaterial is { } material)
                control.Resources.ThemeDictionaries[theme.RequestedVariant] = material.CreateResources(transparentPlot);
        Track(control);
    }

    internal static void ApplyCaptionResources(Control control)
    {
        foreach (var theme in ThemeRegistry.ConcreteThemes)
            if (theme.DrawingMaterial is { } material)
                control.Resources.ThemeDictionaries[theme.RequestedVariant] = new ResourceDictionary
                {
                    [Services.ThemeResourceBindings.Surface] = material.CaptionBackground
                };
        Track(control);
    }

    internal static void Track(Control control)
    {
        var previous = Material(control);
        control.ActualThemeVariantChanged += (_, _) =>
        {
            var next = Material(control);
            if (previous is not null || next is not null)
            {
                // Custom canvases resolve brushes while painting; resource
                // changes alone need not dirty their cached render operations.
                control.InvalidateVisual();
                foreach (var child in control.GetVisualDescendants().OfType<Control>()) child.InvalidateVisual();
            }
            previous = next;
        };
    }

    internal static void AddPaper(Grid page, Control? themeOwner = null)
    {
        var paper = new Paper(themeOwner) { IsHitTestVisible = false, ClipToBounds = true, ZIndex = -1 };
        Grid.SetRowSpan(paper, Math.Max(1, page.RowDefinitions.Count));
        Grid.SetColumnSpan(paper, Math.Max(1, page.ColumnDefinitions.Count));
        // Preserve the content's logical ordering; only the paint order changes.
        page.Children.Add(paper);
    }

    private sealed class Paper : Control
    {
        private readonly Control? _owner;
        internal Paper(Control? owner)
        {
            _owner = owner;
            ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        }
        public override void Render(DrawingContext context) =>
            Material(_owner ?? this)?.DrawPaper(context, new Rect(Bounds.Size), scene: false);
    }

    internal static Control LegendOrnament(Control legend) => new LegendMargin(legend)
    { IsHitTestVisible = false, ClipToBounds = true, ZIndex = -1 };

    private sealed class LegendMargin : Control
    {
        private readonly Control _legend;
        internal LegendMargin(Control legend)
        {
            _legend = legend;
            legend.SizeChanged += (_, _) => InvalidateVisual();
            ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        }
        public override void Render(DrawingContext context)
        {
            var top = Math.Max(0, _legend.Bounds.Bottom + 28);
            var free = new Rect(14, top, Math.Max(0, Bounds.Width - 28), Math.Max(0, Bounds.Height - top - 24));
            if (free.Width >= 70 && free.Height >= 150)
                Material(this)?.DrawLegendOrnament(context, free, AnalysisArtwork.GetKind(this));
        }
    }

    internal static Control CaptionOrnament(Control summary) => new CaptionMargin(summary)
    { IsHitTestVisible = false, ClipToBounds = true, ZIndex = -1 };

    /// <summary>Uses actual laid-out text lines; decoration is omitted when no safe blank rectangle exists.</summary>
    internal sealed class CaptionMargin(Control summary) : Control
    {
        internal Rect? OrnamentBounds { get; private set; }
        internal IReadOnlyList<Rect> OccupiedTextBounds()
        {
            var occupied = new List<Rect>();
            foreach (var text in summary.GetVisualDescendants().OfType<TextBlock>().Prepend(summary as TextBlock).OfType<TextBlock>())
            {
                if (!text.IsVisible || text.TranslatePoint(default, this) is not { } origin) continue;
                var y = origin.Y;
                foreach (var line in text.TextLayout.TextLines)
                {
                    var width = line.WidthIncludingTrailingWhitespace;
                    var x = origin.X + (text.TextAlignment switch
                    {
                        TextAlignment.Right => Math.Max(0, text.Bounds.Width - width),
                        TextAlignment.Center => Math.Max(0, (text.Bounds.Width - width) / 2),
                        _ => 0
                    });
                    occupied.Add(new Rect(x, y, width, line.Height).Inflate(5)); y += line.Height;
                }
            }
            return occupied;
        }
        public override void Render(DrawingContext context)
        {
            OrnamentBounds = null;
            if (!GetValue(AnalysisArtwork.EnabledProperty) || Material(this) is not { } material) return;
            var occupied = OccupiedTextBounds();
            var best = default(Rect);
            foreach (var top in occupied.Select(rect => rect.Bottom + 4).Prepend(8).Distinct())
            {
                var bottom = Bounds.Height - 8;
                if (bottom - top < 64) continue;
                var left = occupied.Where(rect => rect.Top < bottom && rect.Bottom > top)
                    .Select(rect => rect.Right + 8).DefaultIfEmpty(8).Max();
                var width = Math.Min(320, Bounds.Width - left - 8);
                var height = Math.Min(132, bottom - top);
                if (width < 150) continue;
                var candidate = new Rect(Bounds.Width - width - 8, bottom - height, width, height);
                if (occupied.Any(rect => rect.Intersects(candidate))) continue;
                if (candidate.Width * candidate.Height > best.Width * best.Height) best = candidate;
            }
            if (best.Width <= 0) return;
            OrnamentBounds = best;
            material.DrawCaptionOrnament(context, best, AnalysisArtwork.GetKind(this));
        }
    }
}
