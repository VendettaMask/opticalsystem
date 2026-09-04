using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using SkiaSharp;

namespace OptilandWorkbench.App.Manufacturing;

public static class OpticalDrawingRenderer
{
    public static byte[] RenderPreview(OpticalDrawingSheet sheet, int width = 1500)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var (pageWidth, pageHeight) = OpticalDrawingRendererCore.PageDimensions(sheet.PageSize);
        var height = ValidatePreviewDimensions(width, pageWidth, pageHeight);
        using var surface = SKSurface.Create(new SKImageInfo(width, height))
            ?? throw new InvalidOperationException("Unable to allocate the optical drawing preview.");
        var canvas = surface.Canvas;
        canvas.Scale(width / pageWidth, height / pageHeight);
        OpticalDrawingRendererCore.Render(canvas, sheet, pageWidth, pageHeight);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static void ExportPdf(string path, OpticalDrawingSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var (pageWidth, pageHeight) = OpticalDrawingRendererCore.PageDimensions(sheet.PageSize);
        BoundedApplicationFile.WriteAtomic(
            path,
            BoundedApplicationFile.MaximumImageDataBytes,
            "Optical drawing PDF",
            stream =>
            {
                using var document = SKDocument.CreatePdf(stream)
                    ?? throw new InvalidOperationException("Unable to create the optical drawing PDF.");
                var canvas = document.BeginPage(pageWidth, pageHeight);
                OpticalDrawingRendererCore.Render(canvas, sheet, pageWidth, pageHeight);
                document.EndPage();
                document.Close();
            });
    }

    public static byte[] RenderSystemPreview(OpticalSystemDrawingSheet sheet, int width = 1500)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var (pageWidth, pageHeight) = OpticalDrawingRendererCore.PageDimensions(sheet.PageSize);
        var height = ValidatePreviewDimensions(width, pageWidth, pageHeight);
        using var surface = SKSurface.Create(new SKImageInfo(width, height))
            ?? throw new InvalidOperationException("Unable to allocate the optical system drawing preview.");
        var canvas = surface.Canvas;
        canvas.Scale(width / pageWidth, height / pageHeight);
        OpticalDrawingRendererCore.RenderSystem(canvas, sheet, pageWidth, pageHeight);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static void ExportSystemPdf(string path, OpticalSystemDrawingSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var (pageWidth, pageHeight) = OpticalDrawingRendererCore.PageDimensions(sheet.PageSize);
        BoundedApplicationFile.WriteAtomic(
            path,
            BoundedApplicationFile.MaximumImageDataBytes,
            "Optical system drawing PDF",
            stream =>
            {
                using var document = SKDocument.CreatePdf(stream)
                    ?? throw new InvalidOperationException("Unable to create the optical system drawing PDF.");
                var canvas = document.BeginPage(pageWidth, pageHeight);
                OpticalDrawingRendererCore.RenderSystem(canvas, sheet, pageWidth, pageHeight);
                document.EndPage();
                document.Close();
            });
    }

    public static (float Width, float Height) PageDimensions(OpticalDrawingPageSize pageSize)
    {
        return OpticalDrawingRendererCore.PageDimensions(pageSize);
    }

    public static string ScaleDesignation(OpticalDrawingSheet sheet)
    {
        return OpticalDrawingRendererCore.ScaleDesignation(sheet);
    }

    public static string StandardDesignation(OpticalDrawingStandard standard)
    {
        return OpticalDrawingRendererCore.StandardDesignation(standard);
    }

    internal static IReadOnlyList<double> SystemAirGaps(Scene2Dto scene)
    {
        return OpticalDrawingRendererCore.SystemAirGaps(scene);
    }

    internal static IReadOnlyList<float> OpticalGlassHatchHalfLengths(
        OpticalDrawingStandard standard)
    {
        return OpticalDrawingRendererCore.OpticalGlassHatchHalfLengths(standard);
    }

    internal static string CementedComponentLabel(int componentIndex)
    {
        return OpticalDrawingRendererCore.CementedComponentLabel(componentIndex);
    }

    internal static string RadiusDimensionText(double radius, double tolerance)
    {
        return OpticalDrawingRendererCore.RadiusDimensionText(radius, tolerance);
    }

    internal static string LaserDamageThresholdIndication(OpticalDrawingSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return OpticalDrawingRendererCore.LaserDamageThresholdIndication(sheet);
    }

    internal static IReadOnlyList<string> ValidateTemplateLayout(OpticalDrawingSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return OpticalDrawingRendererCore.ValidateTemplateLayout(sheet);
    }

    private static int ValidatePreviewDimensions(int width, float pageWidth, float pageHeight)
    {
        if (width is < 1 or > 4_096
            || !float.IsFinite(pageWidth)
            || !float.IsFinite(pageHeight)
            || pageWidth <= 0
            || pageHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        var heightValue = width * (double)pageHeight / pageWidth;
        if (!double.IsFinite(heightValue))
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }
        var height = Math.Max(1, checked((int)Math.Round(heightValue)));
        if (height > 4_096 || (long)width * height > 16_777_216)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Drawing preview cannot exceed 4096 pixels per side or 16,777,216 total pixels.");
        }

        return height;
    }
}
