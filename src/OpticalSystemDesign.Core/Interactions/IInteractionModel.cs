using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Rays;

namespace OpticalSystemDesign.Core.Interactions;

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
    IGeometry? Geometry = null,
    Vector3D? IncidentDirection = null,
    double? ExtinctionCoefficientBefore = null,
    double? ExtinctionCoefficientAfter = null,
    double? IncidenceCosineSquared = null);
