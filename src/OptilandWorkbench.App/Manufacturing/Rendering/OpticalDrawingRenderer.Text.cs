using System.Collections.Concurrent;
using OptilandWorkbench.Application.Contracts;
using SkiaSharp;

namespace OptilandWorkbench.App.Manufacturing;

internal static partial class OpticalDrawingRendererCore
{
    private static void DrawFittedText(
            SKCanvas canvas,
            string text,
            float x,
            float y,
            float maxWidth,
            float preferredSize,
            SKTextAlign alignment,
            bool bold = false)
    {
        var size = preferredSize;
        while (size > 4.8f)
        {
            if (MeasureText(text, size, bold) <= maxWidth)
            {
                DrawText(canvas, text, x, y, size, alignment, bold);
                return;
            }

            size -= 0.25f;
        }

        DrawText(canvas, text, x, y, 4.8f, alignment, bold);
    }

    private static void DrawText(
        SKCanvas canvas,
        string text,
        float x,
        float y,
        float size,
        SKTextAlign alignment,
        bool bold = false)
    {
        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true
        };
        var widths = text.Select(character => CharacterWidth(character, size, bold)).ToArray();
        var totalWidth = widths.Sum();
        var cursor = alignment switch
        {
            SKTextAlign.Center => x - (totalWidth / 2),
            SKTextAlign.Right => x - totalWidth,
            _ => x
        };
        for (var index = 0; index < text.Length; index++)
        {
            using var font = new SKFont(TypefaceFor(text[index]), size) { Embolden = bold };
            canvas.DrawText(text[index].ToString(), cursor, y, SKTextAlign.Left, font, paint);
            cursor += widths[index];
        }
    }

    private static float MeasureText(string text, float size, bool bold) =>
        text.Sum(character => CharacterWidth(character, size, bold));

    private static float CharacterWidth(char character, float size, bool bold)
    {
        using var font = new SKFont(TypefaceFor(character), size) { Embolden = bold };
        return font.MeasureText(character.ToString());
    }

    private static SKTypeface TypefaceFor(char character)
    {
        using var primary = new SKFont(ChineseTypeface.Value, 10);
        if (primary.ContainsGlyphs(character.ToString()))
        {
            return ChineseTypeface.Value;
        }

        return FallbackTypefaces.GetOrAdd(
            character,
            static codepoint => SKFontManager.Default.MatchCharacter(codepoint) ?? SKTypeface.Default);
    }

    private static SKPaint Stroke(SKColor color, float width) => new()
    {
        Color = color,
        StrokeWidth = width,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeCap = SKStrokeCap.Butt,
        StrokeJoin = SKStrokeJoin.Miter
    };

    private static string RadiusText(double radius) =>
        Math.Abs(radius) < 1e-12 || !double.IsFinite(radius)
            ? "平面"
            : radius.ToString("0.###");

    internal static string StandardDesignation(OpticalDrawingStandard standard) => standard switch
    {
        OpticalDrawingStandard.GbT13323_1991 => "GB/T 13323—1991 光学制图",
        OpticalDrawingStandard.GbT13323_2009 => "GB/T 13323—2009 光学制图",
        _ => "ISO 10110-1:2019 表格式"
    };

    private static SKTypeface ResolveChineseTypeface()
    {
        using var embedded = typeof(OpticalDrawingRenderer).Assembly.GetManifestResourceStream(
            "OptilandWorkbench.App.Assets.Fonts.NotoSansCJKsc-Regular.otf");
        if (embedded is not null && SKTypeface.FromStream(embedded) is { } bundled)
        {
            return bundled;
        }

        var families = SKFontManager.Default.FontFamilies.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var family in new[]
                 {
                     "PingFang SC",
                     "Microsoft YaHei",
                     "Noto Sans CJK SC",
                     "Source Han Sans SC",
                     "WenQuanYi Micro Hei",
                     "Arial Unicode MS"
                 })
        {
            if (families.Contains(family))
            {
                return SKTypeface.FromFamilyName(family);
            }
        }

        return SKTypeface.Default;
    }
}
