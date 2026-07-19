using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OptilandWorkbench.Application.Contracts;
using Layout2DPoint = OptilandWorkbench.Application.Contracts.ScenePoint2Dto;
using Layout3DPoint = OptilandWorkbench.Application.Contracts.ScenePoint3Dto;
using Layout2DSurfaceCurve = OptilandWorkbench.Application.Contracts.SceneSurface2Dto;
using Layout2DLensEdge = OptilandWorkbench.Application.Contracts.SceneLensEdge2Dto;
using Layout2DLensElement = OptilandWorkbench.Application.Contracts.SceneLensElement2Dto;
using Layout2DRayPath = OptilandWorkbench.Application.Contracts.SceneRay2Dto;
using Layout2DScene = OptilandWorkbench.Application.Contracts.Scene2Dto;
using Layout3DSurfacePrimitive = OptilandWorkbench.Application.Contracts.SceneSurface3Dto;
using Layout3DLensElement = OptilandWorkbench.Application.Contracts.SceneLensElement3Dto;
using Layout3DRayPath = OptilandWorkbench.Application.Contracts.SceneRay3Dto;
using Layout3DScene = OptilandWorkbench.Application.Contracts.Scene3Dto;

namespace OptilandWorkbench.App.Controls;

public enum OpticSceneViewMode
{
    TwoDimensional,
    ThreeDimensional
}

public enum OpticSceneRenderMode
{
    Solid,
    Wireframe
}

public enum OpticSceneRayColorMode
{
    Field,
    Wavelength
}

public enum OpticSceneVisualStyle
{
    OpticalLayout,
    SolidModel
}

public enum OpticSceneViewPreset
{
    Isometric,
    Front,
    Back,
    Left,
    Right,
    Top,
    Bottom
}

