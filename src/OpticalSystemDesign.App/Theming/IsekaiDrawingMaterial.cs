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
        using (context.PushOpacity(scene ? .9 : .22))
            DrawFlourishes(context, plot, Art.Value!, .72);
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
        using var clip = context.PushClip(new RoundedRect(bounds, inset));
        using var interpolation = context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality });
        var bitmap = Art.Value!.Frame;
        var source = bitmap.Size;
        // Separate the narrow binding's corner caps and central crest so they
        // are not stretched into the broad floral mat used by the first draft.
        double[] sx = [0, 72, source.Width / 2 - 64, source.Width / 2 + 64, source.Width - 72, source.Width];
        double[] sy = [0, 80, source.Height - 80, source.Height];
        double[] dx = [bounds.Left, bounds.Left + inset, bounds.Center.X - inset, bounds.Center.X + inset, bounds.Right - inset, bounds.Right];
        double[] dy = [bounds.Top, bounds.Top + inset, bounds.Bottom - inset, bounds.Bottom];
        for (var row = 0; row < 3; row++)
            for (var column = 0; column < 5; column++)
            {
                if (row == 1 && column is > 0 and < 4) continue;
                var destination = new Rect(dx[column], dy[row], Math.Max(0, dx[column + 1] - dx[column]), Math.Max(0, dy[row + 1] - dy[row]));
                context.FillRectangle(Leather, destination);
                context.DrawImage(bitmap,
                    new Rect(sx[column], sy[row], sx[column + 1] - sx[column], sy[row + 1] - sy[row]), destination);
            }
    }

    private static IImmutableBrush Brush(string color) => new ImmutableSolidColorBrush(Color.Parse(color));

    public void DrawLegendOrnament(DrawingContext context, Rect bounds)
    {
        var bitmap = Art.Value!.Armillary;
        var scale = Math.Min(bounds.Width / bitmap.Size.Width, bounds.Height / bitmap.Size.Height);
        if (scale <= 0) return;
        var size = new Size(bitmap.Size.Width * scale, bitmap.Size.Height * scale);
        using var clip = context.PushClip(bounds);
        using var interpolation = context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality });
        context.DrawImage(bitmap, new Rect(bounds.Center.X - size.Width / 2, bounds.Bottom - size.Height, size.Width, size.Height));
    }

    private static void DrawInnerRules(DrawingContext context, Rect bounds)
    {
        if (bounds.Width <= 20 || bounds.Height <= 20) return;
        context.DrawRectangle(null, InnerRule, bounds.Deflate(4), 6, 6);
        context.DrawRectangle(null, InnerRule, bounds.Deflate(8), 3, 3);
    }

    private static void DrawFlourishes(DrawingContext context, Rect bounds, Artwork artwork, double relativeScale)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var bitmap = artwork.Botanical;
        var scale = Math.Min(bounds.Width / bitmap.Size.Width, bounds.Height / bitmap.Size.Height) * relativeScale;
        var half = new Size(bitmap.Size.Width / 2, bitmap.Size.Height / 2);
        var size = new Size(half.Width * scale, half.Height * scale);
        // Each complete corner drawing grows inward on the paper. Scaling is
        // uniform, and moving a camera never moves these decorative elements.
        for (var row = 0; row < 2; row++)
            for (var column = 0; column < 2; column++)
                context.FillRectangle(artwork.CornerBrushes[row * 2 + column],
                    new Rect(column == 0 ? bounds.Left : bounds.Right - size.Width,
                        row == 0 ? bounds.Top : bounds.Bottom - size.Height, size.Width, size.Height));
    }

    private sealed class Artwork
    {
        internal Bitmap Paper { get; } = Load("vellum.png");
        internal Bitmap Frame { get; } = Load("rounded-gold-binding.png");
        internal Bitmap Botanical { get; } = Load("inner-botanical.png");
        internal Bitmap Armillary { get; } = Load("armillary.png");
        internal DrawingImage SideVine { get; } = CreateSideVine();
        internal DrawingImage CompanyLogo { get; } = BrandAssets.LoadCompanyLogoImage(Color.Parse("#513723"));
        internal ImageBrush PaperBrush { get; }
        internal DrawingBrush CaptionBrush { get; }
        internal ImageBrush[] CornerBrushes { get; }

        internal Artwork()
        {
            CornerBrushes = Enumerable.Range(0, 4).Select(index => new ImageBrush(Botanical)
            {
                SourceRect = new RelativeRect(index % 2 * .5, index / 2 * .5, .5, .5, RelativeUnit.Relative),
                Stretch = Stretch.Fill
            }).ToArray();
            PaperBrush = new ImageBrush(Paper)
            {
                Stretch = Stretch.Fill,
                // The lightly mottled centre is the reading surface. Strong
                // foxing and darkened edges stay in the external decorative mat.
                SourceRect = new RelativeRect(.12, .12, .76, .76, RelativeUnit.Relative)
            };
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
