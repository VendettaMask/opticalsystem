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
