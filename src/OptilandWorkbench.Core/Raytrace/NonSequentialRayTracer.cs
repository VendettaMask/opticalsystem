using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Raytrace;

public enum NonSequentialTerminationReason
{
    Escaped,
    DetectorHit,
    Absorbed,
    MinimumIntensity,
    MaximumInteractions,
    InvalidRay,
    Split,
    MaximumSegments,
    MaximumBranches
}

public sealed record NonSequentialTraceOptions(
    int MaximumInteractions = 64,
    double MinimumIntensity = 1e-9,
    double OriginOffsetMillimeters = 1e-7,
    bool UseSemiDiameterWhenPhysicalApertureIsMissing = true)
{
    internal void Validate()
    {
        if (MaximumInteractions <= 0 || MaximumInteractions > 100_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumInteractions));
        }

        if (!double.IsFinite(MinimumIntensity) || MinimumIntensity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumIntensity));
        }

        if (!double.IsFinite(OriginOffsetMillimeters) || OriginOffsetMillimeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OriginOffsetMillimeters));
        }
    }
}

public sealed class NonSequentialObject
{
    public NonSequentialObject(
        int id,
        string name,
        OpticalSurface surface,
        bool isDetector = false,
        bool isAbsorber = false)
    {
        ArgumentNullException.ThrowIfNull(surface);
        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? $"Object {id}" : name.Trim();
        Surface = surface;
        IsDetector = isDetector;
        IsAbsorber = isAbsorber;
    }

    public int Id { get; }

    public string Name { get; }

    public OpticalSurface Surface { get; }

    public bool IsDetector { get; }

    public bool IsAbsorber { get; }

    public NonSequentialObject Clone() => new(
        Id,
        Name,
        Surface.Clone(),
        IsDetector,
        IsAbsorber);
}

public sealed class NonSequentialScene
{
    private readonly List<NonSequentialObject> _objects = new();

    public IReadOnlyList<NonSequentialObject> Objects => _objects;

    public void Add(NonSequentialObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_objects.Any(existing => existing.Id == item.Id))
        {
            throw new ArgumentException($"A non-sequential object with id {item.Id} already exists.", nameof(item));
        }

        _objects.Add(item);
    }

    public NonSequentialScene Clone()
    {
        var clone = new NonSequentialScene();
        foreach (var item in _objects)
        {
            clone.Add(item.Clone());
        }

        return clone;
    }

    public static NonSequentialScene FromOptic(Optic optic)
    {
        ArgumentNullException.ThrowIfNull(optic);
        var scene = new NonSequentialScene();
        var surfaces = optic.SurfaceGroup.Items;
        for (var index = 1; index < surfaces.Count; index++)
        {
            var surface = surfaces[index].Clone();
            scene.Add(new NonSequentialObject(
                surface.Number,
                surface.Label,
                surface,
                isDetector: index == surfaces.Count - 1));
        }

        return scene;
    }
}

public sealed record NonSequentialInteraction(
    int Sequence,
    int ObjectId,
    string ObjectName,
    RayTraceSample Sample,
    Vector3D SurfaceNormal,
    string IncidentMaterial,
    string OutgoingMaterial);

public sealed record NonSequentialRayPath(
    int SourceRayIndex,
    RealRay SourceRay,
    IReadOnlyList<NonSequentialInteraction> Interactions,
    RealRay FinalRay,
    NonSequentialTerminationReason TerminationReason,
    double CumulativePathLength,
    double CumulativeOpticalPathLength);

public sealed record NonSequentialTrace(IReadOnlyList<NonSequentialRayPath> Paths)
{
    public int InteractionCount => Paths.Sum(path => path.Interactions.Count);
}

public sealed class NonSequentialRayTracer
{
    private readonly Optic? _optic;
    private readonly NonSequentialScene? _scene;

    public NonSequentialRayTracer(Optic optic)
    {
        _optic = optic ?? throw new ArgumentNullException(nameof(optic));
    }

    public NonSequentialRayTracer(NonSequentialScene scene)
    {
        _scene = scene?.Clone() ?? throw new ArgumentNullException(nameof(scene));
    }

    public NonSequentialTrace Trace(
        RealRay ray,
        NonSequentialTraceOptions? options = null,
        IMaterial? initialMaterial = null) =>
        Trace(new RealRayBundle(new[] { ray }), options, initialMaterial);

    public NonSequentialTrace Trace(
        RealRayBundle bundle,
        NonSequentialTraceOptions? options = null,
        IMaterial? initialMaterial = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        options ??= new NonSequentialTraceOptions();
        options.Validate();
        var scene = (_scene ?? NonSequentialScene.FromOptic(_optic!)).Clone();
        var material = initialMaterial?.Clone() ?? new AirMaterial();
        var paths = new NonSequentialRayPath[bundle.Rays.Count];
        for (var index = 0; index < bundle.Rays.Count; index++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            paths[index] = TraceRay(index, bundle.Rays[index], scene, material.Clone(), options);
        }

        return new NonSequentialTrace(paths);
    }

