using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Controls;

public sealed class WavefrontSurfaceControl : Control
{
    private const double InitialPitchDegrees = 28;
    private const double InitialYawOffsetDegrees = 35;
    private const int ContourLevelCount = 11;
    private Point _lastPointer;
    private SurfaceDragMode _dragMode;
    private bool _viewInitialized;
    private double _yawDegrees;
    private double _pitchDegrees;
    private double _zoom = 1;
    private Vector _pan;

    public WavefrontSurfaceControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public AnalysisSeriesDto? Series { get; init; }

    public double RotationDegrees { get; init; }

    public double DisplayScale { get; init; } = 1;

    public string DisplayAs { get; init; } = "表面";

    public string ColorBarTitle { get; init; } = "波前函数";

    public string ColorBarUnit { get; init; } = "波";

    public string XAxisLabel { get; init; } = "X 光瞳（归一化）";

    public string YAxisLabel { get; init; } = "Y 光瞳（归一化）";

    public double? ValueMinimum { get; init; }

    public double? ValueMaximum { get; init; }

    internal double ViewYawDegrees
    {
        get
        {
            EnsureViewInitialized();
            return _yawDegrees;
        }
    }

    internal double ViewPitchDegrees
    {
        get
        {
            EnsureViewInitialized();
            return _pitchDegrees;
        }
    }

    internal double ViewZoom
    {
        get
        {
            EnsureViewInitialized();
            return _zoom;
        }
    }

    internal bool HasRenderableGrid => TryBuildGrid(out _);

    public void ResetView()
    {
        _viewInitialized = true;
        _yawDegrees = RotationDegrees + InitialYawOffsetDegrees;
        _pitchDegrees = InitialPitchDegrees;
        _zoom = 1;
        _pan = default;
        InvalidateVisual();
    }

    internal void RotateView(double yawDeltaDegrees, double pitchDeltaDegrees)
    {
        EnsureViewInitialized();
        _yawDegrees = NormalizeDegrees(_yawDegrees + yawDeltaDegrees);
        _pitchDegrees = Math.Clamp(_pitchDegrees + pitchDeltaDegrees, 8, 82);
        InvalidateVisual();
    }

    internal void ZoomView(double factor)
    {
        EnsureViewInitialized();
        if (!double.IsFinite(factor) || factor <= 0)
        {
            return;
        }

        _zoom = Math.Clamp(_zoom * factor, 0.35, 6);
        InvalidateVisual();
    }

