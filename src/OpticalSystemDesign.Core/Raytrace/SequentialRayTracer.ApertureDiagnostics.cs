using System.Numerics;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Capabilities;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Propagation;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Raytrace;

/// <summary>
/// Diagnostic continuation through circular apertures, never a physically accepted ray.
/// Clearance is radius minus local intercept radius in millimeters; positive is inside.
/// Null clearance means no finite aperture was reached. Null image means propagation failed.
/// </summary>
public sealed record CircularApertureRayDiagnostic(RayTraceSample? UnclippedImage,
    double? MinimumClearanceMillimeters, int? LimitingSurfaceIndex);

/// <summary>
/// Geometric continuation with an independent physical-aperture mask. The image
/// sample is diagnostic only when InsideAllApertures is false. Zero radiometric
/// weight does not prevent geometric continuation.
/// </summary>
public sealed record ApertureRayDiagnostic(RayTraceSample? UnclippedImage,
    bool InsideAllApertures, int? FirstBlockedSurfaceIndex)
{
    /// <summary>Complete unpolarized Jones-chain power, including the ray's scalar weights; null when not requested.</summary>
    public double? UnpolarizedIntensity { get; init; }
    /// <summary>System Jones input power when the system requests polarized rather than unpolarized illumination.</summary>
    public double? PolarizedIntensity { get; init; }
    public double? PolarizationWeightedIntensity => PolarizedIntensity ?? UnpolarizedIntensity;
}

public sealed partial class SequentialRayTracer
{
    /// <summary>Batch continuation; original apertures are measured but never treated as accepted throughput.</summary>
    public IReadOnlyList<CircularApertureRayDiagnostic> DiagnoseCircularApertures(RealRayBundle bundle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        cancellationToken.ThrowIfCancellationRequested();
        var nominal = TraceGpuNominal(bundle.Rays, cancellationToken, diagnostic: true);
        if (nominal is null) return bundle.Rays.Select(ray => DiagnoseCircularApertures(ray, cancellationToken)).ToArray();
        var surfaces = _optic.SurfaceGroup.Items;
        var result = new CircularApertureRayDiagnostic[bundle.Rays.Count];
        for (var ray = 0; ray < result.Length; ray++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double? minimum = null; int? limiting = null; RayTraceSample? image = null;
            for (var index = 0; index < surfaces.Count; index++)
            {
                var value = nominal[ray * surfaces.Count + index];
                if (value.Present == 0 || value.Miss != 0 || value.Blocked != 0 || value.Intensity <= 0) break;
                if (index > 0 && surfaces[index].PhysicalAperture is CircularAperture aperture)
                {
                    var local = value.Point - surfaces[index].CoordinateSystem.Origin;
                    var clearance = aperture.Radius - Math.Sqrt(local.X * local.X + local.Y * local.Y);
                    if (!double.IsFinite(clearance)) break;
                    if (minimum is null || clearance < minimum) { minimum = clearance; limiting = index; }
                }
                if (index == surfaces.Count - 1) image = GpuSample(value, index).ToRayTraceSample();
            }
            result[ray] = new(image, minimum, limiting);
        }
        return result;
    }

