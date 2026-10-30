using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Core.Services;

public sealed record CenteredPrescriptionDescriptor(double EffectiveFocalLengthMillimeters, double FNumber,
    double MaximumHalfFieldDegrees, double BackWorkingSpaceMillimeters, double BackFocalLengthMillimeters);

/// <summary>Uniform dimensional transformation of an explicitly supported centered prescription, never a focus solve.</summary>
public static class CenteredPrescriptionScaling
{
    public static CenteredPrescriptionDescriptor Describe(Optic optic, int lastGlassSurfaceIndex)
    {
        ArgumentNullException.ThrowIfNull(optic);
        if (lastGlassSurfaceIndex <= 0 || lastGlassSurfaceIndex >= optic.SurfaceGroup.Items.Count - 1
            || optic.FieldDefinition != FieldDefinitionKind.Angle
            || optic.SurfaceGroup.Items[lastGlassSurfaceIndex].MaterialBefore is not Materials.CatalogGlassMaterial
            || optic.SurfaceGroup.Items[lastGlassSurfaceIndex].MaterialAfter is not Materials.AirMaterial
            || optic.SurfaceGroup.Items.Skip(lastGlassSurfaceIndex + 1).Any(surface => surface.MaterialAfter is not Materials.AirMaterial))
            throw new NotSupportedException("A physical last glass surface and angular fields are required.");
        var focal = optic.Paraxial.EstimateEffectiveFocalLength();
        var fNumber = optic.Paraxial.EstimateFNumber();
        var cardinal = optic.Paraxial.EstimateCardinalPoints();
        var lastZ = optic.SurfaceGroup.Items[lastGlassSurfaceIndex].CoordinateSystem.Origin.Z;
        var result = new CenteredPrescriptionDescriptor(focal, fNumber,
            FieldCoordinates.MaximumRadius(optic.Fields.Where(field => field.Weight > 0)),
            optic.SurfaceGroup.Items[^1].CoordinateSystem.Origin.Z - lastZ, cardinal.BackFocalPosition - lastZ);
        if (!double.IsFinite(result.EffectiveFocalLengthMillimeters) || focal <= 0 || !double.IsFinite(fNumber) || fNumber <= 0
            || !double.IsFinite(result.BackWorkingSpaceMillimeters) || !double.IsFinite(result.BackFocalLengthMillimeters)
            || !double.IsFinite(result.MaximumHalfFieldDegrees))
            throw new ArithmeticException("The prescription has no finite positive retrieval descriptor.");
        return result;
    }

    public static Optic Scale(OpticSnapshot source, double factor)
    {
        if (!double.IsFinite(factor) || factor is < .001 or > 1000) throw new ArgumentOutOfRangeException(nameof(factor));
        var optic = Optic.FromSnapshot(source);
        if (optic.Aperture.Kind != ApertureKind.FloatByStopSize || !ObjectConjugate.IsInfinite(optic.SurfaceGroup.Items[0])
            || source.FieldDefinition != "Angle" || source.ImageSpaceAfocal || source.Apodization is not null
            || source.RadiusPickups is { Count: > 0 } || source.ThicknessPickups is { Count: > 0 } || source.SemiDiameterPickups is { Count: > 0 }
            || source.MeritOperands is { Count: > 0 } || source.SolveSettings?.KeepImageAtBackFocus != false || source.GlobalReferenceSurfaceNumber != 1)
            throw new NotSupportedException("Uniform centered scaling requires infinite object and actual stop-sized pupil.");
        for (var index = 0; index < optic.SurfaceGroup.Items.Count; index++)
        {
            var surface = optic.SurfaceGroup.Items[index];
            var frame = surface.CoordinateSystem;
            if (frame.Origin.X != 0 || frame.Origin.Y != 0 || frame.RotationXDegrees != 0 || frame.RotationYDegrees != 0 || frame.RotationZDegrees != 0
                || surface.PhysicalAperture is not (null or CircularAperture) || surface.IsReflective
                || surface.CoatingModel is not Coatings.NoneCoatingModel || surface.ScatteringModel is not null
                || surface.MaterialAfter is not (Materials.AirMaterial or Materials.CatalogGlassMaterial)
                || surface.ChipZone != 0 || surface.ThermalExpansionPpmPerC is not (null or 0) || surface.MechanicalSemiDiameterSolveCode != 0
                || surface.Geometry is not (PlaneGeometry or StandardGeometry or EvenAsphereGeometry))
                throw new NotSupportedException("Scaling requires centered rotational refractive surfaces and circular physical apertures.");
            var profile = OpticalDesignParameters.Profile(surface.Geometry);
            var coefficients = profile.Coefficients.Select((coefficient, term) => coefficient * Math.Pow(factor, -2 * term - 1)).ToArray();
            var radius = profile.Curvature == 0 ? 0 : factor / profile.Curvature;
            if (!double.IsFinite(radius) || coefficients.Any(value => !double.IsFinite(value))
                || coefficients.Where((value, term) => value == 0 && profile.Coefficients[term] != 0).Any())
                throw new ArithmeticException("The scaled profile is not representable.");
            surface.Geometry = surface.Geometry switch
            {
                PlaneGeometry => new PlaneGeometry(),
                EvenAsphereGeometry => new EvenAsphereGeometry(radius, profile.Conic, coefficients),
                _ => new StandardGeometry(radius, profile.Conic)
            };
            if (double.IsFinite(surface.Thickness)) surface.Thickness *= factor;
            surface.SemiDiameter *= factor;
            if (source.Surfaces[index].MechanicalSemiDiameter is { } mechanical) surface.MechanicalSemiDiameter = mechanical * factor;
            if (surface.PhysicalAperture is CircularAperture aperture) surface.PhysicalAperture = new CircularAperture(aperture.Radius * factor);
        }
        optic.Aperture.Value *= factor;
        optic.SurfaceGroup.Renumber();
        OpticalDesignParameters.SynchronizeIncidentMedia(optic);
        return optic;
    }
}
