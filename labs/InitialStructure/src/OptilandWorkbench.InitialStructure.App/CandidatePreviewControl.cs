using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.App;

public sealed class CandidatePreviewControl : Control
{
    private static readonly IBrush AxisBrush = new SolidColorBrush(Color.Parse("#7A818A"));
    private static readonly IBrush PrimaryBrush = new SolidColorBrush(Color.Parse("#168A72"));
    private static readonly IBrush SecondaryBrush = new SolidColorBrush(Color.Parse("#D05A47"));
    private static readonly IBrush StopBrush = new SolidColorBrush(Color.Parse("#D69A26"));
    private CandidateSnapshot? _primary;
    private CandidateSnapshot? _secondary;

    public CandidateSnapshot? Primary
    {
        get => _primary;
        set
        {
            _primary = value;
            InvalidateVisual();
        }
    }

    public CandidateSnapshot? Secondary
    {
        get => _secondary;
        set
        {
            _secondary = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds.Deflate(new Thickness(18));
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        var centerY = bounds.Center.Y;
        context.DrawLine(new Pen(AxisBrush, 1),
            new Point(bounds.Left, centerY),
            new Point(bounds.Right, centerY));
        if (_secondary is not null)
        {
            DrawCandidate(context, bounds, _secondary.Optic, SecondaryBrush, 1.5);
        }
        if (_primary is not null)
        {
            DrawCandidate(context, bounds, _primary.Optic, PrimaryBrush, 2.2);
        }
    }

    private static void DrawCandidate(
        DrawingContext context,
        Rect bounds,
        OpticSnapshot optic,
        IBrush brush,
        double thickness)
    {
        if (optic.Surfaces.Count < 3)
        {
            return;
        }

        var positions = SurfacePositions(optic.Surfaces);
        const int firstPhysical = 1;
        var imageIndex = optic.Surfaces.Count - 1;
        var minimumZ = positions[firstPhysical];
        var maximumZ = positions[imageIndex];
        if (!(maximumZ > minimumZ))
        {
            maximumZ = minimumZ + 1;
        }

        var maximumSemiDiameter = optic.Surfaces
            .Skip(firstPhysical)
            .Take(imageIndex - firstPhysical)
            .Max(surface => Math.Max(0.1, surface.SemiDiameter));
        var xScale = bounds.Width / (maximumZ - minimumZ);
        var yScale = (bounds.Height * 0.42) / maximumSemiDiameter;
        double X(double z) => bounds.Left + ((z - minimumZ) * xScale);
        double Y(double height) => bounds.Center.Y - (height * yScale);

        for (var surfaceIndex = firstPhysical; surfaceIndex < imageIndex; surfaceIndex++)
        {
            var surface = optic.Surfaces[surfaceIndex];
            DrawSurface(
                context,
                X(positions[surfaceIndex]),
                Y,
                surface,
                xScale,
                surface.IsStop ? StopBrush : brush,
                surface.IsStop ? thickness + 1 : thickness);
        }

        for (var frontIndex = firstPhysical; frontIndex + 1 < imageIndex; frontIndex += 2)
        {
            var front = optic.Surfaces[frontIndex];
            var back = optic.Surfaces[frontIndex + 1];
            var semiDiameter = Math.Min(front.SemiDiameter, back.SemiDiameter);
            var frontX = X(positions[frontIndex]);
            var backX = X(positions[frontIndex + 1]);
            context.DrawLine(
                new Pen(brush, Math.Max(1, thickness - 0.5)),
                new Point(frontX, Y(semiDiameter)),
                new Point(backX, Y(semiDiameter)));
            context.DrawLine(
                new Pen(brush, Math.Max(1, thickness - 0.5)),
                new Point(frontX, Y(-semiDiameter)),
                new Point(backX, Y(-semiDiameter)));
        }

        var imageX = X(positions[imageIndex]);
        context.DrawLine(
            new Pen(brush, thickness),
            new Point(imageX, Y(maximumSemiDiameter)),
            new Point(imageX, Y(-maximumSemiDiameter)));
    }

    private static void DrawSurface(
        DrawingContext context,
        double vertexX,
        Func<double, double> yMap,
        SurfaceSnapshot surface,
        double xScale,
        IBrush brush,
        double thickness)
    {
        const int segmentCount = 32;
        var semiDiameter = Math.Max(0.1, surface.SemiDiameter);
        var curvature = Math.Abs(surface.Radius) < 1e-12 ? 0 : 1 / surface.Radius;
        Point? previous = null;
        for (var index = 0; index <= segmentCount; index++)
        {
            var height = -semiDiameter + ((2 * semiDiameter * index) / segmentCount);
            var root = Math.Sqrt(Math.Max(0, 1 - (curvature * curvature * height * height)));
            var sag = Math.Abs(curvature) < 1e-12
                ? 0
                : (curvature * height * height) / (1 + root);
            var point = new Point(vertexX + (sag * xScale), yMap(height));
            if (previous is { } start)
            {
                context.DrawLine(new Pen(brush, thickness), start, point);
            }
            previous = point;
        }
    }

    private static double[] SurfacePositions(IReadOnlyList<SurfaceSnapshot> surfaces)
    {
        var positions = new double[surfaces.Count];
        var fallback = 0.0;
        for (var index = 0; index < surfaces.Count; index++)
        {
            positions[index] = surfaces[index].CoordinateSystem is { } coordinate
                && double.IsFinite(coordinate.OriginZ)
                ? coordinate.OriginZ
                : fallback;
            if (double.IsFinite(surfaces[index].Thickness))
            {
                fallback = positions[index] + surfaces[index].Thickness;
            }
        }
        return positions;
    }
}
