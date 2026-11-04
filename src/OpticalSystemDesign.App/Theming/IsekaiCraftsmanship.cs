using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;

namespace OpticalSystemDesign.App.Theming;

/// <summary>Shared, cached bookbinder's materials. These draw decoration only.</summary>
internal static class IsekaiCraftsmanship
{
    private static readonly ThreadLocal<Artwork> Art = new(() => new Artwork());
    private static readonly IBrush Walnut = B("#382017"), Shade = B("#80301C13");
    private static readonly IPen Gold = new ImmutablePen(B("#C5A263"), .8);
    private static readonly IPen Shadow = new ImmutablePen(B("#62421F"), 2.5);
    private static readonly Rect Crest = new(52, 105, 622, 245);
    private static readonly Rect Divider = new(759, 186, 643, 84);
    private static readonly Rect Rosette = new(926, 591, 317, 280);
    private static readonly Rect[] Plaques = [new(23, 226, 688, 261), new(734, 217, 683, 273),
        new(21, 571, 683, 258), new(732, 568, 697, 258)];
    internal static readonly Rect[] PaperCorners = [new(96,33,542,474), new(812,33,541,474),
        new(82,571,556,475), new(811,571,556,475)];

    internal static void DrawLeather(DrawingContext context, Rect bounds)
    {
        context.FillRectangle(Walnut, bounds);
        // Reuse the earlier generated blind-tooled leather, not the rejected web texture.
        using (context.PushOpacity(.82)) context.FillRectangle(Art.Value!.LeatherBrush, bounds);
        using (context.PushOpacity(.35)) context.FillRectangle(Shade, bounds);
    }

