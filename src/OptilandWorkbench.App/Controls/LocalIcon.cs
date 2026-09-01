using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.App.Controls;

public sealed class LocalIcon : Control
{
    public static readonly StyledProperty<string> IconNameProperty =
        AvaloniaProperty.Register<LocalIcon, string>(nameof(IconName), "circle-question-mark");

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<LocalIcon, IBrush?>(nameof(Stroke), Brushes.Black);

    public static readonly StyledProperty<double> StrokeWidthProperty =
        AvaloniaProperty.Register<LocalIcon, double>(nameof(StrokeWidth), 2);

    public static readonly StyledProperty<IBrush?> AccentStrokeProperty =
        AvaloniaProperty.Register<LocalIcon, IBrush?>(nameof(AccentStroke));

    static LocalIcon()
    {
        AffectsRender<LocalIcon>(
            IconNameProperty,
            StrokeProperty,
            StrokeWidthProperty,
            AccentStrokeProperty);
    }

    public string IconName
    {
        get => GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeWidth
    {
        get => GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    public IBrush? AccentStroke
    {
        get => GetValue(AccentStrokeProperty);
        set => SetValue(AccentStrokeProperty, value);
    }

    public LocalIcon()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(24, 24);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Stroke is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        if (!ThemeIconResolver.TryResolve(ActualThemeVariant, IconName, out var definition))
        {
            return;
        }

        var scale = Math.Min(Bounds.Width, Bounds.Height) / 24.0;
        var offsetX = (Bounds.Width - (24 * scale)) / 2.0;
        var offsetY = (Bounds.Height - (24 * scale)) / 2.0;
        var transform = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY);
        using var renderOptions = definition.UseAliasedEdges
            ? context.PushRenderOptions(new RenderOptions { EdgeMode = EdgeMode.Aliased })
            : default;
        using (context.PushTransform(transform))
        {
            var pen = new Pen(
                Stroke,
                Math.Max(0.5, StrokeWidth * definition.StrokeWidthScale),
                lineCap: definition.LineCap,
                lineJoin: definition.LineJoin);
            using (context.PushTransform(definition.ContentTransform))
            {
                foreach (var primitive in definition.Primitives)
                {
                    primitive.Draw(context, pen);
                }
            }

            if (AccentStroke is not null && definition.AccentPrimitives.Count > 0)
            {
                var accentPen = new Pen(
                    AccentStroke,
                    Math.Max(0.5, StrokeWidth * 0.78),
                    lineCap: PenLineCap.Square,
                    lineJoin: PenLineJoin.Miter);
                foreach (var primitive in definition.AccentPrimitives)
                {
                    primitive.Draw(context, accentPen);
                }
            }
        }
    }
}

