using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Controls;

namespace OpticalSystemDesign.App.Theming;

/// <summary>Grimoire binding and engraved ornament, independent of all optical drawing data.</summary>
internal sealed class IsekaiDrawingMaterial : IThemeDrawingMaterial
{
    internal static IsekaiDrawingMaterial Instance { get; } = new();
    private static readonly IImmutableBrush InkBrush = Brush("#513723");
    private static readonly IImmutableBrush MutedInk = Brush("#806344");
    private static readonly IImmutableBrush Leather = Brush("#382017");
    private static readonly IPen InnerRule = new ImmutablePen(Brush("#A28254"), .7);
    // Once per UI thread; no per-frame decoding or unbounded scale caches.
    private static readonly ThreadLocal<Artwork> Art = new(() => new Artwork());

    public IBrush Ink => InkBrush;
    public double FrameInset => 28;
    internal IBrush PaperBackground => Art.Value!.PaperBrush;
    public IBrush CaptionBackground => Art.Value!.CaptionBrush;

    public ResourceDictionary CreateResources(bool transparentPlot) => new()
    {
        [ThemeResourceBindings.PlotBackground] = transparentPlot ? Brushes.Transparent : Art.Value!.PaperBrush,
        [ThemeResourceBindings.Surface] = Art.Value!.PaperBrush,
        [ThemeResourceBindings.SubtleSurface] = Brush("#F0DFC0"),
        [ThemeResourceBindings.SceneBackground] = Art.Value!.PaperBrush,
        [ThemeResourceBindings.Border] = MutedInk,
        [ThemeResourceBindings.TextPrimary] = InkBrush,
        [ThemeResourceBindings.TextSecondary] = InkBrush,
        [ThemeResourceBindings.TextMuted] = MutedInk,
        [ThemeResourceBindings.MutedText] = MutedInk,
        [ThemeResourceBindings.TextDisabled] = Brush("#9B886C"),
        [ThemeResourceBindings.TextAccent] = Brush("#8C581D"),
        [ThemeResourceBindings.PlotText] = InkBrush,
        [ThemeResourceBindings.PlotTick] = MutedInk,
        [ThemeResourceBindings.PlotAxis] = InkBrush,
        [ThemeResourceBindings.PlotGrid] = Brush("#DAC8A9"),
        [ThemeResourceBindings.PlotZeroLine] = Brush("#996C35"),
        [ThemeResourceBindings.PlotHint] = MutedInk,
        [ThemeResourceBindings.PlotTooltipBackground] = Brush("#F8FAF2E2"),
        [ThemeResourceBindings.PlotTooltipBorder] = MutedInk,
        [ThemeResourceBindings.PlotHoverMarkerFill] = Brush("#F7EDD8"),
        [ThemeAssetBindings.CompanyLogo] = Art.Value!.CompanyLogo
    };

