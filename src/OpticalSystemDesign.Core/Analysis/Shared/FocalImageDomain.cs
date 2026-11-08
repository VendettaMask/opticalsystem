using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Propagation;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class FocalImageDomain
{
    public const string ResultKey = "FocalImageDomain";
    public const string CollimatedContactPupil = "CollimatedContactPupil";
    public const string CollimatedPlaneTrain = "CollimatedPlaneTrain";

    internal static bool IsCollimatedPlaneTrain(Optic optic, bool allowSegmentedMirrors = false)
    {
        var surfaces = optic.SurfaceGroup.Items;
        return !optic.ImageSpaceAfocal && optic.FieldDefinition == FieldDefinitionKind.Angle
            && surfaces.Count >= 2 && ObjectConjugate.IsInfinite(surfaces[0])
            && surfaces.Skip(1).All(surface =>
                (surface.Geometry is PlaneGeometry or TiltedPlaneGeometry
                    || surface.Geometry is StandardGeometry standard
                        && (standard.Radius == 0 || double.IsInfinity(standard.Radius))
                    || allowSegmentedMirrors && surface.Geometry is TernaryMirrorArrayGeometry)
                && surface.InteractionModel is RefractiveReflectiveInteractionModel or CoordinateReferenceInteractionModel
                && surface.ScatteringModel is null
                && surface.MaterialBefore is not (UniaxialMaterial or UnresolvedMaterial)
                && surface.MaterialAfter is not (UniaxialMaterial or UnresolvedMaterial)
                && surface.MaterialBefore.PropagationModel is HomogeneousPropagationModel
                && surface.MaterialAfter.PropagationModel is HomogeneousPropagationModel);
    }

    internal static bool IsCollimatedContactPupil(Optic optic) =>
        optic.Fields.Count > 0 && FieldCoordinates.MaximumRadius(optic.Fields) == 0
        && IsCollimatedContactPupil(optic, (0, 0));

    // A flat pupil coincident with the image has neither a focus nor an
    // angular-to-height lever arm. A uniform coating changes transmission,
    // but cannot supply either of these missing geometric references.
    internal static bool IsCollimatedPlaneAtImage(Optic optic)
    {
        var surfaces = optic.SurfaceGroup.Items;
        if (!IsCollimatedPlaneTrain(optic) || surfaces.Count < 3
            || surfaces[0].MaterialAfter is not AirMaterial ambient)
            return false;
        var image = surfaces[^1].CoordinateSystem;
        return surfaces.Skip(1).All(surface =>
            surface.Geometry is PlaneGeometry or StandardGeometry
            && surface.CoordinateSystem.Origin == image.Origin
            && surface.CoordinateSystem.RotationXDegrees == 0
            && surface.CoordinateSystem.RotationYDegrees == 0
            && surface.CoordinateSystem.RotationZDegrees == 0
            && !surface.IsReflective
            && surface.MaterialBefore is AirMaterial before && before.PressureAtmospheres == ambient.PressureAtmospheres
            && surface.MaterialAfter is AirMaterial after && after.PressureAtmospheres == ambient.PressureAtmospheres);
    }

    internal static bool IsCollimatedContactPupil(Optic optic, (double Hx, double Hy) field)
    {
        var surfaces = optic.SurfaceGroup.Items;
        if (optic.ImageSpaceAfocal || optic.FieldDefinition != FieldDefinitionKind.Angle
            || field.Hx != 0 || field.Hy != 0
            || surfaces.Count < 3 || !ObjectConjugate.IsInfinite(surfaces[0])
            || surfaces[0].MaterialAfter is not AirMaterial ambient)
            return false;

        var imageOrigin = surfaces[^1].CoordinateSystem.Origin;
        // An on-axis plane wave only crosses uncoated, non-scattering boundaries
        // in the same ambient medium. The stop and image share a vertex plane.
        // Sag and aperture masks cannot create a converging ray cone here.
        return surfaces.Skip(1).All(surface =>
            surface.CoordinateSystem.Origin == imageOrigin
            && surface.CoordinateSystem.RotationXDegrees == 0
            && surface.CoordinateSystem.RotationYDegrees == 0
            && surface.CoordinateSystem.RotationZDegrees == 0
            && surface.Thickness == 0 && !surface.IsReflective && surface.ScatteringModel is null
            && surface.CoatingModel is NoneCoatingModel
            && surface.Geometry is PlaneGeometry or StandardGeometry or GridSagGeometry
            && surface.InteractionModel is (RefractiveReflectiveInteractionModel { IsReflective: false }
                or CoordinateReferenceInteractionModel)
            && surface.MaterialBefore is AirMaterial before && before.PressureAtmospheres == ambient.PressureAtmospheres
            && surface.MaterialAfter is AirMaterial after && after.PressureAtmospheres == ambient.PressureAtmospheres);
    }

    internal static AnalysisData NotApplicable(string name, string reason, string domain = CollimatedContactPupil) => new(name,
        new Dictionary<string, object> { ["Status"] = reason, [ResultKey] = domain },
        Outcome: AnalysisOutcome.NotApplicable, OutcomeReason: reason);
}
