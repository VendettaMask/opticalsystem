using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OptilandWorkbench.Application.Contracts;
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
        Color.FromRgb(23, 190, 207)
    };

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
        var dataX = UnmapX(position.X, _lastPlot, current);
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
            var xShift = -(delta.X / Math.Max(1, _lastPlot.Width)) * current.XSpan;
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
        context.DrawRectangle(BackgroundBrush, null, Bounds);
        var visibleSeries = Series
            .Select(item => (Series: item, Points: item.Points.Where(IsFinite).ToArray()))
            .Where(item => item.Points.Length > 0)
            .ToArray();
        if (visibleSeries.Length == 0 || Bounds.Width < 160 || Bounds.Height < 140)
        {
            return;
        }

        var legendItems = visibleSeries.Where(item => !string.IsNullOrWhiteSpace(item.Series.Name)).ToArray();
        var legendWidth = PlotOptions.ShowLegend && legendItems.Length > 0 ? Math.Min(190, Bounds.Width * 0.28) : 0;
        var valueSeries = visibleSeries
            .Where(item => item.Series.Kind is AnalysisSeriesKind.Heatmap or AnalysisSeriesKind.ColoredLine)
            .Select(item => item.Series)
            .FirstOrDefault();
        var colorbarWidth = valueSeries is not null && !string.IsNullOrWhiteSpace(valueSeries.ValueLabel) ? 92 : 0;
        var top = PlotOptions.HideAxes ? (string.IsNullOrWhiteSpace(PlotOptions.Title) ? 8.0 : 38.0) : (string.IsNullOrWhiteSpace(PlotOptions.Title) ? 22.0 : 46.0);
        var left = PlotOptions.HideAxes ? 8 : 76;
        var bottom = PlotOptions.HideAxes ? 8 : 62;
        var right = (PlotOptions.HideAxes ? 8 : 20) + legendWidth + colorbarWidth;
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

        double MapX(double value) => plot.Left + ((value - xMin) / (xMax - xMin) * plot.Width);
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
        }

        if (!PlotOptions.HideAxes)
        {
            DrawAxisLabels(context, visibleSeries[0].Series, plot);
        }
        if (colorbarWidth > 0 && valueSeries is not null)
        {
            DrawColorbar(context, valueSeries, plot);
        }

        if (legendWidth > 0)
        {
            DrawLegend(context, legendItems, plot);
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
            var hint = CreateText("滚轮缩放 · 拖动平移 · 双击复位 · 悬停读数", 10.5, new SolidColorBrush(Color.FromRgb(110, 110, 115)));
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
        var guidePen = new Pen(new SolidColorBrush(Color.FromArgb(115, 110, 110, 115)), 1, DashStyle.Dash);
        context.DrawLine(guidePen, new Point(sample.ScreenPoint.X, plot.Top), new Point(sample.ScreenPoint.X, plot.Bottom));
        context.DrawLine(guidePen, new Point(plot.Left, sample.ScreenPoint.Y), new Point(plot.Right, sample.ScreenPoint.Y));
        var color = Palette[Math.Abs(sample.Series.ColorIndex) % Palette.Length];
        context.DrawEllipse(
            Brushes.White,
            new Pen(new SolidColorBrush(color), 2),
            sample.ScreenPoint,
            4,
            4);

        var xLabel = string.IsNullOrWhiteSpace(sample.Series.XAxisLabel) ? "X" : sample.Series.XAxisLabel;
        var yLabel = string.IsNullOrWhiteSpace(sample.Series.YAxisLabel) ? "Y" : sample.Series.YAxisLabel;
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(sample.Series.Name))
        {
            lines.Add(sample.Series.Name);
        }

        lines.Add($"{xLabel}: {FormatTick(sample.Point.X)}");
        lines.Add($"{yLabel}: {FormatTick(sample.Point.Y)}");
        if (sample.Point.Value.HasValue)
        {
            lines.Add($"值: {FormatTick(sample.Point.Value.Value)}");
        }
        else if (sample.Point.Red.HasValue && sample.Point.Green.HasValue && sample.Point.Blue.HasValue)
        {
            lines.Add($"RGB: {sample.Point.Red:0.###}, {sample.Point.Green:0.###}, {sample.Point.Blue:0.###}");
        }

        var text = CreateText(string.Join(Environment.NewLine, lines), 11, Brushes.White, FontWeight.SemiBold);
        var tooltipWidth = text.Width + 16;
        var tooltipHeight = text.Height + 12;
        var tooltipX = Math.Min(plot.Right - tooltipWidth - 4, pointer.X + 14);
        var tooltipY = Math.Min(plot.Bottom - tooltipHeight - 4, pointer.Y + 14);
        tooltipX = Math.Max(plot.Left + 4, tooltipX);
        tooltipY = Math.Max(plot.Top + 4, tooltipY);
        var tooltip = new Rect(tooltipX, tooltipY, tooltipWidth, tooltipHeight);
        context.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(226, 35, 35, 38)),
            new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1),
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
        var gridPen = new Pen(
            new SolidColorBrush(Color.FromArgb(
                (byte)Math.Clamp(Math.Round(PlotOptions.GridOpacity * 255), 0, 255),
                180,
                180,
                180)),
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

            var xText = CreateText(FormatTick(xMin + ((xMax - xMin) * fraction)), 11, TickBrush);
            context.DrawText(xText, new Point(x - (xText.Width / 2), plot.Bottom + 7));
            var yText = CreateText(FormatTick(yMin + ((yMax - yMin) * fraction)), 11, TickBrush);
            context.DrawText(yText, new Point(plot.Left - yText.Width - 8, y - (yText.Height / 2)));
        }
    }

    private void DrawAxes(DrawingContext context, Rect plot)
    {
        context.DrawLine(AxisPen, plot.BottomLeft, plot.BottomRight);
        context.DrawLine(AxisPen, plot.TopLeft, plot.BottomLeft);
        if (!PlotOptions.HideTopAndRightAxes)
        {
            context.DrawLine(AxisPen, plot.TopLeft, plot.TopRight);
            context.DrawLine(AxisPen, plot.TopRight, plot.BottomRight);
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
                new SolidColorBrush(Color.FromRgb(20, 20, 20)),
                PlotOptions.VerticalZeroLineWidth,
                DashFor(PlotOptions.VerticalZeroLineStyle));
            context.DrawLine(zeroPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }

        if (PlotOptions.ShowHorizontalZeroLine && yMin <= 0 && yMax >= 0)
        {
            var y = mapY(0);
            context.DrawLine(AxisPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }
    }

    private static void DrawSeries(
        DrawingContext context,
        AnalysisSeries series,
        IReadOnlyList<AnalysisPoint> points,
        Rect plot,
        Func<double, double> mapX,
        Func<double, double> mapY,
        double yMin,
        double yMax)
    {
        var color = Palette[Math.Abs(series.ColorIndex) % Palette.Length];
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

    private static void DrawColorbar(DrawingContext context, AnalysisSeries series, Rect plot)
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

        context.DrawRectangle(null, AxisPen, new Rect(left, top, width, height));
        var maxText = CreateText(FormatTick(maximum), 10.5, TickBrush);
        var minText = CreateText(FormatTick(minimum), 10.5, TickBrush);
        context.DrawText(maxText, new Point(left + width + 6, top - (maxText.Height / 2)));
        context.DrawText(minText, new Point(left + width + 6, top + height - (minText.Height / 2)));
        var label = CreateText(series.ValueLabel, 10.5, TextBrush);
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

    private static void DrawBars(
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
                BarBrush,
                null,
                new Rect(x, Math.Min(y, baseline), width, Math.Max(1, Math.Abs(baseline - y))));
        }
    }

    private static void DrawTitle(DrawingContext context, string title, Rect plot)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        var text = CreateText(title, 15, TextBrush, FontWeight.SemiBold);
        context.DrawText(text, new Point(plot.Center.X - (text.Width / 2), 13));
    }

    private static void DrawAxisLabels(DrawingContext context, AnalysisSeries series, Rect plot)
    {
        var xLabel = CreateText(series.XAxisLabel, 12.5, TextBrush);
        context.DrawText(xLabel, new Point(plot.Center.X - (xLabel.Width / 2), plot.Bottom + 35));

        var yLabel = CreateText(series.YAxisLabel, 12.5, TextBrush);
        var center = new Point(17, plot.Center.Y);
        using (context.PushTransform(Matrix.CreateRotation(-Math.PI / 2, center)))
        {
            context.DrawText(yLabel, new Point(center.X - (yLabel.Width / 2), center.Y - (yLabel.Height / 2)));
        }
    }

    private static void DrawLegend(
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
            var brush = new SolidColorBrush(Palette[Math.Abs(item.Series.ColorIndex) % Palette.Length]);
            var pen = new Pen(brush, item.Series.LineWidth, DashFor(item.Series.LineStyle));
            context.DrawLine(pen, new Point(x, y + 8), new Point(x + 28, y + 8));
            if (item.Series.Kind == AnalysisSeriesKind.Scatter || item.Series.ShowMarkers)
            {
                DrawMarker(context, brush, new Point(x + 14, y + 8), item.Series.MarkerStyle, 3);
            }

            var label = CreateText(item.Series.Name, lineHeight < 17 ? 10 : 11.5, TextBrush);
            context.DrawText(label, new Point(x + 36, y));
            y += lineHeight;
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
            new Typeface("Arial", FontStyle.Normal, weight ?? FontWeight.Normal),
            size,
            brush);
    }

    private static string FormatTick(double value)
    {
        var magnitude = Math.Abs(value);
        return magnitude is > 0 and (< 0.001 or >= 10000)
            ? value.ToString("0.###E+0", CultureInfo.CurrentCulture)
            : value.ToString("0.###", CultureInfo.CurrentCulture);
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

    private static double UnmapX(double value, Rect plot, PlotViewport viewport)
    {
        return viewport.XMinimum + (((value - plot.Left) / Math.Max(1, plot.Width)) * viewport.XSpan);
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
