using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Interactions;

public interface IInteractionModel
{
    string Kind { get; }

    RealRayInteractionResult Interact(RealRay ray, SurfaceInteractionContext context);

    ParaxialRay Interact(ParaxialRay ray, SurfaceInteractionContext context);

    IInteractionModel Clone();
}

public enum RayInteractionKind
{
    Transmitted,
    Reflected,
    TotalInternalReflection
}

public sealed record RealRayInteractionResult(
    RealRay Ray,
    RayInteractionKind Kind)
{
    public bool IsReflective =>
        Kind is RayInteractionKind.Reflected or RayInteractionKind.TotalInternalReflection;
}

public sealed record SurfaceInteractionContext(
    Vector3D SurfaceNormal,
    double RefractiveIndexBefore,
    double RefractiveIndexAfter,
    double WavelengthNanometers,
    bool IsReflective,
    IGeometry? Geometry = null);
