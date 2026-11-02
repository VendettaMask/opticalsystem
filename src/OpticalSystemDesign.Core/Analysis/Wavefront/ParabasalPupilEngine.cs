using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Core.Analysis;

public sealed record ParabasalPupilResult(double ImageAxialDistanceMillimeters, double DiameterMillimeters,
    Vector3D ConjugatePosition, double ProbeStep);

/// <summary>Images the actual central stop pencil in one image-space meridian using shared geometric tracing.</summary>
public static class ParabasalPupilEngine
{
    public static ParabasalPupilResult Compute(Optic optic, Wavelength wavelength, bool useX = false, double probeStep = 1e-3,
        bool allowGeometricReference = false)
    {
        if (!double.IsFinite(probeStep) || probeStep is < 1e-6 or > .01) throw new ArgumentOutOfRangeException(nameof(probeStep));
        var surfaces = optic.SurfaceGroup.Items;
        var stopIndex = surfaces.ToList().FindIndex(s => s.IsStop);
        if (stopIndex < 1 || stopIndex >= surfaces.Count - 1)
            throw new AnalysisDataUnavailableException("Parabasal pupil", "a physical stop before the image is required");
        var tracer = optic.SequentialRayTracer;
        var chiefBundle = tracer.RayGenerator.GenerateGeneric(0, 0, 0, 0, wavelength.Micrometers, optic.RayAimingEnabled);
        using var traced = tracer.Trace(chiefBundle, TraceRequest.Selected([stopIndex, surfaces.Count - 1]));
        RayTraceSample? stopHit = traced.TryGetSample(0, stopIndex, out var stopSample) ? stopSample.ToRayTraceSample() : null;
        RayTraceSample? chief = traced.TryGetSample(0, surfaces.Count - 1, out var chiefSample) ? chiefSample.ToRayTraceSample() : null;
        if (allowGeometricReference)
        {
            // A phase datum may use an unclipped auxiliary pencil. It never
            // supplies the physical pupil mask, transmitted power or samples.
            if (stopHit is null || stopHit.Vignetted)
                stopHit = tracer.TraceToSurface(chiefBundle.Rays.Single(), stopIndex);
            if (chief is null || chief.Vignetted)
                chief = AnalysisTrace.GeometricImageReference(optic, chiefBundle.Rays.Single());
        }
        if (stopHit is null || chief is null || stopHit.Vignetted || chief.Vignetted)
            throw new AnalysisDataUnavailableException("Parabasal pupil", "the central ray is blocked");
        var material = ExitPupilGeometryEngine.PencilMaterial(surfaces[stopIndex], RayTraceSampleValue.FromRayTraceSample(stopHit));
        var frame = surfaces[^1].CoordinateSystem;
        var perturbation = surfaces[stopIndex].CoordinateSystem.ToGlobalDirection(useX ? new(1, 0, 0) : new(0, 1, 0));
        var ends = new List<RayTraceSample>();
        foreach (var step in new[] { probeStep, probeStep / 2 }) foreach (var sign in new[] { -1, 1 })
        {
            var ray = new RealRay(stopHit.Position, stopHit.Direction + perturbation * (sign * step), wavelength.Nanometers);
            ends.Add(tracer.TracePupilGeometryProbe(ray, stopIndex + 1, material)
                ?? throw new AnalysisDataUnavailableException("Parabasal pupil", "the central direction pencil failed"));
        }
        double Height(Vector3D p) => useX ? frame.ToLocalPoint(p).X : frame.ToLocalPoint(p).Y;
        double Slope(Vector3D d)
        {
            var local = frame.ToLocalDirection(d);
            if (Math.Abs(local.Z) <= 1e-12) throw new AnalysisDataUnavailableException("Parabasal pupil", "a probe is parallel to the image plane");
            return (useX ? local.X : local.Y) / local.Z;
        }
        // Extrapolate the two central differences to zero step. A finite pencil
        // alone biases high-index interfaces; an unnecessarily tiny pencil
        // amplifies the spatial integrator's bounded intercept error.
        var slopeDifference = 8 * (Slope(ends[3].Direction) - Slope(ends[2].Direction))
            - (Slope(ends[1].Direction) - Slope(ends[0].Direction));
        if (Math.Abs(slopeDifference) <= 1e-15)
            throw new AnalysisDataUnavailableException("Parabasal pupil", "the exit pupil is at infinity");
        var distance = -(8 * (Height(ends[3].Position) - Height(ends[2].Position))
            - (Height(ends[1].Position) - Height(ends[0].Position))) / slopeDifference;
        var pupilEnds = new List<double>();
        foreach (var step in new[] { probeStep, probeStep / 2 }) foreach (var sign in new[] { -1, 1 })
        {
            var bundle = tracer.RayGenerator.GenerateGeneric(0, 0, useX ? sign * step : 0,
                useX ? 0 : sign * step, wavelength.Micrometers, optic.RayAimingEnabled);
            var end = tracer.TraceFinalSamples(bundle).Single()
                ?? (allowGeometricReference ? AnalysisTrace.GeometricImageReference(optic, bundle.Rays.Single()) : null)
                ?? throw new AnalysisDataUnavailableException("Parabasal pupil", "the central height pencil failed");
            pupilEnds.Add(Height(end.Position) + distance * Slope(end.Direction));
        }
        var diameter = Math.Abs(8 * (pupilEnds[3] - pupilEnds[2]) - (pupilEnds[1] - pupilEnds[0])) / (3 * probeStep);
        var localChief = frame.ToLocalDirection(chief.Direction);
        var position = chief.Position + chief.Direction * (distance / localChief.Z);
        if (!double.IsFinite(distance) || !double.IsFinite(diameter) || diameter <= 0)
            throw new AnalysisDataUnavailableException("Parabasal pupil", "the pupil conjugate is not finite");
        return new(distance, diameter, position, probeStep);
    }
}
