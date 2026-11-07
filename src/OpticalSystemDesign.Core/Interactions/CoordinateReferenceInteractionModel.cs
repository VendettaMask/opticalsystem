using OpticalSystemDesign.Core.Rays;

namespace OpticalSystemDesign.Core.Interactions;

/// <summary>A retained coordinate plane continues the incident medium without an optical interface.</summary>
public sealed class CoordinateReferenceInteractionModel(bool isIgnoredSurface = false) : IInteractionModel
{
    public string Kind => "coordinate_reference";
    public bool IsIgnoredSurface { get; } = isIgnoredSurface;

    public RealRayInteractionResult Interact(RealRay ray, SurfaceInteractionContext context)
    {
        if (context.IsReflective)
            throw new NotSupportedException("A coordinate reference plane cannot be reflective.");
        return new(ray, RayInteractionKind.Transmitted);
    }

    public ParaxialRay Interact(ParaxialRay ray, SurfaceInteractionContext context) => ray;

    public IInteractionModel Clone() => new CoordinateReferenceInteractionModel(IsIgnoredSurface);
}
