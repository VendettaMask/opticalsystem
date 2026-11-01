using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Phase;
using OpticalSystemDesign.Core.Rays;

namespace OpticalSystemDesign.Core.Interactions;

/// <summary>Surface phase in radians, coordinates in mm, positive optical-path convention.</summary>
public sealed class DiffractivePhaseInteractionModel(IPhaseProfile profile, bool isReflective = false) : IInteractionModel
{
    public string Kind => "diffractive_phase";
    public IPhaseProfile Profile { get; } = profile ?? throw new ArgumentNullException(nameof(profile));
    public bool IsReflective { get; } = isReflective;
    public RealRayInteractionResult Interact(RealRay ray, SurfaceInteractionContext context)
    {
        var factor = context.WavelengthNanometers * 1e-6 / (2 * Math.PI);
        var (dx, dy) = Profile.Gradient(ray.Origin.X, ray.Origin.Y, context.WavelengthNanometers);
        return ScalarSurfaceDiffraction.Interact(ray, context, IsReflective,
            factor * Profile.Phase(ray.Origin.X, ray.Origin.Y, context.WavelengthNanometers),
            factor * dx, factor * dy, Profile.Efficiency);
    }
    public ParaxialRay Interact(ParaxialRay ray, SurfaceInteractionContext context) => ray with
    { Angle = (context.RefractiveIndexBefore * ray.Angle
        + context.WavelengthNanometers * 1e-6 / (2 * Math.PI) * Profile.ParaxialGradient(ray.Height, context.WavelengthNanometers))
            / context.RefractiveIndexAfter };
    public IInteractionModel Clone() => new DiffractivePhaseInteractionModel(Profile.Clone(), IsReflective);
}

internal static class ScalarSurfaceDiffraction
{
    internal static RealRayInteractionResult Interact(RealRay ray, SurfaceInteractionContext context, bool isReflective,
        double path, double dx, double dy, double efficiency = 1)
    {
        var reflective = isReflective || context.IsReflective;
        var before = context.RefractiveIndexBefore;
        var after = reflective ? before : context.RefractiveIndexAfter;
        var normal = context.SurfaceNormal;
        if (Dot(normal, ray.Direction) < 0) normal = -normal;
        var momentum = before * ray.Direction + new Vector3D(dx, dy, 0);
        var tangent = momentum - Dot(momentum, normal) * normal;
        var radicand = after * after - Dot(tangent, tangent);
        var direction = radicand < 0 || !double.IsFinite(radicand) ? new Vector3D(double.NaN, double.NaN, double.NaN)
            : (tangent + (reflective ? -1 : 1) * Math.Sqrt(radicand) * normal) / after;
        if (!double.IsFinite(path) || !double.IsFinite(dx) || !double.IsFinite(dy))
            throw new ArithmeticException("衍射相位或梯度不是有限值。");
        return new(ray with { Direction = direction, IsNormalized = true,
            OpticalPathDifference = ray.OpticalPathDifference + path,
            Intensity = radicand < 0 ? 0 : ray.Intensity * efficiency },
            reflective ? RayInteractionKind.Reflected : RayInteractionKind.Transmitted);
    }
    private static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
