using OptilandWorkbench.Application.Contracts;
using SkiaSharp;

namespace OptilandWorkbench.App.Manufacturing;

public static class OpticalDrawingRenderer
{
    public static byte[] RenderPreview(OpticalDrawingSheet sheet, int width = 1500)
    {
        var (pageWidth, pageHeight) = OpticalDrawingRendererCore.PageDimensions(sheet.PageSize);
        var height = Math.Max(1, (int)Math.Round(width * pageHeight / pageWidth));
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Scale(width / pageWidth, height / pageHeight);
        OpticalDrawingRendererCore.Render(canvas, sheet, pageWidth, pageHeight);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static void ExportPdf(string path, OpticalDrawingSheet sheet)
    {
        var (pageWidth, pageHeight) = OpticalDrawingRendererCore.PageDimensions(sheet.PageSize);
        using var stream = File.Create(path);
        using var document = SKDocument.CreatePdf(stream);
        var canvas = document.BeginPage(pageWidth, pageHeight);
        OpticalDrawingRendererCore.Render(canvas, sheet, pageWidth, pageHeight);
        document.EndPage();
        document.Close();
    }

    public static byte[] RenderSystemPreview(OpticalSystemDrawingSheet sheet, int width = 1500)
    {
        var (pageWidth, pageHeight) = OpticalDrawingRendererCore.PageDimensions(sheet.PageSize);
        var height = Math.Max(1, (int)Math.Round(width * pageHeight / pageWidth));
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Scale(width / pageWidth, height / pageHeight);
        OpticalDrawingRendererCore.RenderSystem(canvas, sheet, pageWidth, pageHeight);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static void ExportSystemPdf(string path, OpticalSystemDrawingSheet sheet)
    {
        var (pageWidth, pageHeight) = OpticalDrawingRendererCore.PageDimensions(sheet.PageSize);
        using var stream = File.Create(path);
        using var document = SKDocument.CreatePdf(stream);
        var canvas = document.BeginPage(pageWidth, pageHeight);
        OpticalDrawingRendererCore.RenderSystem(canvas, sheet, pageWidth, pageHeight);
        document.EndPage();
        document.Close();
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
}