    public void DrawPaper(DrawingContext context, Rect bounds, bool scene)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var clip = context.PushClip(bounds);
        using var interpolation = context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality });
        context.FillRectangle(Art.Value!.PaperBrush, bounds);
        // Flowers belong to the paper, not to the outer leather rim. Scene
        // canvases repaint their own background above this source-sheet layer.
        var plot = scene ? bounds : new Rect(bounds.Position,
            new Size(bounds.Width, Math.Min(bounds.Height, UniformDiagramPage.PlotHeight)));
        DrawInnerRules(context, plot);
        using (context.PushOpacity(scene ? .9 : .32))
            DrawFlourishes(context, plot, Art.Value!, .72, crafted: !scene);
        if (!scene && plot.Width > 100 && plot.Height > 360)
        {
            // Keep stronger pen foliage within the narrow paper margins. The
            // source plots, labels and legends retain all their existing space.
            using var opacity = context.PushOpacity(.55);
            context.DrawImage(Art.Value!.SideVine, new Rect(plot.Left + 1, plot.Top + 160, 16, plot.Height - 320));
            context.DrawImage(Art.Value!.SideVine, new Rect(plot.Right - 17, plot.Top + 160, 16, plot.Height - 320));
        }
    }

    public void DrawFrame(DrawingContext context, Rect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var inset = Math.Min(FrameInset, Math.Min(bounds.Width, bounds.Height) / 2);
        using var clip = context.PushClip(bounds);
        IsekaiCraftsmanship.DrawBinding(context, bounds, inset);
    }

    private static IImmutableBrush Brush(string color) => new ImmutableSolidColorBrush(Color.Parse(color));

    public void DrawLegendOrnament(DrawingContext context, Rect bounds, AnalysisArtworkKind kind)
    {
        DrawSprite(context, Art.Value!.Accessories, AccessoryRegions[(int)kind], bounds);
    }

    public void DrawCaptionOrnament(DrawingContext context, Rect bounds, AnalysisArtworkKind kind) =>
        DrawSprite(context, Art.Value!.Folios, FolioRegions[(int)kind], bounds);

    // Tight source bounds measured from the bundled alpha channel, never from optical data.
    private static readonly Rect[] AccessoryRegions = [new(62, 20, 402, 711), new(567, 31, 395, 700),
        new(49, 781, 415, 709), new(530, 788, 451, 704)];
    private static readonly Rect[] FolioRegions = [new(46, 155, 640, 299), new(732, 242, 669, 209),
        new(44, 672, 621, 230), new(732, 633, 667, 268)];
    private static readonly Rect NavigationRegion = new(21, 160, 2132, 365);

    private static void DrawSprite(DrawingContext context, Bitmap bitmap, Rect source, Rect bounds)
    {
        var scale = Math.Min(bounds.Width / source.Width, bounds.Height / source.Height);
        if (scale <= 0) return;
        var size = new Size(source.Width * scale, source.Height * scale);
        using var clip = context.PushClip(bounds);
        using var interpolation = context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality });
        context.DrawImage(bitmap, source, new Rect(bounds.Center.X - size.Width / 2, bounds.Bottom - size.Height, size.Width, size.Height));
    }

    internal void DrawNavigation(DrawingContext context, Rect bounds, double usedWidth)
    {
        DrawManuscriptBand(context, bounds, footer: true);
        var freeWidth = Math.Max(0, bounds.Width - usedWidth - 24);
        var width = Math.Min(190, freeWidth);
        var height = Math.Min(24, Math.Max(0, bounds.Height - 18));
        if (width > 100 && height > 0)
            using (context.PushOpacity(.42))
                DrawSprite(context, Art.Value!.Navigation, NavigationRegion,
                    new Rect(bounds.Right - width - 18, bounds.Center.Y - height / 2, width, height));
    }

    internal void DrawManuscriptBand(DrawingContext context, Rect bounds, bool footer)
    {
        // Live controls read as a manuscript's header/index on the same paper,
        // rather than independent leather straps pasted over the drawing.
        context.FillRectangle(Art.Value!.PaperBrush, bounds);
        var y = footer ? bounds.Top + 3 : bounds.Bottom - 3;
        using var opacity = context.PushOpacity(.6);
        context.DrawLine(InnerRule, new Point(bounds.Left + 16, y), new Point(bounds.Right - 16, y));
        context.DrawEllipse(MutedInk, null, new Point(bounds.Left + 16, y), 1.2, 1.2);
        context.DrawEllipse(MutedInk, null, new Point(bounds.Right - 16, y), 1.2, 1.2);
    }

    internal static ResourceDictionary ManuscriptCommandResources() => new()
    {
        [ThemeResourceBindings.TextMuted] = Brush("#806344"),
        [ThemeResourceBindings.TextAccent] = Brush("#83561F"),
        [ThemeResourceBindings.TextSuccess] = Brush("#49683A"),
        [ThemeResourceBindings.TextWarning] = Brush("#85531F"),
        [ThemeResourceBindings.TextError] = Brush("#A03E2E")
    };

    private static void DrawInnerRules(DrawingContext context, Rect bounds)
    {
        if (bounds.Width <= 20 || bounds.Height <= 20) return;
        using var opacity = context.PushOpacity(.35);
        context.DrawLine(InnerRule, new Point(bounds.Left + 8, bounds.Bottom - 4), new Point(bounds.Right - 8, bounds.Bottom - 4));
    }

    private static void DrawFlourishes(DrawingContext context, Rect bounds, Artwork artwork, double relativeScale, bool crafted = true)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        if (!crafted)
        {
            // Preserve the approved astronomical/alchemical corner compositions
            // inside 2D/3D scenes; the new botanical atlas belongs to analysis paper.
            var scale = Math.Min(bounds.Width / artwork.Botanical.Size.Width, bounds.Height / artwork.Botanical.Size.Height) * relativeScale;
            var size = new Size(artwork.Botanical.Size.Width / 2 * scale, artwork.Botanical.Size.Height / 2 * scale);
            for (var row = 0; row < 2; row++) for (var col = 0; col < 2; col++)
                context.DrawImage(artwork.Botanical,
                    new Rect(col * artwork.Botanical.Size.Width / 2, row * artwork.Botanical.Size.Height / 2,
                        artwork.Botanical.Size.Width / 2, artwork.Botanical.Size.Height / 2), new Rect(
                    col == 0 ? bounds.Left : bounds.Right - size.Width,
                    row == 0 ? bounds.Top : bounds.Bottom - size.Height, size.Width, size.Height));
            return;
        }
        var width = Math.Min(bounds.Width * .19, bounds.Height * .34) * relativeScale;
        // Use complete alpha-bounded drawings. The old half-atlas crop left
        // disconnected petal fragments floating inside the chart.
        for (var row = 0; row < 2; row++)
            for (var column = 0; column < 2; column++)
            {
                var index = row * 2 + column;
                var source = IsekaiCraftsmanship.PaperCorners[index];
                var height = width * source.Height / source.Width;
                IsekaiCraftsmanship.DrawPaperCorner(context, index,
                    new Rect(column == 0 ? bounds.Left + 8 : bounds.Right - width - 8,
                        row == 0 ? bounds.Top + 8 : bounds.Bottom - height - 8, width, height));
            }
    }

    private sealed class Artwork
    {
        internal Bitmap Paper { get; } = Load("frayed-vellum.png");
        internal Bitmap Botanical { get; } = Load("inner-botanical.png");
        internal Bitmap Accessories { get; } = Load("analysis-accessory-atlas.png");
        internal Bitmap Folios { get; } = Load("analysis-folio-atlas.png");
        internal Bitmap Navigation { get; } = Load("book-navigation-ornament.png");
        internal DrawingImage SideVine { get; } = CreateSideVine();
        internal DrawingImage CompanyLogo { get; } = BrandAssets.LoadCompanyLogoImage(Color.Parse("#513723"));
        internal IBrush PaperBrush { get; }
        internal DrawingBrush CaptionBrush { get; }

        internal Artwork()
        {
            var paperTexture = new ImageBrush(Paper)
            {
                Stretch = Stretch.Fill,
                // Keep the original generated fibres and pressed folds visible;
                // the outer 28-DIP material uses this same sheet's frayed edges.
                SourceRect = new RelativeRect(.055, .07, .89, .86, RelativeUnit.Relative)
            };
            var paper = new DrawingGroup();
            using (var context = paper.Open())
            {
                var page = new Rect(0, 0, UniformDiagramPage.SheetWidth, UniformDiagramPage.SheetHeight);
                context.FillRectangle(Brush("#F5E8CE"), page);
                context.FillRectangle(paperTexture, page);
            }
            // Preserve fibre/fold proportions in the very wide caption band;
            // stretching the whole sheet there makes parchment resemble wood grain.
            PaperBrush = new DrawingBrush(paper) { Stretch = Stretch.UniformToFill };
            var drawing = new DrawingGroup();
            using (var context = drawing.Open())
            {
                var bounds = new Rect(0, 0, UniformDiagramPage.SheetWidth, UniformDiagramPage.CaptionHeight);
                context.FillRectangle(PaperBrush, bounds);
                DrawInnerRules(context, bounds);
                using var interpolation = context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality });
                using var opacity = context.PushOpacity(.22);
                DrawFlourishes(context, bounds, this, 1);
            }
            CaptionBrush = new DrawingBrush(drawing) { Stretch = Stretch.Fill };
        }

        private static Bitmap Load(string name)
        {
            using var stream = typeof(IsekaiDrawingMaterial).Assembly.GetManifestResourceStream(
                "OpticalSystemDesign.App.Assets.Themes.Isekai." + name)
                ?? throw new InvalidOperationException("Missing bundled drawing material: " + name);
            return new Bitmap(stream);
        }

        private static DrawingImage CreateSideVine()
        {
            // A complete narrow botanical drawing, not a cropped sliver of a
            // larger flower. Cache all paths; no geometry parsing while painting.
            var drawing = new DrawingGroup();
            var pen = new ImmutablePen(MutedInk, .7);
            var wash = Brush("#24A28254");
            using (var context = drawing.Open())
            {
                context.DrawGeometry(null, pen, StreamGeometry.Parse("M9,0 C3,25 15,45 9,70 S3,115 9,140 S15,185 9,210 S3,255 9,280"));
                for (var i = 0; i < 7; i++)
                {
                    var y = 15 + i * 37;
                    var tip = i % 2 == 0 ? 1 : 17;
                    var curve = i % 2 == 0 ? 0 : 18;
                    var path = FormattableString.Invariant($"M9,{y + 20} Q{curve},{y + 18} {tip},{y} Q{tip},{y + 12} 9,{y + 20} M9,{y + 20} L{tip},{y + 3}");
                    context.DrawGeometry(wash, pen, StreamGeometry.Parse(path));
                }
            }
            return new DrawingImage(drawing);
        }
    }
}
