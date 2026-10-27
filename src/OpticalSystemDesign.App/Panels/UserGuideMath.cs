using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using OpticalSystemDesign.App.Services;
using SkiaSharp;

namespace OpticalSystemDesign.App.Panels;

/// <summary>Owned, pre-typeset formula outlines. The product has no TeX/JS runtime dependency.</summary>
internal static class UserGuideMath
{
    internal sealed record Formula(string Id, string Latex);
    internal sealed record Outline(string Path, Matrix3x2 Transform);
    internal sealed record Artwork(float Width, float Height, IReadOnlyList<Outline> Outlines);
    private const string Prefix = "OpticalSystemDesign.App.Assets.UserGuideMath.";
    private static readonly Lazy<IReadOnlyDictionary<string, Formula>> Definitions = new(() =>
    {
        using var stream = Open("formulas.json");
        return JsonSerializer.Deserialize<Formula[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!
            .ToDictionary(f => f.Id, StringComparer.Ordinal);
    });
    internal static IReadOnlyDictionary<string, Formula> All => Definitions.Value;
    internal static Stream Open(string name) => typeof(UserGuideMath).Assembly.GetManifestResourceStream(Prefix + name)
        ?? throw new InvalidDataException("Missing guide formula resource: " + name);

    internal static Artwork Load(string id)
    {
        if (!All.ContainsKey(id)) throw new InvalidDataException("Unknown guide formula: " + id);
        using var stream = Open(id + ".svg");
        var root = XDocument.Load(stream).Root ?? throw new InvalidDataException("Empty formula SVG.");
        var box = Numbers((string?)root.Attribute("viewBox") ?? "");
        if (box.Length != 4 || box[2] <= 0 || box[3] <= 0) throw new InvalidDataException("Invalid formula bounds.");
        var outlines = new List<Outline>();
        Visit(root, Matrix3x2.CreateTranslation(-box[0], -box[1]));
        return new(box[2], box[3], outlines);

        void Visit(XElement element, Matrix3x2 parent)
        {
            var matrix = Transform((string?)element.Attribute("transform")) * parent;
            switch (element.Name.LocalName)
            {
                case "svg":
                case "g":
                    foreach (var child in element.Elements()) Visit(child, matrix);
                    break;
                case "path": outlines.Add(new((string?)element.Attribute("d") ?? throw new InvalidDataException("Missing outline."), matrix)); break;
                case "rect":
                    var x = Number(element, "x"); var y = Number(element, "y");
                    var w = Number(element, "width"); var h = Number(element, "height");
                    outlines.Add(new(FormattableString.Invariant($"M{x},{y}h{w}v{h}h{-w}Z"), matrix)); break;
                case "title": case "desc": break;
                default: throw new InvalidDataException("Unsupported formula SVG element: " + element.Name.LocalName);
            }
        }
    }

    internal static void Draw(SKCanvas canvas, Artwork artwork, float x, float y, float emSize, SKColor color)
    {
        using var saved = new SKAutoCanvasRestore(canvas, true);
        canvas.Translate(x, y); canvas.Scale(emSize / 1000);
        using var paint = new SKPaint { IsAntialias = true, Color = color, Style = SKPaintStyle.Fill };
        foreach (var outline in artwork.Outlines)
        {
            using var path = SKPath.ParseSvgPathData(outline.Path) ?? throw new InvalidDataException("Invalid formula outline.");
            var m = outline.Transform;
            path.Transform(new SKMatrix(m.M11, m.M21, m.M31, m.M12, m.M22, m.M32, 0, 0, 1));
            canvas.DrawPath(path, paint);
        }
    }

    private static float Number(XElement element, string name) => float.Parse((string?)element.Attribute(name) ?? "0", CultureInfo.InvariantCulture);
    private static float[] Numbers(string text) => Regex.Matches(text, @"[-+]?(?:\d*\.)?\d+(?:[eE][-+]?\d+)?")
        .Select(m => float.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
    private static Matrix3x2 Transform(string? text)
    {
        var result = Matrix3x2.Identity;
        if (string.IsNullOrWhiteSpace(text)) return result;
        var matches = Regex.Matches(text, @"(translate|scale|matrix)\s*\(([^)]*)\)");
        if (matches.Count == 0) throw new InvalidDataException("Invalid formula transform.");
        foreach (Match match in matches)
        {
            var n = Numbers(match.Groups[2].Value);
            var next = match.Groups[1].Value switch
            {
                "translate" when n.Length is 1 or 2 => Matrix3x2.CreateTranslation(n[0], n.Length == 1 ? 0 : n[1]),
                "scale" when n.Length is 1 or 2 => Matrix3x2.CreateScale(n[0], n.Length == 1 ? n[0] : n[1]),
                "matrix" when n.Length == 6 => new Matrix3x2(n[0], n[1], n[2], n[3], n[4], n[5]),
                _ => throw new InvalidDataException("Unsupported formula transform.")
            };
            result = next * result;
        }
        return result;
    }
}

internal sealed class UserGuideMathView : Control
{
    public static readonly StyledProperty<IBrush> ForegroundProperty = AvaloniaProperty.Register<UserGuideMathView, IBrush>(nameof(Foreground), Brushes.Black);
    private readonly UserGuideMath.Artwork _artwork;
    private readonly (Geometry Shape, Matrix Matrix)[] _outlines;
    internal const double EmSize = 21;
    public IBrush Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    static UserGuideMathView() => AffectsRender<UserGuideMathView>(ForegroundProperty);
    internal UserGuideMathView(string id)
    {
        Name = "UserGuideTypesetFormula";
        FormulaId = id;
        _artwork = UserGuideMath.Load(id);
        _outlines = _artwork.Outlines.Select(o => (Geometry.Parse(o.Path), new Matrix(o.Transform.M11, o.Transform.M12,
            o.Transform.M21, o.Transform.M22, o.Transform.M31, o.Transform.M32))).ToArray();
        this.BindThemeResource(ForegroundProperty, ThemeResourceBindings.TextPrimary);
        AutomationProperties.SetName(this, UserGuideMath.All[id].Latex);
    }
    internal string FormulaId { get; }
    protected override Size MeasureOverride(Size availableSize) => new(_artwork.Width * EmSize / 1000, _artwork.Height * EmSize / 1000);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        using var scale = context.PushTransform(Matrix.CreateScale(EmSize / 1000, EmSize / 1000));
        foreach (var (shape, matrix) in _outlines)
        {
            using var transform = context.PushTransform(matrix);
            context.DrawGeometry(Foreground, null, shape);
        }
    }
}
