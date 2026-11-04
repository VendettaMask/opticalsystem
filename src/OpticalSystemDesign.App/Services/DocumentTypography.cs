using System.Text;
using Avalonia.Media;
using SkiaSharp;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.App.Services;

/// <summary>A captured font policy shared by all portable document/rendering backends.</summary>
internal sealed class DocumentTypography
{
    private readonly Dictionary<ProductFontRole, string[]> _families;
    internal DocumentTypography() => _families = DisplayTypography.CaptureDocumentFonts();
    internal string Primary(ProductFontRole role) => _families[role][0];
    internal string EastAsian => ProductFonts.DocumentFamilyName;
    internal IEnumerable<ProductFontAsset> Assets => new[] { ProductFontRole.Document, ProductFontRole.Code, ProductFontRole.Math }
        .SelectMany(role => _families[role])
        .Distinct(StringComparer.Ordinal).SelectMany(ProductFonts.Faces).DistinctBy(asset => asset.Path);

    internal SKFont Font(ProductFontRole role, float size, bool bold = false, bool italic = false)
    {
        var asset = ProductFonts.Face(Primary(role), bold, italic);
        return new SKFont(asset.Typeface, size) { Embolden = bold && asset.Weight < 600, SkewX = italic && !asset.Italic ? -.2f : 0 };
    }

    internal SKTypeface TypefaceFor(ProductFontRole role, int codepoint, bool bold = false, bool italic = false)
    {
        foreach (var name in _families[role])
        {
            var face = ProductFonts.Face(name, bold, italic).Typeface;
            if (face.GetGlyph(codepoint) != 0) return face;
        }
        // The final fallback is registered and bundled, never an implicit machine font.
        return ProductFonts.DocumentAsset.Typeface;
    }

    private IEnumerable<(string Text, SKTypeface Face)> Runs(string text, ProductFontRole role, SKFont font)
    {
        var builder = new StringBuilder(); SKTypeface? previous = null;
        foreach (var rune in text.EnumerateRunes())
        {
            var face = TypefaceFor(role, rune.Value, font.Embolden || font.Typeface?.FontStyle.Weight >= 600, font.SkewX != 0 || font.Typeface?.FontStyle.Slant != SKFontStyleSlant.Upright);
            if (previous is not null && face != previous) { yield return (builder.ToString(), previous); builder.Clear(); }
            builder.Append(rune.ToString()); previous = face;
        }
        if (previous is not null) yield return (builder.ToString(), previous);
    }

    private static SKFont RunFont(SKFont font, SKTypeface face) => new(face, font.Size)
    {
        Embolden = font.Embolden && face.FontStyle.Weight < 600, SkewX = font.SkewX,
        Subpixel = font.Subpixel, Edging = font.Edging, Hinting = font.Hinting
    };

    internal float Measure(string text, SKFont font, ProductFontRole role = ProductFontRole.Document) =>
        Runs(text, role, font).Sum(run => { using var selected = RunFont(font, run.Face); return selected.MeasureText(run.Text); });

    internal void Draw(SKCanvas canvas, string text, float x, float baseline, SKFont font, SKPaint paint,
        ProductFontRole role = ProductFontRole.Document, SKTextAlign alignment = SKTextAlign.Left)
    {
        var width = Measure(text, font, role);
        if (alignment == SKTextAlign.Center) x -= width / 2;
        else if (alignment == SKTextAlign.Right) x -= width;
        foreach (var item in Runs(text, role, font))
        {
            using var selected = RunFont(font, item.Face);
            var glyphs = selected.GetGlyphs(item.Text); var bytes = Encoding.UTF8.GetBytes(item.Text);
            using var builder = new SKTextBlobBuilder();
            var run = builder.AllocateTextRun(selected, glyphs.Length, 0, 0, bytes.Length);
            run.SetGlyphs(glyphs); run.SetText(bytes);
            var index = 0; var offset = 0;
            foreach (var rune in item.Text.EnumerateRunes()) { run.Clusters[index++] = (uint)offset; offset += rune.Utf8SequenceLength; }
            using var blob = builder.Build(); canvas.DrawText(blob, x, baseline, paint);
            x += selected.MeasureText(item.Text);
        }
    }

    internal string Css()
    {
        var css = new StringBuilder();
        foreach (var group in Assets.GroupBy(asset => asset.Family))
            css.Append("/* ").Append(group.Key).Append('\n').Append(ProductFonts.License(group.First()).Replace("*/", "* /", StringComparison.Ordinal)).Append(" */");
        foreach (var asset in Assets)
        {
            var type = asset.Path.EndsWith(".otf", StringComparison.Ordinal) ? "opentype" : "truetype";
            css.Append("@font-face{font-family:").Append(ProductFonts.CssNames([asset.Family]))
                .Append(";font-weight:").Append(asset.Weight).Append(";font-style:").Append(asset.Italic ? "italic" : "normal")
                .Append(";src:url(data:font/").Append(type == "opentype" ? "otf" : "ttf").Append(";base64,")
                .Append(Convert.ToBase64String(asset.Bytes)).Append(") format('").Append(type).Append("');}");
        }
        css.Append(":root{");
        foreach (var role in new[] { ProductFontRole.Document, ProductFontRole.Code, ProductFontRole.Math })
            css.Append("--osd-").Append(role.ToString().ToLowerInvariant()).Append(':').Append(ProductFonts.CssNames(_families[role])).Append(';');
        return css.Append('}').ToString();
    }
}
