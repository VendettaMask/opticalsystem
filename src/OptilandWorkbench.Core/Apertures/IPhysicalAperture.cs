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

public sealed class RectangularAperture : IPhysicalAperture
{
    public RectangularAperture(double halfWidth, double halfHeight)
    {
        HalfWidth = Math.Max(0.001, halfWidth);
        HalfHeight = Math.Max(0.001, halfHeight);
    }

    public string Kind => "rectangular";

    public double HalfWidth { get; set; }

    public double HalfHeight { get; set; }

    public bool Contains(Vector3D localPoint)
    {
        return Math.Abs(localPoint.X) <= HalfWidth && Math.Abs(localPoint.Y) <= HalfHeight;
    }

    public IPhysicalAperture Clone()
    {
        return new RectangularAperture(HalfWidth, HalfHeight);
    }
}
