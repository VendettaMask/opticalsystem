using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using SkiaSharp;

namespace OpticalSystemDesign.App.Controls;

/// <summary>Rasterizes sampled colour cells on the CPU, then submits one native image.</summary>
internal sealed class ScalarMapRasterOperation : ICustomDrawOperation
{
    private readonly double[] _xs, _ys;
    private readonly double[,] _values;
    private readonly double _xStep, _yStep, _minimum, _maximum;
    private readonly AnalysisColorMap _colorMap;
    private readonly ScientificColorMap _scientificMap;
    private readonly EdgeMode _edgeMode;
    private readonly object _gate = new();
    private readonly Dictionary<Color, IImmutableBrush> _brushes = new();
    private SKImage? _image;
    private RasterKey? _key;
    private bool _disposed;

    internal ScalarMapRasterOperation(double[] xs, double[] ys, double[,] values, Rect bounds,
        double xStep, double yStep, double minimum, double maximum, AnalysisColorMap colorMap, EdgeMode edgeMode)
    {
        _xs = xs; _ys = ys; _values = values; Bounds = bounds;
        _xStep = xStep; _yStep = yStep; _minimum = minimum; _maximum = maximum;
        _colorMap = colorMap; _edgeMode = edgeMode;
        _scientificMap = ScientificColorMaps.Current;
    }

    public Rect Bounds { get; }
    public bool HitTest(Point point) => Bounds.Contains(point);

    public bool Equals(ICustomDrawOperation? other) => other is ScalarMapRasterOperation map
        && ReferenceEquals(_xs, map._xs) && ReferenceEquals(_ys, map._ys)
        && ReferenceEquals(_values, map._values) && Bounds == map.Bounds
        && _xStep.Equals(map._xStep) && _yStep.Equals(map._yStep)
        && _minimum.Equals(map._minimum) && _maximum.Equals(map._maximum)
        && _colorMap == map._colorMap && _scientificMap == map._scientificMap && _edgeMode == map._edgeMode;

    public void Render(ImmediateDrawingContext context)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null)
            {
                VisitCells((brush, rect) => context.DrawRectangle(brush, null, rect));
                return;
            }
            using (var lease = feature.Lease())
            {
                if (lease.GrContext is not null)
                {
                    RenderRasterized(lease);
                    return;
                }
            }
            // Software drawing and original PNG/report pixels retain their
            // direct primitive path; only a hardware context needs batching.
            VisitCells((brush, rect) => context.DrawRectangle(brush, null, rect));
        }
    }

    internal void RenderRasterized(ISkiaSharpApiLease lease)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var canvas = lease.SkCanvas;
            var matrix = canvas.TotalMatrix;
            var transform = new Matrix(matrix.ScaleX, matrix.SkewY, matrix.SkewX, matrix.ScaleY,
                matrix.TransX, matrix.TransY);
            var clip = canvas.DeviceClipBounds;
            var visible = Bounds.TransformToAABB(transform).Intersect(new Rect(clip.Left, clip.Top, clip.Width, clip.Height));
            if (visible.Width <= 0 || visible.Height <= 0 || lease.CurrentOpacity <= 0) return;
            var left = (int)Math.Floor(visible.Left);
            var top = (int)Math.Floor(visible.Top);
            var width = checked((int)Math.Ceiling(visible.Right) - left);
            var height = checked((int)Math.Ceiling(visible.Bottom) - top);
            if (width > 8192 || height > 8192 || (long)width * height > 64L * 1024 * 1024)
                throw new InvalidOperationException("Scalar drawing exceeds the image pixel budget.");
            var key = new RasterKey(matrix, left, top, width, height, lease.CurrentOpacity);
            if (_key != key || _image is null)
            {
                var started = Stopwatch.GetTimestamp();
                var next = Rasterize(key, transform);
                _image?.Dispose();
                _image = next;
                _key = key;
                Trace.TraceInformation($"Scalar map CPU raster completed in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms. Grid={_xs.Length}x{_ys.Length} Pixels={width}x{height} Opacity={key.Opacity:G6}; native submission=one image.");
            }
            canvas.Save();
            try
            {
                // Cell opacity is already in the CPU image, including overlap
                // at cell edges. Keep the native clip and device transform.
                canvas.ResetMatrix();
                canvas.DrawImage(_image, left, top);
            }
            finally { canvas.Restore(); }
        }
    }

    private SKImage Rasterize(RasterKey key, Matrix transform)
    {
        var size = new PixelSize(key.Width, key.Height);
        using var target = new RenderTargetBitmap(size, new Vector(96, 96));
        // Pinned Avalonia.Skia uses a framebuffer for RenderTargetBitmap. This
        // deliberately avoids replaying the individual cells on the GPU.
        using (var drawing = target.CreateDrawingContext())
        using (drawing.PushTransform(transform * Matrix.CreateTranslation(-key.Left, -key.Top)))
        using (drawing.PushOpacity(key.Opacity))
        using (drawing.PushRenderOptions(new RenderOptions { EdgeMode = _edgeMode }))
        using (drawing.PushClip(Bounds))
            VisitCells((brush, rect) => drawing.DrawRectangle(brush, null, rect));
        using var pixels = new SKBitmap(new SKImageInfo(key.Width, key.Height,
            SKImageInfo.PlatformColorType, SKAlphaType.Premul));
        target.CopyPixels(new PixelRect(size), pixels.GetPixels(), pixels.ByteCount, pixels.RowBytes);
        pixels.SetImmutable();
        return SKImage.FromBitmap(pixels);
    }

    private void VisitCells(Action<IImmutableBrush, Rect> draw)
    {
        var xMinimum = _xs[0] - _xStep / 2;
        var xMaximum = _xs[^1] + _xStep / 2;
        var yMinimum = _ys[0] - _yStep / 2;
        var yMaximum = _ys[^1] + _yStep / 2;
        var range = Math.Max(1e-12, _maximum - _minimum);
        double MapX(double x) => Bounds.Left + (x - xMinimum) / Math.Max(1e-12, xMaximum - xMinimum) * Bounds.Width;
        double MapY(double y) => Bounds.Bottom - (y - yMinimum) / Math.Max(1e-12, yMaximum - yMinimum) * Bounds.Height;
        for (var row = 0; row < _ys.Length; row++)
            for (var column = 0; column < _xs.Length; column++)
            {
                var value = _values[row, column];
                if (!double.IsFinite(value)) continue;
                var normalized = (value - _minimum) / range;
                var color = _colorMap is AnalysisColorMap.Gray or AnalysisColorMap.InverseGray
                    ? AnalysisPlotControl.ColorFor(_colorMap, normalized)
                    : ScientificColorMaps.ColorFor(_scientificMap, normalized);
                if (!_brushes.TryGetValue(color, out var brush))
                {
                    if (_brushes.Count >= 4096) _brushes.Clear();
                    brush = new ImmutableSolidColorBrush(color);
                    _brushes.Add(color, brush);
                }
                var left = MapX(_xs[column] - _xStep / 2);
                var right = MapX(_xs[column] + _xStep / 2);
                var top = MapY(_ys[row] + _yStep / 2);
                var bottom = MapY(_ys[row] - _yStep / 2);
                draw(brush, new Rect(Math.Min(left, right), Math.Min(top, bottom),
                    Math.Max(1, Math.Abs(right - left) + .35), Math.Max(1, Math.Abs(bottom - top) + .35)));
            }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _image?.Dispose();
            _image = null;
            _brushes.Clear();
        }
    }

    private readonly record struct RasterKey(SKMatrix Matrix, int Left, int Top, int Width, int Height, double Opacity);
}
