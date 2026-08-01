using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Interactions;

namespace OptilandWorkbench.Core.Rays;

public sealed record RealRay(
    Vector3D Origin,
    Vector3D Direction,
    double WavelengthNanometers,
    double Intensity = 1,
    double OpticalPathDifference = 0,
    Matrix3x3? PolarizationMatrix = null,
    bool IsNormalized = true)
{
    public RealRay Normalize()
    {
        var length = Direction.Length;
        return length <= 1e-12
            ? this with { Direction = new Vector3D(0, 0, 1), IsNormalized = true }
            : this with { Direction = Direction / length, IsNormalized = true };
    }

    public bool IsAlive => Intensity > 0;

    public bool CanTrace => Direction.Length > 1e-12
        && double.IsFinite(Origin.X)
        && double.IsFinite(Origin.Y)
        && double.IsFinite(Origin.Z)
        && double.IsFinite(Direction.X)
        && double.IsFinite(Direction.Y)
        && double.IsFinite(Direction.Z);
}

public sealed record ParaxialRay(
    double Height,
    double Angle,
    double Z,
    double WavelengthNanometers,
    double Intensity = 1);

public sealed record PolarizedRay(
    RealRay Ray,
    Matrix3x3 JonesMatrix);

public sealed record RayTraceSample(
    int SurfaceNumber,
    string SurfaceLabel,
    Vector3D Position,
    Vector3D Direction,
    double Intensity,
    bool Vignetted,
    double SegmentLength = 0,
    double SegmentOpticalPathLength = 0,
    double CumulativePathLength = 0,
    double CumulativeOpticalPathLength = 0,
    double OpticalPathDifference = 0,
    RayInteractionKind? InteractionKind = null);

public sealed class RealRayBundle
{
    public RealRayBundle(IEnumerable<RealRay> rays)
    {
        Rays = rays.Select(ray => ray.Normalize()).ToList();
    }

    public IReadOnlyList<RealRay> Rays { get; }
}
