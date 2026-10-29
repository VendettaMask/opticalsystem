using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace OpticalSystemDesign.App.Services;

/// <summary>Frames a complete export page without reparenting or resizing its live content.</summary>
internal static class ExportImageFrame
{
    private const double BorderWidth = 1;

    internal static RenderTargetBitmap Capture(Control content, double scale = 1, Control? themeContext = null)
    {
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        var size = content.Bounds.Size;
        if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0)
            throw new InvalidOperationException("导出图像尚未显示");
        var width = Math.Ceiling((size.Width + 2 * BorderWidth) * scale);
        var height = Math.Ceiling((size.Height + 2 * BorderWidth) * scale);
        if (!double.IsFinite(width) || !double.IsFinite(height) || width > 32_768 || height > 32_768
            || width * height > 33_554_432)
            throw new InvalidOperationException("导出图片超过尺寸上限，请缩小窗口或减少同时显示的内容");

        var owner = themeContext ?? content;
        var brush = owner.TryFindResource(ThemeResourceBindings.PlotAxis, owner.ActualThemeVariant, out var resource)
            && resource is IBrush axis ? axis : new SolidColorBrush(Color.FromRgb(112, 128, 148));
        var page = new FramedPage(content, brush)
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

    private sealed class FramedPage(Control content, IBrush brush) : Control
    {
        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(new VisualBrush(content)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            }, null, new Rect(BorderWidth, BorderWidth, content.Bounds.Width, content.Bounds.Height));
            context.DrawRectangle(null, new Pen(brush, BorderWidth),
                new Rect(BorderWidth / 2, BorderWidth / 2, Bounds.Width - BorderWidth, Bounds.Height - BorderWidth));
        }
    }
}
