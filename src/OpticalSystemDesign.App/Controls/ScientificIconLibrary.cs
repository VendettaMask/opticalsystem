using System.Collections.Concurrent;
using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.App.Controls;

/// <summary>Finite offline SVG catalog; geometry is loaded once on demand and shared across themes.</summary>
internal static class ScientificIconLibrary
{
    private const string Prefix = "OpticalSystemDesign.App.Assets.Icons.Scientific.";
    private const string ThemePrefix = "OpticalSystemDesign.App.Assets.IconThemes.";
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Resources = new(() =>
        typeof(ScientificIconLibrary).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal) && name.EndsWith(".svg", StringComparison.Ordinal))
            .ToDictionary(name => name[Prefix.Length..^4], name => name, StringComparer.OrdinalIgnoreCase));
    private static readonly ConcurrentDictionary<string, Lazy<IconDefinition>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<IReadOnlyDictionary<string, string>> ThemeResources = new(() =>
        typeof(ScientificIconLibrary).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ThemePrefix, StringComparison.Ordinal) && name.EndsWith(".svg", StringComparison.Ordinal))
            .ToDictionary(name => name[ThemePrefix.Length..^4], name => name, StringComparer.OrdinalIgnoreCase));

    internal static IReadOnlyCollection<string> Names => Resources.Value.Keys.ToArray();
    internal static bool Contains(string name) => Resources.Value.ContainsKey(name);
    internal static bool HasThemeDrawing(string theme, string name) => ThemeResources.Value.ContainsKey($"{theme}.{name}");
    internal static IReadOnlyCollection<string> ThemeNames(string theme) => ThemeResources.Value.Keys
        .Where(name => name.StartsWith(theme + ".", StringComparison.Ordinal)).Select(name => name[(theme.Length + 1)..]).ToArray();
    internal static IconDefinition Resolve(string name, string? theme = null)
    {
        var key = Contains(name) ? name : "circle-question-mark";
        var resource = theme is not null && ThemeResources.Value.TryGetValue($"{theme}.{key}", out var themed)
            ? themed : Resources.Value[key];
        return Cache.GetOrAdd(resource, static resource => new Lazy<IconDefinition>(() =>
        {
            using var stream = typeof(ScientificIconLibrary).Assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Missing scientific icon: {resource}");
            return IconDefinition.Standard(Array.Empty<IconPrimitive>()) with
            { Artwork = ScientificSvgArtwork.Read(stream) };
        })).Value;
    }
}

internal sealed class ScientificThemeIconPack(string theme) : IThemeIconPack
{
    public string Id { get; } = $"ScientificOutline:{theme}";
    public bool TryResolve(string iconName, out IconDefinition definition)
    {
        definition = ScientificIconLibrary.Resolve(iconName, theme);
        return true;
    }
}

/// <summary>The authored subset is intentionally explicit; unsupported artwork fails visibly in validation.</summary>
internal sealed class ScientificSvgArtwork
{
    internal const double SourceStrokeWidth = 1.65;
    private readonly IReadOnlyList<Part> _parts;
    private ScientificSvgArtwork(IReadOnlyList<Part> parts) => _parts = parts;
    internal int PartCount => _parts.Count;
    internal IReadOnlyCollection<int> Roles => _parts.Select(part => part.Ink).Distinct().ToArray();
    // Geometry objects own a dispatcher. Cache metadata globally, geometry on its drawing thread.
    private sealed record Part(ThreadLocal<Geometry> Geometry, int Ink, bool Filled, double Opacity, IDashStyle? Dash);
    internal void EnsureGeometry() { foreach (var part in _parts) _ = part.Geometry.Value; }

