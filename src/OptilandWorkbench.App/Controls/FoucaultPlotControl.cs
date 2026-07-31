using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Controls;

public sealed class FoucaultPlotControl : Control
{
    private IBrush ThemeBrush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush
            ? brush
            : fallback;

    private Color ThemeColor(string key, Color fallback, byte? alpha = null)
    {
        var color = this.TryFindResource(key, ActualThemeVariant, out var value)
            && value is ISolidColorBrush brush
                ? brush.Color
                : fallback;
        return alpha.HasValue ? Color.FromArgb(alpha.Value, color.R, color.G, color.B) : color;
    }

    public AnalysisSeriesDto? Series { get; init; }

    public string DisplayAs { get; init; } = "灰度";

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(ThemeBrush(ThemeResourceBindings.PlotBackground, Brushes.White), null, Bounds);
        if (Series is null || Series.Points.Count == 0 || Bounds.Width < 240 || Bounds.Height < 210)
        {
            return;
        }

        var availableWidth = Math.Max(120, Bounds.Width - 185);
        var availableHeight = Math.Max(120, Bounds.Height - 105);
        var side = Math.Min(availableWidth, availableHeight);
        var plot = new Rect(
            Math.Max(62, (Bounds.Width - side - 100) / 2),
            34,
            side,
            side);
        var xs = Series.Points.Select(point => point.X).Distinct().Order().ToArray();
        var ys = Series.Points.Select(point => point.Y).Distinct().Order().ToArray();
        var stepX = xs.Length > 1 ? xs[1] - xs[0] : 2;
        var stepY = ys.Length > 1 ? ys[1] - ys[0] : 2;

        double MapX(double x) => plot.Left + (((x + 1) / 2) * plot.Width);
        double MapY(double y) => plot.Bottom - (((y + 1) / 2) * plot.Height);

        foreach (var point in Series.Points)
        {
            var value = Math.Clamp(point.Value ?? 0, 0, 1);
            var left = MapX(point.X - (stepX / 2));
            var right = MapX(point.X + (stepX / 2));
            var top = MapY(point.Y + (stepY / 2));
            var bottom = MapY(point.Y - (stepY / 2));
            context.DrawRectangle(
                new SolidColorBrush(DisplayColor(value)),
                null,
                new Rect(left, top, Math.Max(1, right - left + 0.5), Math.Max(1, bottom - top + 0.5)));
        }

        var axisPen = new Pen(ThemeBrush(ThemeResourceBindings.PlotAxis, new SolidColorBrush(Color.FromRgb(45, 45, 45))), 0.9);
        context.DrawRectangle(null, axisPen, plot);
        for (var index = 0; index <= 2; index++)
        {
            var value = -1 + index;
            var x = MapX(value);
            var y = MapY(value);
            var xText = Text(value.ToString("0.0", CultureInfo.InvariantCulture), 10, ThemeBrush(ThemeResourceBindings.PlotText, Brushes.Black));
            var yText = Text(value.ToString("0.0", CultureInfo.InvariantCulture), 10, ThemeBrush(ThemeResourceBindings.PlotText, Brushes.Black));
            context.DrawText(xText, new Point(x - (xText.Width / 2), plot.Bottom + 5));
            context.DrawText(yText, new Point(plot.Left - yText.Width - 7, y - (yText.Height / 2)));
        }

        var xLabel = Text("相对光瞳位置", 12, ThemeBrush(ThemeResourceBindings.PlotText, Brushes.Black));
        context.DrawText(xLabel, new Point(plot.Center.X - (xLabel.Width / 2), plot.Bottom + 30));
        var yLabel = Text("相对光瞳位置", 12, ThemeBrush(ThemeResourceBindings.PlotText, Brushes.Black));
        var yCenter = new Point(plot.Left - 48, plot.Center.Y);
        using (context.PushTransform(Matrix.CreateRotation(-Math.PI / 2, yCenter)))
        {
            context.DrawText(yLabel, new Point(
                yCenter.X - (yLabel.Width / 2),
                yCenter.Y - (yLabel.Height / 2)));
        }

        DrawColorBar(context, new Rect(plot.Right + 20, plot.Top + 26, 38, plot.Height - 76));
    }

    private void DrawColorBar(DrawingContext context, Rect bar)
    {
        const int strips = 100;
        for (var index = 0; index < strips; index++)
        {
            var fraction = index / (double)(strips - 1);
            var y = bar.Bottom - ((index + 1) * bar.Height / strips);
            context.DrawRectangle(
                new SolidColorBrush(DisplayColor(fraction)),
                null,
                new Rect(bar.Left, y, bar.Width, (bar.Height / strips) + 1));
        }

        context.DrawRectangle(null, new Pen(ThemeBrush(ThemeResourceBindings.PlotAxis, Brushes.Black), 0.7), bar);
        for (var index = 0; index <= 10; index++)
        {
            var fraction = index / 10.0;
            var label = Text(fraction.ToString("0.00", CultureInfo.InvariantCulture), 10, ThemeBrush(ThemeResourceBindings.PlotText, Brushes.Black));
            context.DrawText(
                label,
                new Point(bar.Right + 5, bar.Bottom - (fraction * bar.Height) - (label.Height / 2)));
        }
    }

    private Color DisplayColor(double value)
    {
        if (DisplayAs.Contains("灰", StringComparison.Ordinal))
        {
            var component = (byte)Math.Round((1 - value) * 255);
            return Color.FromRgb(component, component, component);
        }

        var red = Math.Clamp(1.5 - Math.Abs((4 * value) - 3), 0, 1);
        var green = Math.Clamp(1.5 - Math.Abs((4 * value) - 2), 0, 1);
        var blue = Math.Clamp(1.5 - Math.Abs((4 * value) - 1), 0, 1);
        return Color.FromRgb((byte)(red * 255), (byte)(green * 255), (byte)(blue * 255));
    }

    private static FormattedText Text(string value, double size, IBrush brush)
    {
        return new FormattedText(
            value,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            DisplayTypography.Typeface(),
            DisplayTypography.Scale(size),
            brush);
    }
}
