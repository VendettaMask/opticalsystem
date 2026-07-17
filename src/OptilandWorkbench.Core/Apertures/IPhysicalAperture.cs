using OptilandWorkbench.Core.Backend;

namespace OptilandWorkbench.Core.Apertures;

public interface IPhysicalAperture
{
    string Kind { get; }

    bool Contains(Vector3D localPoint);

    IPhysicalAperture Clone();
}

public sealed class CircularAperture : IPhysicalAperture
{
    public CircularAperture(double radius)
    {
        Radius = Math.Max(0.001, radius);
    }

    public string Kind => "circular";

    public double Radius { get; set; }

    public bool Contains(Vector3D localPoint)
    {
        return ((localPoint.X * localPoint.X) + (localPoint.Y * localPoint.Y)) <= Radius * Radius;
    }

    public IPhysicalAperture Clone()
    {
        return new CircularAperture(Radius);
    }
}

public sealed class AnnularAperture : IPhysicalAperture
{
    public AnnularAperture(double outerRadius, double innerRadius)
    {
        OuterRadius = Math.Max(0.001, outerRadius);
        InnerRadius = Math.Clamp(innerRadius, 0, OuterRadius);
    }

    public string Kind => "annular";

    public double OuterRadius { get; set; }

    public double InnerRadius { get; set; }

    public bool Contains(Vector3D localPoint)
    {
        var radiusSquared = (localPoint.X * localPoint.X) + (localPoint.Y * localPoint.Y);
        return radiusSquared <= OuterRadius * OuterRadius && radiusSquared >= InnerRadius * InnerRadius;
    }

    public IPhysicalAperture Clone()
    {
        return new AnnularAperture(OuterRadius, InnerRadius);
    }
}

public sealed class OffsetRadialAperture : IPhysicalAperture
{
    public OffsetRadialAperture(
        double outerRadius,
        double innerRadius = 0,
        double offsetX = 0,
        double offsetY = 0)
    {
        OuterRadius = Math.Max(0.001, outerRadius);
        InnerRadius = Math.Clamp(innerRadius, 0, OuterRadius);
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    public string Kind => "offset_radial";

    public double OuterRadius { get; set; }

    public double InnerRadius { get; set; }

    public double OffsetX { get; set; }

    public double OffsetY { get; set; }

    public bool Contains(Vector3D localPoint)
    {
        var x = localPoint.X - OffsetX;
        var y = localPoint.Y - OffsetY;
        var radiusSquared = (x * x) + (y * y);
        return radiusSquared <= OuterRadius * OuterRadius && radiusSquared >= InnerRadius * InnerRadius;
    }

    public IPhysicalAperture Clone()
    {
        return new OffsetRadialAperture(OuterRadius, InnerRadius, OffsetX, OffsetY);
    }
}

public sealed class RectangularAperture : IPhysicalAperture
{
    public RectangularAperture(
        double halfWidth,
        double halfHeight,
        double centerX = 0,
        double centerY = 0)
    {
        HalfWidth = Math.Max(0.001, halfWidth);
        HalfHeight = Math.Max(0.001, halfHeight);
        CenterX = centerX;
        CenterY = centerY;
    }

    public string Kind => "rectangular";

    public double HalfWidth { get; set; }

    public double HalfHeight { get; set; }

    public double CenterX { get; set; }

    public double CenterY { get; set; }

    public double XMinimum => CenterX - HalfWidth;

    public double XMaximum => CenterX + HalfWidth;

    public double YMinimum => CenterY - HalfHeight;

    public double YMaximum => CenterY + HalfHeight;

    public bool Contains(Vector3D localPoint)
    {
        return Math.Abs(localPoint.X - CenterX) <= HalfWidth
            && Math.Abs(localPoint.Y - CenterY) <= HalfHeight;
    }

    public IPhysicalAperture Clone()
    {
        return new RectangularAperture(HalfWidth, HalfHeight, CenterX, CenterY);
    }
}

public sealed class EllipticalAperture : IPhysicalAperture
{
    public EllipticalAperture(
        double semiAxisX,
        double semiAxisY,
        double offsetX = 0,
        double offsetY = 0)
    {
        SemiAxisX = Math.Max(0.001, semiAxisX);
        SemiAxisY = Math.Max(0.001, semiAxisY);
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    public string Kind => "elliptical";

    public double SemiAxisX { get; set; }

    public double SemiAxisY { get; set; }

    public double OffsetX { get; set; }

    public double OffsetY { get; set; }

    public bool Contains(Vector3D localPoint)
    {
        var x = (localPoint.X - OffsetX) / SemiAxisX;
        var y = (localPoint.Y - OffsetY) / SemiAxisY;
        return (x * x) + (y * y) <= 1;
    }

    public IPhysicalAperture Clone()
    {
        return new EllipticalAperture(SemiAxisX, SemiAxisY, OffsetX, OffsetY);
    }
}

public class PolygonAperture : IPhysicalAperture
{
    public PolygonAperture(IEnumerable<(double X, double Y)> vertices)
    {
        Vertices = vertices.ToArray();
    }

    public virtual string Kind => "polygon";

    public IReadOnlyList<(double X, double Y)> Vertices { get; }