public sealed class OpticSceneControl : Control
{
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(250, 252, 254));
    private static readonly IBrush ThreeDBackgroundBrush = new LinearGradientBrush
    {
        StartPoint = RelativePoint.TopLeft,
        EndPoint = RelativePoint.BottomRight,
        GradientStops =
        {
            new GradientStop(Color.FromRgb(248, 250, 253), 0),
            new GradientStop(Color.FromRgb(229, 235, 243), 1)
        }
    };
    private static readonly IBrush SolidModelBackgroundBrush = new LinearGradientBrush
    {
        StartPoint = RelativePoint.TopLeft,
        EndPoint = RelativePoint.BottomRight,
        GradientStops =
        {
            new GradientStop(Color.FromRgb(79, 99, 158), 0),
            new GradientStop(Color.FromRgb(194, 200, 218), 1)
        }
    };
    private static readonly IBrush LensFillBrush = new SolidColorBrush(Color.FromArgb(104, 105, 151, 185));
    private static readonly Pen ReferencePlanePen = new(new SolidColorBrush(Color.FromRgb(34, 48, 58)), 2.6);
    private static readonly Pen AxisPen = new(new SolidColorBrush(Color.FromRgb(134, 146, 166)), 1);
    private static readonly Pen StopPen = new(new SolidColorBrush(Color.FromRgb(33, 96, 144)), 3);
    private static readonly Pen ApertureStopPen = new(new SolidColorBrush(Color.FromRgb(31, 31, 33)), 2);
    private static readonly Pen SurfacePen = new(new SolidColorBrush(Color.FromRgb(38, 50, 56)), 2);
    private static readonly Pen LensEdgePen = new(new SolidColorBrush(Color.FromRgb(87, 112, 132)), 1.4);
    private static readonly Pen VignettedRayPen = new(new SolidColorBrush(Color.FromRgb(188, 74, 60)), 1.2);
    private static readonly Pen ThreeDWirePen = new(new SolidColorBrush(Color.FromRgb(71, 93, 128)), 1.15);
    private static readonly Pen ThreeDLensEdgePen = new(new SolidColorBrush(Color.FromRgb(24, 58, 142)), 1.7);
    private static readonly Pen ThreeDLensHighlightPen = new(new SolidColorBrush(Color.FromArgb(155, 128, 174, 245)), 0.9);
    private static readonly Pen SolidLensEdgePen = new(new SolidColorBrush(Color.FromRgb(38, 42, 51)), 1.8);
    private static readonly Pen TargetPen = new(new SolidColorBrush(Color.FromRgb(104, 119, 139)), 1.1);
    private static readonly IBrush ThreeDLensFaceBrush = new SolidColorBrush(Color.FromArgb(138, 30, 65, 190));
    private static readonly IBrush ThreeDLensSideBrush = new SolidColorBrush(Color.FromArgb(156, 18, 72, 145));
    private static readonly IBrush SolidLensFaceBrush = new LinearGradientBrush
    {
        StartPoint = RelativePoint.TopLeft,
        EndPoint = RelativePoint.BottomRight,
        GradientStops =
        {
            new GradientStop(Color.FromRgb(52, 56, 67), 0),
            new GradientStop(Color.FromRgb(183, 187, 197), 0.52),
            new GradientStop(Color.FromRgb(43, 47, 57), 1)
        }
    };
    private static readonly IBrush SolidLensSideBrush = new SolidColorBrush(Color.FromRgb(66, 70, 82));
    private static readonly IBrush ThreeDLensCutBrush = new SolidColorBrush(Color.FromArgb(170, 225, 139, 48));
    private static readonly Color[] RayColors =
    {
        Color.FromRgb(220, 55, 48),
        Color.FromRgb(36, 156, 86),
        Color.FromRgb(34, 111, 202),
        Color.FromRgb(232, 137, 29),
        Color.FromRgb(197, 57, 157),
        Color.FromRgb(15, 158, 177),
        Color.FromRgb(164, 174, 25)
    };

    private readonly SceneViewport _viewport = new();
    private OpticSceneViewMode _viewMode;
    private OpticSceneRenderMode _renderMode = OpticSceneRenderMode.Solid;
    private double _yaw = -0.52;
    private double _pitch = 0.28;
    private bool _dragging;
    private bool _rotating;
    private Point _lastPointer;
    private bool _showRays = true;
    private bool _cutawayEnabled;
    private bool _showRayArrows;
    private bool _showScaleBar = true;
    private double _verticalStretch = 1;
    private double _rayLineWidth = 1.25;
    private OpticSceneRayColorMode _rayColorMode;
    private OpticSceneVisualStyle _visualStyle;

    public OpticSceneControl()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public SceneDto? Scene { get; set; }

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

    public OpticSceneRenderMode RenderMode
    {
        get => _renderMode;
        set
        {
            if (_renderMode != value)
            {
                _renderMode = value;
                InvalidateVisual();
            }
        }
    }

    public bool ShowRayArrows
    {
        get => _showRayArrows;
        set
        {
            if (_showRayArrows != value)
            {
                _showRayArrows = value;
                InvalidateVisual();
            }
        }
    }

    public OpticSceneVisualStyle VisualStyle
    {
        get => _visualStyle;
        set
        {
            if (_visualStyle != value)
            {
                _visualStyle = value;
                InvalidateVisual();
            }
        }
    }

    public bool CutawayEnabled
    {
        get => _cutawayEnabled;
        set
        {
            if (_cutawayEnabled != value)
            {
                _cutawayEnabled = value;
                InvalidateVisual();
            }
        }
    }

    public bool ShowScaleBar
    {
        get => _showScaleBar;
        set
        {
            if (_showScaleBar != value)
            {
                _showScaleBar = value;
                InvalidateVisual();
            }
        }
    }

    public double VerticalStretch
    {
        get => _verticalStretch;
        set
        {
            var normalized = Math.Clamp(value, 0.1, 10);
            if (Math.Abs(_verticalStretch - normalized) > 1e-12)
            {
                _verticalStretch = normalized;
                InvalidateVisual();
            }
        }
    }

    public double RayLineWidth
    {
        get => _rayLineWidth;
        set
        {
            var normalized = Math.Clamp(value, 0.5, 4);
            if (Math.Abs(_rayLineWidth - normalized) > 1e-12)
            {
                _rayLineWidth = normalized;
                InvalidateVisual();
            }
        }
    }

    public OpticSceneRayColorMode RayColorMode
    {
        get => _rayColorMode;
        set
        {
            if (_rayColorMode != value)
            {
                _rayColorMode = value;
                InvalidateVisual();
            }
        }
    }

    public void ResetView()
    {
        _viewport.Reset();
        _yaw = -0.52;
        _pitch = 0.28;
        InvalidateVisual();
    }

    public void FitView()
    {
        _viewport.Reset();
        InvalidateVisual();
    }

    public void SetViewPreset(OpticSceneViewPreset preset)
    {
        (_yaw, _pitch) = preset switch
        {
            OpticSceneViewPreset.Front => (0, 0),
            OpticSceneViewPreset.Back => (Math.PI, 0),
            OpticSceneViewPreset.Left => (-Math.PI / 2.0, 0),
            OpticSceneViewPreset.Right => (Math.PI / 2.0, 0),
            OpticSceneViewPreset.Top => (0, (Math.PI / 2.0) - 0.01),
            OpticSceneViewPreset.Bottom => (0, (-Math.PI / 2.0) + 0.01),
            _ => (-0.52, 0.28)
        };
        _viewport.Reset();
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
            _viewport.PanBy(delta);
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _viewport.ZoomAt(Math.Pow(1.15, e.Delta.Y), e.GetPosition(this), Bounds.Size);
        InvalidateVisual();
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(
            ViewMode == OpticSceneViewMode.ThreeDimensional
                ? VisualStyle == OpticSceneVisualStyle.SolidModel
                    ? SolidModelBackgroundBrush
                    : ThreeDBackgroundBrush
                : BackgroundBrush,
            null,
            Bounds);

        if (Scene is null)
        {
            return;
        }

        if (ViewMode == OpticSceneViewMode.ThreeDimensional)
        {
            if (Scene.ThreeDimensional is not null)
            {
                Draw3D(context, Scene.ThreeDimensional);
            }

            return;
        }

        if (Scene.TwoDimensional is not null)
        {
            Draw2D(context, Scene.TwoDimensional);
        }
    }

    private void Draw2D(DrawingContext context, Layout2DScene scene)
    {
        var padding = 28.0;
        var width = Math.Max(1, Bounds.Width - (padding * 2));
        var height = Math.Max(1, Bounds.Height - (padding * 2));
        var centerX = padding + (width / 2.0);
        var centerY = padding + (height / 2.0);
        var zSpan = Math.Max(1, scene.ZMax - scene.ZMin);
        var aperture = Math.Max(1, scene.YExtent);
        var scale = 0.94 * Math.Min(width / zSpan, height / (aperture * 2.0 * VerticalStretch));
        var zCenter = scene.ZMin + (zSpan / 2.0);

        double MapZ(double z) => _viewport.Apply(
            new Point(centerX + ((z - zCenter) * scale), centerY),
            Bounds.Size).X;
        double MapY(double y) => _viewport.Apply(
            new Point(centerX, centerY - (y * scale * VerticalStretch)),
            Bounds.Size).Y;

        context.DrawLine(
            AxisPen,
            new Point(MapZ(scene.ZMin), MapY(0)),
            new Point(MapZ(scene.ZMax), MapY(0)));

        DrawLensElements(context, scene.LensElements, MapZ, MapY);
        if (ShowRays)
        {
            DrawRays(context, scene.Rays, MapZ, MapY);
        }
        DrawLensEdges(context, scene.LensEdges, MapZ, MapY);
        DrawSurfaces(context, scene.Surfaces, MapZ, MapY);
        if (ShowScaleBar)
        {
            DrawScaleBar(context, scale * _viewport.Zoom);
        }
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
        var projectedHeight = (scene.YExtent * 2.0 * VerticalStretch) + (scene.XExtent * 1.4);
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
            var screenY = (point.Y * VerticalStretch * cosPitch) - (depth * sinPitch);
            return _viewport.Apply(
                new Point(centerX + (screenX * scale), centerY - (screenY * scale)),
                Bounds.Size);
        }

        double Depth(Layout3DPoint point)
        {
            var z = point.Z - zCenter;
            var depth = (-z * Math.Sin(_yaw)) + (point.X * Math.Cos(_yaw));
            return (point.Y * Math.Sin(_pitch)) + (depth * Math.Cos(_pitch));
        }

        context.DrawLine(
            AxisPen,
            Project(new Layout3DPoint(0, 0, scene.ZMin)),
            Project(new Layout3DPoint(0, 0, scene.ZMax)));

        var solidModelStyle = VisualStyle == OpticSceneVisualStyle.SolidModel;
        if (solidModelStyle && ShowRays)
        {
            Draw3DRays(context, scene.Rays, Project);
        }

        if (RenderMode == OpticSceneRenderMode.Solid)
        {
            Draw3DSolidLensElements(
                context,
                scene.LensElements,
                scene.Surfaces,
                Project,
                Depth,
                CutawayEnabled,
                solidModelStyle);
        }

        Draw3DLensElements(
            context,
            scene.LensElements,
            Project,
            RenderMode == OpticSceneRenderMode.Wireframe,
            CutawayEnabled,
            solidModelStyle);
        Draw3DSurfaces(
            context,
            scene.Surfaces,
            Project,
            showMeridians: true,
            CutawayEnabled,
            solidModelStyle);
        if (!solidModelStyle && ShowRays)
        {
            Draw3DRays(context, scene.Rays, Project);
        }

        DrawObjectTarget(context, Project(new Layout3DPoint(0, 0, scene.ZMin)));
        DrawOrientationGizmo(context);
        if (ShowScaleBar)
        {
            DrawScaleBar(context, scale * _viewport.Zoom);
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
            if (surface.IsStop)
            {
                DrawApertureStop(context, surface, mapZ, mapY);
                continue;
            }

            var pen = SurfacePenFor(surface);
            DrawPolyline(context, pen, surface.Points, mapZ, mapY);
        }
    }

    private static void DrawApertureStop(
        DrawingContext context,
        Layout2DSurfaceCurve surface,
        Func<double, double> mapZ,
        Func<double, double> mapY)
    {
        if (surface.Points.Count == 0)
        {
            return;
        }

        var center = surface.Points.MinBy(point => Math.Abs(point.Y))!;
        var upper = surface.Points.MaxBy(point => point.Y)!;
        var lower = surface.Points.MinBy(point => point.Y)!;
        var x = mapZ(center.Z);
        var upperY = Math.Min(mapY(upper.Y), mapY(lower.Y));
        var lowerY = Math.Max(mapY(upper.Y), mapY(lower.Y));
        var apertureHeight = Math.Max(1, lowerY - upperY);
        var bladeLength = Math.Clamp(apertureHeight * 0.14, 11, 30);
        const double capHalfWidth = 5;

        context.DrawLine(
            ApertureStopPen,
            new Point(x, upperY),
            new Point(x, upperY - bladeLength));
        context.DrawLine(
            ApertureStopPen,
            new Point(x - capHalfWidth, upperY),
            new Point(x + capHalfWidth, upperY));
        context.DrawLine(
            ApertureStopPen,
            new Point(x, lowerY),
            new Point(x, lowerY + bladeLength));
        context.DrawLine(
            ApertureStopPen,
            new Point(x - capHalfWidth, lowerY),
            new Point(x + capHalfWidth, lowerY));
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

    private void DrawRays(
        DrawingContext context,
        IReadOnlyList<Layout2DRayPath> rays,
        Func<double, double> mapZ,
        Func<double, double> mapY)
    {
        foreach (var path in rays)
        {
            var pen = RayPenFor(RayColorIndex(path.FieldIndex, path.WavelengthIndex), path.Vignetted, RayLineWidth);
            DrawPolyline(context, pen, path.Points, mapZ, mapY);
            if (ShowRayArrows)
            {
                DrawArrow(
                    context,
                    pen,
                    path.Points.Select(point => new Point(mapZ(point.Z), mapY(point.Y))).ToArray());
            }
        }
    }

    private static void Draw3DLensElements(
        DrawingContext context,
        IReadOnlyList<Layout3DLensElement> elements,
        Func<Layout3DPoint, Point> project,
        bool showConnectors,
        bool cutawayEnabled,
        bool solidModelStyle)
    {
        var edgePen = solidModelStyle ? SolidLensEdgePen : ThreeDLensEdgePen;
        foreach (var element in elements)
        {
            DrawPolyline3D(context, edgePen, element.FrontRim, project, cutawayEnabled);
            DrawPolyline3D(context, edgePen, element.BackRim, project, cutawayEnabled);
            if (!solidModelStyle)
            {
                DrawPolyline3D(context, ThreeDLensHighlightPen, element.FrontRim, project, cutawayEnabled);
            }

            if (!showConnectors)
            {
                continue;
            }

            var count = Math.Min(element.FrontRim.Count, element.BackRim.Count);
            if (count <= 1)
            {
                continue;
            }

            var quarter = Math.Max(1, (count - 1) / 4);
            for (var index = 0; index < count - 1; index += quarter)
            {
                DrawSegment3D(
                    context,
                    ThreeDWirePen,
                    element.FrontRim[index],
                    element.BackRim[index],
                    project,
                    cutawayEnabled);
            }
        }
    }

    private static void Draw3DSolidLensElements(
        DrawingContext context,
        IReadOnlyList<Layout3DLensElement> elements,
        IReadOnlyList<Layout3DSurfacePrimitive> surfaces,
        Func<Layout3DPoint, Point> project,
        Func<Layout3DPoint, double> depth,
        bool cutawayEnabled,
        bool solidModelStyle)
    {
        var faces = new List<ProjectedFace>();
        var faceBrush = solidModelStyle ? SolidLensFaceBrush : ThreeDLensFaceBrush;
        var sideBrush = solidModelStyle ? SolidLensSideBrush : ThreeDLensSideBrush;
        var lensSurfaceNumbers = elements
            .SelectMany(element => new[] { element.FrontSurfaceNumber, element.BackSurfaceNumber })
            .ToHashSet();

        foreach (var surface in surfaces.Where(surface => lensSurfaceNumbers.Contains(surface.SurfaceNumber)))
        {
            foreach (var face in surface.Faces)
            {
                AddProjectedFace(
                    faces,
                    cutawayEnabled ? ClipPolygonToCutaway(face.Points) : face.Points,
                    faceBrush,
                    depth);
            }
        }

        foreach (var element in elements)
        {
            var count = Math.Min(element.FrontRim.Count, element.BackRim.Count) - 1;
            for (var index = 0; index < count; index++)
            {
                AddProjectedFace(
                    faces,
                    cutawayEnabled
                        ? ClipPolygonToCutaway(new[]
                        {
                            element.FrontRim[index],
                            element.FrontRim[index + 1],
                            element.BackRim[index + 1],
                            element.BackRim[index]
                        })
                        : new[]
                    {
                        element.FrontRim[index],
                        element.FrontRim[index + 1],
                        element.BackRim[index + 1],
                        element.BackRim[index]
                    },
                    sideBrush,
                    depth);
            }
        }

        if (cutawayEnabled)
        {
            var surfacesByNumber = surfaces.ToDictionary(surface => surface.SurfaceNumber);
            foreach (var element in elements)
            {
                if (!surfacesByNumber.TryGetValue(element.FrontSurfaceNumber, out var front) ||
                    !surfacesByNumber.TryGetValue(element.BackSurfaceNumber, out var back))
                {
                    continue;
                }

                var cutFace = front.MeridianY
                    .Concat(back.MeridianY.Reverse())
                    .ToArray();
                AddProjectedFace(faces, cutFace, ThreeDLensCutBrush, depth);
            }
        }

        foreach (var face in faces.OrderBy(face => face.Depth))
        {
            DrawProjectedPolygon(context, face.Fill, face.Points, project);
        }
    }

    private static void Draw3DSurfaces(
        DrawingContext context,
        IReadOnlyList<Layout3DSurfacePrimitive> surfaces,
        Func<Layout3DPoint, Point> project,
        bool showMeridians,
        bool cutawayEnabled,
        bool solidModelStyle)
    {
        foreach (var surface in surfaces)
        {
            var pen = surface.IsStop
                ? StopPen
                : surface.IsReferencePlane
                    ? ReferencePlanePen
                    : solidModelStyle
                        ? SolidLensEdgePen
                        : ThreeDWirePen;
            var clipSurface = cutawayEnabled && !surface.IsReferencePlane;
            DrawPolyline3D(context, pen, surface.Rim, project, clipSurface);
            if (showMeridians && !surface.IsReferencePlane)
            {
                var meridianPen = solidModelStyle ? SolidLensEdgePen : ThreeDWirePen;
                DrawPolyline3D(context, meridianPen, surface.MeridianY, project);
                DrawPolyline3D(context, meridianPen, surface.MeridianX, project, clipSurface);
            }
        }
    }

    private void Draw3DRays(
        DrawingContext context,
        IReadOnlyList<Layout3DRayPath> rays,
        Func<Layout3DPoint, Point> project)
    {
        foreach (var ray in rays)
        {
            var pen = RayPenFor(
                RayColorIndex(ray.FieldIndex, ray.WavelengthIndex),
                ray.Vignetted,
                RayLineWidth);
            DrawPolyline3D(
                context,
                pen,
                ray.Points,
                project);
            if (ShowRayArrows)
            {
                DrawArrow(context, pen, ray.Points.Select(project).ToArray());
            }
        }
    }

    private static void DrawObjectTarget(DrawingContext context, Point center)
    {
        context.DrawEllipse(null, TargetPen, center, 8, 8);
        context.DrawLine(TargetPen, center + new Vector(-13, 0), center + new Vector(13, 0));
        context.DrawLine(TargetPen, center + new Vector(0, -13), center + new Vector(0, 13));
    }

    private void DrawOrientationGizmo(DrawingContext context)
    {
        var origin = new Point(48, Math.Max(48, Bounds.Height - 48));
        var red = new Pen(new SolidColorBrush(Color.FromRgb(210, 60, 52)), 2.2);
        var green = new Pen(new SolidColorBrush(Color.FromRgb(43, 154, 82)), 2.2);
        var blue = new Pen(new SolidColorBrush(Color.FromRgb(38, 102, 196)), 2.2);
        context.DrawLine(red, origin, origin + new Vector(23, 7));
        context.DrawLine(green, origin, origin + new Vector(-6, -24));
        context.DrawLine(blue, origin, origin + new Vector(-18, 12));
        context.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(108, 122, 140)), 1), origin, 4, 4);

        var cube = new Rect(Math.Max(8, Bounds.Width - 66), Math.Max(8, Bounds.Height - 66), 46, 46);
        context.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(214, 255, 255, 255)),
            new Pen(new SolidColorBrush(Color.FromRgb(120, 132, 147)), 1),
            cube);
        var cubeCenter = cube.Center;
        context.DrawLine(red, cubeCenter, cubeCenter + new Vector(15, 5));
        context.DrawLine(green, cubeCenter, cubeCenter + new Vector(-4, -16));
        context.DrawLine(blue, cubeCenter, cubeCenter + new Vector(-12, 9));
    }

    private static Pen SurfacePenFor(Layout2DSurfaceCurve surface)
    {
        return surface.IsReferencePlane ? ReferencePlanePen : SurfacePen;
    }

    private int RayColorIndex(int fieldIndex, int wavelengthIndex) =>
        RayColorMode == OpticSceneRayColorMode.Wavelength ? wavelengthIndex : fieldIndex;

    private static Pen RayPenFor(int colorIndex, bool vignetted, double thickness)
    {
        if (vignetted)
        {
            return new Pen(VignettedRayPen.Brush, thickness);
        }

        return new Pen(new SolidColorBrush(RayColors[Math.Abs(colorIndex) % RayColors.Length]), thickness);
    }

    private void DrawScaleBar(DrawingContext context, double pixelsPerUnit)
    {
        if (!double.IsFinite(pixelsPerUnit) || pixelsPerUnit <= 1e-9)
        {
            return;
        }

        var units = NiceScaleLength(120 / pixelsPerUnit);
        var width = units * pixelsPerUnit;
        var centerX = Bounds.Width / 2.0;
        var y = Math.Max(24, Bounds.Height - 24);
        var left = new Point(centerX - (width / 2.0), y);
        var right = new Point(centerX + (width / 2.0), y);
        context.DrawLine(AxisPen, left, right);
        context.DrawLine(AxisPen, left + new Vector(0, -5), left + new Vector(0, 5));
        context.DrawLine(AxisPen, right + new Vector(0, -5), right + new Vector(0, 5));
        var label = new FormattedText(
            $"{units:0.###} mm",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            12,
            Brushes.Black);
        context.DrawText(label, new Point(centerX - (label.Width / 2.0), y - 21));
    }

    private static double NiceScaleLength(double target)
    {
        var exponent = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(target, 1e-9))));
        var normalized = target / exponent;
        var factor = normalized switch
        {
            < 1.5 => 1,
            < 3.5 => 2,
            < 7.5 => 5,
            _ => 10
        };
        return factor * exponent;
    }

    private static void DrawArrow(DrawingContext context, Pen pen, IReadOnlyList<Point> points)
    {
        if (points.Count < 2)
        {
            return;
        }

        var segmentIndex = Math.Max(1, points.Count / 2);
        var start = points[segmentIndex - 1];
        var end = points[segmentIndex];
        var direction = end - start;
        var length = Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y));
        if (length < 8)
        {
            return;
        }

        var unit = direction / length;
        var normal = new Vector(-unit.Y, unit.X);
        var tip = start + (direction * 0.58);
        var basePoint = tip - (unit * 8);
        context.DrawLine(pen, tip, basePoint + (normal * 4));
        context.DrawLine(pen, tip, basePoint - (normal * 4));
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
        Func<Layout3DPoint, Point> project,
        bool clipToCutaway = false)
    {
        for (var index = 1; index < points.Count; index++)
        {
            DrawSegment3D(
                context,
                pen,
                points[index - 1],
                points[index],
                project,
                clipToCutaway);
        }
    }

    private static void DrawSegment3D(
        DrawingContext context,
        Pen pen,
        Layout3DPoint start,
        Layout3DPoint end,
        Func<Layout3DPoint, Point> project,
        bool clipToCutaway)
    {
        if (!clipToCutaway)
        {
            context.DrawLine(pen, project(start), project(end));
            return;
        }

        var startInside = IsInsideCutaway(start);
        var endInside = IsInsideCutaway(end);
        if (!startInside && !endInside)
        {
            return;
        }

        if (startInside && endInside)
        {
            context.DrawLine(pen, project(start), project(end));
            return;
        }

        var intersection = CutawayIntersection(start, end);
        context.DrawLine(
            pen,
            project(startInside ? start : intersection),
            project(endInside ? end : intersection));
    }

    private static IReadOnlyList<Layout3DPoint> ClipPolygonToCutaway(IReadOnlyList<Layout3DPoint> points)
    {
        if (points.Count == 0)
        {
            return points;
        }

        var clipped = new List<Layout3DPoint>(points.Count + 2);
        var previous = points[^1];
        var previousInside = IsInsideCutaway(previous);
        foreach (var current in points)
        {
            var currentInside = IsInsideCutaway(current);
            if (currentInside != previousInside)
            {
                clipped.Add(CutawayIntersection(previous, current));
            }

            if (currentInside)
            {
                clipped.Add(current);
            }

            previous = current;
            previousInside = currentInside;
        }

        return clipped;
    }

    private static bool IsInsideCutaway(Layout3DPoint point) => point.X <= 1e-9;

    private static Layout3DPoint CutawayIntersection(Layout3DPoint start, Layout3DPoint end)
    {
        var deltaX = end.X - start.X;
        if (Math.Abs(deltaX) < 1e-12)
        {
            return new Layout3DPoint(0, start.Y, start.Z);
        }

        var ratio = -start.X / deltaX;
        return new Layout3DPoint(
            0,
            start.Y + ((end.Y - start.Y) * ratio),
            start.Z + ((end.Z - start.Z) * ratio));
    }

    private static void AddProjectedFace(
        ICollection<ProjectedFace> faces,
        IReadOnlyList<Layout3DPoint> points,
        IBrush fill,
        Func<Layout3DPoint, double> depth)
    {
        if (points.Count < 3)
        {
            return;
        }

        faces.Add(new ProjectedFace(points, points.Average(depth), fill));
    }

    private static void DrawProjectedPolygon(
        DrawingContext context,
        IBrush fill,
        IReadOnlyList<Layout3DPoint> points,
        Func<Layout3DPoint, Point> project)
    {
        if (points.Count < 3)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(project(points[0]), true);
            for (var index = 1; index < points.Count; index++)
            {
                stream.LineTo(project(points[index]));
            }

            stream.EndFigure(true);
        }

        context.DrawGeometry(fill, null, geometry);
    }

    private sealed record ProjectedFace(
        IReadOnlyList<Layout3DPoint> Points,
        double Depth,
        IBrush Fill);
}
