using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Visualization;

namespace OptilandWorkbench.App.Controls;

public enum OpticSceneViewMode
{
    TwoDimensional,
    ThreeDimensional
}

public sealed class OpticSceneControl : Control
{
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(250, 252, 254));
    private static readonly IBrush LensFillBrush = new SolidColorBrush(Color.FromArgb(92, 154, 162, 170));
    private static readonly Pen ReferencePlanePen = new(new SolidColorBrush(Color.FromRgb(34, 48, 58)), 2.6);
    private static readonly Pen AxisPen = new(new SolidColorBrush(Color.FromRgb(134, 146, 166)), 1);
    private static readonly Pen StopPen = new(new SolidColorBrush(Color.FromRgb(33, 96, 144)), 3);
    private static readonly Pen SurfacePen = new(new SolidColorBrush(Color.FromRgb(38, 50, 56)), 2);
    private static readonly Pen LensEdgePen = new(new SolidColorBrush(Color.FromRgb(87, 112, 132)), 1.4);
    private static readonly Pen VignettedRayPen = new(new SolidColorBrush(Color.FromRgb(188, 74, 60)), 1.2);
    private static readonly Pen ThreeDWirePen = new(new SolidColorBrush(Color.FromRgb(92, 105, 118)), 1.2);
    private static readonly Pen ThreeDLensEdgePen = new(new SolidColorBrush(Color.FromRgb(55, 68, 78)), 1.5);
    private static readonly Color[] RayColors =
    {
        Color.FromRgb(24, 113, 188),
        Color.FromRgb(209, 106, 26),
        Color.FromRgb(31, 145, 94),
        Color.FromRgb(202, 62, 53)
    };

    private OpticSceneViewMode _viewMode;
    private double _zoom = 1;
    private Vector _pan;
    private double _yaw = -0.34;
    private double _pitch = 0.2;
    private bool _dragging;
    private bool _rotating;
    private Point _lastPointer;
    private bool _showRays = true;

    public OpticSceneControl()
    {
        Focusable = true;
    }

    public Optic? Optic { get; set; }

    public OpticSceneViewMode ViewMode
    {
        get => _viewMode;
        set
        {
            if (_viewMode != value)
            {
                _viewMode = value;
                InvalidateVisual();
            }
        }
    }

    public bool ShowRays
    {
        get => _showRays;
        set
        {
            if (_showRays != value)
            {
                _showRays = value;
                InvalidateVisual();
            }
        }
    }

    public void ResetView()
    {
        _zoom = 1;
        _pan = default;
        _yaw = -0.34;
        _pitch = 0.2;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus();
        _dragging = true;
        _rotating = ViewMode == OpticSceneViewMode.ThreeDimensional
            && !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _lastPointer = point.Position;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging)
        {
            return;
        }

        var position = e.GetPosition(this);
        var delta = position - _lastPointer;
        _lastPointer = position;
        if (_rotating)
        {
            _yaw += delta.X * 0.01;
            _pitch = Math.Clamp(_pitch + (delta.Y * 0.01), -1.25, 1.25);
        }
        else
        {
            _pan += delta;
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _zoom = Math.Clamp(_zoom * Math.Pow(1.15, e.Delta.Y), 0.2, 12);
        InvalidateVisual();
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(BackgroundBrush, null, Bounds);

        if (Optic is null || Optic.SurfaceGroup.Items.Count == 0)
        {
            return;
        }

        if (ViewMode == OpticSceneViewMode.ThreeDimensional)
        {
            Draw3D(context, new Layout2DBuilder(Optic).Build3D());
            return;
        }

        Draw2D(context, new Layout2DBuilder(Optic).Build());
    }

    private void Draw2D(DrawingContext context, Layout2DScene scene)
    {
        var padding = 28.0;
        var width = Math.Max(1, Bounds.Width - (padding * 2));
        var height = Math.Max(1, Bounds.Height - (padding * 2));
        var centerY = padding + (height / 2.0);
        var zSpan = Math.Max(1, scene.ZMax - scene.ZMin);
        var aperture = Math.Max(1, scene.YExtent);

        double MapZ(double z) => ((padding + ((z - scene.ZMin) / zSpan * width) - (Bounds.Width / 2)) * _zoom)
            + (Bounds.Width / 2) + _pan.X;
        double MapY(double y) => ((centerY - (y / aperture * height / 2.0) - (Bounds.Height / 2)) * _zoom)
            + (Bounds.Height / 2) + _pan.Y;

        context.DrawLine(AxisPen, new Point(MapZ(scene.ZMin), centerY), new Point(MapZ(scene.ZMax), centerY));

        DrawLensElements(context, scene.LensElements, MapZ, MapY);
        if (ShowRays)
        {
            DrawRays(context, scene.Rays, MapZ, MapY);
        }
        DrawLensEdges(context, scene.LensEdges, MapZ, MapY);
        DrawSurfaces(context, scene.Surfaces, MapZ, MapY);
    }

    private void Draw3D(DrawingContext context, Layout3DScene scene)
    {
        var padding = 34.0;
        var width = Math.Max(1, Bounds.Width - (padding * 2));
        var height = Math.Max(1, Bounds.Height - (padding * 2));
        var centerX = padding + (width / 2.0);
        var centerY = padding + (height / 2.0);
        var zSpan = Math.Max(1, scene.ZMax - scene.ZMin);
        var projectedWidth = zSpan + (scene.XExtent * 1.4);
        var projectedHeight = (scene.YExtent * 2.0) + (scene.XExtent * 1.4);
        var scale = 0.88 * Math.Min(width / Math.Max(1, projectedWidth), height / Math.Max(1, projectedHeight));
        var zCenter = scene.ZMin + (zSpan / 2.0);

        Point Project(Layout3DPoint point)
        {
            var z = point.Z - zCenter;
            var cosYaw = Math.Cos(_yaw);
            var sinYaw = Math.Sin(_yaw);
            var screenX = (z * cosYaw) + (point.X * sinYaw);
            var depth = (-z * sinYaw) + (point.X * cosYaw);
            var cosPitch = Math.Cos(_pitch);
            var sinPitch = Math.Sin(_pitch);
            var screenY = (point.Y * cosPitch) - (depth * sinPitch);
            return new Point(
                centerX + (screenX * scale * _zoom) + _pan.X,
                centerY - (screenY * scale * _zoom) + _pan.Y);
        }

        context.DrawLine(
            AxisPen,
            Project(new Layout3DPoint(0, 0, scene.ZMin)),
            Project(new Layout3DPoint(0, 0, scene.ZMax)));

        Draw3DLensElements(context, scene.LensElements, Project);
        Draw3DSurfaces(context, scene.Surfaces, Project);
        if (ShowRays)
        {
            Draw3DRays(context, scene.Rays, Project);
        }
    }

    private static void DrawSurfaces(
        DrawingContext context,
        IReadOnlyList<Layout2DSurfaceCurve> surfaces,
        Func<double, double> mapZ,
        Func<double, double> mapY)
    {
        foreach (var surface in surfaces)
        {
            var pen = SurfacePenFor(surface);
            DrawPolyline(context, pen, surface.Points, mapZ, mapY);
        }
    }

    private static void DrawLensElements(
        DrawingContext context,
        IReadOnlyList<Layout2DLensElement> elements,
        Func<double, double> mapZ,
        Func<double, double> mapY)
    {
        foreach (var element in elements)
        {
            DrawFilledPolygon(context, LensFillBrush, LensEdgePen, element.Boundary, mapZ, mapY);
        }
    }

    private static void DrawLensEdges(
        DrawingContext context,
        IReadOnlyList<Layout2DLensEdge> edges,
        Func<double, double> mapZ,
        Func<double, double> mapY)
    {
        foreach (var edge in edges)
        {
            context.DrawLine(
                LensEdgePen,
                new Point(mapZ(edge.Start.Z), mapY(edge.Start.Y)),
                new Point(mapZ(edge.End.Z), mapY(edge.End.Y)));
        }
    }

    private static void DrawRays(
        DrawingContext context,
        IReadOnlyList<Layout2DRayPath> rays,
        Func<double, double> mapZ,
        Func<double, double> mapY)
    {
        foreach (var path in rays)
        {
            var pen = RayPenFor(path.FieldIndex, path.Vignetted, 1.25);
            DrawPolyline(context, pen, path.Points, mapZ, mapY);
        }
    }

    private static void Draw3DLensElements(
        DrawingContext context,
        IReadOnlyList<Layout3DLensElement> elements,
        Func<Layout3DPoint, Point> project)
    {
        foreach (var element in elements)
        {
            DrawPolyline3D(context, ThreeDLensEdgePen, element.FrontRim, project);
            DrawPolyline3D(context, ThreeDLensEdgePen, element.BackRim, project);

            var count = Math.Min(element.FrontRim.Count, element.BackRim.Count);
            if (count <= 1)
            {
                continue;
            }

            var quarter = Math.Max(1, (count - 1) / 4);
            for (var index = 0; index < count - 1; index += quarter)
            {
                context.DrawLine(ThreeDWirePen, project(element.FrontRim[index]), project(element.BackRim[index]));
            }
        }
    }

    private static void Draw3DSurfaces(
        DrawingContext context,
        IReadOnlyList<Layout3DSurfacePrimitive> surfaces,
        Func<Layout3DPoint, Point> project)
    {
        foreach (var surface in surfaces)
        {
            var pen = surface.IsStop
                ? StopPen
                : surface.IsReferencePlane
                    ? ReferencePlanePen
                    : ThreeDWirePen;
            DrawPolyline3D(context, pen, surface.Rim, project);
            if (!surface.IsReferencePlane)
            {
                DrawPolyline3D(context, ThreeDWirePen, surface.MeridianY, project);
                DrawPolyline3D(context, ThreeDWirePen, surface.MeridianX, project);
            }
        }
    }

    private static void Draw3DRays(
        DrawingContext context,
        IReadOnlyList<Layout3DRayPath> rays,
        Func<Layout3DPoint, Point> project)
    {
        foreach (var ray in rays)
        {
            DrawPolyline3D(context, RayPenFor(ray.FieldIndex, ray.Vignetted, 1.35), ray.Points, project);
        }
    }

    private static Pen SurfacePenFor(Layout2DSurfaceCurve surface)
    {
        if (surface.IsStop)
        {
            return StopPen;
        }

        return surface.IsReferencePlane ? ReferencePlanePen : SurfacePen;
    }

    private static Pen RayPenFor(int fieldIndex, bool vignetted, double thickness)
    {
        if (vignetted)
        {
            return VignettedRayPen;
        }

        return new Pen(new SolidColorBrush(RayColors[Math.Abs(fieldIndex) % RayColors.Length]), thickness);
    }

    private static void DrawFilledPolygon(
        DrawingContext context,
        IBrush fill,
        Pen outline,
        IReadOnlyList<Layout2DPoint> points,
        Func<double, double> mapZ,
        Func<double, double> mapY)
    {
        if (points.Count < 3)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(new Point(mapZ(points[0].Z), mapY(points[0].Y)), true);
            for (var index = 1; index < points.Count; index++)
            {
                stream.LineTo(new Point(mapZ(points[index].Z), mapY(points[index].Y)));
            }

            stream.EndFigure(true);
        }

        context.DrawGeometry(fill, outline, geometry);
    }

    private static void DrawPolyline(
        DrawingContext context,
        Pen pen,
        IReadOnlyList<Layout2DPoint> points,
        Func<double, double> mapZ,
        Func<double, double> mapY)
    {
        for (var index = 1; index < points.Count; index++)
        {
            var previous = points[index - 1];
            var current = points[index];
            context.DrawLine(
                pen,
                new Point(mapZ(previous.Z), mapY(previous.Y)),
                new Point(mapZ(current.Z), mapY(current.Y)));
        }
    }

    private static void DrawPolyline3D(
        DrawingContext context,
        Pen pen,
        IReadOnlyList<Layout3DPoint> points,
        Func<Layout3DPoint, Point> project)
    {
        for (var index = 1; index < points.Count; index++)
        {
            context.DrawLine(pen, project(points[index - 1]), project(points[index]));
        }
    }
}
