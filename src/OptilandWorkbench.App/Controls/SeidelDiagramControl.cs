using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Controls;

public sealed class SeidelDiagramControl : Control
{
    private static readonly Color[] Colors =
    {
        Color.FromRgb(244, 24, 15),
        Color.FromRgb(15, 230, 26),
        Color.FromRgb(122, 124, 235),
        Color.FromRgb(16, 213, 215),
        Color.FromRgb(244, 236, 11),
        Color.FromRgb(151, 151, 91),
        Color.FromRgb(160, 199, 159)
    };

    private static readonly string[] Names =
    {
        "球差", "彗差", "像散", "场曲", "畸变", "轴上色差", "垂轴色差"
    };

    public AnalysisTableDto? Table { get; init; }

    public double MaximumAberration { get; init; } = 0.1;

    public double GridInterval { get; init; } = 0.01;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.White, null, Bounds);
        if (Table is null || Table.Rows.Count == 0 || Bounds.Width < 180 || Bounds.Height < 180)
        {
            return;
        }

        var maximum = Math.Max(1e-12, MaximumAberration);
        var interval = Math.Clamp(GridInterval, maximum / 100, maximum);
        var plot = new Rect(24, 44, Math.Max(1, Bounds.Width - 48), Math.Max(1, Bounds.Height - 112));
        var rowCount = Table.Rows.Count;
        var groupWidth = plot.Width / rowCount;
        var zeroY = plot.Center.Y;
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(212, 212, 212)), 1);
        var separatorPen = new Pen(new SolidColorBrush(Color.FromRgb(38, 38, 38)), 1);

        var gridCount = Math.Min(100, (int)Math.Floor(maximum / interval));
        for (var index = -gridCount; index <= gridCount; index++)
        {
            var y = zeroY - ((index * interval / maximum) * (plot.Height / 2));
            context.DrawLine(
                index == 0 ? separatorPen : gridPen,
                new Point(plot.Left, y),
                new Point(plot.Right, y));
        }

        for (var rowIndex = 0; rowIndex <= rowCount; rowIndex++)
        {
            var x = plot.Left + (rowIndex * groupWidth);
            context.DrawLine(separatorPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            if (rowIndex == rowCount)
            {
                continue;
            }

            var label = CreateText(Table.Rows[rowIndex][0], groupWidth < 34 ? 8 : 10, Brushes.Black);
            context.DrawText(
                label,
                new Point(x + ((groupWidth - label.Width) / 2), Math.Max(3, plot.Top - label.Height - 7)));
        }

        var usableBarWidth = Math.Max(1, groupWidth * 0.72);
        var barWidth = Math.Max(1, usableBarWidth / Colors.Length);
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var row = Table.Rows[rowIndex];
            var groupLeft = plot.Left + (rowIndex * groupWidth) + ((groupWidth - usableBarWidth) / 2);
            for (var coefficientIndex = 0; coefficientIndex < Colors.Length; coefficientIndex++)
            {
                if (!double.TryParse(
                        row.ElementAtOrDefault(coefficientIndex + 1),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var coefficient))
                {
                    continue;
                }

                var clipped = Math.Clamp(coefficient, -maximum, maximum);
                var valueY = zeroY - ((clipped / maximum) * (plot.Height / 2));
                var x = groupLeft + (coefficientIndex * barWidth);
                context.DrawRectangle(
                    new SolidColorBrush(Colors[coefficientIndex]),
                    null,
                    new Rect(
                        x,
                        Math.Min(zeroY, valueY),
                        Math.Max(1, barWidth - 0.5),
                        Math.Max(1, Math.Abs(zeroY - valueY))));
            }
        }

        var legendTop = plot.Bottom + 10;
        var legendWidth = plot.Width / Colors.Length;
        for (var index = 0; index < Colors.Length; index++)
        {
            var x = plot.Left + (index * legendWidth);
            context.DrawRectangle(
                new SolidColorBrush(Colors[index]),
                null,
                new Rect(x, legendTop, legendWidth, 21));
            var label = CreateText(Names[index], 9, Brushes.Black);
            context.DrawText(
                label,
                new Point(x + ((legendWidth - label.Width) / 2), legendTop + 27));
        }
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
