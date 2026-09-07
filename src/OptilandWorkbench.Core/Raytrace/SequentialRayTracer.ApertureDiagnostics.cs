using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Raytrace;

/// <summary>
/// Diagnostic continuation through circular apertures, never a physically accepted ray.
/// Clearance is radius minus local intercept radius in millimeters; positive is inside.
/// Null clearance means no finite aperture was reached. Null image means propagation failed.
/// </summary>
public sealed record CircularApertureRayDiagnostic(RayTraceSample? UnclippedImage,
    double? MinimumClearanceMillimeters, int? LimitingSurfaceIndex);

public sealed partial class SequentialRayTracer
{
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
