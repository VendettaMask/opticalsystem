using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Laboratories;

public sealed class AssemblyReticleView : Control, IDisposable
{
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
        if (result is not null && result.RecordedRays > 0)
        {
            _sensorWidth = result.SensorWidth;
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
        var side = Math.Max(1, Math.Min(Bounds.Width - 68, Bounds.Height - 54));
        var area = new Rect((Bounds.Width - side) / 2 + 12, 12, side, side);
        context.FillRectangle(Brushes.Black, area);
        if (_bitmap is null)
        {
            context.DrawText(Text("选择一个可计算的位置以查看叉丝", Brushes.LightGray),
                new Point(area.Left + 14, area.Top + area.Height / 2));
            return;
        }
        context.DrawImage(_bitmap, new Rect(0, 0, _bitmap.Size.Width, _bitmap.Size.Height), area);
        var pen = new Pen(Brushes.Gray, 1);
        for (var i = 0; i <= 4; i++)
        {
            var x = area.Left + side * i / 4;
            var y = area.Top + side * i / 4;
            context.DrawLine(pen, new Point(x, area.Bottom), new Point(x, area.Bottom + 5));
            context.DrawLine(pen, new Point(area.Left - 5, y), new Point(area.Left, y));
            var horizontal = Text((_sensorWidth * (i / 4.0 - 0.5)).ToString("0.##", CultureInfo.InvariantCulture));
            var vertical = Text((_sensorWidth * (0.5 - i / 4.0)).ToString("0.##", CultureInfo.InvariantCulture));
            context.DrawText(horizontal, new Point(x - horizontal.Width / 2, area.Bottom + 8));
            context.DrawText(vertical, new Point(area.Left - vertical.Width - 8, y - vertical.Height / 2));
        }
        context.DrawText(Text("mm"), new Point(area.Right + 6, area.Bottom + 8));
    }

    private static FormattedText Text(string text, IBrush? brush = null) => new(text,
        CultureInfo.InvariantCulture, FlowDirection.LeftToRight, DisplayTypography.Typeface(), DisplayTypography.BodySmall, brush ?? Brushes.Gray);

    public void Dispose() => SetResult(null);
}
