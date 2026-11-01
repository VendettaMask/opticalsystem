using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Rays;

namespace OpticalSystemDesign.Core.Interactions;

/// <summary>Straight grooves parallel to local X; density is in lines per micrometer.</summary>
public sealed class RuledGratingInteractionModel : IInteractionModel
{
    public RuledGratingInteractionModel(double linesPerMicrometer, int order, bool isReflective = false)
    {
        if (!double.IsFinite(linesPerMicrometer)) throw new ArgumentOutOfRangeException(nameof(linesPerMicrometer));
        if (order is < -1_000_000 or > 1_000_000) throw new ArgumentOutOfRangeException(nameof(order));
        LinesPerMicrometer = linesPerMicrometer; Order = order; IsReflective = isReflective;
    }
    public string Kind => "ruled_grating";
    public double LinesPerMicrometer { get; }
    public int Order { get; }
    public bool IsReflective { get; }

    public RealRayInteractionResult Interact(RealRay ray, SurfaceInteractionContext context)
    {
        if (context.Geometry is not (PlaneGeometry or StandardGeometry))
            throw new NotSupportedException("直线刻槽光栅目前要求平面或圆锥基底。");
        var gradient = Order * LinesPerMicrometer * context.WavelengthNanometers / 1000;
        return ScalarSurfaceDiffraction.Interact(ray, context, IsReflective, gradient * ray.Origin.Y, 0, gradient);
    }

    // Native first-order tracing deliberately ignores the groove diffraction.
    public ParaxialRay Interact(ParaxialRay ray, SurfaceInteractionContext context) =>
        new RefractiveReflectiveInteractionModel(IsReflective).Interact(ray, context);
    public IInteractionModel Clone() => new RuledGratingInteractionModel(LinesPerMicrometer, Order, IsReflective);
}
