using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Raytrace;

public sealed partial class SequentialRayTracer
{
    /// <summary>
    /// Geometric Gaussian-pupil integral: ignores physical apertures and permits
    /// virtual intersections of overlapping faces through the shared surface engine.
    /// These rays never establish physical throughput, enter the normal trace cache,
    /// or populate surface display data. Propagation errors and TIR remain failures.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<RayTraceSample>> TraceGaussianPupil(RealRayBundle bundle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        cancellationToken.ThrowIfCancellationRequested();
        OpticCapabilityPreflight.EnsureSupported(_optic, OpticCapabilityOperation.RayTrace);
        if (bundle.Rays.Count > SequentialTraceLimits.MaximumRayCount
            || (long)bundle.Rays.Count * _optic.SurfaceGroup.Items.Count > SequentialTraceLimits.MaximumRetainedSamples)
            throw new ArgumentException("Gaussian-pupil history exceeds the shared trace limits.", nameof(bundle));
        SequentialTraceMeasurement.Record(bundle.Rays.Count);
        return bundle.Rays.Select(source =>
        {
            var history = new List<RayTraceSample>(_optic.SurfaceGroup.Items.Count);
            var ray = RayState.FromRealRay(source.Normalize());
            var material = ResolveMaterial("Air");
            var path = 0d;
            var opticalPath = 0d;
            for (var index = 0; index < _optic.SurfaceGroup.Items.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ComputationCancellation.ThrowIfCancellationRequested();
                var result = TraceSequentialSurface(_optic.SurfaceGroup.Items[index], index, ray,
                    material, path, opticalPath, ignorePhysicalAperture: true, allowVirtualIntersection: true);
                var sample = result.Sample.ToRayTraceSample();
                if (sample.InteractionKind == RayInteractionKind.TotalInternalReflection)
                    sample = sample with { Vignetted = true, Intensity = 0 };
                history.Add(sample);
                if (sample.Vignetted || result.StopTracing || sample.Intensity <= 0) break;
                ray = result.Ray;
                material = result.OutgoingMaterial;
                path = result.CumulativePathLength;
                opticalPath = result.CumulativeOpticalPathLength;
            }
            return (IReadOnlyList<RayTraceSample>)history.ToArray();
        }).ToArray();
    }
}
