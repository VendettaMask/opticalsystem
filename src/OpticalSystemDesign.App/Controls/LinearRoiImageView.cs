using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.Analysis.Imaging;

namespace OpticalSystemDesign.App.Controls;

/// <summary>Selection overlay only. Raster fitting never changes the authoritative image or ROI coordinates.</summary>
public sealed class LinearRoiImageView : Control, IDisposable
{
    private WriteableBitmap? _bitmap;
    private int _width, _height;
    private Point? _start;
    private SfrRegion? _draft;
    private IReadOnlyList<LinearSfrRoi> _regions = [];
    private IReadOnlyList<LinearSfrRoiResult> _results = [];
    private string? _selected;
    public event Action<SfrRegion>? RegionDrawn;
    public LinearRoiImageView()
    {
        MinHeight = 240; Height = 420; Focusable = true;
        AutomationProperties.SetName(this, "完整线性图 ROI 选择；也可使用下方坐标输入");
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
    }

    public void SetImage(CameraImage? image)
    {
        _bitmap?.Dispose(); _bitmap = null; _width = image?.Width ?? 0; _height = image?.Height ?? 0; _draft = null; _start = null;
        if (image is not null)
        {
            _bitmap = new(new(image.Width, image.Height), new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using var buffer = _bitmap.Lock(); var row = new byte[image.Width * 4];
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var i = (y * image.Width + x) * 3; var j = x * 4;
                    row[j] = image.Rgb24[i + 2]; row[j + 1] = image.Rgb24[i + 1]; row[j + 2] = image.Rgb24[i]; row[j + 3] = 255;
                }
                Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, row.Length);
            }
        }
        InvalidateVisual();
    }

    public void SetRegions(IReadOnlyList<LinearSfrRoi> regions, string? selected, IReadOnlyList<LinearSfrRoiResult>? results = null)
    { _regions = regions.ToArray(); _selected = selected; _results = results?.ToArray() ?? []; InvalidateVisual(); }

    internal Rect ImageRectangle
    {
        get
        {
            if (_width <= 0 || _height <= 0) return default;
            var s = Math.Min(Bounds.Width / _width, Bounds.Height / _height);
            return new((Bounds.Width - _width * s) / 2, (Bounds.Height - _height * s) / 2, _width * s, _height * s);
        }
    }

    private Point? Pixel(Point p, bool clamp)
    {
        var rect = ImageRectangle;
        if (rect.Width <= 0 || rect.Height <= 0 || !clamp && !rect.Contains(p)) return null;
        return new(Math.Clamp((p.X - rect.X) / rect.Width * _width, 0, _width - .000001),
            Math.Clamp((p.Y - rect.Y) / rect.Height * _height, 0, _height - .000001));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Pixel(e.GetPosition(this), false) is not { } p) return;
        _start = p; _draft = null; e.Pointer.Capture(this); Focus(); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_start is not { } start || Pixel(e.GetPosition(this), true) is not { } end) return;
        var x = (int)Math.Floor(Math.Min(start.X, end.X)); var y = (int)Math.Floor(Math.Min(start.Y, end.Y));
        _draft = new(x, y, (int)Math.Floor(Math.Max(start.X, end.X)) - x + 1, (int)Math.Floor(Math.Max(start.Y, end.Y)) - y + 1);
        InvalidateVisual(); e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_start is null) return;
        var selected = _draft; _start = null; _draft = null; e.Pointer.Capture(null); InvalidateVisual();
        if (selected is not null) RegionDrawn?.Invoke(selected); e.Handled = true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    { base.OnPointerCaptureLost(e); _start = null; _draft = null; InvalidateVisual(); }

    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        if (_bitmap is null) return;
        var rect = ImageRectangle; if (rect.Width <= 0 || rect.Height <= 0) return;
        context.DrawImage(_bitmap, new Rect(0, 0, _width, _height), rect);
        using var clip = context.PushClip(rect);
        Rect Box(SfrRegion r) => new(rect.X + r.X * rect.Width / _width, rect.Y + r.Y * rect.Height / _height,
            r.Width * rect.Width / _width, r.Height * rect.Height / _height);
        foreach (var roi in _regions)
        {
            var result = _results.FirstOrDefault(r => r.Roi.Id == roi.Id);
            var color = result is { Status: not LinearSfrRoiStatus.Measured } ? Brushes.OrangeRed : Brushes.Cyan;
            context.DrawRectangle(null, new Pen(color, roi.Id == _selected ? 3 : 1.5), Box(roi.Region));
            if (result?.Edge is { } edge)
            {
                var m = edge.Measurement; var r = roi.Region;
                var horizontal = roi.Orientation == LinearEdgeOrientation.NearHorizontal;
                var u0 = horizontal ? r.X : r.Y; var u1 = u0 + (horizontal ? r.Width : r.Height) - 1;
                Point P(double u) => horizontal ? new(m.EdgeIntercept + m.EdgeSlope * u, u) : new(u, m.EdgeIntercept + m.EdgeSlope * u);
                // P stores row,column for both orientations; translate pixel centres into the fitted display rectangle.
                Point Screen(Point p) => new(rect.X + (p.Y + .5) * rect.Width / _width, rect.Y + (p.X + .5) * rect.Height / _height);
                context.DrawLine(new Pen(Brushes.Yellow, 1.5), Screen(P(u0)), Screen(P(u1)));
            }
        }
        if (_draft is { } draft) context.DrawRectangle(null, new Pen(Brushes.White, 2), Box(draft));
    }

    public void Dispose() => SetImage(null);
}
