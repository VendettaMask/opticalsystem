using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.App.Services;
using AnalysisPoint = OptilandWorkbench.Application.Contracts.AnalysisPointDto;
using AnalysisSeries = OptilandWorkbench.Application.Contracts.AnalysisSeriesDto;
using AnalysisPlotOptions = OptilandWorkbench.Application.Contracts.AnalysisPlotOptionsDto;

namespace OptilandWorkbench.App.Controls;

public sealed class AnalysisPlotControl : Control
{
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(38, 38, 38));
    private static readonly IBrush TickBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));
    private static readonly IBrush BarBrush = new SolidColorBrush(Color.FromArgb(190, 31, 145, 94));
    private static readonly Pen AxisPen = new(new SolidColorBrush(Color.FromRgb(38, 38, 38)), 1);
    private static readonly Color[] Palette =
    {
        Color.FromRgb(31, 119, 180),
        Color.FromRgb(255, 127, 14),
        Color.FromRgb(44, 160, 44),
        Color.FromRgb(214, 39, 40),
        Color.FromRgb(148, 103, 189),
        Color.FromRgb(140, 86, 75),
        Color.FromRgb(227, 119, 194),
        Color.FromRgb(127, 127, 127),
        Color.FromRgb(188, 189, 34),
        Color.FromRgb(23, 190, 207),
        Color.FromRgb(20, 20, 20)
    };


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

    private Pen ThemePen(string key, Pen fallback) => new(
        ThemeBrush(key, fallback.Brush ?? Brushes.Black),
        fallback.Thickness,
        fallback.DashStyle,
        fallback.LineCap,
        fallback.LineJoin,
        fallback.MiterLimit);
    internal static Color SeriesColor(int colorIndex)
    {
        var normalizedIndex = colorIndex == int.MinValue ? 0 : Math.Abs(colorIndex);
        return Palette[normalizedIndex % Palette.Length];
    }

    internal static Color SeriesColor(AnalysisSeries series)
    {
        return TryGetWavelengthNanometers(series, out var wavelengthNanometers)
            ? SpectralColorMap.FromNanometers(wavelengthNanometers)
            : SeriesColor(series.ColorIndex);
    }

    internal static Color WavelengthColor(double wavelengthNanometers) =>
        SpectralColorMap.FromNanometers(wavelengthNanometers);

    internal static bool TryGetWavelengthNanometers(
        AnalysisSeries series,
        out double wavelengthNanometers)
    {
        const string wavelengthPrefix = "wavelength:";
        if (!string.IsNullOrWhiteSpace(series.LegendKey))
        {
            if (series.LegendKey.StartsWith(wavelengthPrefix, StringComparison.OrdinalIgnoreCase)
                && double.TryParse(
                    series.LegendKey[wavelengthPrefix.Length..],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var keyedMicrometers))
            {
                wavelengthNanometers = keyedMicrometers * 1000;
                return double.IsFinite(wavelengthNanometers) && wavelengthNanometers > 0;
            }

            wavelengthNanometers = 0;
            return false;
        }

        return TryParseWavelength(series.LegendLabel, out wavelengthNanometers)
            || TryParseWavelength(series.Name, out wavelengthNanometers);
    }

    private static bool TryParseWavelength(string text, out double wavelengthNanometers)
    {
        wavelengthNanometers = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var (unit, multiplier) in new[]
        {
            ("µm", 1000.0),
            ("μm", 1000.0),
            ("um", 1000.0),
            ("nm", 1.0)
        })
        {
            var unitIndex = text.IndexOf(unit, StringComparison.OrdinalIgnoreCase);
            if (unitIndex < 0)
            {
                continue;
            }

            var prefix = text[..unitIndex].TrimEnd();
            var start = prefix.Length;
            while (start > 0)
            {
                var character = prefix[start - 1];
                if (!char.IsDigit(character)
                    && character is not '.' and not ',' and not '+' and not '-' and not 'e' and not 'E')
                {
                    break;
                }

                start--;
            }

            var numberText = prefix[start..].Replace(',', '.');
            if (double.TryParse(
                numberText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var wavelength))
            {
                wavelengthNanometers = wavelength * multiplier;
                return double.IsFinite(wavelengthNanometers) && wavelengthNanometers > 0;
            }
        }

        return false;
    }

    private IReadOnlyList<AnalysisSeries> _series = Array.Empty<AnalysisSeries>();
    private AnalysisPlotOptions _plotOptions = new();
    private PlotViewport? _viewport;
    private Rect _lastPlot;
    private PlotViewport _lastRenderedViewport;
    private bool _panning;
    private Point _lastPointer;
    private Point? _hoverPointer;

    public AnalysisPlotControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public IReadOnlyList<AnalysisSeries> Series
    {
        get => _series;
        set
        {
            _series = value ?? Array.Empty<AnalysisSeries>();
            ResetView();
            InvalidateVisual();
        }
    }

    public AnalysisPlotOptions PlotOptions
    {
        get => _plotOptions;
        set
        {
            _plotOptions = value ?? new AnalysisPlotOptions();
            ResetView();
            InvalidateVisual();
        }
    }

    public void ResetView()
    {
        _viewport = null;
        _hoverPointer = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var position = e.GetPosition(this);
        var viewport = CurrentViewport();
        if (!_lastPlot.Contains(position) || viewport is null)
        {
            return;
        }

        var factor = Math.Pow(0.82, e.Delta.Y);
        var current = viewport.Value;
        var dataX = UnmapX(position.X, _lastPlot, current, PlotOptions.ReverseX);
        var dataY = UnmapY(position.Y, _lastPlot, current);
        _viewport = new PlotViewport(
            dataX - ((dataX - current.XMinimum) * factor),
            dataX + ((current.XMaximum - dataX) * factor),
            dataY - ((dataY - current.YMinimum) * factor),
            dataY + ((current.YMaximum - dataY) * factor));
        _hoverPointer = position;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed || !_lastPlot.Contains(point.Position))
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            ResetView();
            e.Handled = true;
            return;
        }

        Focus();
        _panning = true;
        _lastPointer = point.Position;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        _hoverPointer = _lastPlot.Contains(position) ? position : null;
        var viewport = CurrentViewport();
        if (_panning && viewport is not null)
        {
            var delta = position - _lastPointer;
            _lastPointer = position;
            var current = viewport.Value;
            var xShift = (PlotOptions.ReverseX ? 1 : -1)
                * (delta.X / Math.Max(1, _lastPlot.Width))
                * current.XSpan;
            var yShift = (delta.Y / Math.Max(1, _lastPlot.Height)) * current.YSpan;
            _viewport = new PlotViewport(
                current.XMinimum + xShift,
                current.XMaximum + xShift,
                current.YMinimum + yShift,
                current.YMaximum + yShift);
        }

        InvalidateVisual();
        e.Handled = _panning;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_panning)
        {
            return;
        }

        _panning = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!_panning)
        {
            _hoverPointer = null;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(ThemeBrush(ThemeResourceBindings.PlotBackground, BackgroundBrush), null, Bounds);
        var visibleSeries = NormalizeSeriesUnits(Series)
            .Select(item => (Series: item, Points: item.Points.Where(IsFinite).ToArray()))
            .Where(item => item.Points.Length > 0)
            .ToArray();
        if (visibleSeries.Length == 0 || Bounds.Width < 48 || Bounds.Height < 48)
        {
            return;
        }

        var compact = Bounds.Width < 160 || Bounds.Height < 140;
        var primarySeries = visibleSeries[0].Series;
        var primaryXAxisLabel = AnalysisAxisFormatting.FormatLabel(
            primarySeries.XAxisLabel,
            primarySeries.XQuantity,
            primarySeries.XUnit);
        var primaryYAxisLabel = AnalysisAxisFormatting.FormatLabel(
            primarySeries.YAxisLabel,
            primarySeries.YQuantity,
            primarySeries.YUnit);
        var compactWithoutTicks = CanUseMinimalAxisMargins(
            compact,
            PlotOptions.HideAxes,
            PlotOptions.HideTickLabels,
            primaryXAxisLabel,
            primaryYAxisLabel);
        var legendItems = visibleSeries.Where(item => !string.IsNullOrWhiteSpace(item.Series.Name)).ToArray();
        var legendBelow = !compact
            && PlotOptions.ShowLegend
            && PlotOptions.LegendBelow
            && legendItems.Length > 0;
        var legendWidth = !compact && !legendBelow && PlotOptions.ShowLegend && legendItems.Length > 0
            ? Math.Min(190, Bounds.Width * 0.28)
            : 0;
        var legendBelowHeight = legendBelow ? 32 : 0;
        var valueSeries = visibleSeries
            .Where(item => item.Series.Kind is AnalysisSeriesKind.Heatmap or AnalysisSeriesKind.ColoredLine)
            .Select(item => item.Series)
            .FirstOrDefault();
        var colorbarWidth = !compact
            && valueSeries is not null
            && !string.IsNullOrWhiteSpace(valueSeries.ValueLabel)
                ? 92
                : 0;
        var top = compactWithoutTicks
            ? string.IsNullOrWhiteSpace(PlotOptions.Title) ? 4.0 : 24.0
            : PlotOptions.HideAxes
                ? string.IsNullOrWhiteSpace(PlotOptions.Title) ? 8.0 : 38.0
                : string.IsNullOrWhiteSpace(PlotOptions.Title) ? 22.0 : 46.0;
        var left = compactWithoutTicks
            ? 4
            : PlotOptions.HideAxes ? 8 : PlotOptions.HideTickLabels ? 52 : 76;
        var bottom = compactWithoutTicks
            ? 4
            : (PlotOptions.HideAxes ? 8 : PlotOptions.HideTickLabels ? 46 : 62)
                + legendBelowHeight;
        var right = (compactWithoutTicks ? 4 : PlotOptions.HideAxes ? 8 : 20)
            + legendWidth
            + colorbarWidth;
        var plot = new Rect(
            left,
            top,
            Math.Max(1, Bounds.Width - left - right),
            Math.Max(1, Bounds.Height - top - bottom));

        var allPoints = visibleSeries.SelectMany(item => item.Points).ToArray();
        var xMin = PlotOptions.XMinimum ?? allPoints.Min(point => point.X);
        var xMax = PlotOptions.XMaximum ?? allPoints.Max(point => point.X);
        var yMin = PlotOptions.YMinimum ?? allPoints.Min(point => point.Y);
        var yMax = PlotOptions.YMaximum ?? allPoints.Max(point => point.Y);
        if (visibleSeries.Any(item => item.Series.Kind == AnalysisSeriesKind.Bar))
        {
            yMin = Math.Min(0, yMin);
            yMax = Math.Max(0, yMax);
        }

        ExpandRange(ref xMin, ref xMax, PlotOptions.XMinimum.HasValue, PlotOptions.XMaximum.HasValue);
        ExpandRange(ref yMin, ref yMax, PlotOptions.YMinimum.HasValue, PlotOptions.YMaximum.HasValue);
        if (PlotOptions.SymmetricX)
        {
            var limit = Math.Max(Math.Abs(xMin), Math.Abs(xMax));
            xMin = -limit;
            xMax = limit;
        }

        if (PlotOptions.EqualAspect)
        {
            MakeEqualAspect(plot, ref xMin, ref xMax, ref yMin, ref yMax);
        }

        if (_viewport is { } viewport && HasValidViewport(viewport))
        {
            xMin = viewport.XMinimum;
            xMax = viewport.XMaximum;
            yMin = viewport.YMinimum;
            yMax = viewport.YMaximum;
        }

        _lastPlot = plot;
        _lastRenderedViewport = new PlotViewport(xMin, xMax, yMin, yMax);

        double MapX(double value) => PlotOptions.ReverseX
            ? plot.Right - ((value - xMin) / (xMax - xMin) * plot.Width)
            : plot.Left + ((value - xMin) / (xMax - xMin) * plot.Width);
        double MapY(double value) => plot.Bottom - ((value - yMin) / (yMax - yMin) * plot.Height);

        DrawTitle(context, PlotOptions.Title, plot);
        using (context.PushClip(plot))
        {
            foreach (var item in visibleSeries.Where(item => item.Series.Kind is AnalysisSeriesKind.Heatmap or AnalysisSeriesKind.Raster))
            {
                DrawSeries(context, item.Series, item.Series.Points, plot, MapX, MapY, yMin, yMax);
            }
        }

        if (!PlotOptions.HideAxes)
        {
            DrawGridAndTicks(context, plot, xMin, xMax, yMin, yMax);
            DrawAxes(context, plot);
            DrawZeroLines(context, plot, xMin, xMax, yMin, yMax, MapX, MapY);
        }

        using (context.PushClip(plot))
        {
            foreach (var item in visibleSeries.Where(item => item.Series.Kind is not (AnalysisSeriesKind.Heatmap or AnalysisSeriesKind.Raster)))
            {
                DrawSeries(context, item.Series, item.Series.Points, plot, MapX, MapY, yMin, yMax);
            }

            if (PlotOptions.ShowPointLabels)
            {
                DrawPointLabels(context, visibleSeries, plot, MapX, MapY);
            }
        }

        if (!PlotOptions.HideAxes)
        {
            DrawAxisLabels(
                context,
                visibleSeries[0].Series,
                plot,
                PlotOptions.HideTickLabels);
        }
        if (colorbarWidth > 0 && valueSeries is not null)
        {
            DrawColorbar(context, valueSeries, plot);
        }

        if (legendWidth > 0)
        {
            DrawLegend(context, legendItems, plot);
        }
        else if (legendBelow)
        {
            DrawLegendBelow(context, legendItems, plot);
        }

        DrawInteractionOverlay(context, visibleSeries, plot, MapX, MapY);
    }

    private void DrawInteractionOverlay(
        DrawingContext context,
        IReadOnlyList<(AnalysisSeries Series, AnalysisPoint[] Points)> visibleSeries,
        Rect plot,
        Func<double, double> mapX,
        Func<double, double> mapY)
    {
        if (_hoverPointer is not Point pointer || !plot.Contains(pointer))
        {
            if (plot.Width < 320 || plot.Height < 180)
            {
                return;
            }

            var hint = CreateText("滚轮缩放 · 拖动平移 · 双击复位 · 悬停读数", 10.5, ThemeBrush(ThemeResourceBindings.PlotHint, new SolidColorBrush(Color.FromRgb(110, 110, 115))));
            context.DrawText(hint, new Point(plot.Right - hint.Width - 8, plot.Bottom - hint.Height - 6));
            return;
        }

        HoverSample? nearest = null;
        var nearestDistance = double.PositiveInfinity;
        foreach (var item in visibleSeries)
        {
            var stride = Math.Max(1, item.Points.Length / 5000);
            for (var index = 0; index < item.Points.Length; index += stride)
            {
                var point = item.Points[index];
                var screenPoint = new Point(mapX(point.X), mapY(point.Y));
                if (!plot.Contains(screenPoint))
                {
                    continue;
                }

                var deltaX = screenPoint.X - pointer.X;
                var deltaY = screenPoint.Y - pointer.Y;
                var distance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
                if (distance >= nearestDistance)
                {
                    continue;
                }

                nearestDistance = distance;
                nearest = new HoverSample(item.Series, point, screenPoint);
            }
        }

        if (nearest is null || nearestDistance > 24)
        {
            return;
        }

        var sample = nearest.Value;
        var guidePen = new Pen(new SolidColorBrush(ThemeColor(ThemeResourceBindings.PlotHint, Color.FromRgb(110, 110, 115), 115)), 1, DashStyle.Dash);
        context.DrawLine(guidePen, new Point(sample.ScreenPoint.X, plot.Top), new Point(sample.ScreenPoint.X, plot.Bottom));
        context.DrawLine(guidePen, new Point(plot.Left, sample.ScreenPoint.Y), new Point(plot.Right, sample.ScreenPoint.Y));
        var color = SeriesColor(sample.Series);
        context.DrawEllipse(
            ThemeBrush(ThemeResourceBindings.PlotHoverMarkerFill, Brushes.White),
            new Pen(new SolidColorBrush(color), 2),
            sample.ScreenPoint,
            4,
            4);
        var xLabel = AnalysisAxisFormatting.FormatLabel(
            sample.Series.XAxisLabel,
            sample.Series.XQuantity,
            sample.Series.XUnit);
        var yLabel = AnalysisAxisFormatting.FormatLabel(
            sample.Series.YAxisLabel,
            sample.Series.YQuantity,
            sample.Series.YUnit);
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(sample.Series.Name))
        {
            lines.Add(sample.Series.Name);
        }

        if (!string.IsNullOrWhiteSpace(sample.Point.Label)
            && !sample.Point.Label.Equals(sample.Series.Name, StringComparison.Ordinal))
        {
            lines.Add(sample.Point.Label);
        }

        lines.Add($"{xLabel}: {AnalysisAxisFormatting.FormatValue(sample.Point.X, sample.Series.XUnit)}");
        lines.Add($"{yLabel}: {AnalysisAxisFormatting.FormatValue(sample.Point.Y, sample.Series.YUnit)}");
        if (sample.Point.Value.HasValue)
        {
            lines.Add($"值: {AnalysisAxisFormatting.FormatValue(sample.Point.Value.Value, sample.Series.ValueUnit)}");
        }
        else if (sample.Point.Red.HasValue && sample.Point.Green.HasValue && sample.Point.Blue.HasValue)
        {
            lines.Add(
                $"RGB: {FormatTick(sample.Point.Red.Value)}, {FormatTick(sample.Point.Green.Value)}, {FormatTick(sample.Point.Blue.Value)}");
        }

        var text = CreateText(string.Join(Environment.NewLine, lines), 11, ThemeBrush(ThemeResourceBindings.TextOnAccent, Brushes.White), FontWeight.SemiBold);
        var tooltipWidth = text.Width + 16;
        var tooltipHeight = text.Height + 12;
        var tooltipX = Math.Min(plot.Right - tooltipWidth - 4, pointer.X + 14);
        var tooltipY = Math.Min(plot.Bottom - tooltipHeight - 4, pointer.Y + 14);
        tooltipX = Math.Max(plot.Left + 4, tooltipX);
        tooltipY = Math.Max(plot.Top + 4, tooltipY);
        var tooltip = new Rect(tooltipX, tooltipY, tooltipWidth, tooltipHeight);
        context.DrawRectangle(
            ThemeBrush(ThemeResourceBindings.PlotTooltipBackground, new SolidColorBrush(Color.FromArgb(226, 35, 35, 38))),
            new Pen(ThemeBrush(ThemeResourceBindings.PlotTooltipBorder, new SolidColorBrush(Color.FromArgb(150, 255, 255, 255))), 1),
            tooltip);
        context.DrawText(text, new Point(tooltip.Left + 8, tooltip.Top + 6));
    }

    private void DrawGridAndTicks(
        DrawingContext context,
        Rect plot,
        double xMin,
        double xMax,
        double yMin,
        double yMax)
    {
        var gridColor = ThemeColor(ThemeResourceBindings.PlotGrid, Color.FromRgb(180, 180, 180));
        var gridPen = new Pen(
            new SolidColorBrush(Color.FromArgb(
                (byte)Math.Clamp(Math.Round(PlotOptions.GridOpacity * 255), 0, 255),
                gridColor.R,
                gridColor.G,
                gridColor.B)),
            1,
            PlotOptions.DottedGrid ? new DashStyle(new[] { 1.0, 3.0 }, 0) : null);
        const int divisions = 5;
        for (var index = 0; index <= divisions; index++)
        {
            var fraction = index / (double)divisions;
            var x = plot.Left + (plot.Width * fraction);
            var y = plot.Bottom - (plot.Height * fraction);
            context.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));

            if (!PlotOptions.HideTickLabels)
            {
                var xValue = PlotOptions.ReverseX
                    ? xMax - ((xMax - xMin) * fraction)
                    : xMin + ((xMax - xMin) * fraction);
                var xText = CreateText(FormatTick(xValue), 11, ThemeBrush(ThemeResourceBindings.PlotTick, TickBrush));
                context.DrawText(xText, new Point(x - (xText.Width / 2), plot.Bottom + 7));
                var yText = CreateText(FormatTick(yMin + ((yMax - yMin) * fraction)), 11, ThemeBrush(ThemeResourceBindings.PlotTick, TickBrush));
                context.DrawText(yText, new Point(plot.Left - yText.Width - 8, y - (yText.Height / 2)));
            }
        }
    }

    private void DrawAxes(DrawingContext context, Rect plot)
    {
        var axisPen = ThemePen(ThemeResourceBindings.PlotAxis, AxisPen);
        context.DrawLine(axisPen, plot.BottomLeft, plot.BottomRight);
        context.DrawLine(axisPen, plot.TopLeft, plot.BottomLeft);
        if (!PlotOptions.HideTopAndRightAxes)
        {
            context.DrawLine(axisPen, plot.TopLeft, plot.TopRight);
            context.DrawLine(axisPen, plot.TopRight, plot.BottomRight);
        }
    }
    private void DrawZeroLines(
        DrawingContext context,
        Rect plot,
        double xMin,
        double xMax,
        double yMin,
        double yMax,
        Func<double, double> mapX,
        Func<double, double> mapY)
    {
        if (PlotOptions.ShowVerticalZeroLine && xMin <= 0 && xMax >= 0)
        {
            var x = mapX(0);
            var zeroPen = new Pen(
                ThemeBrush(ThemeResourceBindings.PlotZeroLine, new SolidColorBrush(Color.FromRgb(20, 20, 20))),
                PlotOptions.VerticalZeroLineWidth,
                DashFor(PlotOptions.VerticalZeroLineStyle));
            context.DrawLine(zeroPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }

        if (PlotOptions.ShowHorizontalZeroLine && yMin <= 0 && yMax >= 0)
        {
            var y = mapY(0);
            context.DrawLine(ThemePen(ThemeResourceBindings.PlotAxis, AxisPen), new Point(plot.Left, y), new Point(plot.Right, y));
        }
    }

    private void DrawSeries(
        DrawingContext context,
        AnalysisSeries series,
        IReadOnlyList<AnalysisPoint> points,
        Rect plot,
        Func<double, double> mapX,
        Func<double, double> mapY,
        double yMin,
        double yMax)
    {
        var color = SeriesColor(series);
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Clamp(Math.Round(series.Opacity * 255), 0, 255),
            color.R,
            color.G,
            color.B));
        var pen = new Pen(brush, series.LineWidth, DashFor(series.LineStyle));
        if (series.Kind == AnalysisSeriesKind.Heatmap)
        {
            DrawHeatmap(context, series, points, mapX, mapY);
            return;
        }

        if (series.Kind == AnalysisSeriesKind.Raster)
        {
            DrawRaster(context, points, mapX, mapY);
            return;
        }

        if (series.Kind == AnalysisSeriesKind.ColoredLine)
        {
            DrawColoredLine(context, points, mapX, mapY, series.LineWidth, series.ColorMap);
            return;
        }

        if (series.Kind == AnalysisSeriesKind.Bar)
        {
            var baseline = mapY(Math.Clamp(0, yMin, yMax));
            DrawBars(context, points, plot, mapX, mapY, baseline);
            return;
        }

        if (series.Kind == AnalysisSeriesKind.Line)
        {
            for (var index = 1; index < points.Count; index++)
            {
                if (!IsFinite(points[index - 1]) || !IsFinite(points[index]))
                {
                    continue;
                }

                context.DrawLine(
                    pen,
                    new Point(mapX(points[index - 1].X), mapY(points[index - 1].Y)),
                    new Point(mapX(points[index].X), mapY(points[index].Y)));
            }
        }

        if (series.Kind == AnalysisSeriesKind.Scatter || series.ShowMarkers)
        {
            foreach (var point in points)
            {
                if (!IsFinite(point))
                {
                    continue;
                }

                DrawMarker(
                    context,
                    brush,
                    new Point(mapX(point.X), mapY(point.Y)),
                    series.MarkerStyle,
                    series.MarkerSize);
            }
        }
    }

    private static void DrawHeatmap(
        DrawingContext context,
        AnalysisSeries series,
        IReadOnlyList<AnalysisPoint> points,
        Func<double, double> mapX,
        Func<double, double> mapY)
    {
        var valued = points.Where(point => IsFinite(point) && point.Value.HasValue && double.IsFinite(point.Value.Value)).ToArray();
        if (valued.Length == 0)
        {
            return;
        }

        var values = valued.Select(point => point.Value!.Value).ToArray();
        var minimum = series.ValueMinimum ?? values.Min();
        var maximum = series.ValueMaximum ?? values.Max();
        var xValues = valued.Select(point => point.X).Distinct().Order().ToArray();
        var yValues = valued.Select(point => point.Y).Distinct().Order().ToArray();
        var xStep = xValues.Length > 1 ? xValues.Zip(xValues.Skip(1), (left, right) => right - left).Where(step => step > 0).DefaultIfEmpty(1).Min() : 1;
        var yStep = yValues.Length > 1 ? yValues.Zip(yValues.Skip(1), (left, right) => right - left).Where(step => step > 0).DefaultIfEmpty(1).Min() : 1;
        foreach (var point in valued)
        {
            var normalized = Math.Abs(maximum - minimum) <= 1e-30 ? 0.5 : (point.Value!.Value - minimum) / (maximum - minimum);
            var brush = new SolidColorBrush(ColorFor(series.ColorMap, normalized));
            var left = mapX(point.X - (xStep / 2));
            var right = mapX(point.X + (xStep / 2));
            var top = mapY(point.Y + (yStep / 2));
            var bottom = mapY(point.Y - (yStep / 2));
            context.DrawRectangle(brush, null, new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top)));
        }
    }

    private static void DrawRaster(
        DrawingContext context,
        IReadOnlyList<AnalysisPoint> points,
        Func<double, double> mapX,
        Func<double, double> mapY)
    {
        var pixels = points.Where(point =>
            IsFinite(point)
            && point.Red.HasValue
            && point.Green.HasValue
            && point.Blue.HasValue).ToArray();
        if (pixels.Length == 0)
        {
            return;
        }

        var xValues = pixels.Select(point => point.X).Distinct().Order().ToArray();
        var yValues = pixels.Select(point => point.Y).Distinct().Order().ToArray();
        var xStep = xValues.Length > 1 ? xValues[1] - xValues[0] : 1;
        var yStep = yValues.Length > 1 ? yValues[1] - yValues[0] : 1;
        foreach (var pixel in pixels)
        {
            byte Channel(double? value) => (byte)Math.Clamp(Math.Round(Math.Clamp(value!.Value, 0, 1) * 255), 0, 255);
            var brush = new SolidColorBrush(Color.FromRgb(Channel(pixel.Red), Channel(pixel.Green), Channel(pixel.Blue)));
            var left = mapX(pixel.X - (xStep / 2));
            var right = mapX(pixel.X + (xStep / 2));
            var top = mapY(pixel.Y + (yStep / 2));
            var bottom = mapY(pixel.Y - (yStep / 2));
            context.DrawRectangle(brush, null, new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top)));
        }
    }

    private static void DrawColoredLine(
        DrawingContext context,
        IReadOnlyList<AnalysisPoint> points,
        Func<double, double> mapX,
        Func<double, double> mapY,
        double lineWidth,
        AnalysisColorMap colorMap)
    {
        var values = points.Where(point => point.Value.HasValue && double.IsFinite(point.Value.Value))
            .Select(point => point.Value!.Value).ToArray();
        if (values.Length == 0)
        {
            return;
        }

        var minimum = values.Min();
        var maximum = values.Max();
        for (var index = 1; index < points.Count; index++)
        {
            var left = points[index - 1];
            var right = points[index];
            if (!IsFinite(left) || !IsFinite(right) || !left.Value.HasValue || !right.Value.HasValue)
            {
                continue;
            }

            var value = (left.Value.Value + right.Value.Value) / 2;
            var normalized = Math.Abs(maximum - minimum) <= 1e-30 ? 0.5 : (value - minimum) / (maximum - minimum);
            var pen = new Pen(new SolidColorBrush(ColorFor(colorMap, normalized)), lineWidth);
            context.DrawLine(pen, new Point(mapX(left.X), mapY(left.Y)), new Point(mapX(right.X), mapY(right.Y)));
        }
    }

    private static Color Viridis(double value)
    {
        value = Math.Clamp(value, 0, 1);
        var anchors = new[]
        {
            (T: 0.0, Color: Color.FromRgb(68, 1, 84)),
            (T: 0.25, Color: Color.FromRgb(59, 82, 139)),
            (T: 0.5, Color: Color.FromRgb(33, 145, 140)),
            (T: 0.75, Color: Color.FromRgb(94, 201, 98)),
            (T: 1.0, Color: Color.FromRgb(253, 231, 37))
        };
        var upper = Array.FindIndex(anchors, anchor => anchor.T >= value);
        if (upper <= 0)
        {
            return anchors[0].Color;
        }

        var lower = anchors[upper - 1];
        var high = anchors[upper];
        var fraction = (value - lower.T) / (high.T - lower.T);
        byte Mix(byte left, byte right) => (byte)Math.Round(left + ((right - left) * fraction));
        return Color.FromRgb(
            Mix(lower.Color.R, high.Color.R),
            Mix(lower.Color.G, high.Color.G),
            Mix(lower.Color.B, high.Color.B));
    }

    private static Color Inferno(double value)
    {
        value = Math.Clamp(value, 0, 1);
        var anchors = new[]
        {
            (T: 0.0, Color: Color.FromRgb(0, 0, 4)),
            (T: 0.2, Color: Color.FromRgb(66, 10, 104)),
            (T: 0.4, Color: Color.FromRgb(147, 38, 103)),
            (T: 0.6, Color: Color.FromRgb(221, 81, 58)),
            (T: 0.8, Color: Color.FromRgb(252, 165, 10)),
            (T: 1.0, Color: Color.FromRgb(252, 255, 164))
        };
        var upper = Array.FindIndex(anchors, anchor => anchor.T >= value);
        if (upper <= 0)
        {
            return anchors[0].Color;
        }

        var lower = anchors[upper - 1];
        var high = anchors[upper];
        var fraction = (value - lower.T) / (high.T - lower.T);
        byte Mix(byte left, byte right) => (byte)Math.Round(left + ((right - left) * fraction));
        return Color.FromRgb(
            Mix(lower.Color.R, high.Color.R),
            Mix(lower.Color.G, high.Color.G),
            Mix(lower.Color.B, high.Color.B));
    }

    private static Color Jet(double value)
    {
        value = Math.Clamp(value, 0, 1);
        var red = Segment(value, new[] { (0.0, 0.0), (0.35, 0.0), (0.66, 1.0), (0.89, 1.0), (1.0, 0.5) });
        var green = Segment(value, new[] { (0.0, 0.0), (0.125, 0.0), (0.375, 1.0), (0.64, 1.0), (0.91, 0.0), (1.0, 0.0) });
        var blue = Segment(value, new[] { (0.0, 0.5), (0.11, 1.0), (0.34, 1.0), (0.65, 0.0), (1.0, 0.0) });
        return Color.FromRgb(
            (byte)Math.Round(red * 255),
            (byte)Math.Round(green * 255),
            (byte)Math.Round(blue * 255));
    }

    private static double Segment(double value, IReadOnlyList<(double Position, double Value)> points)
    {
        for (var index = 1; index < points.Count; index++)
        {
            if (value > points[index].Position)
            {
                continue;
            }

            var left = points[index - 1];
            var right = points[index];
            var fraction = (value - left.Position) / (right.Position - left.Position);
            return left.Value + ((right.Value - left.Value) * fraction);
        }

        return points[^1].Value;
    }

    private static Color ColorFor(AnalysisColorMap colorMap, double value)
    {
        return colorMap switch
        {
            AnalysisColorMap.Inferno => Inferno(value),
            AnalysisColorMap.Jet => Jet(value),
            _ => Viridis(value)
        };
    }

    private void DrawColorbar(DrawingContext context, AnalysisSeries series, Rect plot)
    {
        var values = series.Points
            .Where(point => point.Value.HasValue && double.IsFinite(point.Value.Value))
            .Select(point => point.Value!.Value)
            .ToArray();
        if (values.Length == 0)
        {
            return;
        }

        var minimum = series.ValueMinimum ?? values.Min();
        var maximum = series.ValueMaximum ?? values.Max();
        const double width = 14;
        var height = Math.Min(220, plot.Height * 0.7);
        var left = plot.Right + 24;
        var top = plot.Center.Y - (height / 2);
        const int steps = 96;
        for (var index = 0; index < steps; index++)
        {
            var fraction = index / (double)(steps - 1);
            var y = top + (height * (1 - fraction));
            context.DrawRectangle(
                new SolidColorBrush(ColorFor(series.ColorMap, fraction)),
                null,
                new Rect(left, y, width, (height / steps) + 1));
        }

        context.DrawRectangle(null, ThemePen(ThemeResourceBindings.PlotAxis, AxisPen), new Rect(left, top, width, height));
        var maxText = CreateText(FormatTick(maximum), 10.5, ThemeBrush(ThemeResourceBindings.PlotTick, TickBrush));
        var minText = CreateText(FormatTick(minimum), 10.5, ThemeBrush(ThemeResourceBindings.PlotTick, TickBrush));
        context.DrawText(maxText, new Point(left + width + 6, top - (maxText.Height / 2)));
        context.DrawText(minText, new Point(left + width + 6, top + height - (minText.Height / 2)));
        var label = CreateText(series.ValueLabel, 10.5, ThemeBrush(ThemeResourceBindings.PlotText, TextBrush));
        var center = new Point(left + 66, top + (height / 2));
        using (context.PushTransform(Matrix.CreateRotation(-Math.PI / 2, center)))
        {
            context.DrawText(label, new Point(center.X - (label.Width / 2), center.Y - (label.Height / 2)));
        }
    }

    private static void DrawMarker(
        DrawingContext context,
        IBrush brush,
        Point center,
        AnalysisMarkerStyle markerStyle,
        double size)
    {
        switch (markerStyle)
        {
            case AnalysisMarkerStyle.Cross:
                var crossPen = new Pen(brush, Math.Max(1, size * 0.45));
                context.DrawLine(
                    crossPen,
                    new Point(center.X - size, center.Y - size),
                    new Point(center.X + size, center.Y + size));
                context.DrawLine(
                    crossPen,
                    new Point(center.X - size, center.Y + size),
                    new Point(center.X + size, center.Y - size));
                break;
            case AnalysisMarkerStyle.Square:
                context.DrawRectangle(brush, null, new Rect(center.X - size, center.Y - size, size * 2, size * 2));
                break;
            case AnalysisMarkerStyle.Triangle:
                var top = new Point(center.X, center.Y - size);
                var left = new Point(center.X - size, center.Y + size);
                var right = new Point(center.X + size, center.Y + size);
                var geometry = new StreamGeometry();
                using (var geometryContext = geometry.Open())
                {
                    geometryContext.BeginFigure(top, true);
                    geometryContext.LineTo(left);
                    geometryContext.LineTo(right);
                    geometryContext.EndFigure(true);
                }

                context.DrawGeometry(brush, null, geometry);
                break;
            default:
                context.DrawEllipse(brush, null, center, size, size);
                break;
        }
    }

    private void DrawBars(
        DrawingContext context,
        IReadOnlyList<AnalysisPoint> points,
        Rect plot,
        Func<double, double> mapX,
        Func<double, double> mapY,
        double baseline)
    {
        var width = Math.Clamp(plot.Width / Math.Max(2, points.Count * 1.8), 8, 56);
        foreach (var point in points)
        {
            var x = mapX(point.X) - (width / 2);
            var y = mapY(point.Y);
            context.DrawRectangle(
                ThemeBrush(ThemeResourceBindings.PlotBar, BarBrush),
                null,
                new Rect(x, Math.Min(y, baseline), width, Math.Max(1, Math.Abs(baseline - y))));
        }
    }

    private static void DrawPointLabels(
        DrawingContext context,
        IReadOnlyList<(AnalysisSeries Series, AnalysisPoint[] Points)> visibleSeries,
        Rect plot,
        Func<double, double> mapX,
        Func<double, double> mapY)
    {
        foreach (var item in visibleSeries.Where(item => item.Series.Kind == AnalysisSeriesKind.Scatter))
        {
            var color = SeriesColor(item.Series);
            var brush = new SolidColorBrush(Color.FromArgb(220, color.R, color.G, color.B));
            foreach (var point in item.Points)
            {
                if (string.IsNullOrWhiteSpace(point.Label))
                {
                    continue;
                }

                var position = new Point(mapX(point.X) + 3, mapY(point.Y) - 7);
                if (!plot.Contains(position))
                {
                    continue;
                }

                context.DrawText(CreateText(point.Label, 7.5, brush), position);
            }
        }
    }

    private void DrawTitle(DrawingContext context, string title, Rect plot)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        var text = CreateText(title, 15, ThemeBrush(ThemeResourceBindings.PlotText, TextBrush), FontWeight.SemiBold);
        context.DrawText(text, new Point(plot.Center.X - (text.Width / 2), 13));
    }

    private void DrawAxisLabels(
        DrawingContext context,
        AnalysisSeries series,
        Rect plot,
        bool hideTickLabels)
    {
        var xLabelText = AnalysisAxisFormatting.FormatLabel(
            series.XAxisLabel,
            series.XQuantity,
            series.XUnit);
        var xLabel = CreateText(xLabelText, 12.5, ThemeBrush(ThemeResourceBindings.PlotText, TextBrush));
        context.DrawText(
            xLabel,
            new Point(
                plot.Center.X - (xLabel.Width / 2),
                plot.Bottom + XAxisLabelOffset(hideTickLabels)));

        var yLabelText = AnalysisAxisFormatting.FormatLabel(
            series.YAxisLabel,
            series.YQuantity,
            series.YUnit);
        var yLabel = CreateText(yLabelText, 12.5, ThemeBrush(ThemeResourceBindings.PlotText, TextBrush));
        var center = new Point(17, plot.Center.Y);
        using (context.PushTransform(Matrix.CreateRotation(-Math.PI / 2, center)))
        {
            context.DrawText(yLabel, new Point(center.X - (yLabel.Width / 2), center.Y - (yLabel.Height / 2)));
        }
    }

    internal static double XAxisLabelOffset(bool hideTickLabels) =>
        hideTickLabels ? 18 : 35;

    internal static IReadOnlyList<AnalysisSeries> NormalizeSeriesUnits(
        IReadOnlyList<AnalysisSeries> series)
    {
        var primary = series.FirstOrDefault(item => item.Points.Count > 0);
        if (primary is null)
        {
            return series;
        }

        return series.Select(item => NormalizeSeriesUnits(item, primary)).ToArray();
    }

    private static AnalysisSeries NormalizeSeriesUnits(
        AnalysisSeries series,
        AnalysisSeries primary)
    {
        var convertX = series.XQuantity == primary.XQuantity
            && AnalysisAxisFormatting.CanConvert(series.XUnit, primary.XUnit);
        var convertY = series.YQuantity == primary.YQuantity
            && AnalysisAxisFormatting.CanConvert(series.YUnit, primary.YUnit);
        if (!convertX && !convertY)
        {
            return series;
        }

        return series with
        {
            Points = series.Points.Select(point => point with
            {
                X = convertX
                    ? AnalysisAxisFormatting.Convert(point.X, series.XUnit, primary.XUnit)
                    : point.X,
                Y = convertY
                    ? AnalysisAxisFormatting.Convert(point.Y, series.YUnit, primary.YUnit)
                    : point.Y
            }).ToArray(),
            XUnit = convertX ? primary.XUnit : series.XUnit,
            YUnit = convertY ? primary.YUnit : series.YUnit
        };
    }

    internal static bool CanUseMinimalAxisMargins(
        bool compact,
        bool hideAxes,
        bool hideTickLabels,
        string? xAxisLabel,
        string? yAxisLabel) =>
        compact
        && hideTickLabels
        && (hideAxes
            || (string.IsNullOrWhiteSpace(xAxisLabel)
                && string.IsNullOrWhiteSpace(yAxisLabel)));

    private void DrawLegend(
        DrawingContext context,
        IReadOnlyList<(AnalysisSeries Series, AnalysisPoint[] Points)> legendItems,
        Rect plot)
    {
        var x = plot.Right + 22;
        var lineHeight = Math.Min(22, Math.Max(13, plot.Height / Math.Max(1, legendItems.Count)));
        var totalHeight = legendItems.Count * lineHeight;
        var y = plot.Center.Y - (totalHeight / 2);
        foreach (var item in legendItems)
        {
            var brush = new SolidColorBrush(SeriesColor(item.Series));
            var pen = new Pen(brush, item.Series.LineWidth, DashFor(item.Series.LineStyle));
            context.DrawLine(pen, new Point(x, y + 8), new Point(x + 28, y + 8));
            if (item.Series.Kind == AnalysisSeriesKind.Scatter || item.Series.ShowMarkers)
            {
                DrawMarker(context, brush, new Point(x + 14, y + 8), item.Series.MarkerStyle, 3);
            }

            var label = CreateText(item.Series.Name, lineHeight < 17 ? 10 : 11.5, ThemeBrush(ThemeResourceBindings.PlotText, TextBrush));
            context.DrawText(label, new Point(x + 36, y));
            y += lineHeight;
        }
    }

    private void DrawLegendBelow(
        DrawingContext context,
        IReadOnlyList<(AnalysisSeries Series, AnalysisPoint[] Points)> legendItems,
        Rect plot)
    {
        var entries = legendItems.Select(item =>
        {
            var label = CreateText(item.Series.Name, 11.5, ThemeBrush(ThemeResourceBindings.PlotText, TextBrush));
            return (item.Series, Label: label, Width: 34 + label.Width + 20);
        }).ToArray();
        var totalWidth = entries.Sum(entry => entry.Width);
        var x = Math.Max(plot.Left, plot.Center.X - (totalWidth / 2));
        var y = plot.Bottom + 58;
        foreach (var entry in entries)
        {
            var color = SeriesColor(entry.Series);
            var brush = new SolidColorBrush(color);
            var pen = new Pen(brush, entry.Series.LineWidth, DashFor(entry.Series.LineStyle));
            context.DrawLine(pen, new Point(x, y + 8), new Point(x + 26, y + 8));
            if (entry.Series.Kind == AnalysisSeriesKind.Scatter || entry.Series.ShowMarkers)
            {
                DrawMarker(context, brush, new Point(x + 13, y + 8), entry.Series.MarkerStyle, 3);
            }

            context.DrawText(entry.Label, new Point(x + 32, y));
            x += entry.Width;
        }
    }

    private static IDashStyle? DashFor(AnalysisLineStyle style)
    {
        return style switch
        {
            AnalysisLineStyle.Dashed => DashStyle.Dash,
            AnalysisLineStyle.Dotted => new DashStyle(new[] { 1.0, 3.0 }, 0),
            _ => null
        };
    }

    private static FormattedText CreateText(
        string text,
        double size,
        IBrush brush,
        FontWeight? weight = null)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            DisplayTypography.Typeface(weight),
            DisplayTypography.Scale(size),
            brush);
    }

    private static string FormatTick(double value)
    {
        return NumericDisplayFormatter.Format(value);
    }

    private static bool IsFinite(AnalysisPoint point)
    {
        return double.IsFinite(point.X) && double.IsFinite(point.Y);
    }

    private static bool HasValidViewport(PlotViewport viewport)
    {
        return double.IsFinite(viewport.XMinimum)
            && double.IsFinite(viewport.XMaximum)
            && double.IsFinite(viewport.YMinimum)
            && double.IsFinite(viewport.YMaximum)
            && viewport.XSpan > 1e-18
            && viewport.YSpan > 1e-18;
    }

    private PlotViewport? CurrentViewport()
    {
        if (_viewport is { } pending && HasValidViewport(pending))
        {
            return pending;
        }

        return HasValidViewport(_lastRenderedViewport) ? _lastRenderedViewport : null;
    }

    private static double UnmapX(
        double value,
        Rect plot,
        PlotViewport viewport,
        bool reverse)
    {
        var fraction = (value - plot.Left) / Math.Max(1, plot.Width);
        return reverse
            ? viewport.XMaximum - (fraction * viewport.XSpan)
            : viewport.XMinimum + (fraction * viewport.XSpan);
    }

    private static double UnmapY(double value, Rect plot, PlotViewport viewport)
    {
        return viewport.YMinimum + (((plot.Bottom - value) / Math.Max(1, plot.Height)) * viewport.YSpan);
    }

    private static void MakeEqualAspect(
        Rect plot,
        ref double xMin,
        ref double xMax,
        ref double yMin,
        ref double yMax)
    {
        var xUnitsPerPixel = (xMax - xMin) / plot.Width;
        var yUnitsPerPixel = (yMax - yMin) / plot.Height;
        if (xUnitsPerPixel > yUnitsPerPixel)
        {
            var center = (yMin + yMax) / 2;
            var halfRange = xUnitsPerPixel * plot.Height / 2;
            yMin = center - halfRange;
            yMax = center + halfRange;
        }
        else
        {
            var center = (xMin + xMax) / 2;
            var halfRange = yUnitsPerPixel * plot.Width / 2;
            xMin = center - halfRange;
            xMax = center + halfRange;
        }
    }

    private static void ExpandRange(
        ref double minimum,
        ref double maximum,
        bool fixedMinimum,
        bool fixedMaximum)
    {
        if (Math.Abs(maximum - minimum) < 1e-12)
        {
            var margin = Math.Max(1, Math.Abs(maximum) * 0.1);
            if (!fixedMinimum)
            {
                minimum -= margin;
            }

            if (!fixedMaximum)
            {
                maximum += margin;
            }

            if (Math.Abs(maximum - minimum) < 1e-12)
            {
                maximum = minimum + 1;
            }

            return;
        }

        var padding = (maximum - minimum) * 0.06;
        if (!fixedMinimum)
        {
            minimum -= padding;
        }

        if (!fixedMaximum)
        {
            maximum += padding;
        }
    }

    private readonly record struct PlotViewport(
        double XMinimum,
        double XMaximum,
        double YMinimum,
        double YMaximum)
    {
        public double XSpan => XMaximum - XMinimum;

        public double YSpan => YMaximum - YMinimum;
    }

    private readonly record struct HoverSample(
        AnalysisSeries Series,
        AnalysisPoint Point,
        Point ScreenPoint);
}