    private static NonSequentialRayPath TraceRay(
        int sourceRayIndex,
        RealRay sourceRay,
        NonSequentialScene scene,
        IMaterial initialMaterial,
        NonSequentialTraceOptions options)
    {
        if (!sourceRay.CanTrace)
        {
            return Completed(NonSequentialTerminationReason.InvalidRay, sourceRay);
        }

        var ray = sourceRay.Normalize();
        var material = initialMaterial;
        var interactions = new List<NonSequentialInteraction>();
        var cumulativePathLength = 0.0;
        var cumulativeOpticalPathLength = 0.0;
        var originOffsetApplied = false;
        var termination = NonSequentialTerminationReason.MaximumInteractions;

        for (var sequence = 0; sequence < options.MaximumInteractions; sequence++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            if (!ray.CanTrace)
            {
                termination = NonSequentialTerminationReason.InvalidRay;
                break;
            }

            if (ray.Intensity < options.MinimumIntensity)
            {
                termination = NonSequentialTerminationReason.MinimumIntensity;
                break;
            }

            var hit = FindNearestHit(scene.Objects, ray, options);
            if (hit is null)
            {
                termination = NonSequentialTerminationReason.Escaped;
                break;
            }

            var destinationMaterial = hit.IncidentFromBefore
                ? hit.Item.Surface.MaterialAfter
                : hit.Item.Surface.MaterialBefore;
            var result = hit.Item.Surface.TraceRayValue(
                ray,
                material,
                destinationMaterial,
                cumulativePathLength,
                cumulativeOpticalPathLength,
                ignorePhysicalAperture: true);
            var offsetCorrection = originOffsetApplied ? options.OriginOffsetMillimeters : 0;
            var opticalOffsetCorrection = offsetCorrection
                * material.RefractiveIndex(ray.WavelengthNanometers);
            cumulativePathLength = result.CumulativePathLength + offsetCorrection;
            cumulativeOpticalPathLength = result.CumulativeOpticalPathLength + opticalOffsetCorrection;
            var sample = result.Sample with
            {
                SegmentLength = result.Sample.SegmentLength + offsetCorrection,
                SegmentOpticalPathLength = result.Sample.SegmentOpticalPathLength + opticalOffsetCorrection,
                CumulativePathLength = cumulativePathLength,
                CumulativeOpticalPathLength = cumulativeOpticalPathLength
            };
            var outgoingMaterial = result.InteractionKind == RayInteractionKind.Transmitted
                ? destinationMaterial
                : material;
            interactions.Add(new NonSequentialInteraction(
                sequence + 1,
                hit.Item.Id,
                hit.Item.Name,
                sample.ToRayTraceSample(),
                hit.GlobalNormal,
                material.Name,
                outgoingMaterial.Name));
            ray = result.Ray;
            if (opticalOffsetCorrection > 0)
            {
                ray = ray with
                {
                    OpticalPathDifference = ray.OpticalPathDifference + opticalOffsetCorrection
                };
            }
            material = outgoingMaterial;

            if (hit.Item.IsDetector)
            {
                termination = NonSequentialTerminationReason.DetectorHit;
                break;
            }

            if (hit.Item.IsAbsorber || result.StopTracing)
            {
                ray = ray with { Intensity = 0 };
                termination = NonSequentialTerminationReason.Absorbed;
                break;
            }

            ray = ray with
            {
                Origin = ray.Origin + (ray.Direction * options.OriginOffsetMillimeters)
            };
            originOffsetApplied = true;
        }

        return new NonSequentialRayPath(
            sourceRayIndex,
            sourceRay,
            interactions,
            ray,
            termination,
            cumulativePathLength,
            cumulativeOpticalPathLength);

        NonSequentialRayPath Completed(NonSequentialTerminationReason reason, RealRay finalRay) => new(
            sourceRayIndex,
            sourceRay,
            Array.Empty<NonSequentialInteraction>(),
            finalRay,
            reason,
            0,
            0);
    }

    private static CandidateHit? FindNearestHit(
        IReadOnlyList<NonSequentialObject> objects,
        RealRay ray,
        NonSequentialTraceOptions options)
    {
        CandidateHit? nearest = null;
        foreach (var item in objects)
        {
            var surface = item.Surface;
            var localOrigin = surface.CoordinateSystem.ToLocalPoint(ray.Origin);
            var localDirection = surface.CoordinateSystem.ToLocalDirection(ray.Direction);
            var distance = surface.Geometry.DistanceToIntersection(localOrigin, localDirection);
            if (distance is null
                || !double.IsFinite(distance.Value)
                || distance.Value <= options.OriginOffsetMillimeters)
            {
                continue;
            }

            var localHit = localOrigin + (localDirection * distance.Value);
            if (!Contains(surface, localHit, options.UseSemiDiameterWhenPhysicalApertureIsMissing))
            {
                continue;
            }

            if (nearest is not null && distance.Value >= nearest.Distance)
            {
                continue;
            }

            var localNormal = surface.Geometry.SurfaceNormal(localHit);
            if (!IsFiniteNonZero(localNormal))
            {
                continue;
            }

            var globalNormal = surface.CoordinateSystem.ToGlobalDirection(localNormal);
            nearest = new CandidateHit(
                item,
                distance.Value,
                globalNormal / globalNormal.Length,
                Dot(localDirection, localNormal) > 0);
        }

        return nearest;
    }

    private static bool Contains(
        OpticalSurface surface,
        Vector3D localHit,
        bool useSemiDiameterWhenPhysicalApertureIsMissing)
    {
        if (surface.PhysicalAperture is not null)
        {
            return surface.PhysicalAperture.Contains(localHit);
        }

        if (!useSemiDiameterWhenPhysicalApertureIsMissing)
        {
            return true;
        }

        return (localHit.X * localHit.X) + (localHit.Y * localHit.Y)
            <= surface.SemiDiameter * surface.SemiDiameter;
    }

    private static bool IsFiniteNonZero(Vector3D vector) =>
        vector.Length > 1e-15
        && double.IsFinite(vector.X)
        && double.IsFinite(vector.Y)
        && double.IsFinite(vector.Z);

    private static double Dot(Vector3D left, Vector3D right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    private sealed record CandidateHit(
        NonSequentialObject Item,
        double Distance,
        Vector3D GlobalNormal,
        bool IncidentFromBefore);
}