    public bool Contains(Vector3D localPoint)
    {
        if (Vertices.Count < 3)
        {
            return false;
        }

        var inside = false;
        for (var current = 0; current < Vertices.Count; current++)
        {
            var previous = current == 0 ? Vertices.Count - 1 : current - 1;
            var a = Vertices[previous];
            var b = Vertices[current];
            if (PointOnSegment(localPoint.X, localPoint.Y, a, b))
            {
                return true;
            }

            var crosses = (a.Y > localPoint.Y) != (b.Y > localPoint.Y)
                && localPoint.X < (((b.X - a.X) * (localPoint.Y - a.Y)) / (b.Y - a.Y)) + a.X;
            if (crosses)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    public virtual IPhysicalAperture Clone()
    {
        return new PolygonAperture(Vertices);
    }

    private static bool PointOnSegment(
        double x,
        double y,
        (double X, double Y) a,
        (double X, double Y) b)
    {
        var cross = ((x - a.X) * (b.Y - a.Y)) - ((y - a.Y) * (b.X - a.X));
        if (Math.Abs(cross) > 1e-10)
        {
            return false;
        }

        var dot = ((x - a.X) * (b.X - a.X)) + ((y - a.Y) * (b.Y - a.Y));
        var lengthSquared = ((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y));
        return dot >= -1e-10 && dot <= lengthSquared + 1e-10;
    }
}

public sealed class FileAperture : PolygonAperture
{
    public FileAperture(
        IEnumerable<(double X, double Y)> vertices,
        string filePath,
        string? delimiter,
        int skipHeader)
        : base(vertices)
    {
        FilePath = filePath;
        Delimiter = delimiter;
        SkipHeader = Math.Max(0, skipHeader);
    }

    public override string Kind => "file";

    public string FilePath { get; }

    public string? Delimiter { get; }

    public int SkipHeader { get; }

    public override IPhysicalAperture Clone()
    {
        return new FileAperture(Vertices, FilePath, Delimiter, SkipHeader);
    }
}

public abstract class BooleanAperture : IPhysicalAperture
{
    protected BooleanAperture(IPhysicalAperture left, IPhysicalAperture right)
    {
        Left = left;
        Right = right;
    }

    public abstract string Kind { get; }

    public IPhysicalAperture Left { get; }

    public IPhysicalAperture Right { get; }

    public abstract bool Contains(Vector3D localPoint);

    public abstract IPhysicalAperture Clone();
}

public sealed class UnionAperture : BooleanAperture
{
    public UnionAperture(IPhysicalAperture left, IPhysicalAperture right)
        : base(left, right)
    {
    }

    public override string Kind => "union";

    public override bool Contains(Vector3D localPoint)
    {
        return Left.Contains(localPoint) || Right.Contains(localPoint);
    }

    public override IPhysicalAperture Clone()
    {
        return new UnionAperture(Left.Clone(), Right.Clone());
    }
}

public sealed class IntersectionAperture : BooleanAperture
{
    public IntersectionAperture(IPhysicalAperture left, IPhysicalAperture right)
        : base(left, right)
    {
    }

    public override string Kind => "intersection";

    public override bool Contains(Vector3D localPoint)
    {
        return Left.Contains(localPoint) && Right.Contains(localPoint);
    }

    public override IPhysicalAperture Clone()
    {
        return new IntersectionAperture(Left.Clone(), Right.Clone());
    }
}

public sealed class DifferenceAperture : BooleanAperture
{
    public DifferenceAperture(IPhysicalAperture left, IPhysicalAperture right)
        : base(left, right)
    {
    }

    public override string Kind => "difference";

    public override bool Contains(Vector3D localPoint)
    {
        return Left.Contains(localPoint) && !Right.Contains(localPoint);
    }

    public override IPhysicalAperture Clone()
    {
        return new DifferenceAperture(Left.Clone(), Right.Clone());
    }
}

public readonly record struct PhysicalApertureBounds(
    double XMinimum,
    double XMaximum,
    double YMinimum,
    double YMaximum);

public static class PhysicalApertureBoundsCalculator
{
    public static bool TryGetBounds(IPhysicalAperture? aperture, out PhysicalApertureBounds bounds)
    {
        switch (aperture)
        {
            case CircularAperture circular:
                bounds = new(-circular.Radius, circular.Radius, -circular.Radius, circular.Radius);
                return true;
            case AnnularAperture annular:
                bounds = new(-annular.OuterRadius, annular.OuterRadius, -annular.OuterRadius, annular.OuterRadius);
                return true;
            case OffsetRadialAperture offset:
                bounds = new(
                    offset.OffsetX - offset.OuterRadius,
                    offset.OffsetX + offset.OuterRadius,
                    offset.OffsetY - offset.OuterRadius,
                    offset.OffsetY + offset.OuterRadius);
                return true;
            case RectangularAperture rectangular:
                bounds = new(
                    rectangular.XMinimum,
                    rectangular.XMaximum,
                    rectangular.YMinimum,
                    rectangular.YMaximum);
                return true;
            case EllipticalAperture elliptical:
                bounds = new(
                    elliptical.OffsetX - elliptical.SemiAxisX,
                    elliptical.OffsetX + elliptical.SemiAxisX,
                    elliptical.OffsetY - elliptical.SemiAxisY,
                    elliptical.OffsetY + elliptical.SemiAxisY);
                return true;
            case PolygonAperture polygon when polygon.Vertices.Count > 0:
                bounds = new(
                    polygon.Vertices.Min(vertex => vertex.X),
                    polygon.Vertices.Max(vertex => vertex.X),
                    polygon.Vertices.Min(vertex => vertex.Y),
                    polygon.Vertices.Max(vertex => vertex.Y));
                return true;
            case BooleanAperture boolean
                when TryGetBounds(boolean.Left, out var left)
                && TryGetBounds(boolean.Right, out var right):
                bounds = new(
                    Math.Min(left.XMinimum, right.XMinimum),
                    Math.Max(left.XMaximum, right.XMaximum),
                    Math.Min(left.YMinimum, right.YMinimum),
                    Math.Max(left.YMaximum, right.YMaximum));
                return true;
            default:
                bounds = default;
                return false;
        }
    }
}
