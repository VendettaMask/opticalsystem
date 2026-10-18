using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class DiffractionEngine
{
    private static WavefrontReferenceSphere CumulativeEnergyReferenceSphere(
        Optic optic, (double Hx, double Hy) field, Wavelength reference)
    {
        var sphere = WavefrontEngine.CreateChiefRayReferenceSphere(optic, field, reference, optic.RayAimingEnabled);
        var surfaces = optic.SurfaceGroup.Items;
        if (ObjectConjugate.IsInfinite(surfaces[0]) || !surfaces.Any(surface => surface.IsReflective)) return sphere;
        var frame = surfaces[^1].CoordinateSystem;
        var center = new Vector3D(sphere.CenterX, sphere.CenterY, sphere.CenterZ);
        var localCenter = frame.ToLocalPoint(center);
        // Retain the established coaxial reference. A folded, decentered
        // conjugate needs the real differential pupil, including its side.
        if (localCenter.X == 0 && localCenter.Y == 0) return sphere;

        var generator = optic.SequentialRayTracer.RayGenerator;
        var central = generator.GenerateGeneric(0, 0, 0, 0, reference.Micrometers, false).Rays[0];
        var pupilZ = optic.Paraxial.EstimateEntrancePupilLocation();
        if (Math.Abs(central.Direction.Z) <= 1e-12) return sphere;
        var pupil = central.Origin + central.Direction * ((pupilZ - central.Origin.Z) / central.Direction.Z);
        var distance = (pupil - central.Origin).Length;
        if (!double.IsFinite(distance) || distance <= 1e-12) return sphere;
        var centralSample = optic.SequentialRayTracer.TraceToSurface(central, surfaces.Count - 1);
        if (centralSample is null) return sphere;
        var centralPoint = frame.ToLocalPoint(centralSample.Position);
        // A finite parabasal ray and the central ray define this reference.
        // Averaging opposite perturbations instead changes the reference pupil
        // in a decentered conjugate. Retain the one-microradian probe and orient
        // it toward the central image meridian so mirrored systems agree.
        var interval = 1e-6 * Math.Max(1, distance);
        var axis = surfaces[0].CoordinateSystem.ToGlobalDirection(new(0, Math.CopySign(1, -centralPoint.Y), 0));
        var perturbed = Probe(interval);
        if (perturbed is null) return sphere;
        var probePoint = frame.ToLocalPoint(perturbed.Position);
        var probeDirection = frame.ToLocalDirection(perturbed.Direction);
        var centralDirection = frame.ToLocalDirection(centralSample.Direction);
        if (Math.Abs(probeDirection.Z) <= 1e-12 || Math.Abs(centralDirection.Z) <= 1e-12) return sphere;
        var slopeDifference = probeDirection.Y / probeDirection.Z - centralDirection.Y / centralDirection.Z;
        if (!double.IsFinite(slopeDifference) || Math.Abs(slopeDifference) <= 1e-15) return sphere;
        var axial = -(probePoint.Y - centralPoint.Y) / slopeDifference;
        var chief = optic.SequentialRayTracer.TraceToSurface(
            generator.GenerateGeneric(field.Hx, field.Hy, 0, 0, reference.Micrometers, optic.RayAimingEnabled).Rays[0],
            surfaces.Count - 1);
        if (chief is null) return sphere;
        var direction = frame.ToLocalDirection(chief.Direction);
        if (Math.Abs(direction.Z) <= 1e-12) return sphere;
        var signedRadius = -axial / direction.Z;
        return double.IsFinite(signedRadius) && Math.Abs(signedRadius) > 1e-12
            ? sphere with { Radius = Math.Abs(signedRadius), IsVirtual = signedRadius < 0 }
            : sphere;

        RayTraceSample? Probe(double offset)
        {
            var origin = central.Origin + axis * offset;
            var direction = pupil - origin;
            var ray = new RealRay(origin, direction / direction.Length, reference.Nanometers);
            return optic.SequentialRayTracer.TraceToSurface(ray, surfaces.Count - 1);
        }
    }
}
