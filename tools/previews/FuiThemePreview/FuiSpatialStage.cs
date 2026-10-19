using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.FuiThemePreview;

/// <summary>Display projection of shared-Core meshes only; no optical geometry or ray calculation.</summary>
internal sealed class FuiSpatialStage : Control
{
    private Point? _drag;
    private double _yaw = 0.65;
    private double _pitch = 0.42;
    private double _zoom = 1;
    private SceneDto? _scene;
    internal SceneDto? Scene { get => _scene; set { _scene = value; InvalidateVisual(); } }
    internal bool ShowGuides { get; set; } = true;

    internal FuiSpatialStage()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    internal void ResetCamera() { _yaw = 0.65; _pitch = 0.42; _zoom = 1; InvalidateVisual(); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var area = new Rect(Bounds.Size);
        context.FillRectangle(new LinearGradientBrush
        {
            StartPoint = RelativePoint.TopLeft,
            EndPoint = RelativePoint.BottomRight,
            GradientStops = { new(Color.Parse("#0B1A20"), 0), new(Color.Parse("#050D11"), 0.65), new(Color.Parse("#08171C"), 1) }
        }, area);
        if (ShowGuides) DrawGrid(context, area);
        if (Scene?.ThreeDimensional is not { } model) return;
        var surfaces = model.Surfaces.Where(surface => !surface.IsReferencePlane && surface.Faces.Count > 0).ToArray();
        var points = surfaces.SelectMany(surface => surface.Rim.Concat(surface.MeridianX).Concat(surface.MeridianY)).ToArray();
        if (points.Length == 0) return;
        Point Rotate(ScenePoint3Dto p) => new(p.Z * Math.Cos(_yaw) + p.X * Math.Sin(_yaw),
            -p.Y * Math.Cos(_pitch) + (p.Z * Math.Sin(_yaw) - p.X * Math.Cos(_yaw)) * Math.Sin(_pitch));
        var projected = points.Select(Rotate).ToArray();
        var minX = projected.Min(p => p.X); var maxX = projected.Max(p => p.X);
        var minY = projected.Min(p => p.Y); var maxY = projected.Max(p => p.Y);
        var scale = Math.Min(area.Width * 0.72 / Math.Max(1, maxX - minX), area.Height * 0.60 / Math.Max(1, maxY - minY)) * _zoom;
        Point Project(ScenePoint3Dto p) { var q = Rotate(p); return new(area.Width * 0.50 + (q.X - (minX + maxX) / 2) * scale, area.Height * 0.51 + (q.Y - (minY + maxY) / 2) * scale); }
        var wire = new Pen(B("#566D6B"), 0.45);
        var rim = new Pen(B("#C29858"), 0.9);
        var meridian = new Pen(B("#9FB4AF"), 0.65);
        foreach (var surface in surfaces.OrderByDescending(surface => surface.SurfaceNumber))
        {
            foreach (var face in surface.Faces.Where((_, index) => index % 2 == 0))
            {
                if (face.Points.Count < 3) continue;
                var geometry = new StreamGeometry();
                using (var drawing = geometry.Open())
                {
                    drawing.BeginFigure(Project(face.Points[0]), true);
                    foreach (var point in face.Points.Skip(1)) drawing.LineTo(Project(point));
                    drawing.EndFigure(true);
                }
                context.DrawGeometry(new SolidColorBrush(Color.FromArgb(17, 135, 172, 168)), wire, geometry);
            }
            Polyline(context, rim, surface.Rim.Select(Project));
            Polyline(context, meridian, surface.MeridianX.Select(Project));
            Polyline(context, meridian, surface.MeridianY.Select(Project));
        }
        foreach (var element in model.LensElements)
        {
            for (var i = 0; i < Math.Min(element.FrontRim.Count, element.BackRim.Count); i += 4)
                context.DrawLine(new Pen(B("#735D3D"), 0.6), Project(element.FrontRim[i]), Project(element.BackRim[i]));
        }
        if (ShowGuides)
        {
            foreach (var element in model.LensElements)
            {
                var target = element.FrontRim.Select(Project).MinBy(point => point.Y);
                var end = new Point(Math.Clamp(target.X - 50, 90, area.Width - 140), Math.Clamp(target.Y - 64, 70, area.Height - 100));
                context.DrawEllipse(null, new Pen(B("#B5BFB5"), 0.7), target, 3, 3);
                Polyline(context, new Pen(B("#82978F"), 0.65), [target, new Point(end.X + 26, end.Y + 16), new Point(end.X + 124, end.Y + 16)]);
                Label(context, $"S{element.FrontSurfaceNumber:00}—{element.BackSurfaceNumber:00}  /  {element.Material}", end, "#B2BEB4", 10);
            }
            Label(context, "STRUCTURAL MESH  /  SHARED CORE", new Point(18, area.Height - 42), "#7A9290", 10);
            Label(context, "拖动旋转 · 滚轮缩放   /   结构显示，不代表像质验收", new Point(18, area.Height - 25), "#607D82", 10);
        }
        Label(context, "01  /  SPATIAL ASSEMBLY", new Point(18, 18), "#B5BFB5", 11);
        Label(context, $"REV {Scene.SourceRevision:000000}  ·  {model.LensElements.Count:00} ELEMENTS", new Point(18, 37), "#607C80", 10);
    }

