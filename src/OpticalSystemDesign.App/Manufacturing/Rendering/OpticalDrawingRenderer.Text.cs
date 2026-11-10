using System.Collections.Concurrent;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.App.Services;
using SkiaSharp;

namespace OpticalSystemDesign.App.Manufacturing;

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
            var typography = ActiveTypography.Value ?? new DocumentTypography();
            using var font = typography.Font(ProductFontRole.Engineering, size, bold);
            typography.Draw(canvas, text[index].ToString(), cursor, y, font, paint, ProductFontRole.Engineering);
            cursor += widths[index];
        }
    }

    private static float MeasureText(string text, float size, bool bold) =>
        text.Sum(character => CharacterWidth(character, size, bold));

    private static float CharacterWidth(char character, float size, bool bold)
    {
        var typography = ActiveTypography.Value ?? new DocumentTypography();
        using var font = typography.Font(ProductFontRole.Engineering, size, bold);
        return typography.Measure(character.ToString(), font, ProductFontRole.Engineering);
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
            ? UiLocalization.Text("平面")
            : radius.ToString("0.###");

    internal static string StandardDesignation(OpticalDrawingStandard standard) => standard switch
    {
        OpticalDrawingStandard.GbT13323_1991 => UiLocalization.Text("GB/T 13323—1991 光学制图"),
        _ => UiLocalization.Text("ISO 10110-1:2019 表格式")
    };

}