    internal static void DrawBinding(DrawingContext context, Rect bounds, double inset)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var interpolation = context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality });
        var image = Art.Value!.Binding;
        // Actual translucent frayed fibres define the silhouette. There is no
        // rectangular fill or metallic moulding behind their transparent gaps.
        NineSlice(context, image, new Rect(image.Size), bounds, 100, 100, inset, inset, omitCentre: true);
    }

    internal static void DrawBar(DrawingContext context, Rect bounds, bool decoratedEnds = true)
    {
        DrawLeather(context, bounds);
        using (context.PushOpacity(.3))
        foreach (var y in new[] { bounds.Top + 1, bounds.Bottom - 1 })
        {
            context.DrawLine(Gold, new Point(bounds.Left, y), new Point(bounds.Right, y));
        }
        if (decoratedEnds && bounds.Width > 80 && bounds.Height > 18)
        {
            var size = Math.Min(8, bounds.Height - 4);
            using var opacity = context.PushOpacity(.65);
            DrawTrim(context, Rosette, new Rect(bounds.Left + 3, bounds.Center.Y - size / 2, size, size));
            DrawTrim(context, Rosette, new Rect(bounds.Right - size - 3, bounds.Center.Y - size / 2, size, size));
        }
    }

    internal static void DrawCrest(DrawingContext context, Rect bounds) => DrawTrim(context, Crest, bounds);
    internal static void DrawDivider(DrawingContext context, Rect bounds) => DrawTrim(context, Divider, bounds);

    internal static void DrawPlaque(DrawingContext context, Rect bounds, int state)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var interpolation = context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality });
        NineSlice(context, Art.Value!.Controls, Plaques[state], bounds, 95, 55,
            Math.Min(14, bounds.Width / 3), Math.Min(7, bounds.Height / 3), false);
    }

    internal static IBrush PlaqueBrush(int state) => Art.Value!.PlaqueBrushes[state];

    internal static void DrawFolioRail(DrawingContext context, Rect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        // Tile the real leather grain; stretching a small button centre over a
        // full-width rail would turn it into long wood-like streaks.
        var body = bounds.Deflate(new Thickness(7, 3));
        if (body.Width > 0 && body.Height > 0)
            using (context.PushClip(new RoundedRect(body, 5))) DrawLeather(context, body);
        NineSlice(context, Art.Value!.Controls, Plaques[0], bounds, 95, 55,
            Math.Min(14, bounds.Width / 3), Math.Min(7, bounds.Height / 3), omitCentre: true);
    }

    internal static void DrawPaperCorner(DrawingContext context, int index, Rect bounds) =>
        context.FillRectangle(Art.Value!.CornerBrushes[index], bounds);

    private static void DrawTrim(DrawingContext context, Rect source, Rect bounds)
    {
        var scale = Math.Min(bounds.Width / source.Width, bounds.Height / source.Height);
        if (scale <= 0) return;
        var width = source.Width * scale; var height = source.Height * scale;
        context.DrawImage(Art.Value!.Trims, source,
            new Rect(bounds.Center.X - width / 2, bounds.Center.Y - height / 2, width, height));
    }

    private static void NineSlice(DrawingContext context, Bitmap image, Rect s, Rect d,
        double sx, double sy, double dx, double dy, bool omitCentre)
    {
        dx = Math.Min(dx, d.Width / 2); dy = Math.Min(dy, d.Height / 2);
        double[] xs = [s.Left, s.Left + sx, s.Right - sx, s.Right];
        double[] ys = [s.Top, s.Top + sy, s.Bottom - sy, s.Bottom];
        double[] xd = [d.Left, d.Left + dx, d.Right - dx, d.Right];
        double[] yd = [d.Top, d.Top + dy, d.Bottom - dy, d.Bottom];
        for (var row = 0; row < 3; row++) for (var col = 0; col < 3; col++)
        {
            if (omitCentre && row == 1 && col == 1) continue;
            var target = new Rect(xd[col], yd[row], Math.Max(0, xd[col + 1] - xd[col]), Math.Max(0, yd[row + 1] - yd[row]));
            if (target.Width > 0 && target.Height > 0)
            {
                var source = new Rect(xs[col], ys[row], xs[col + 1] - xs[col], ys[row + 1] - ys[row]);
                context.DrawImage(image, source, target);
            }
        }
    }

    private static IImmutableBrush B(string value) => new ImmutableSolidColorBrush(Color.Parse(value));
    private sealed class Artwork
    {
        internal readonly Bitmap Leather = Load("crafted-leather.png");
        internal readonly Bitmap Binding = Load("frayed-vellum.png");
        internal readonly Bitmap Controls = Load("scroll-control-skins.png");
        internal readonly Bitmap Corners = Load("crafted-paper-corners.png");
        internal readonly Bitmap Trims = Load("crafted-crest-trims.png");
        internal readonly ImageBrush LeatherBrush;
        internal readonly ImageBrush[] CornerBrushes;
        internal readonly IBrush[] PlaqueBrushes;
        internal Artwork()
        {
            LeatherBrush = new ImageBrush(Leather) { TileMode = TileMode.Tile, Stretch = Stretch.Fill,
                DestinationRect = new RelativeRect(0, 0, 520, 520, RelativeUnit.Absolute) };
            CornerBrushes = PaperCorners.Select(source => new ImageBrush(Corners)
                { SourceRect = new RelativeRect(source, RelativeUnit.Absolute), Stretch = Stretch.Fill }).ToArray();
            // A brush for the native Dock/Fluent bodies; no text or hit targets are in the bitmap.
            PlaqueBrushes = Enumerable.Range(0, 4).Select(state =>
            {
                // Cache four small 3x skins, never a per-DPI/per-frame cache.
                // DrawingGroup cannot record DrawImage in Avalonia 12, while
                // separate ImageBrush source slices clip in logical coordinates
                // at high DPI. A complete zero-origin texture avoids both paths.
                var skin = new RenderTargetBitmap(new PixelSize(660, 108), new Vector(288, 288));
                using (var context = skin.CreateDrawingContext())
                    NineSlice(context, Controls, Plaques[state], new Rect(0, 0, 220, 36), 95, 55, 14, 7, false);
                var drawing = new DrawingGroup();
                using (var context = drawing.Open())
                    context.FillRectangle(new ImageBrush(skin) { Stretch = Stretch.Fill }, new Rect(0, 0, 220, 36));
                return (IBrush)new DrawingBrush(drawing) { Stretch = Stretch.Fill };
            }).ToArray();
        }
        private static Bitmap Load(string name)
        {
            using var stream = typeof(IsekaiCraftsmanship).Assembly.GetManifestResourceStream(
                "OpticalSystemDesign.App.Assets.Themes.Isekai." + name)
                ?? throw new InvalidOperationException("Missing Isekai material: " + name);
            return new Bitmap(stream);
        }
    }
}

/// <summary>A paint-only title background; native caption actions keep their own hit testing.</summary>
internal sealed class IsekaiTitleMaterial : Control
{
    internal IsekaiTitleMaterial()
    {
        IsHitTestVisible = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        if (ThemeDrawingSurface.Material(this) is IsekaiDrawingMaterial)
            IsekaiCraftsmanship.DrawBar(context, new Rect(Bounds.Size), false);
    }
}
