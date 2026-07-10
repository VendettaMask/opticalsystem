using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Visualization;

namespace OptilandWorkbench.App.Controls;

public sealed class OpticSceneControl : Control
{
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(250, 252, 254));
    private static readonly Pen AxisPen = new(new SolidColorBrush(Color.FromRgb(134, 146, 166)), 1);
    private static readonly Pen StopPen = new(new SolidColorBrush(Color.FromRgb(33, 96, 144)), 3);
    private static readonly Pen SurfacePen = new(new SolidColorBrush(Color.FromRgb(38, 50, 56)), 2);
    private static readonly Pen LensEdgePen = new(new SolidColorBrush(Color.FromRgb(87, 112, 132)), 1.4);
    private static readonly Pen VignettedRayPen = new(new SolidColorBrush(Color.FromRgb(188, 74, 60)), 1.2);
    private static readonly Pen[] RayPens =
    {
        new(new SolidColorBrush(Color.FromRgb(50, 114, 179)), 1.1),
        new(new SolidColorBrush(Color.FromRgb(61, 145, 108)), 1.1),
        new(new SolidColorBrush(Color.FromRgb(171, 107, 45)), 1.1)
    };

    public Optic? Optic { get; set; }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(BackgroundBrush, null, Bounds);

        if (Optic is null || Optic.SurfaceGroup.Items.Count == 0)
        {
            return;
        }

        var scene = new Layout2DBuilder(Optic).Build();
        var padding = 28.0;
        var width = Math.Max(1, Bounds.Width - (padding * 2));
        var height = Math.Max(1, Bounds.Height - (padding * 2));
        var centerY = padding + (height / 2.0);
        var zSpan = Math.Max(1, scene.ZMax - scene.ZMin);
        var aperture = Math.Max(1, scene.YExtent);

        double MapZ(double z) => padding + ((z - scene.ZMin) / zSpan * width);
        double MapY(double y) => centerY - (y / aperture * height / 2.0);

        context.DrawLine(AxisPen, new Point(MapZ(scene.ZMin), centerY), new Point(MapZ(scene.ZMax), centerY));

        DrawLensEdges(context, scene.LensEdges, MapZ, MapY);
        DrawSurfaces(context, scene.Surfaces, MapZ, MapY);
        DrawRays(context, scene.Rays, MapZ, MapY);
    }

    private static void DrawSurfaces(
        DrawingContext context,
        IReadOnlyList<Layout2DSurfaceCurve> surfaces,
        Func<double, double> mapZ,
        Func<double, double> mapY)
    {
        foreach (var surface in surfaces)
        {
            var pen = surface.IsStop ? StopPen : SurfacePen;
            DrawPolyline(context, pen, surface.Points, mapZ, mapY);
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
            var pen = path.Vignetted ? VignettedRayPen : RayPens[path.RayNumber % RayPens.Length];
            DrawPolyline(context, pen, path.Points, mapZ, mapY);
        }
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
}
