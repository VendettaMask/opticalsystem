using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Laboratories;

public sealed class AssemblyReticleView : Control, IDisposable
{
    public static readonly StyledProperty<IBrush?> AxisBrushProperty =
        AvaloniaProperty.Register<AssemblyReticleView, IBrush?>(nameof(AxisBrush), Brushes.Gray);
    public IBrush? AxisBrush { get => GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }
    static AssemblyReticleView() => AffectsRender<AssemblyReticleView>(AxisBrushProperty);
    private WriteableBitmap? _bitmap;
    private double _sensorWidth;
    public AssemblyReticleResult? Result { get; private set; }

    public AssemblyReticleView()
    {
        ClipToBounds = true;
        AutomationProperties.SetName(this, "返回叉丝几何像");
        AutomationProperties.SetHelpText(this, "固定毫米刻度的只读探测面图像，像长、倍率和返回光线数列于下方。");
    }

    public void SetResult(AssemblyReticleResult? result)
    {
        _bitmap?.Dispose();
        _bitmap = null;
        Result = result;
        if (result is not null) _sensorWidth = result.SensorWidth;
        if (result is not null && result.RecordedRays > 0)
        {
            _bitmap = new WriteableBitmap(new PixelSize(result.Size, result.Size),
                new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            var maximum = result.Pixels.Max();
            using var pixels = _bitmap.Lock();
            var row = new byte[result.Size * 4];
            for (var y = 0; y < result.Size; y++)
            {
                for (var x = 0; x < result.Size; x++)
                {
                    var value = (byte)Math.Clamp(255 * Math.Sqrt(result.Pixels[y * result.Size + x] / maximum), 0, 255);
                    row[x * 4] = value;
                    row[x * 4 + 1] = value;
                    row[x * 4 + 2] = value;
                    row[x * 4 + 3] = 255;
                }
                Marshal.Copy(row, 0, pixels.Address + y * pixels.RowBytes, row.Length);
            }
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        // An uncomputed detector is an ordinary UI empty state, not a black image.
        if (Result is null) return;
        var horizontal = Enumerable.Range(0, 5).Select(i => Text((_sensorWidth * (i / 4.0 - 0.5)).ToString("0.##", CultureInfo.InvariantCulture), AxisBrush)).ToArray();
        var vertical = Enumerable.Range(0, 5).Select(i => Text((_sensorWidth * (0.5 - i / 4.0)).ToString("0.##", CultureInfo.InvariantCulture), AxisBrush)).ToArray();
        var unit = Text("mm", AxisBrush);
        var left = vertical.Max(t => t.Width) + 12; var right = horizontal.Max(t => t.Width) / 2 + 8;
        var top = unit.Height + 8; var bottom = horizontal.Max(t => t.Height) + 16;
        var side = Math.Min(Bounds.Width - left - right, Bounds.Height - top - bottom);
        if (side <= 0) return;
        var area = new Rect(left + (Bounds.Width - left - right - side) / 2,
            top + (Bounds.Height - top - bottom - side) / 2, side, side);
        context.FillRectangle(Brushes.Black, area);
        if (_bitmap is not null) context.DrawImage(_bitmap, new Rect(0, 0, _bitmap.Size.Width, _bitmap.Size.Height), area);
        var pen = new Pen(AxisBrush, 1);
        for (var i = 0; i <= 4; i++)
        {
            var x = area.Left + side * i / 4;
            var y = area.Top + side * i / 4;
            context.DrawLine(pen, new Point(x, area.Bottom), new Point(x, area.Bottom + 5));
            context.DrawLine(pen, new Point(area.Left - 5, y), new Point(area.Left, y));
            context.DrawText(horizontal[i], new Point(x - horizontal[i].Width / 2, area.Bottom + 8));
            context.DrawText(vertical[i], new Point(area.Left - vertical[i].Width - 8, y - vertical[i].Height / 2));
        }
        context.DrawText(unit, new Point(area.Right - unit.Width, area.Top - unit.Height - 6));
    }

    private static FormattedText Text(string text, IBrush? brush = null) => new(text,
        CultureInfo.InvariantCulture, FlowDirection.LeftToRight, DisplayTypography.Typeface(), DisplayTypography.BodySmall, brush ?? Brushes.Gray);

    public void Dispose() => SetResult(null);
}