    internal void PanView(Vector delta)
    {
        EnsureViewInitialized();
        _pan += delta;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsSurfaceMode)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed && e.ClickCount >= 2)
        {
            ResetView();
            e.Handled = true;
            return;
        }

        _dragMode = point.Properties.IsLeftButtonPressed
            ? SurfaceDragMode.Rotate
            : point.Properties.IsRightButtonPressed || point.Properties.IsMiddleButtonPressed
                ? SurfaceDragMode.Pan
                : SurfaceDragMode.None;
        if (_dragMode == SurfaceDragMode.None)
        {
            return;
        }

        Focus();
        _lastPointer = point.Position;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragMode == SurfaceDragMode.None)
        {
            return;
        }

        var position = e.GetPosition(this);
        var delta = position - _lastPointer;
        _lastPointer = position;
        if (_dragMode == SurfaceDragMode.Rotate)
        {
            RotateView(delta.X * 0.45, -delta.Y * 0.35);
        }
        else
        {
            PanView(delta);
        }

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragMode == SurfaceDragMode.None)
        {
            return;
        }

        _dragMode = SurfaceDragMode.None;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!IsSurfaceMode)
        {
            return;
        }

        ZoomView(Math.Pow(1.16, e.Delta.Y));
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.White, null, Bounds);
        if (!TryBuildGrid(out var grid) || Bounds.Width < 260 || Bounds.Height < 220)
        {
            return;
        }

        var minimum = ValueMinimum ?? grid.Minimum;
        var maximum = ValueMaximum ?? grid.Maximum;
        if (maximum < minimum)
        {
            (minimum, maximum) = (maximum, minimum);
        }

        if (IsSurfaceMode)
        {
            RenderSurface(context, grid, minimum, maximum);
        }
        else
        {
            RenderPlanar(context, grid, minimum, maximum, IsContourMode);
        }
    }

    private bool IsSurfaceMode => DisplayAs.Contains("表面", StringComparison.Ordinal)
        || DisplayAs.Contains("surface", StringComparison.OrdinalIgnoreCase);

    private bool IsContourMode => DisplayAs.Contains("等高", StringComparison.Ordinal)
        || DisplayAs.Contains("contour", StringComparison.OrdinalIgnoreCase);

    private void RenderPlanar(
        DrawingContext context,
        SurfaceGrid grid,
        double minimum,
        double maximum,
        bool contourMode)
    {
        var plot = PlanarPlotRect();
        if (plot.Width <= 1 || plot.Height <= 1)
        {
            return;
        }

        var xStep = MinimumStep(grid.Xs);
        var yStep = MinimumStep(grid.Ys);
        var xMinimum = grid.Xs[0] - (xStep / 2);
        var xMaximum = grid.Xs[^1] + (xStep / 2);
        var yMinimum = grid.Ys[0] - (yStep / 2);
        var yMaximum = grid.Ys[^1] + (yStep / 2);
        var range = Math.Max(1e-12, maximum - minimum);

        double MapX(double value) => plot.Left
            + ((value - xMinimum) / Math.Max(1e-12, xMaximum - xMinimum) * plot.Width);
        double MapY(double value) => plot.Bottom
            - ((value - yMinimum) / Math.Max(1e-12, yMaximum - yMinimum) * plot.Height);

        if (contourMode)
        {
            DrawPlanarGrid(context, plot);
            DrawContours(context, grid, plot, minimum, maximum);
        }
        else
        {
            using (context.PushClip(plot))
            {
                for (var row = 0; row < grid.Ys.Length; row++)
                {
                    for (var column = 0; column < grid.Xs.Length; column++)
                    {
                        var value = grid.Values[row, column];
                        if (!double.IsFinite(value))
                        {
                            continue;
                        }

                        var normalized = (value - minimum) / range;
                        var left = MapX(grid.Xs[column] - (xStep / 2));
                        var right = MapX(grid.Xs[column] + (xStep / 2));
                        var top = MapY(grid.Ys[row] + (yStep / 2));
                        var bottom = MapY(grid.Ys[row] - (yStep / 2));
                        context.DrawRectangle(
                            new SolidColorBrush(JetColor(normalized)),
                            null,
                            new Rect(
                                Math.Min(left, right),
                                Math.Min(top, bottom),
                                Math.Max(1, Math.Abs(right - left) + 0.35),
                                Math.Max(1, Math.Abs(bottom - top) + 0.35)));
                    }
                }
            }
        }

        DrawPlanarAxes(
            context,
            plot,
            xMinimum,
            xMaximum,
            yMinimum,
            yMaximum,
            drawGrid: false);
        var colorBar = new Rect(
            Math.Min(Bounds.Width - 76, plot.Right + 38),
            plot.Top + 28,
            24,
            Math.Max(80, plot.Height - 56));
        if (contourMode)
        {
            DrawContourLegend(context, colorBar, minimum, maximum);
        }
        else
        {
            DrawColorBar(context, colorBar, minimum, maximum);
        }
    }

    private void RenderSurface(
        DrawingContext context,
        SurfaceGrid grid,
        double minimum,
        double maximum)
    {
        EnsureViewInitialized();
        var plot = new Rect(28, 22, Math.Max(1, Bounds.Width - 150), Math.Max(1, Bounds.Height - 62));
        var range = Math.Max(1e-12, maximum - minimum);
        var yaw = _yawDegrees * Math.PI / 180;
        var pitch = _pitchDegrees * Math.PI / 180;
        var cosYaw = Math.Cos(yaw);
        var sinYaw = Math.Sin(yaw);
        var sinPitch = Math.Sin(pitch);
        var cosPitch = Math.Cos(pitch);
        var spatialScale = Math.Min(plot.Width / 2.35, plot.Height / 2.05) * _zoom;
        var center = new Point(
            plot.Center.X + _pan.X,
            plot.Center.Y + (plot.Height * 0.14) + _pan.Y);
        var heightScale = Math.Clamp(DisplayScale, 0.01, 100) * 0.62;

        ProjectedPoint Project(int row, int column)
        {
            var x = grid.Xs.Length <= 1 ? 0 : ((2.0 * column) / (grid.Xs.Length - 1)) - 1;
            var y = grid.Ys.Length <= 1 ? 0 : ((2.0 * row) / (grid.Ys.Length - 1)) - 1;
            var rotatedX = (x * cosYaw) - (y * sinYaw);
            var rotatedY = (x * sinYaw) + (y * cosYaw);
            var normalizedValue = (grid.Values[row, column] - minimum) / range;
            var z = Math.Clamp(normalizedValue, 0, 1) * heightScale;
            return new ProjectedPoint(
                new Point(
                    center.X + (rotatedX * spatialScale * 0.78),
                    center.Y
                    + (rotatedY * spatialScale * 0.78 * sinPitch)
                    - (z * spatialScale * cosPitch)),
                (rotatedY * cosPitch) + (z * sinPitch),
                normalizedValue);
        }

        Point ProjectAxis(double x, double y, double z)
        {
            var rotatedX = (x * cosYaw) - (y * sinYaw);
            var rotatedY = (x * sinYaw) + (y * cosYaw);
            return new Point(
                center.X + (rotatedX * spatialScale * 0.78),
                center.Y
                + (rotatedY * spatialScale * 0.78 * sinPitch)
                - (z * heightScale * spatialScale * cosPitch));
        }

        var triangles = new List<SurfaceTriangle>();
        for (var row = 0; row < grid.Ys.Length - 1; row++)
        {
            for (var column = 0; column < grid.Xs.Length - 1; column++)
            {
                var p00 = Project(row, column);
                var p10 = Project(row, column + 1);
                var p01 = Project(row + 1, column);
                var p11 = Project(row + 1, column + 1);
                var finiteCorners = new[] { p00, p10, p11, p01 }
                    .Where(point => double.IsFinite(point.Value))
                    .ToArray();
                if (finiteCorners.Length == 4)
                {
                    AddTriangle(p00, p10, p11);
                    AddTriangle(p00, p11, p01);
                }
                else if (finiteCorners.Length == 3)
                {
                    AddTriangle(finiteCorners[0], finiteCorners[1], finiteCorners[2]);
                }
            }
        }

        var edgePen = Math.Max(grid.Xs.Length, grid.Ys.Length) <= 64
            ? new Pen(new SolidColorBrush(Color.FromArgb(30, 20, 20, 20)), 0.3)
            : null;
        using (context.PushClip(plot))
        {
            foreach (var triangle in triangles.OrderBy(item => item.Depth))
            {
                var geometry = new StreamGeometry();
                using (var stream = geometry.Open())
                {
                    stream.BeginFigure(triangle.A, true);
                    stream.LineTo(triangle.B);
                    stream.LineTo(triangle.C);
                    stream.EndFigure(true);
                }

                context.DrawGeometry(
                    new SolidColorBrush(JetColor(triangle.Value)),
                    edgePen,
                    geometry);
            }

            DrawSurfaceAxes(context, plot, ProjectAxis);
        }

        DrawColorBar(
            context,
            new Rect(
                Math.Max(8, Bounds.Width - 92),
                72,
                28,
                Math.Max(80, Bounds.Height - 175)),
            minimum,
            maximum);
        DrawInteractionHint(context);

        void AddTriangle(ProjectedPoint first, ProjectedPoint second, ProjectedPoint third)
        {
            triangles.Add(new SurfaceTriangle(
                first.Screen,
                second.Screen,
                third.Screen,
                (first.Depth + second.Depth + third.Depth) / 3,
                (first.Value + second.Value + third.Value) / 3));
        }
    }

    private void DrawContours(
        DrawingContext context,
        SurfaceGrid grid,
        Rect plot,
        double minimum,
        double maximum)
    {
        var range = Math.Max(1e-12, maximum - minimum);
        using (context.PushClip(plot))
        {
            for (var index = 0; index < ContourLevelCount; index++)
            {
                var fraction = index / (double)(ContourLevelCount - 1);
                var level = minimum + (range * fraction);
                var pen = new Pen(new SolidColorBrush(JetColor(fraction)), 1.15);
                foreach (var segment in BuildContourSegments(grid.Values, level))
                {
                    var start = new Point(
                        plot.Left + ((segment.Start.X + 0.5) / grid.Xs.Length * plot.Width),
                        plot.Bottom - ((segment.Start.Y + 0.5) / grid.Ys.Length * plot.Height));
                    var end = new Point(
                        plot.Left + ((segment.End.X + 0.5) / grid.Xs.Length * plot.Width),
                        plot.Bottom - ((segment.End.Y + 0.5) / grid.Ys.Length * plot.Height));
                    context.DrawLine(pen, start, end);
                }
            }
        }
    }

    internal static IReadOnlyList<ContourSegment> BuildContourSegments(double[,] values, double level)
    {
        var rows = values.GetLength(0);
        var columns = values.GetLength(1);
        var result = new List<ContourSegment>();
        for (var row = 0; row < rows - 1; row++)
        {
            for (var column = 0; column < columns - 1; column++)
            {
                var bottomLeft = values[row, column];
                var bottomRight = values[row, column + 1];
                var topRight = values[row + 1, column + 1];
                var topLeft = values[row + 1, column];
                if (!double.IsFinite(bottomLeft)
                    || !double.IsFinite(bottomRight)
                    || !double.IsFinite(topRight)
                    || !double.IsFinite(topLeft))
                {
                    continue;
                }

                var intersections = new List<Point>(4);
                AddIntersection(bottomLeft, bottomRight, new Point(column, row), new Point(column + 1, row));
                AddIntersection(bottomRight, topRight, new Point(column + 1, row), new Point(column + 1, row + 1));
                AddIntersection(topRight, topLeft, new Point(column + 1, row + 1), new Point(column, row + 1));
                AddIntersection(topLeft, bottomLeft, new Point(column, row + 1), new Point(column, row));
                if (intersections.Count == 2)
                {
                    result.Add(new ContourSegment(intersections[0], intersections[1]));
                }
                else if (intersections.Count == 4)
                {
                    var center = (bottomLeft + bottomRight + topRight + topLeft) / 4;
                    if (center >= level)
                    {
                        result.Add(new ContourSegment(intersections[0], intersections[3]));
                        result.Add(new ContourSegment(intersections[1], intersections[2]));
                    }
                    else
                    {
                        result.Add(new ContourSegment(intersections[0], intersections[1]));
                        result.Add(new ContourSegment(intersections[2], intersections[3]));
                    }
                }

                void AddIntersection(double first, double second, Point firstPoint, Point secondPoint)
                {
                    if (!CrossesLevel(first, second, level))
                    {
                        return;
                    }

                    var fraction = Math.Abs(second - first) <= 1e-30
                        ? 0.5
                        : Math.Clamp((level - first) / (second - first), 0, 1);
                    intersections.Add(new Point(
                        firstPoint.X + ((secondPoint.X - firstPoint.X) * fraction),
                        firstPoint.Y + ((secondPoint.Y - firstPoint.Y) * fraction)));
                }
            }
        }

        return result;
    }

    private static bool CrossesLevel(double first, double second, double level)
    {
        return (first < level && second >= level)
            || (second < level && first >= level);
    }

    private void DrawPlanarAxes(
        DrawingContext context,
        Rect plot,
        double xMinimum,
        double xMaximum,
        double yMinimum,
        double yMaximum,
        bool drawGrid)
    {
        var framePen = new Pen(new SolidColorBrush(Color.FromRgb(35, 35, 35)), 0.9);
        if (drawGrid)
        {
            DrawPlanarGrid(context, plot);
        }

        context.DrawRectangle(null, framePen, plot);
        const int divisions = 6;
        for (var index = 0; index <= divisions; index++)
        {
            var fraction = index / (double)divisions;
            var x = plot.Left + (fraction * plot.Width);
            var y = plot.Bottom - (fraction * plot.Height);
            context.DrawLine(framePen, new Point(x, plot.Bottom), new Point(x, plot.Bottom + 4));
            context.DrawLine(framePen, new Point(plot.Left - 4, y), new Point(plot.Left, y));
            if (index is not (0 or divisions / 2 or divisions))
            {
                continue;
            }

            var xValue = xMinimum + ((xMaximum - xMinimum) * fraction);
            var yValue = yMinimum + ((yMaximum - yMinimum) * fraction);
            var xText = Text(FormatAxisValue(xValue), 10, Brushes.Black);
            var yText = Text(FormatAxisValue(yValue), 10, Brushes.Black);
            context.DrawText(xText, new Point(x - (xText.Width / 2), plot.Bottom + 7));
            context.DrawText(yText, new Point(plot.Left - yText.Width - 8, y - (yText.Height / 2)));
        }

        var xLabel = Text(XAxisLabel, 12, Brushes.Black);
        var yLabel = Text(YAxisLabel, 12, Brushes.Black);
        context.DrawText(
            xLabel,
            new Point(plot.Center.X - (xLabel.Width / 2), plot.Bottom + 34));
        var yCenter = new Point(plot.Left - 48, plot.Center.Y);
        using (context.PushTransform(Matrix.CreateRotation(-Math.PI / 2, yCenter)))
        {
            context.DrawText(
                yLabel,
                new Point(yCenter.X - (yLabel.Width / 2), yCenter.Y - (yLabel.Height / 2)));
        }
    }

    private static void DrawPlanarGrid(DrawingContext context, Rect plot)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(38, 110, 110, 110)), 0.6);
        const int divisions = 10;
        for (var index = 1; index < divisions; index++)
        {
            var fraction = index / (double)divisions;
            var x = plot.Left + (fraction * plot.Width);
            var y = plot.Top + (fraction * plot.Height);
            context.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }
    }

    private void DrawSurfaceAxes(
        DrawingContext context,
        Rect plot,
        Func<double, double, double, Point> project)
    {
        var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(45, 45, 45)), 0.8);
        var origin = project(-1, -1, 0);
        var xEnd = project(1, -1, 0);
        var yEnd = project(-1, 1, 0);
        var zEnd = project(-1, -1, 1);
        context.DrawLine(axisPen, origin, xEnd);
        context.DrawLine(axisPen, origin, yEnd);
        context.DrawLine(axisPen, origin, zEnd);

        var xLabel = Text(XAxisLabel, 10, Brushes.Black);
        var yLabel = Text(YAxisLabel, 10, Brushes.Black);
        context.DrawText(
            xLabel,
            ClampLabelPosition(new Point(xEnd.X - xLabel.Width, xEnd.Y + 7), xLabel, plot));
        context.DrawText(
            yLabel,
            ClampLabelPosition(new Point(yEnd.X, yEnd.Y + 7), yLabel, plot));
    }

    private void DrawColorBar(
        DrawingContext context,
        Rect bar,
        double minimum,
        double maximum)
    {
        const int strips = 96;
        for (var index = 0; index < strips; index++)
        {
            var fraction = index / (double)(strips - 1);
            var y = bar.Bottom - ((index + 1) * bar.Height / strips);
            context.DrawRectangle(
                new SolidColorBrush(JetColor(fraction)),
                null,
                new Rect(bar.Left, y, bar.Width, (bar.Height / strips) + 1));
        }

        context.DrawRectangle(null, new Pen(Brushes.Black, 0.7), bar);
        DrawColorLegendText(context, bar, minimum, maximum);
    }

    private void DrawContourLegend(
        DrawingContext context,
        Rect bar,
        double minimum,
        double maximum)
    {
        var title = Text(ColorBarTitle, 12, Brushes.Black);
        context.DrawText(title, new Point(bar.Center.X - (title.Width / 2), bar.Top - title.Height - 8));
        for (var index = 0; index < ContourLevelCount; index++)
        {
            var fraction = index / (double)(ContourLevelCount - 1);
            var y = bar.Bottom - (fraction * bar.Height);
            var pen = new Pen(new SolidColorBrush(JetColor(fraction)), 1.3);
            context.DrawLine(pen, new Point(bar.Left - 8, y), new Point(bar.Right + 8, y));
            var value = minimum + ((maximum - minimum) * fraction);
            var label = Text(value.ToString("0.####", CultureInfo.InvariantCulture), 10, Brushes.Black);
            context.DrawText(label, new Point(bar.Right + 13, y - (label.Height / 2)));
        }

        DrawColorBarUnit(context, bar);
    }

    private void DrawColorLegendText(
        DrawingContext context,
        Rect bar,
        double minimum,
        double maximum)
    {
        var title = Text(ColorBarTitle, 12, Brushes.Black);
        context.DrawText(title, new Point(bar.Center.X - (title.Width / 2), bar.Top - title.Height - 8));
        for (var index = 0; index <= 8; index++)
        {
            var fraction = index / 8.0;
            var value = minimum + ((maximum - minimum) * fraction);
            var label = Text(value.ToString("0.####", CultureInfo.InvariantCulture), 10, Brushes.Black);
            var y = bar.Bottom - (fraction * bar.Height) - (label.Height / 2);
            context.DrawText(label, new Point(bar.Right + 6, y));
        }

        DrawColorBarUnit(context, bar);
    }

    private void DrawColorBarUnit(DrawingContext context, Rect bar)
    {
        if (string.IsNullOrWhiteSpace(ColorBarUnit))
        {
            return;
        }

        var unit = Text(ColorBarUnit, 11, Brushes.Black);
        context.DrawText(unit, new Point(bar.Right + 6, bar.Bottom + 8));
    }

    private void DrawInteractionHint(DrawingContext context)
    {
        const string hint = "左键拖动旋转 · 右键/中键拖动平移 · 滚轮缩放 · 双击复位";
        var text = Text(hint, 10, new SolidColorBrush(Color.FromRgb(120, 124, 132)));
        context.DrawText(
            text,
            new Point(
                Math.Max(8, Bounds.Width - text.Width - 14),
                Math.Max(4, Bounds.Height - text.Height - 7)));
    }

    private Rect PlanarPlotRect()
    {
        const double leftMargin = 76;
        const double topMargin = 36;
        const double rightMargin = 150;
        const double bottomMargin = 72;
        var availableWidth = Math.Max(1, Bounds.Width - leftMargin - rightMargin);
        var availableHeight = Math.Max(1, Bounds.Height - topMargin - bottomMargin);
        var side = Math.Max(1, Math.Min(availableWidth, availableHeight));
        return new Rect(
            leftMargin + ((availableWidth - side) / 2),
            topMargin + ((availableHeight - side) / 2),
            side,
            side);
    }

    private bool TryBuildGrid(out SurfaceGrid grid)
    {
        var samples = Series?.Points
            .Where(point => point.Value.HasValue
                && double.IsFinite(point.X)
                && double.IsFinite(point.Y)
                && double.IsFinite(point.Value.Value))
            .ToArray()
            ?? Array.Empty<AnalysisPointDto>();
        if (samples.Length < 4)
        {
            grid = null!;
            return false;
        }

        var xs = samples.Select(point => point.X).Distinct().Order().ToArray();
        var ys = samples.Select(point => point.Y).Distinct().Order().ToArray();
        if (xs.Length < 2 || ys.Length < 2)
        {
            grid = null!;
            return false;
        }

        var xIndices = xs.Select((value, index) => (value, index))
            .ToDictionary(item => CoordinateKey(item.value), item => item.index);
        var yIndices = ys.Select((value, index) => (value, index))
            .ToDictionary(item => CoordinateKey(item.value), item => item.index);
        var values = new double[ys.Length, xs.Length];
        for (var row = 0; row < ys.Length; row++)
        {
            for (var column = 0; column < xs.Length; column++)
            {
                values[row, column] = double.NaN;
            }
        }

        foreach (var sample in samples)
        {
            values[
                yIndices[CoordinateKey(sample.Y)],
                xIndices[CoordinateKey(sample.X)]] = sample.Value!.Value;
        }

        var finiteValues = values.Cast<double>()
            .Where(double.IsFinite)
            .ToArray();
        if (finiteValues.Length < 4)
        {
            grid = null!;
            return false;
        }

        grid = new SurfaceGrid(xs, ys, values, finiteValues.Min(), finiteValues.Max());
        return true;
    }

    private void EnsureViewInitialized()
    {
        if (!_viewInitialized)
        {
            ResetView();
        }
    }

    private static Point ClampLabelPosition(Point point, FormattedText text, Rect bounds)
    {
        return new Point(
            Math.Clamp(point.X, bounds.Left + 2, Math.Max(bounds.Left + 2, bounds.Right - text.Width - 2)),
            Math.Clamp(point.Y, bounds.Top + 2, Math.Max(bounds.Top + 2, bounds.Bottom - text.Height - 2)));
    }

    private static double MinimumStep(IReadOnlyList<double> coordinates)
    {
        return coordinates.Count > 1
            ? coordinates.Zip(coordinates.Skip(1), (left, right) => right - left)
                .Where(step => step > 0)
                .DefaultIfEmpty(1)
                .Min()
            : 1;
    }

    private static string FormatAxisValue(double value)
    {
        if (Math.Abs(value) < 5e-10)
        {
            return "0";
        }

        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static double NormalizeDegrees(double value)
    {
        value %= 360;
        return value < 0 ? value + 360 : value;
    }

    private static long CoordinateKey(double value) => (long)Math.Round(value * 1_000_000_000);

    private static Color JetColor(double value)
    {
        value = Math.Clamp(value, 0, 1);
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

    internal readonly record struct ContourSegment(Point Start, Point End);

    private sealed record SurfaceGrid(
        double[] Xs,
        double[] Ys,
        double[,] Values,
        double Minimum,
        double Maximum);

    private readonly record struct ProjectedPoint(Point Screen, double Depth, double Value);

    private readonly record struct SurfaceTriangle(
        Point A,
        Point B,
        Point C,
        double Depth,
        double Value);

    private enum SurfaceDragMode
    {
        None,
        Rotate,
        Pan
    }
}
