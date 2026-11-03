using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Propagation;

namespace OpticalSystemDesign.Core.Interactions;

internal static class ParaxialPhaseRules
{
    // Refraction, reflection and a constant ruled-grating momentum shift at
    // planes preserve a plane wave. No aberrated/finite beam is approximated here.
    internal static bool IsPlaneWavePrefix(IReadOnlyList<OpticalSurface> prefix) => prefix.Count > 0
        && ObjectConjugate.IsInfinite(prefix[0])
        && prefix.Skip(1).All(s => s.Geometry is PlaneGeometry or TiltedPlaneGeometry
            && s.InteractionModel is RefractiveReflectiveInteractionModel or RuledGratingInteractionModel or BirefringentInteractionModel or CoordinateReferenceInteractionModel
            && s.MaterialBefore.PropagationModel is HomogeneousPropagationModel
            && s.MaterialAfter.PropagationModel is HomogeneousPropagationModel);
}
