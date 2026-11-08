using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Propagation;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class FocalImageDomain
{
    public const string NoSagittalDiffractionScale = "NoSagittalDiffractionScale";

    internal static AnalysisData? CheckFftImageDomain(string name, Optic optic, (double Hx, double Hy) field,
        Wavelength wave, bool requireSagittalScale = true)
    {
        if (requireSagittalScale && field == (0d, 0d) && HasZeroSagittalAngularSpan(optic, wave))
            return NotApplicable(name, "The transmitted sagittal pupil has no angular span and cannot define a focal FFT image scale.",
                NoSagittalDiffractionScale);
        return CheckFftNearFocus(name, optic, field, wave);
    }

    private static bool HasZeroSagittalAngularSpan(Optic optic, Wavelength wave)
    {
        var surfaces = optic.SurfaceGroup.Items;
        if (optic.ImageSpaceAfocal || optic.RayAimingEnabled || optic.FieldDefinition != FieldDefinitionKind.Angle
            || surfaces.Count < 3 || !ObjectConjugate.IsInfinite(surfaces[0])) return false;

        // Every local frame and optical interaction must preserve translation in X.
        // Pupil probes then verify actual transmission and distinguish a sagittal
        // line focus from a fully collimated train or an untraceable case.
        if (!surfaces.Any(s => IsMeridionalGeometry(s.Geometry)) || !surfaces.Skip(1).All(s =>
                s.CoordinateSystem.RotationYDegrees == 0 && s.CoordinateSystem.RotationZDegrees == 0
                && (s.Geometry is PlaneGeometry or StandardGeometry { Radius: 0 } || IsMeridionalGeometry(s.Geometry))
                && s.InteractionModel is RefractiveReflectiveInteractionModel or CoordinateReferenceInteractionModel
                && s.ScatteringModel is null
                && s.MaterialBefore is not (UniaxialMaterial or UnresolvedMaterial)
                && s.MaterialAfter is not (UniaxialMaterial or UnresolvedMaterial)
                && s.MaterialBefore.PropagationModel is HomogeneousPropagationModel
                && s.MaterialAfter.PropagationModel is HomogeneousPropagationModel)) return false;

        PupilSample[] probes = [new(0, 0, 1), new(-.5, 0, 1), new(.5, 0, 1), new(-.85, 0, 1), new(.85, 0, 1),
            new(0, -.5, 1), new(0, .5, 1)];
        RayTraceSample?[] rays;
        try
        {
            var tracer = optic.SequentialRayTracer;
            rays = tracer.TraceFinalSamples(tracer.RayGenerator.GenerateNormalizedPupilSamples(0, 0, wave.Micrometers,
                probes, aimAtStop: false)).ToArray();
        }
        catch (Exception error) when (error is InvalidOperationException or ArithmeticException or NotSupportedException)
        {
            return false;
        }
        if (rays.Any(r => r is not { Vignetted: false, Intensity: > 0 })) return false;
        var chief = rays[0]!;
        return rays.Skip(1).Take(4).All(r => (r!.Direction - chief.Direction).Length <= 1e-12)
            && (rays[5]!.Direction - rays[6]!.Direction).Length > 1e-9
            && Math.Abs(rays[3]!.Position.X - rays[4]!.Position.X) > 1e-9;
    }

    private static bool IsMeridionalGeometry(IGeometry geometry) => geometry is TernaryMirrorArrayGeometry { AddressingMode: 0 }
        || geometry is ToroidalGeometry toroidal && double.IsInfinity(toroidal.SagittalRadius);
}
