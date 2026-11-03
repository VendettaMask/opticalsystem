using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace OpticalSystemDesign.App.Services;

/// <summary>Frames a complete export page without reparenting or resizing its live content.</summary>
internal static class ExportImageFrame
{
    private const double BorderWidth = Controls.AnalysisImageFrame.LineWidth;

    internal static RenderTargetBitmap CaptureConfigured(Control content, Control? themeContext = null)
    {
        var canvas = new PixelSize(ImageExportResolution.PixelWidth, ImageExportResolution.PixelHeight);
        ValidatePixels(canvas.Width, canvas.Height);
        var frame = ImageExportResolution.FitFrame(content.Bounds.Size, canvas, ImageExportResolution.PreserveAspectRatio);
        if (frame.Width <= 0 || frame.Height <= 0) throw new InvalidOperationException("导出图像尚未显示");
        var scale = Math.Min(canvas.Width / (content.Bounds.Width + 2), canvas.Height / (content.Bounds.Height + 2));
        using var source = Capture(content, scale, themeContext);
        using var pixels = new WriteableBitmap(source.PixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = pixels.Lock()) source.CopyPixels(buffer);
        var owner = themeContext ?? content;
        var background = owner.TryFindResource(ThemeResourceBindings.PlotBackground, owner.ActualThemeVariant, out var resource)
            && resource is IBrush brush ? brush : Brushes.White;
        var page = new CanvasPage(pixels, background, new Rect(frame.X, frame.Y, frame.Width, frame.Height))
        { Width = canvas.Width, Height = canvas.Height, UseLayoutRounding = false };
        RenderOptions.SetBitmapInterpolationMode(page, ImageExportResolution.PreserveAspectRatio
            ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
        // Copy the source alpha as well; source-over would blend antialiased edge pixels twice.
        RenderOptions.SetBitmapBlendingMode(page, BitmapBlendingMode.Source);
        page.Measure(new Size(page.Width, page.Height)); page.Arrange(new Rect(page.DesiredSize));
        var bitmap = new RenderTargetBitmap(canvas, new Vector(96, 96));
        try { bitmap.Render(page); PngImageWriter.SetPrintDpi(bitmap, ImageExportResolution.Dpi); return bitmap; }
        catch { bitmap.Dispose(); throw; }
    }

    // Reports keep the original sheet aspect and physical figure size. Padding is an image-canvas option.
    internal static RenderTargetBitmap CaptureForReport(Control content, Control? themeContext = null)
    {
        // Print metadata must not change the raster pixels or the live drawing.
        var bitmap = CaptureCore(content, ImageExportResolution.Scale, themeContext,
            ImageExportResolution.ReferenceHeight / (content.Bounds.Height + 2 * BorderWidth));
        PngImageWriter.SetPrintDpi(bitmap, ImageExportResolution.Dpi);
        return bitmap;
    }

    internal static RenderTargetBitmap Capture(Control content, double scale = 1, Control? themeContext = null)
        => CaptureCore(content, scale, themeContext, 1);

    private static RenderTargetBitmap CaptureCore(Control content, double scale, Control? themeContext, double sourceScale)
    {
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        var size = content.Bounds.Size;
        if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0)
            throw new InvalidOperationException("导出图像尚未显示");
        if (!double.IsFinite(sourceScale) || sourceScale <= 0) throw new InvalidOperationException("导出图像尚未显示");
        var width = Math.Ceiling((size.Width + 2 * BorderWidth) * sourceScale * scale - 1e-8);
        var height = Math.Ceiling((size.Height + 2 * BorderWidth) * sourceScale * scale - 1e-8);
        ValidatePixels(width, height);

        var owner = themeContext ?? content;
        var brush = owner.TryFindResource(ThemeResourceBindings.PlotAxis, owner.ActualThemeVariant, out var resource)
            && resource is IBrush axis ? axis : new SolidColorBrush(Color.FromRgb(112, 128, 148));
        var page = new FramedPage(content, brush, sourceScale)
        {
            Width = width / scale,
            Height = height / scale,
            // The detached page has a logical layout scale of 1. Keep fractional
            // DIP bounds so its edge still reaches the last output pixel at 125% DPI.
            UseLayoutRounding = false
        };
        page.Measure(new Size(page.Width, page.Height));
        page.Arrange(new Rect(0, 0, page.Width, page.Height));
        var bitmap = new RenderTargetBitmap(new PixelSize((int)width, (int)height), new Vector(96 * scale, 96 * scale));
        try { bitmap.Render(page); return bitmap; }
        catch { bitmap.Dispose(); throw; }
    }

    private static void ValidatePixels(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0
            || width > 32_768 || height > 32_768 || width * height > 33_554_432)
            throw new InvalidOperationException("导出图片超过尺寸上限，请降低导出像素尺寸或减少同时显示的内容");
    }

    private sealed class CanvasPage(Bitmap source, IBrush background, Rect frame) : Control
    {
        public override void Render(DrawingContext context)
        {
            context.FillRectangle(background, new Rect(Bounds.Size));
            context.DrawImage(source, frame);
        }
    }

    private sealed class FramedPage(Control content, IBrush brush, double sourceScale) : Control
    {
        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(new VisualBrush(content)
            {
                Stretch = sourceScale == 1 ? Stretch.None : Stretch.Fill,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            }, null, new Rect(BorderWidth * sourceScale, BorderWidth * sourceScale,
                content.Bounds.Width * sourceScale, content.Bounds.Height * sourceScale));
            var stroke = BorderWidth * sourceScale;
            context.DrawRectangle(null, new Pen(brush, stroke),
                new Rect(stroke / 2, stroke / 2, Bounds.Width - stroke, Bounds.Height - stroke));
        }
    }
}
