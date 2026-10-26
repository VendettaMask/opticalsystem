using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Controls;

public sealed class SeidelDiagramControl : Control, IReadOnlyChartAutomationSource
{
    public SeidelDiagramControl()
    {
        ClipToBounds = true;
        AutomationProperties.SetName(this, "Seidel 像差图");
        AutomationProperties.SetHelpText(this, "只读图表；可在分析结果的数据页查看完整系数表。");
    }

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

    private static readonly Color[] Colors =
    {
        Color.FromRgb(31, 119, 180), // SI: spherical
        Color.FromRgb(44, 160, 44), // SII: coma
        Color.FromRgb(214, 39, 40), // SIII: astigmatism
        Color.FromRgb(23, 190, 207), // SIV: Petzval
        Color.FromRgb(148, 103, 189), // SV: distortion
        Color.FromRgb(188, 189, 34), // axial color
        Color.FromRgb(255, 127, 14) // lateral color
    };

    private static readonly string[] Names =
    {
        "球差", "彗差", "像散", "场曲", "畸变", "轴上色差", "垂轴色差"
    };

    public AnalysisTableDto? Table { get; init; }

    public double MaximumAberration { get; init; } = 0.1;

    public double GridInterval { get; init; } = 0.01;

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new ReadOnlyChartAutomationPeer(this);

    string IReadOnlyChartAutomationSource.AutomationValue => Table is null || Table.Rows.Count == 0
        ? "Seidel 像差图；没有可用数据。"
        : $"Seidel 像差图；{Table.Rows.Count} 个表面，{Math.Max(0, Table.Columns.Count - 1)} 类像差系数；"
          + $"显示范围正负 {MaximumAberration:G6}。";

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(ThemeBrush(ThemeResourceBindings.PlotBackground, Brushes.White), null, Bounds);
        if (Table is null || Table.Rows.Count == 0 || Bounds.Width < 180 || Bounds.Height < 180)
        {
            return;
        }

        var maximum = Math.Max(1e-12, MaximumAberration);
        var interval = Math.Clamp(GridInterval, maximum / 100, maximum);
        var legendColumns = Math.Clamp((int)(Bounds.Width / DisplayTypography.Scale(82)), 1, Colors.Length);
        var legendRows = (int)Math.Ceiling(Colors.Length / (double)legendColumns);
        var legendRowHeight = DisplayTypography.Scale(48);
        var plot = new Rect(DisplayTypography.Scale(24), DisplayTypography.Scale(44),
            Math.Max(1, Bounds.Width - DisplayTypography.Scale(48)),
            Math.Max(1, Bounds.Height - DisplayTypography.Scale(64) - legendRows * legendRowHeight));
        var rowCount = Table.Rows.Count;
        var groupWidth = plot.Width / rowCount;
        var zeroY = plot.Center.Y;
        var gridPen = new Pen(new SolidColorBrush(ThemeColor(ThemeResourceBindings.PlotGrid, Color.FromRgb(212, 212, 212))), 1);
        var separatorPen = new Pen(ThemeBrush(ThemeResourceBindings.PlotAxis, new SolidColorBrush(Color.FromRgb(38, 38, 38))), 1);

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

            var label = CreateText(Table.Rows[rowIndex][0], groupWidth < 34 ? 8 : 10, ThemeBrush(ThemeResourceBindings.PlotText, Brushes.Black));
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

        var legendTop = plot.Bottom + DisplayTypography.Scale(10);
        var legendWidth = plot.Width / legendColumns;
        for (var index = 0; index < Colors.Length; index++)
        {
            var x = plot.Left + (index % legendColumns * legendWidth);
            var y = legendTop + index / legendColumns * legendRowHeight;
            context.DrawRectangle(
                new SolidColorBrush(Colors[index]),
                null,
                new Rect(x, y, legendWidth, DisplayTypography.Scale(16)));
            var label = CreateText(Names[index], 9, ThemeBrush(ThemeResourceBindings.PlotText, Brushes.Black));
            context.DrawText(
                label,
                new Point(x + ((legendWidth - label.Width) / 2), y + DisplayTypography.Scale(22)));
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
