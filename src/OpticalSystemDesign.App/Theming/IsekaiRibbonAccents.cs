using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace OpticalSystemDesign.App.Theming;

/// <summary>The approved quiet engraving, fitted only into measured vacant Ribbon space.</summary>
internal static class IsekaiRibbonAccents
{
    private static readonly ThreadLocal<Artwork> Art = new(() => new Artwork());
    private static readonly ImmutablePen Shadow = new(new ImmutableSolidColorBrush(Color.Parse("#110a06")), 1.7,
        lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private static readonly ImmutablePen Gold = new(new ImmutableSolidColorBrush(Color.Parse("#d6b77a")), .9,
        lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private static readonly ImmutablePen Tick = new(new ImmutableSolidColorBrush(Color.Parse("#57d6b77a")), .55);

    internal static Rect EngravingBounds(Rect vacant)
    {
        if (vacant.Width < 66 || vacant.Height < 18) return default;
        var source = Art.Value!.Bounds.Inflate(2);
        var scale = Math.Min(Math.Min(130 / source.Width, vacant.Width / source.Width), vacant.Height / source.Height);
        var width = source.Width * scale; var height = source.Height * scale;
        return new Rect(vacant.Right - width, vacant.Center.Y - height / 2, width, height);
    }

    internal static void Draw(DrawingContext context, Rect bounds, RibbonDecorationLayout layout)
    {
        var target = EngravingBounds(layout.VacantEnd);
        if (target.Width > 0)
        {
            var art = Art.Value!; var source = art.Bounds.Inflate(2); var scale = target.Width / source.Width;
            using var clip = context.PushClip(layout.VacantEnd);
            using var transform = context.PushTransform(Matrix.CreateTranslation(-source.X, -source.Y)
                * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(target.X, target.Y));
            using (context.PushOpacity(.48))
            using (context.PushTransform(Matrix.CreateTranslation(0, 1)))
                foreach (var geometry in art.Geometry) context.DrawGeometry(null, Shadow, geometry);
            using (context.PushOpacity(.34))
                foreach (var geometry in art.Geometry) context.DrawGeometry(null, Gold, geometry);
        }
        var tab = layout.SelectedTab.Deflate(3);
        if (tab.Width > 12 && tab.Height > 12)
        {
            const double size = 3;
            foreach (var (point, x, y) in new[] { (tab.TopLeft, 1, 1), (tab.TopRight, -1, 1),
                (tab.BottomLeft, 1, -1), (tab.BottomRight, -1, -1) })
            {
                context.DrawLine(Tick, point, point + new Vector(x * size, 0));
                context.DrawLine(Tick, point, point + new Vector(0, y * size));
            }
        }
        // The bottom ticks stay inside actual unused page padding, not over a button.
        if (layout.FooterGap >= 3)
        {
            var height = Math.Min(5, layout.FooterGap - 2);
            foreach (var (x, direction) in new[] { (5d, 1), (bounds.Right - 5, -1) })
            {
                var point = new Point(x, bounds.Bottom - 1.5);
                context.DrawLine(Tick, point, point + new Vector(direction * 6, 0));
                context.DrawLine(Tick, point, point + new Vector(0, -height));
            }
        }
    }

    private sealed class Artwork
    {
        internal readonly Geometry[] Geometry;
        internal readonly Rect Bounds;
        internal Artwork()
        {
            using var stream = typeof(IsekaiRibbonAccents).Assembly.GetManifestResourceStream(
                "OpticalSystemDesign.App.Assets.Themes.Isekai.ribbon-small-accents.svg")
                ?? throw new InvalidOperationException("Missing approved Ribbon engraving");
            var root = XDocument.Load(stream).Root!;
            Geometry = root.Descendants().Where(e => e.Name.LocalName == "g"
                && (string?)e.Attribute("id") is "botanical" or "astrolabe")
                .SelectMany(g => g.Elements()).Select(element => element.Name.LocalName switch
                {
                    "path" => Avalonia.Media.Geometry.Parse((string)element.Attribute("d")!),
                    "circle" => (Geometry)new EllipseGeometry { Center = new Point(Number(element, "cx"), Number(element, "cy")),
                        RadiusX = Number(element, "r"), RadiusY = Number(element, "r") },
                    _ => throw new InvalidOperationException("Unsupported Ribbon engraving primitive")
                }).ToArray();
            Bounds = Geometry.Select(g => g.Bounds).Aggregate((a, b) => a.Union(b));
        }
        private static double Number(XElement e, string name) => double.Parse((string)e.Attribute(name)!, CultureInfo.InvariantCulture);
    }
}