    internal void Draw(DrawingContext context, Rect bounds, ScientificIconPalette palette,
        double strokeWidth = SourceStrokeWidth)
    {
        var scale = Math.Min(bounds.Width, bounds.Height) / 32;
        if (scale <= 0) return;
        using var transform = context.PushTransform(Matrix.CreateScale(scale, scale)
            * Matrix.CreateTranslation((bounds.Width - 32 * scale) / 2, (bounds.Height - 32 * scale) / 2));
        foreach (var part in _parts)
        {
            using var opacity = context.PushOpacity(part.Opacity);
            var brush = palette.Brush(part.Ink, part.Filled);
            if (palette.Finish == ScientificIconFinish.SteelHud && !part.Filled && part.Ink == 1 && part.Dash is null)
            {
                // A bounded pair of translucent strokes lights only the glyph, never its label/host.
                for (var halo = 0; halo < 2; halo++)
                {
                    context.DrawGeometry(null, new Pen(halo == 0 ? palette.GlowOuter : palette.GlowInner,
                        strokeWidth + (halo == 0 ? 1.6 : .7),
                        lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), part.Geometry.Value!);
                }
            }
            var pen = part.Filled ? null : new Pen(brush, strokeWidth, part.Dash,
                lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawGeometry(part.Filled ? brush : null, pen, part.Geometry.Value!);
        }
    }

    internal static ScientificSvgArtwork Read(Stream stream)
    {
        var root = XDocument.Load(stream).Root ?? throw new InvalidOperationException("Empty scientific SVG");
        if ((string?)root.Attribute("viewBox") != "0 0 32 32")
            throw new InvalidOperationException("Scientific icons require a 32×32 source grid");
        var parts = new List<Part>();
        ReadPart(root, 0, false, 1, parts);
        if (parts.Count == 0) throw new InvalidOperationException("Scientific icon has no geometry");
        return new ScientificSvgArtwork(parts);
    }

    private static void ReadPart(XElement element, int ink, bool filled, double opacity, List<Part> parts)
    {
        var tag = element.Name.LocalName;
        if (tag is "title" or "style" or "defs") return;
        if (element.Attribute("transform") is not null)
            throw new InvalidOperationException("Scientific artwork must contain normalized coordinates");
        var role = (string?)element.Attribute("class");
        (ink, filled) = role switch
        {
            "structure" => (0, false), "signal" => (1, false), "secondary" => (2, false),
            "signal-fill" => (1, true), "secondary-fill" => (2, true), null or "glyph" => (ink, filled),
            "hot-fill" => (3, true),
            "dark" when tag == "svg" => (ink, filled),
            _ => throw new InvalidOperationException($"Unsupported scientific SVG role: {role}")
        };
        opacity *= Number(element, "opacity", 1);
        if (tag is "svg" or "g")
        {
            foreach (var child in element.Elements()) ReadPart(child, ink, filled, opacity, parts);
            return;
        }
        var geometry = new ThreadLocal<Geometry>(() => tag switch
        {
            "path" => Geometry.Parse((string)element.Attribute("d")!),
            "circle" => new EllipseGeometry { Center = new Point(Number(element, "cx"), Number(element, "cy")),
                RadiusX = Number(element, "r"), RadiusY = Number(element, "r") },
            "ellipse" => new EllipseGeometry { Center = new Point(Number(element, "cx"), Number(element, "cy")),
                RadiusX = Number(element, "rx"), RadiusY = Number(element, "ry") },
            "rect" => new RectangleGeometry(new Rect(Number(element, "x"), Number(element, "y"),
                Number(element, "width"), Number(element, "height")), Number(element, "rx"), Number(element, "ry", Number(element, "rx"))),
            "line" => Geometry.Parse(FormattableString.Invariant($"M{Number(element, "x1")} {Number(element, "y1")}L{Number(element, "x2")} {Number(element, "y2")}")),
            "polyline" or "polygon" => Polyline(element, tag == "polygon"),
            _ => throw new InvalidOperationException($"Unsupported scientific SVG primitive: {tag}")
        });
        var dash = (string?)element.Attribute("stroke-dasharray");
        parts.Add(new Part(geometry, ink, filled, opacity, dash is null ? null : new DashStyle(
            dash.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => double.Parse(value, CultureInfo.InvariantCulture) / SourceStrokeWidth), 0).ToImmutable()));
    }

    private static Geometry Polyline(XElement element, bool close)
    {
        var values = ((string)element.Attribute("points")!).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (values.Length < 4 || values.Length % 2 != 0) throw new InvalidOperationException("Invalid scientific polyline");
        return Geometry.Parse("M" + string.Join(" ", values.Take(2)) + "L" + string.Join(" ", values.Skip(2)) + (close ? "Z" : ""));
    }

    private static double Number(XElement element, string name, double fallback = 0) =>
        element.Attribute(name) is { } value ? double.Parse(value.Value, CultureInfo.InvariantCulture) : fallback;
}
