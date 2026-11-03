using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App.Controls;

/// <summary>Canvas/frame layout only; deliberately contains no simulated optical data.</summary>
internal sealed class ExportCanvasPreview : Control
{
    private PixelSize _canvas = new(1920, 1080);
    private bool _preserve = true;
    public void SetCanvas(int width, int height, bool preserve)
    {
        _canvas = new(Math.Max(1, width), Math.Max(1, height)); _preserve = preserve; InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        var scale = Math.Min(Math.Max(1, Bounds.Width - 4) / _canvas.Width, Math.Max(1, Bounds.Height - 4) / _canvas.Height);
        var canvas = new Rect((Bounds.Width - _canvas.Width * scale) / 2, 2, _canvas.Width * scale, _canvas.Height * scale);
        var source = ImageExportResolution.FitFrame(new Size(1200, 900), _canvas, _preserve);
        var frame = new Rect(canvas.X + source.X * scale, canvas.Y + source.Y * scale, source.Width * scale, source.Height * scale);
        IBrush Brush(string key, IBrush fallback) => this.TryFindResource(key, ActualThemeVariant, out var resource) && resource is IBrush brush ? brush : fallback;
        var axis = Brush(ThemeResourceBindings.PlotAxis, Brushes.SteelBlue);
        context.DrawRectangle(Brush(ThemeResourceBindings.SettingsSurface, Brushes.WhiteSmoke), new Pen(Brush(ThemeResourceBindings.TextMuted, Brushes.Gray), .7), canvas);
        context.DrawRectangle(Brush(ThemeResourceBindings.PlotBackground, Brushes.White), new Pen(axis, 1), frame);
        var captionY = frame.Bottom - frame.Height * 160 / 902;
        context.DrawLine(new Pen(axis, .7), new Point(frame.Left, captionY), new Point(frame.Right, captionY));
    }
}
