using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;

namespace OptilandWorkbench.Core.Serialization;

internal sealed record ParsedSurface(
    OpticalSurface Surface,
    IGeometry Geometry,
    IInteractionModel Interaction,
    ICoatingModel Coating,
    IPhysicalAperture? Aperture,
    CoordinateSystem? CoordinateSystem);

internal sealed record ParsedInteraction(
    IInteractionModel Interaction,
    bool IsReflective,
    ICoatingModel Coating);
