using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Controls;

public sealed class FullFieldAberrationControl : Control
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

    public double XFieldWidth { get; init; } = 1;

    public double YFieldWidth { get; init; } = 1;

    public string DisplayAs { get; init; } = "图标";

    public string DisplayMode { get; init; } = "绝对值";

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(ThemeBrush(ThemeResourceBindings.PlotBackground, Brushes.White), null, Bounds);
        if (Series is null || Series.Points.Count == 0 || Bounds.Width < 200 || Bounds.Height < 180)
        {
            return;
        }

        var plot = new Rect(82, 30, Math.Max(1, Bounds.Width - 112), Math.Max(1, Bounds.Height - 102));
        var yHalf = Math.Max(1e-9, YFieldWidth * 1.1);
        var xHalf = Math.Max(XFieldWidth * 1.1, yHalf * plot.Width / plot.Height);
        var gridPen = new Pen(new SolidColorBrush(ThemeColor(ThemeResourceBindings.PlotGrid, Color.FromRgb(224, 224, 224))), 1);
        var axisPen = new Pen(ThemeBrush(ThemeResourceBindings.PlotAxis, new SolidColorBrush(Color.FromRgb(35, 35, 35))), 1);
        const int tickCount = 6;

        double MapX(double x) => plot.Left + ((x + xHalf) / (2 * xHalf) * plot.Width);
        double MapY(double y) => plot.Bottom - ((y + yHalf) / (2 * yHalf) * plot.Height);

        for (var index = 0; index <= tickCount; index++)
        {
            var fraction = index / (double)tickCount;
            var x = plot.Left + (fraction * plot.Width);
            var y = plot.Bottom - (fraction * plot.Height);
            context.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));

            var xValue = -xHalf + (2 * xHalf * fraction);
            var xText = CreateText(FormatTick(xValue), 9.5, ThemeBrush(ThemeResourceBindings.PlotText, Brushes.Black));
            context.DrawText(xText, new Point(x - (xText.Width / 2), plot.Bottom + 7));
            var yValue = -yHalf + (2 * yHalf * fraction);
            var yText = CreateText(FormatTick(yValue), 9.5, ThemeBrush(ThemeResourceBindings.PlotText, Brushes.Black));
            context.DrawText(yText, new Point(plot.Left - yText.Width - 8, y - (yText.Height / 2)));
        }

        context.DrawRectangle(null, axisPen, plot);
        var xLabel = CreateText(Series.XAxisLabel, 12.5, ThemeBrush(ThemeResourceBindings.PlotText, Brushes.Black));
        context.DrawText(xLabel, new Point(plot.Center.X - (xLabel.Width / 2), plot.Bottom + 33));
        var yLabel = CreateText(Series.YAxisLabel, 12.5, ThemeBrush(ThemeResourceBindings.PlotText, Brushes.Black));
        var yCenter = new Point(20, plot.Center.Y);
        using (context.PushTransform(Matrix.CreateRotation(-Math.PI / 2, yCenter)))
        {
            context.DrawText(
                yLabel,
                new Point(yCenter.X - (yLabel.Width / 2), yCenter.Y - (yLabel.Height / 2)));
        }

        var values = Series.Points
            .Where(point => point.Value.HasValue && double.IsFinite(point.Value.Value))
            .Select(point => point.Value!.Value)
            .ToArray();
        var maximumMagnitude = values.Select(Math.Abs).DefaultIfEmpty(1).Max();
        var minimum = values.DefaultIfEmpty(0).Min();
        var maximum = values.DefaultIfEmpty(0).Max();
        var sampleStepX = EstimateStep(Series.Points.Select(point => point.X), XFieldWidth);
        var sampleStepY = EstimateStep(Series.Points.Select(point => point.Y), YFieldWidth);
        var maximumRadius = Math.Max(
            3,
            Math.Min(
                Math.Abs(MapX(sampleStepX) - MapX(0)),
                Math.Abs(MapY(sampleStepY) - MapY(0))) * 0.47);
        foreach (var point in Series.Points)
        {
            var value = point.Value ?? 0;
            var normalized = maximumMagnitude <= 1e-15
                ? 0
                : Math.Sqrt(Math.Abs(value) / maximumMagnitude);
            var radius = 3 + (normalized * Math.Max(0, maximumRadius - 3));
            var center = new Point(MapX(point.X), MapY(point.Y));
            if (DisplayAs.Contains("颜色", StringComparison.Ordinal))
            {
                var colorFraction = Math.Abs(maximum - minimum) <= 1e-15
                    ? 0.5
                    : (value - minimum) / (maximum - minimum);
                var color = HeatColor(colorFraction);
                context.DrawEllipse(new SolidColorBrush(color), axisPen, center, radius, radius);
            }
            else
            {
                var pen = DisplayMode.Contains("带符号", StringComparison.Ordinal)
                    ? new Pen(new SolidColorBrush(value < 0
                        ? Color.FromRgb(42, 101, 196)
                        : Color.FromRgb(196, 57, 42)), 1)
                    : axisPen;
                context.DrawEllipse(null, pen, center, radius, radius);
            }
        }
    }

    private static double EstimateStep(IEnumerable<double> values, double fallbackWidth)
    {
        var ordered = values.Distinct().Order().ToArray();
        return ordered.Length > 1
            ? ordered.Zip(ordered.Skip(1), (left, right) => right - left)
                .Where(step => step > 1e-12)
                .DefaultIfEmpty(fallbackWidth)
                .Min()
            : fallbackWidth;
    }

    private static Color HeatColor(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return value < 0.5
            ? Color.FromRgb(
                (byte)(40 + (value * 2 * 190)),
                (byte)(90 + (value * 2 * 130)),
                235)
            : Color.FromRgb(
                235,
                (byte)(220 - ((value - 0.5) * 2 * 170)),
                (byte)(235 - ((value - 0.5) * 2 * 200)));
    }

    private static string FormatTick(double value)
    {
        return Math.Abs(value) < 1e-10 ? "0" : value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static FormattedText CreateText(string text, double size, IBrush brush)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            DisplayTypography.Typeface(),
            DisplayTypography.Scale(size),
            brush);
    }
}