    /// <summary>
    /// Traces through the shared surface engine and checks every original aperture
    /// at its local intercept. Does not mutate apertures, record surface data or
    /// publish unclipped rays to the normal trace cache.
    /// Geometric transmission bypasses bulk absorption and coatings, not the
    /// initial ray weight, refraction, or the physical-aperture mask.
    /// </summary>
    public ApertureRayDiagnostic DiagnoseApertures(RealRay sourceRay,
        CancellationToken cancellationToken = default,
        bool stopAtIncidentImage = false,
        bool usePolarization = false,
        bool allowHomogeneousAbsorbingPower = false,
        bool ignoreScalarTransmission = false)
    {
        ArgumentNullException.ThrowIfNull(sourceRay);
        if (ignoreScalarTransmission && usePolarization)
            throw new ArgumentException("Geometric transmission cannot be combined with polarized power transport.");
        if (allowHomogeneousAbsorbingPower && (!usePolarization || !_optic.Polarization.Unpolarized))
            throw new ArgumentException("Homogeneous absorbing power is only defined for explicit unpolarized power tracing.");
        cancellationToken.ThrowIfCancellationRequested();
        OpticCapabilityPreflight.EnsureSupported(_optic, OpticCapabilityOperation.RayTrace);
        SequentialTraceMeasurement.Record(1);
        var surfaces = _optic.SurfaceGroup.Items;
        var ray = RayState.FromRealRay(sourceRay.Normalize());
        var material = ResolveMaterial("Air");
        var path = 0.0;
        var opticalPath = 0.0;
        var polarization = usePolarization ? new UnpolarizedPowerTransport(ray.Direction) : null;
        (Complex X, Complex Y)? coherentInput = null;
        if (usePolarization && !_optic.Polarization.Unpolarized)
        {
            var frame = surfaces[0].CoordinateSystem;
            var basis = _optic.Polarization.Input.Resolve(frame.ToLocalDirection(ray.Direction));
            polarization = new(frame.ToGlobalDirection(basis.X), frame.ToGlobalDirection(basis.Y));
            coherentInput = (basis.Jx, basis.Jy);
        }
        int? firstBlocked = null;
        RayTraceSample? image = null;
        for (var index = 0; index < surfaces.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ComputationCancellation.ThrowIfCancellationRequested();
            var surface = surfaces[index];
            var incidentImage = stopAtIncidentImage && index == surfaces.Count - 1;
            if (polarization is not null && (material.PropagationModel is not HomogeneousPropagationModel
                || (index == 0 && ObjectConjugate.IsInfinite(surface)
                    && surface.MaterialAfter.PropagationModel is not HomogeneousPropagationModel)))
                throw new NotSupportedException("Polarization transport through inhomogeneous media is not implemented.");
            var result = TraceSequentialSurface(surface, index, ray, material, path, opticalPath,
                ignorePhysicalAperture: true, stopBeforeInteraction: incidentImage,
                bypassCoating: ignoreScalarTransmission || usePolarization && surface.CoatingModel is CoherentMultilayerCoating,
                ignoreMaterialAbsorption: ignoreScalarTransmission);
            if (result.Sample.Vignetted || result.StopTracing)
                return new(null, false, firstBlocked);
            if ((index != 0 || !ObjectConjugate.IsInfinite(surface))
                && surface.PhysicalAperture is { } aperture
                && !aperture.Contains(surface.CoordinateSystem.ToLocalPoint(result.Sample.Position)))
                firstBlocked ??= index;
            if (polarization is not null && !incidentImage && result.Sample.InteractionKind is { } kind)
                ApplyPolarization(polarization, surface, result.Sample, material, sourceRay.WavelengthNanometers, kind,
                    positiveTime: coherentInput is not null, allowHomogeneousAbsorbingPower: allowHomogeneousAbsorbingPower);
            if (index == surfaces.Count - 1) image = result.Sample.ToRayTraceSample();
            ray = result.Ray;
            material = result.OutgoingMaterial;
            path = result.CumulativePathLength;
            opticalPath = result.CumulativeOpticalPathLength;
        }
        return new(image, image is not null && firstBlocked is null, firstBlocked)
        {
            UnpolarizedIntensity = image is not null && polarization is not null ? image.Intensity * polarization.Power : null,
            PolarizedIntensity = image is not null && polarization is not null && coherentInput is { } input
                ? image.Intensity * polarization.Combine(input.X, input.Y).SquaredNorm : null
        };
    }

    private static void ApplyPolarization(UnpolarizedPowerTransport transport, OpticalSurface surface,
        RayTraceSampleValue sample, IMaterial incidentMaterial, double wavelength, RayInteractionKind kind, bool positiveTime = false,
        bool allowHomogeneousAbsorbingPower = false)
    {
        var response = EvaluatePolarizationInterface(surface, sample, incidentMaterial, wavelength, kind, allowHomogeneousAbsorbingPower);
        transport.Apply(response.Incoming, response.Outgoing, response.Normal,
            positiveTime ? Complex.Conjugate(response.S) : response.S,
            positiveTime ? Complex.Conjugate(response.P) : response.P);
    }

    private static double VectorDot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>
    /// Uses the normal surface intersection and interaction engine while continuing outside
    /// circular apertures. Does not mutate the optic, record surface data, or use the trace cache.
    /// Other physical aperture shapes are explicitly unsupported by this diagnostic.
    /// </summary>
    public CircularApertureRayDiagnostic DiagnoseCircularApertures(RealRay sourceRay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceRay);
        cancellationToken.ThrowIfCancellationRequested();
        OpticCapabilityPreflight.EnsureSupported(_optic, OpticCapabilityOperation.RayTrace);
        var surfaces = _optic.SurfaceGroup.Items;
        if (surfaces.Where((surface, index) => index != 0 || !ObjectConjugate.IsInfinite(surface))
            .Any(surface => surface.PhysicalAperture is not (null or CircularAperture)))
            throw new NotSupportedException("Aperture clearance diagnostics support circular physical apertures only.");
        SequentialTraceMeasurement.Record(1);
        var ray = RayState.FromRealRay(sourceRay.Normalize());
        var material = ResolveMaterial("Air");
        var path = 0.0;
        var opticalPath = 0.0;
        double? minimum = null;
        int? limiting = null;
        RayTraceSample? image = null;
        for (var index = 0; index < surfaces.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var surface = surfaces[index];
            var result = TraceSequentialSurface(surface, index, ray, material, path, opticalPath,
                ignorePhysicalAperture: true);
            if (result.Sample.Vignetted || result.StopTracing || result.Sample.Intensity <= 0)
                return new(null, minimum, limiting);
            if ((index != 0 || !ObjectConjugate.IsInfinite(surface)) && surface.PhysicalAperture is CircularAperture aperture)
            {
                var hit = surface.CoordinateSystem.ToLocalPoint(result.Sample.Position);
                var clearance = aperture.Radius - Math.Sqrt(hit.X * hit.X + hit.Y * hit.Y);
                if (!double.IsFinite(clearance)) return new(null, minimum, limiting);
                if (minimum is null || clearance < minimum)
                {
                    minimum = clearance;
                    limiting = index;
                }
            }
            if (index == surfaces.Count - 1) image = result.Sample.ToRayTraceSample();
            ray = result.Ray;
            material = result.OutgoingMaterial;
            path = result.CumulativePathLength;
            opticalPath = result.CumulativeOpticalPathLength;
        }
        return new(image, minimum, limiting);
    }
}