    private static void DrawGrid(DrawingContext context, Rect area)
    {
        var minor = new Pen(B("#112329"), 0.55);
        for (var x = 16d; x < area.Width; x += 28) context.DrawLine(minor, new(x, 0), new(x, area.Height));
        for (var y = 16d; y < area.Height; y += 28) context.DrawLine(minor, new(0, y), new(area.Width, y));
        var mark = new Pen(B("#2F4749"), 0.7);
        for (var x = 16d; x < area.Width; x += 112)
            for (var y = 16d; y < area.Height; y += 112)
            { context.DrawLine(mark, new(x - 2, y), new(x + 2, y)); context.DrawLine(mark, new(x, y - 2), new(x, y + 2)); }
        var arc = new Pen(B("#243C40"), 0.7);
        context.DrawEllipse(null, arc, new Point(area.Width * 0.52, area.Height * 0.55), area.Width * 0.44, area.Height * 0.40);
        context.DrawEllipse(null, new Pen(B("#152C31"), 0.7), new Point(area.Width * 0.52, area.Height * 0.55), area.Width * 0.47, area.Height * 0.44);
        context.DrawLine(new Pen(B("#395053"), 0.7), new(18, area.Height - 58), new(100, area.Height - 58));
    }

    private static void Polyline(DrawingContext context, IPen pen, IEnumerable<Point> points)
    { Point? last = null; foreach (var point in points) { if (last is { } start) context.DrawLine(pen, start, point); last = point; } }
    private static SolidColorBrush B(string value) => new(Color.Parse(value));
    private static void Label(DrawingContext context, string value, Point point, string color, double size) =>
        context.DrawText(new FormattedText(value, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Cascadia Mono, Consolas, Microsoft YaHei UI"), size, B(color)), point);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    { base.OnPointerPressed(e); _drag = e.GetPosition(this); e.Pointer.Capture(this); Focus(); e.Handled = true; }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is not { } last) return;
        var point = e.GetPosition(this); _yaw += (point.X - last.X) * 0.006;
        _pitch = Math.Clamp(_pitch + (point.Y - last.Y) * 0.006, -1.2, 1.2); _drag = point; InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { base.OnPointerReleased(e); _drag = null; e.Pointer.Capture(null); }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    { base.OnPointerWheelChanged(e); _zoom = Math.Clamp(_zoom * Math.Pow(1.12, e.Delta.Y), 0.5, 2.5); InvalidateVisual(); e.Handled = true; }
}
