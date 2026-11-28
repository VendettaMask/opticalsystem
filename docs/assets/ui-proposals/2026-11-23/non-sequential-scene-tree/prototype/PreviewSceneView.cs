using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.Application.Contracts;

// Candidate presentation only. Geometry and ray coordinates come from the shared Core scene.
sealed class PreviewSceneView(Scene3Dto scene) : Control
{
    public event Action<int>? ObjectSelected;
    public HashSet<int> Hidden { get; } = [];
    public bool ShowRays { get; set; } = true;
    public int MaximumDisplayedRays { get; set; } = 80;
    public Func<SceneRay3Dto, bool>? RayFilter { get; set; }
    public HashSet<int> Highlighted { get; } = [];
    readonly Dictionary<int, Point[]> outlines = [];
    Size cacheSize;
    int selected = 3; double zoom = 1; Vector pan;
    (Point center, double width, double height) model = Frame(scene);
    public void SetScene(Scene3Dto replacement)
    {
        scene = replacement; model = Frame(scene); zoom = 1; pan = default; outlines.Clear(); InvalidateVisual();
    }
    public int SelectedNumber { get => selected; set { selected = value; InvalidateVisual(); } }
    static IEnumerable<ScenePoint3Dto> Points(SceneSurface3Dto surface) => surface.Faces.SelectMany(f => f.Points).Concat(surface.Rim);
    static Point Rotate(ScenePoint3Dto point) => new(
        point.Z * Math.Cos(.475) + point.X * Math.Sin(.475),
        -point.Y * Math.Cos(.255) - point.X * Math.Cos(.475) * Math.Sin(.255) + point.Z * Math.Sin(.475) * Math.Sin(.255));
    static (Point center, double width, double height) Frame(Scene3Dto scene)
    {
        var points = scene.Surfaces.SelectMany(Points).Select(Rotate).ToArray();
        var minX = points.Min(p => p.X); var maxX = points.Max(p => p.X); var minY = points.Min(p => p.Y); var maxY = points.Max(p => p.Y);
        return (new((maxX + minX) / 2, (maxY + minY) / 2), Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
    }
    Point Project(ScenePoint3Dto point)
    {
        var scale = .73 * Math.Min(Bounds.Width / model.width, Bounds.Height / model.height) * zoom; var center = model.center; var p = Rotate(point);
        return new(Bounds.Width / 2 + (p.X - center.X) * scale + pan.X, Bounds.Height * .52 + (p.Y - center.Y) * scale + pan.Y);
    }
    Point[] Outline(SceneSurface3Dto surface)
    {
        if (cacheSize != Bounds.Size) { outlines.Clear(); cacheSize = Bounds.Size; }
        if (!outlines.TryGetValue(surface.SurfaceNumber, out var points)) outlines[surface.SurfaceNumber] = points = Hull(Points(surface).Select(Project));
        return points;
    }
    public Rect ObjectBounds(int number)
    {
        var points = scene.Surfaces.Where(s => s.SurfaceNumber == number).SelectMany(Points).Select(Project).ToArray();
        return points.Length == 0 ? default : new Rect(new Point(points.Min(p => p.X), points.Min(p => p.Y)), new Point(points.Max(p => p.X), points.Max(p => p.Y)));
    }
    public Point PickPoint(int number) => ObjectBounds(number).Center;
    public void FocusObject(int number)
    {
        var box = ObjectBounds(number); zoom *= 1.1; pan += new Vector(Bounds.Width / 2 - box.Center.X, Bounds.Height / 2 - box.Center.Y); outlines.Clear(); InvalidateVisual();
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(this);
        foreach (var surface in scene.Surfaces.Reverse().Where(s => !Hidden.Contains(s.SurfaceNumber)))
            if (Inside(point, Outline(surface)))
            {
                ObjectSelected?.Invoke(surface.SurfaceNumber); if (e.ClickCount == 2) FocusObject(surface.SurfaceNumber); e.Handled = true; break;
            }
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context); if (Bounds.Width < 1 || Bounds.Height < 1) return;
        context.DrawRectangle(Brushes.White, null, new Rect(Bounds.Size)); using var clip = context.PushClip(new Rect(Bounds.Size));
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(70, 137, 158, 184)), .8, DashStyle.Dash), Project(new(0, 0, -38)), Project(new(0, 0, 80)));
        if (ShowRays)
        {
            var max = scene.Rays.Select(r => r.FinalIntensity).DefaultIfEmpty(1).Max();
            foreach (var ray in scene.Rays.Where(r => RayFilter?.Invoke(r) ?? true).Take(MaximumDisplayedRays))
            {
                var color = OpticSceneControl.NonSequentialSourceColor(ray.WavelengthNanometers);
                var alpha = (byte)Math.Clamp(155 * Math.Sqrt(Math.Max(0, ray.FinalIntensity) / max), 5, 155);
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)), .9);
                foreach (var segment in ray.Segments) context.DrawLine(pen, Project(segment.Start), Project(segment.End));
            }
        }
        foreach (var surface in scene.Surfaces.Where(s => !Hidden.Contains(s.SurfaceNumber)))
        {
            var hull = Outline(surface); if (hull.Length < 3) continue;
            var isLens = surface.Material != "Air" && surface.RenderRole != SceneSurfaceRenderRole.Source && surface.RenderRole != SceneSurfaceRenderRole.Detector;
            var geometry = Polygon(hull);
            if (isLens)
            {
                var glass = new LinearGradientBrush { StartPoint = RelativePoint.TopLeft, EndPoint = RelativePoint.BottomRight, GradientStops = {
                    new(Color.FromArgb(76, 220, 242, 249), 0), new(Color.FromArgb(88, 139, 192, 217), .5), new(Color.FromArgb(35, 218, 241, 247), 1) } };
                context.DrawGeometry(glass, new Pen(new SolidColorBrush(Color.FromRgb(87, 143, 173)), 1.05), geometry);
                if (surface.Rim.Count > 2) context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(92, 87, 143, 173)), .65), Polygon(surface.Rim.Select(Project).ToArray()));
            }
            else
            {
                var color = surface.RenderRole == SceneSurfaceRenderRole.Source ? OpticSceneControl.NonSequentialSourceColor(surface.DisplayWavelengthNanometers)
                    : surface.RenderRole == SceneSurfaceRenderRole.Detector ? Color.FromRgb(45, 139, 180) : Color.FromRgb(130, 145, 160);
                context.DrawGeometry(new SolidColorBrush(Color.FromArgb(36, color.R, color.G, color.B)), new Pen(new SolidColorBrush(Color.FromArgb(180, color.R, color.G, color.B)), 1.15), geometry);
            }
            if (surface.SurfaceNumber == selected || Highlighted.Contains(surface.SurfaceNumber))
            {
                context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(42, 232, 158, 50)), 6), geometry);
                context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(216, 143, 32)), 1.8), geometry);
            }
        }
        var origin = new Point(35, Bounds.Height - 35);
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(78, 159, 115)), 1.4), origin, origin + new Vector(0, -29));
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(72, 127, 197)), 1.4), origin, origin + new Vector(30, 4));
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(204, 100, 89)), 1.4), origin, origin + new Vector(-14, 15));
    }
    static StreamGeometry Polygon(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry(); using var context = geometry.Open();
        if (points.Count > 0) { context.BeginFigure(points[0], true); foreach (var point in points.Skip(1)) context.LineTo(point); context.EndFigure(true); } return geometry;
    }
    static double Cross(Point a, Point b, Point c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    static Point[] Hull(IEnumerable<Point> source)
    {
        var points = source.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToArray(); if (points.Length < 3) return points;
        var lower = new List<Point>(); foreach (var p in points) { while (lower.Count > 1 && Cross(lower[^2], lower[^1], p) <= 0) lower.RemoveAt(lower.Count - 1); lower.Add(p); }
        var upper = new List<Point>(); foreach (var p in points.Reverse()) { while (upper.Count > 1 && Cross(upper[^2], upper[^1], p) <= 0) upper.RemoveAt(upper.Count - 1); upper.Add(p); }
        return lower.Take(lower.Count - 1).Concat(upper.Take(upper.Count - 1)).ToArray();
    }
    static bool Inside(Point p, IReadOnlyList<Point> hull)
    {
        if (hull.Count < 3) return false;
        for (var i = 0; i < hull.Count; i++) if (Cross(hull[i], hull[(i + 1) % hull.Count], p) < 0) return false;
        return true;
    }
}
