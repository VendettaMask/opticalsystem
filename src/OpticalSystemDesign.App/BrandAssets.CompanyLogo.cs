using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;
using SkiaSharp;

namespace OpticalSystemDesign.App;

internal static partial class BrandAssets
{
    private const string CompanyLogoResourceName = "OpticalSystemDesign.App.Assets.Brand.CompanyLogo.svg";
    private static readonly Lazy<LogoArtwork> CompanyLogoArtwork = new(ReadCompanyLogoArtwork);

    // Both renderers consume the same complete outlined SVG. No bitmap cache or
    // workstation font participates in UI scaling or engineering PDF output.
    internal static DrawingImage LoadCompanyLogoImage(Color? color = null)
    {
        var artwork = CompanyLogoArtwork.Value;
        return new DrawingImage(new DrawingGroup
        {
            Children =
            {
                new GeometryDrawing
                {
                    Brush = Brushes.Transparent,
                    Geometry = new RectangleGeometry(new Rect(0, 0, artwork.Width, artwork.Height))
                },
                new GeometryDrawing
                {
                    Brush = new SolidColorBrush(color ?? Color.Parse(artwork.Fill)),
                    Geometry = Geometry.Parse(artwork.PathData)
                }
            }
        });
    }

    internal static DrawingImage? TryLoadCompanyLogoImage(Color color)
    {
        try { return LoadCompanyLogoImage(color); }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains("IPlatformRenderInterface", StringComparison.Ordinal))
        {
            // Source-only/theme-contract tests may run before a render backend
            // exists. Real application resources are created after platform setup.
            return null;
        }
    }

    internal static void DrawCompanyLogo(SKCanvas canvas, SKRect bounds, SKColor? color = null)
    {
        var artwork = CompanyLogoArtwork.Value;
        using var path = SKPath.ParseSvgPathData(artwork.PathData)
            ?? throw new InvalidDataException("The company logo path is invalid.");
        using var paint = new SKPaint
        {
            Color = color ?? SKColor.Parse(artwork.Fill),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        var scale = Math.Min(bounds.Width / artwork.Width, bounds.Height / artwork.Height);
        using var saved = new SKAutoCanvasRestore(canvas, true);
        canvas.Translate(bounds.MidX - artwork.Width * scale / 2, bounds.MidY - artwork.Height * scale / 2);
        canvas.Scale(scale);
        canvas.DrawPath(path, paint);
    }

    internal static Stream OpenCompanyLogoStream() =>
        typeof(BrandAssets).Assembly.GetManifestResourceStream(CompanyLogoResourceName)
        ?? throw new InvalidOperationException($"Embedded brand asset '{CompanyLogoResourceName}' was not found.");

    private static LogoArtwork ReadCompanyLogoArtwork()
    {
        using var stream = OpenCompanyLogoStream();
        var root = XDocument.Load(stream).Root
            ?? throw new InvalidDataException("The company logo SVG is empty.");
        XNamespace svg = "http://www.w3.org/2000/svg";
        // This is a deliberately small contract for our owned, fixed artwork,
        // not a general SVG importer. Reject unsupported content explicitly.
        if (root.Name != svg + "svg" || root.Elements().Any(element =>
            element.Name != svg + "path" && element.Name != svg + "title" && element.Name != svg + "desc"))
            throw new InvalidDataException("The company logo must contain outlined vector artwork only.");
        var path = root.Elements(svg + "path").Single();
        var viewBox = ((string?)root.Attribute("viewBox") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => float.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        if (viewBox.Length != 4 || viewBox.Any(value => !float.IsFinite(value))
            || viewBox[0] != 0 || viewBox[1] != 0 || viewBox[2] <= 0 || viewBox[3] <= 0
            || path.Attributes().Any(attribute => attribute.Name != "d" && attribute.Name != "fill"))
            throw new InvalidDataException("The company logo viewBox or path attributes are unsupported.");
        return new LogoArtwork(viewBox[2], viewBox[3],
            (string?)path.Attribute("d") ?? throw new InvalidDataException("The company logo path is missing."),
            (string?)path.Attribute("fill") ?? "#0B1F3F");
    }

    private sealed record LogoArtwork(float Width, float Height, string PathData, string Fill);
}
