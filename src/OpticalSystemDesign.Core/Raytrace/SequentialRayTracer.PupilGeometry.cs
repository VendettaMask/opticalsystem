using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Capabilities;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Raytrace;

public sealed partial class SequentialRayTracer
{
    // Geometric derivative probes only: never publish these unclipped rays as
    // physical throughput, or share them with the normal final-ray cache.
    internal RayTraceSample? TracePupilGeometryProbe(RealRay source, int firstSurface, IMaterial material)
    {
        OpticCapabilityPreflight.EnsureSupported(_optic, OpticCapabilityOperation.RayTrace);
        SequentialTraceMeasurement.Record(1);
        var ray = RayState.FromRealRay(source.Normalize());
        double path = 0, opticalPath = 0;
        var surfaces = _optic.SurfaceGroup.Items;
        for (var index = firstSurface; index < surfaces.Count; index++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var step = TraceSequentialSurface(surfaces[index], index, ray, material, path, opticalPath,
                ignorePhysicalAperture: true, stopBeforeInteraction: index == surfaces.Count - 1,
                allowVirtualIntersection: index == surfaces.Count - 1);
            if (step.StopTracing || step.Sample.Vignetted) return null;
            if (index == surfaces.Count - 1) return step.Sample.ToRayTraceSample();
            ray = step.Ray;
            material = step.OutgoingMaterial;
            path = step.CumulativePathLength;
            opticalPath = step.CumulativeOpticalPathLength;
        }
        return null;
    }
}