public sealed class LocalIconLabel : StackPanel
{
    public LocalIconLabel(string iconName, string text, double iconSize = 16)
    {
        Orientation = Avalonia.Layout.Orientation.Horizontal;
        Spacing = 6;
        Children.Add(new LocalIcon
        {
            IconName = iconName,
            Width = iconSize,
            Height = iconSize,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        Children.Add(new TextBlock
        {
            Text = text,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
    }
}

public static class LocalIconLibrary
{
    private const string CatalogResourceName =
        "OptilandWorkbench.App.Assets.Icons.lucide-icon-nodes.json";
    private static readonly Lazy<IReadOnlyDictionary<string, IconDefinition>> Definitions = new(Load);

    public static IReadOnlyCollection<string> Names => Definitions.Value.Keys.ToArray();

    public static bool Contains(string iconName) => Definitions.Value.ContainsKey(iconName);

    internal static bool TryGetStandard(string iconName, out IconDefinition definition) =>
        Definitions.Value.TryGetValue(iconName, out definition!);

    private static IReadOnlyDictionary<string, IconDefinition> Load()
    {
        using var stream = OpenCatalog();
        if (stream is null)
        {
            return new Dictionary<string, IconDefinition>(StringComparer.OrdinalIgnoreCase);
        }

        using var document = JsonDocument.Parse(stream);
        var definitions = new Dictionary<string, IconDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var icon in document.RootElement.EnumerateObject())
        {
            var primitives = new List<IconPrimitive>();
            foreach (var node in icon.Value.EnumerateArray())
            {
                var tag = node[0].GetString();
                var attributes = node[1];
                var primitive = tag switch
                {
                    "path" => Path(attributes),
                    "circle" => Circle(attributes),
                    "ellipse" => Ellipse(attributes),
                    "line" => Line(attributes),
                    "rect" => Rectangle(attributes),
                    "polyline" => Polyline(attributes, close: false),
                    "polygon" => Polyline(attributes, close: true),
                    _ => null
                };
                if (primitive is not null)
                {
                    primitives.Add(primitive);
                }
            }

            definitions[icon.Name] = IconDefinition.Standard(primitives);
        }

        return definitions;
    }

    private static Stream? OpenCatalog()
    {
        var assembly = typeof(LocalIconLibrary).Assembly;
        var embedded = assembly.GetManifestResourceStream(CatalogResourceName);
        if (embedded is not null)
        {
            return embedded;
        }

        var assetUri = new Uri("avares://OptilandWorkbench.App/Assets/Icons/lucide-icon-nodes.json");
        if (AssetLoader.Exists(assetUri))
        {
            return AssetLoader.Open(assetUri);
        }

        var candidates = new[]
        {
            System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "lucide-icon-nodes.json"),
            System.IO.Path.Combine(
                Environment.CurrentDirectory,
                "src",
                "OptilandWorkbench.App",
                "Assets",
                "Icons",
                "lucide-icon-nodes.json")
        };
        var path = candidates.FirstOrDefault(File.Exists);
        return path is null ? null : File.OpenRead(path);
    }

    private static IconPrimitive Path(JsonElement attributes) =>
        new PathPrimitive(String(attributes, "d"));

    private static IconPrimitive Circle(JsonElement attributes) => new EllipsePrimitive(
        new Point(Number(attributes, "cx"), Number(attributes, "cy")),
        Number(attributes, "r"),
        Number(attributes, "r"));

    private static IconPrimitive Ellipse(JsonElement attributes) => new EllipsePrimitive(
        new Point(Number(attributes, "cx"), Number(attributes, "cy")),
        Number(attributes, "rx"),
        Number(attributes, "ry"));

    private static IconPrimitive Line(JsonElement attributes) => new LinePrimitive(
        new Point(Number(attributes, "x1"), Number(attributes, "y1")),
        new Point(Number(attributes, "x2"), Number(attributes, "y2")));

    private static IconPrimitive Rectangle(JsonElement attributes)
    {
        var width = Number(attributes, "width");
        var height = Number(attributes, "height");
        var radiusX = Number(attributes, "rx", 0);
        var radiusY = Number(attributes, "ry", radiusX);
        return new RectanglePrimitive(
            new Rect(Number(attributes, "x"), Number(attributes, "y"), width, height),
            radiusX,
            radiusY);
    }

    private static IconPrimitive Polyline(JsonElement attributes, bool close)
    {
        var values = String(attributes, "points")
            .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => double.Parse(value, CultureInfo.InvariantCulture))
            .ToArray();
        var points = new List<Point>(values.Length / 2);
        for (var index = 0; index + 1 < values.Length; index += 2)
        {
            points.Add(new Point(values[index], values[index + 1]));
        }

        return new PolylinePrimitive(points, close);
    }

    private static string String(JsonElement attributes, string name) =>
        attributes.GetProperty(name).GetString() ?? string.Empty;

    private static double Number(JsonElement attributes, string name, double fallback = 0) =>
        attributes.TryGetProperty(name, out var value)
            ? double.Parse(value.GetString() ?? fallback.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
            : fallback;
}

internal sealed record IconDefinition(
    IReadOnlyList<IconPrimitive> Primitives,
    IReadOnlyList<IconPrimitive> AccentPrimitives,
    Matrix ContentTransform,
    PenLineCap LineCap,
    PenLineJoin LineJoin,
    double StrokeWidthScale,
    bool UseAliasedEdges)
{
    public static IconDefinition Standard(IReadOnlyList<IconPrimitive> primitives) => new(
        primitives,
        Array.Empty<IconPrimitive>(),
        Matrix.Identity,
        PenLineCap.Round,
        PenLineJoin.Round,
        1,
        false);
}

internal interface IThemeIconPack
{
    string Id { get; }

    bool TryResolve(string iconName, out IconDefinition definition);
}

internal sealed class StandardThemeIconPack : IThemeIconPack
{
    public static StandardThemeIconPack Instance { get; } = new();

    public string Id => "StandardLucide";

    private StandardThemeIconPack()
    {
    }

    public bool TryResolve(string iconName, out IconDefinition definition) =>
        LocalIconLibrary.TryGetStandard(iconName, out definition!) ||
        LocalIconLibrary.TryGetStandard("circle-question-mark", out definition!);
}

internal static class ThemeIconResolver
{
    public static string PackId(Avalonia.Styling.ThemeVariant? variant) =>
        ThemeRegistry.FromActualVariant(variant).IconPack.Id;

    public static bool TryResolve(
        Avalonia.Styling.ThemeVariant? variant,
        string iconName,
        out IconDefinition definition) =>
        ThemeRegistry.FromActualVariant(variant).IconPack.TryResolve(iconName, out definition!);
}

internal abstract record IconPrimitive
{
    public abstract void Draw(DrawingContext context, Pen pen);
}

internal sealed record PathPrimitive(string PathData) : IconPrimitive
{
    private readonly Lazy<Geometry> _geometry = new(() => Geometry.Parse(PathData));

    public override void Draw(DrawingContext context, Pen pen) =>
        context.DrawGeometry(null, pen, _geometry.Value);
}

internal sealed record FilledPathPrimitive(string PathData) : IconPrimitive
{
    private readonly Lazy<Geometry> _geometry = new(() => Geometry.Parse(PathData));

    public override void Draw(DrawingContext context, Pen pen) =>
        context.DrawGeometry(pen.Brush, null, _geometry.Value);
}

internal sealed record LinePrimitive(Point Start, Point End) : IconPrimitive
{
    public override void Draw(DrawingContext context, Pen pen) => context.DrawLine(pen, Start, End);
}

internal sealed record EllipsePrimitive(Point Center, double RadiusX, double RadiusY) : IconPrimitive
{
    public override void Draw(DrawingContext context, Pen pen) =>
        context.DrawEllipse(null, pen, Center, RadiusX, RadiusY);
}

internal sealed record RectanglePrimitive(Rect Rect, double RadiusX, double RadiusY) : IconPrimitive
{
    public override void Draw(DrawingContext context, Pen pen) =>
        context.DrawRectangle(null, pen, new RoundedRect(Rect, RadiusX, RadiusY));
}

internal sealed record PolylinePrimitive(IReadOnlyList<Point> Points, bool Close) : IconPrimitive
{
    public override void Draw(DrawingContext context, Pen pen)
    {
        for (var index = 1; index < Points.Count; index++)
        {
            context.DrawLine(pen, Points[index - 1], Points[index]);
        }

        if (Close && Points.Count > 2)
        {
            context.DrawLine(pen, Points[^1], Points[0]);
        }
    }
}
