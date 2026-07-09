using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.App.Controls;

public sealed class OpticSceneControl : Control
{
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(250, 252, 254));
    private static readonly Pen AxisPen = new(new SolidColorBrush(Color.FromRgb(134, 146, 166)), 1);
    private static readonly Pen StopPen = new(new SolidColorBrush(Color.FromRgb(33, 96, 144)), 3);
    private static readonly Pen SurfacePen = new(new SolidColorBrush(Color.FromRgb(38, 50, 56)), 2);
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

        var padding = 28.0;
        var width = Math.Max(1, Bounds.Width - (padding * 2));
        var height = Math.Max(1, Bounds.Height - (padding * 2));
        var centerY = padding + (height / 2.0);
        var totalTrack = Math.Max(1, Optic.SurfaceGroup.TotalTrack);
        var aperture = Math.Max(1, Optic.SurfaceGroup.ApertureRadius() * 1.45);

        double MapZ(double z) => padding + (z / totalTrack * width);
        double MapY(double y) => centerY - (y / aperture * height / 2.0);

        context.DrawLine(AxisPen, new Point(padding, centerY), new Point(padding + width, centerY));

        DrawSurfaces(context, Optic.SurfaceGroup.Items, MapZ, MapY);
        DrawRays(context, Optic.RealRayTracer.TraceMeridionalRays(5), MapZ, MapY);
    }

    private static void DrawSurfaces(
        DrawingContext context,
        IReadOnlyList<OpticalSurface> surfaces,
        Func<double, double> mapZ,
        Func<double, double> mapY)
    {
        var z = 0.0;
        foreach (var surface in surfaces)
        {
            var x = mapZ(z);
            var top = mapY(surface.SemiDiameter);
            var bottom = mapY(-surface.SemiDiameter);
            context.DrawLine(surface.IsStop ? StopPen : SurfacePen, new Point(x, top), new Point(x, bottom));
            z += surface.Thickness;
        }
    }

    private static void DrawRays(
        DrawingContext context,
        RayTraceResult trace,
        Func<double, double> mapZ,
        Func<double, double> mapY)
    {
        var index = 0;
        foreach (var path in trace.Paths)
        {
            var pen = path.Vignetted ? VignettedRayPen : RayPens[index % RayPens.Length];
            foreach (var segment in path.Segments)
            {
                context.DrawLine(
                    pen,
                    new Point(mapZ(segment.Start.Z), mapY(segment.Start.Y)),
                    new Point(mapZ(segment.End.Z), mapY(segment.End.Y)));
            }

            index++;
        }
    }
}
