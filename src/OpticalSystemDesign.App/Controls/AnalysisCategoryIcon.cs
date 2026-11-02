using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.App.Controls;

/// <summary>Draws the approved analysis-category SVGs as native, cached vector geometry.</summary>
internal sealed class AnalysisCategoryIcon : Control
{
    private const double StrokeWidth = 1.65;
    private static readonly IReadOnlyDictionary<string, string> Assets = new Dictionary<string, string>
    {
        ["chart-scatter"] = "ray-spots",
        ["file-text"] = "engineering-report",
        ["chart-spline"] = "aberration",
        ["waves-horizontal"] = "wavefront",
        ["focus"] = "point-spread",
        ["chart-no-axes-combined"] = "mtf-transfer",
        ["chart-line"] = "rms-curve",
        ["circle-dot"] = "encircled-energy",
        ["image"] = "image-analysis"
    };
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<Part>>> Drawings = new(Load);
    private static readonly IBrush[] LightInk = BrushesFor("#8298a8", "#398ff0", "#28adaf");
    private static readonly IBrush[] DarkInk = BrushesFor("#9db1c0", "#63b2ff", "#54d0cd");

    internal string SemanticName { get; }
    internal static bool Supports(string name) => Assets.ContainsKey(name);

    internal AnalysisCategoryIcon(string semanticName)
    {
        if (!Supports(semanticName)) throw new ArgumentOutOfRangeException(nameof(semanticName));
        SemanticName = semanticName;
        IsHitTestVisible = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(32, 32);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 32;
        if (scale <= 0) return;
        var ink = ThemeRegistry.FromActualVariant(ActualThemeVariant).IsDarkLike ? DarkInk : LightInk;
        using var transform = context.PushTransform(Matrix.CreateScale(scale, scale)
            * Matrix.CreateTranslation((Bounds.Width - 32 * scale) / 2, (Bounds.Height - 32 * scale) / 2));
        foreach (var part in Drawings.Value[SemanticName])
        {
            using var opacity = context.PushOpacity(part.Opacity);
            var brush = ink[part.Ink];
            var pen = part.Filled ? null : new Pen(brush, StrokeWidth, part.Dash,
                lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawGeometry(part.Filled ? brush : null, pen, part.Geometry);
        }
    }

    private static IBrush[] BrushesFor(params string[] colors) =>
        colors.Select(color => (IBrush)new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse(color))).ToArray();

    private sealed record Part(Geometry Geometry, int Ink, bool Filled, double Opacity, IDashStyle? Dash);

    private static IReadOnlyDictionary<string, IReadOnlyList<Part>> Load()
    {
        var result = new Dictionary<string, IReadOnlyList<Part>>();
        foreach (var (semantic, asset) in Assets)
        {
            using var stream = typeof(AnalysisCategoryIcon).Assembly.GetManifestResourceStream(
                $"OpticalSystemDesign.App.Assets.Icons.Analysis.{asset}.svg")
                ?? throw new InvalidOperationException($"Missing analysis artwork: {asset}");
            var root = XDocument.Load(stream).Root!;
            if ((string?)root.Attribute("viewBox") != "0 0 32 32")
                throw new InvalidOperationException($"Unexpected analysis artwork bounds: {asset}");
            var parts = new List<Part>();
            Read(root, 0, false, 1, parts);
            result[semantic] = parts;
        }
        return result;
    }

    // The authored asset subset is deliberately small; do not silently ignore new SVG primitives.
    private static void Read(XElement element, int ink, bool filled, double opacity, List<Part> parts)
    {
        var role = (string?)element.Attribute("class");
        (ink, filled) = role switch
        {
            "structure" => (0, false), "signal" => (1, false), "secondary" => (2, false),
            "signal-fill" => (1, true), "secondary-fill" => (2, true), null or "glyph" => (ink, filled),
            _ => throw new InvalidOperationException($"Unsupported analysis SVG role: {role}")
        };
        opacity *= Number(element, "opacity", 1);
        Geometry geometry;
        switch (element.Name.LocalName)
        {
            case "title": case "style": return;
            case "svg": case "g":
                foreach (var child in element.Elements()) Read(child, ink, filled, opacity, parts);
                return;
            case "path": geometry = Geometry.Parse((string)element.Attribute("d")!); break;
            case "circle":
                geometry = new EllipseGeometry { Center = new Point(Number(element, "cx"), Number(element, "cy")),
                    RadiusX = Number(element, "r"), RadiusY = Number(element, "r") }; break;
            case "ellipse":
                geometry = new EllipseGeometry { Center = new Point(Number(element, "cx"), Number(element, "cy")),
                    RadiusX = Number(element, "rx"), RadiusY = Number(element, "ry") }; break;
            case "rect":
                geometry = new RectangleGeometry(new Rect(Number(element, "x"), Number(element, "y"),
                    Number(element, "width"), Number(element, "height")), Number(element, "rx"),
                    Number(element, "ry", Number(element, "rx"))); break;
            default: throw new InvalidOperationException($"Unsupported analysis SVG element: {element.Name.LocalName}");
        }
        var dash = (string?)element.Attribute("stroke-dasharray");
        parts.Add(new Part(geometry, ink, filled, opacity, dash is null ? null : new DashStyle(
            dash.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => double.Parse(value, CultureInfo.InvariantCulture) / StrokeWidth), 0).ToImmutable()));
    }

    private static double Number(XElement element, string name, double fallback = 0) =>
        element.Attribute(name) is { } value ? double.Parse(value.Value, CultureInfo.InvariantCulture) : fallback;
}
