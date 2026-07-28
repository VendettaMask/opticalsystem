using OptilandWorkbench.Core.Backend;

namespace OptilandWorkbench.Core.Rays;

internal readonly record struct RayState(
    Vector3D Origin,
    Vector3D Direction,
    double WavelengthNanometers,
    double Intensity,
    double OpticalPathDifference,
    Matrix3x3? PolarizationMatrix,
    bool IsNormalized)
{
    public bool CanTrace => Direction.Length > 1e-12
        && double.IsFinite(Origin.X)
        && double.IsFinite(Origin.Y)
        && double.IsFinite(Origin.Z)
        && double.IsFinite(Direction.X)
        && double.IsFinite(Direction.Y)
        && double.IsFinite(Direction.Z);

    public RayState Normalize()
    {
        if (IsNormalized)
        {
            return this;
        }

        var length = Direction.Length;
        return length <= 1e-12
            ? this with { Direction = new Vector3D(0, 0, 1), IsNormalized = true }
            : this with { Direction = Direction / length, IsNormalized = true };
    }

    public RealRay ToRealRay() => new(
        Origin,
        Direction,
        WavelengthNanometers,
        Intensity,
        OpticalPathDifference,
        PolarizationMatrix,
        IsNormalized);

    public static RayState FromRealRay(RealRay ray) => new(
        ray.Origin,
        ray.Direction,
        ray.WavelengthNanometers,
        ray.Intensity,
        ray.OpticalPathDifference,
        ray.PolarizationMatrix,
        ray.IsNormalized);
}
