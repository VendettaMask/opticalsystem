using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.NonSequential;

public enum NonSequentialTracePurpose
{
    Layout,
    Analysis
}

public enum NonSequentialTraceOutputMode
{
    LayoutSample,
    InMemory,
    RayDatabase,
    SummaryOnly
}

public enum NonSequentialSplittingMode
{
    None,
    FullFresnel,
    SimpleStochastic
}

public interface INonSequentialTraceSink
{
    void OnBranch(NonSequentialRayBranch branch);
}

public sealed record NonSequentialDocumentTraceRequest(
    NonSequentialTracePurpose Purpose = NonSequentialTracePurpose.Analysis,
    Guid? SourceObjectId = null,
    RealRay? DirectRay = null,
    bool? SplitFresnelRays = null,
    NonSequentialTraceOutputMode OutputMode = NonSequentialTraceOutputMode.InMemory,
    int MaximumRetainedBranches = 2_000,
    string? PathFilterExpression = null,
    NonSequentialSplittingMode? SplittingMode = null,
    int? RandomSeed = null,
    int? MaximumSegmentsPerRay = null,
    int? MaximumActiveBranches = null,
    double? MinimumRelativeIntensity = null,
    int? RayCountOverride = null,
    IReadOnlyList<Guid>? SourceObjectIds = null);

public sealed record NonSequentialRaySegment(
    long BranchId,
    Guid? ObjectId,
    int FaceNumber,
    Vector3D Start,
    Vector3D End,
    Vector3D OutgoingDirection,
    Vector3D SurfaceNormal,
    double WavelengthNanometers,
    double Intensity,
    double SegmentLength,
    double CumulativePathLength,
    double CumulativeOpticalPathLength,
    RayInteractionKind? InteractionKind);

public sealed record NonSequentialRayBranch(
    long Id,
    long? ParentId,
    int Level,
    Guid? SourceObjectId,
    IReadOnlyList<NonSequentialRaySegment> Segments,
    NonSequentialTerminationReason TerminationReason,
    double FinalIntensity,
    double WavelengthNanometers = 0);

public sealed record NonSequentialDetectorFrame(
    Guid DetectorId,
    string DetectorName,
    int PixelsX,
    int PixelsY,
    IReadOnlyDictionary<int, IReadOnlyList<double>> PowerByWavelength,
    double TotalPowerWatts,
    IReadOnlyDictionary<int, IReadOnlyList<long>>? HitCountByWavelength = null,
    IReadOnlyDictionary<int, IReadOnlyList<double>>? AngularPowerByWavelength = null,
    IReadOnlyDictionary<int, IReadOnlyList<long>>? AngularHitCountByWavelength = null);

public sealed record NonSequentialEnergyBalance(
    double SourcePowerWatts,
    double DetectorPowerWatts,
    double AbsorbedPowerWatts,
    double EscapedPowerWatts,
    double TruncatedPowerWatts)
{
    public double AccountedPowerWatts => DetectorPowerWatts + AbsorbedPowerWatts + EscapedPowerWatts + TruncatedPowerWatts;
}

public sealed record NonSequentialDocumentTraceResult(
    IReadOnlyList<NonSequentialRayBranch> Branches,
    IReadOnlyList<NonSequentialDetectorFrame> Detectors,
    NonSequentialEnergyBalance EnergyBalance,
    int TotalBranchCount = 0,
    int MatchedBranchCount = 0,
    long TotalSegmentCount = 0)
{
    public int SegmentCount => Branches.Sum(branch => branch.Segments.Count);
}

public sealed class NonSequentialDocumentTracer
{
    private const double OriginOffset = 1e-7;

    public NonSequentialDocumentTraceResult Trace(
        NonSequentialDocument document,
        MaterialRegistry materials,
        NonSequentialDocumentTraceRequest? request = null,
        INonSequentialTraceSink? sink = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(materials);
        document.Validate();
        request ??= new NonSequentialDocumentTraceRequest();
        if (request.MaximumRetainedBranches <= 0 || request.MaximumRetainedBranches > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "保留光线分支数量必须在 1 到 1000000 之间。");
        }
        if (request.RayCountOverride is <= 0 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "临时覆盖射线数必须在 1 到 1000000 之间。");
        }
        if (request.MaximumSegmentsPerRay is <= 0 or > 2_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "每条光线最大段数必须在 1 到 2000000 之间。");
        }
        if (request.MaximumActiveBranches is <= 0 or > 10_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "最大活动分支数必须在 1 到 10000000 之间。");
        }
        if (request.MinimumRelativeIntensity is <= 0 or >= 1 || double.IsNaN(request.MinimumRelativeIntensity ?? 0))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "最小相对能量必须大于 0 且小于 1。");
        }
        var pathFilter = NonSequentialPathFilter.Parse(request.PathFilterExpression);
        var settings = document.TraceSettings with
        {
            RandomSeed = request.RandomSeed ?? document.TraceSettings.RandomSeed,
            MaximumSegmentsPerRay = request.MaximumSegmentsPerRay ?? document.TraceSettings.MaximumSegmentsPerRay,
            MaximumActiveBranches = request.MaximumActiveBranches ?? document.TraceSettings.MaximumActiveBranches,
            MinimumRelativeIntensity = request.MinimumRelativeIntensity ?? document.TraceSettings.MinimumRelativeIntensity
        };
        var splittingMode = request.SplittingMode
            ?? ((request.SplitFresnelRays ?? settings.SplitFresnelRays)
                ? NonSequentialSplittingMode.FullFresnel
                : NonSequentialSplittingMode.None);
        var objects = document.Objects.Where(item => item.Enabled && !IsSource(item.Kind)).ToArray();
        var bvh = BvhNode.Build(objects.Select(item => BoundedObject.Create(document, item)).ToArray());
        var detectors = objects
            .Where(item => item.Kind == NonSequentialObjectKind.DetectorRectangle)
            .ToDictionary(item => item.Id, item => new DetectorAccumulator(item));
        var sourceCount = CountSourceRays(document, request);
        if (sourceCount > settings.MaximumTotalSourceRays)
        {
            throw new InvalidOperationException(
                $"非序列追迹需要 {sourceCount} 条源射线，超过 {settings.MaximumTotalSourceRays} 条上限。");
        }

        var branches = new List<NonSequentialRayBranch>();
        var totalBranchCount = 0;
        var matchedBranchCount = 0;
        long totalSegmentCount = 0;
        var queue = new Queue<BranchState>();
        long nextBranchId = 1;
        var random = new Random(settings.RandomSeed);
        using var sourceEnumerator = GenerateSourceRays(document, request, settings, random).GetEnumerator();
        var hasSource = sourceEnumerator.MoveNext();
        var sourcePower = 0.0;
        var detectorPower = 0.0;
        var absorbedPower = 0.0;
        var escapedPower = 0.0;
        var truncatedPower = 0.0;
        var createdBranchCount = sourceCount;

        while (queue.Count > 0 || hasSource)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            if (queue.Count == 0)
            {
                var source = sourceEnumerator.Current;
                sourcePower += source.Ray.Intensity;
                queue.Enqueue(new BranchState(
                    nextBranchId++,
                    null,
                    0,
                    source.SourceObjectId,
                    source.Ray.Normalize(),
                    source.InitialMaterial,
                    new HashSet<Guid>(),
                    new List<NonSequentialRaySegment>(),
                    0,
                    0));
                hasSource = sourceEnumerator.MoveNext();
            }

            var state = queue.Dequeue();
            var termination = NonSequentialTerminationReason.MaximumSegments;
            var completed = false;
            for (var segmentIndex = 0; segmentIndex < settings.MaximumSegmentsPerRay; segmentIndex++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                if (!state.Ray.CanTrace)
                {
                    termination = NonSequentialTerminationReason.InvalidRay;
                    truncatedPower += Math.Max(0, state.Ray.Intensity);
                    completed = true;
                    break;
                }

                if (state.Ray.Intensity < settings.MinimumRelativeIntensity)
                {
                    termination = NonSequentialTerminationReason.MinimumIntensity;
                    truncatedPower += state.Ray.Intensity;
                    completed = true;
                    break;
                }

                var hit = FindNearestHit(document, bvh, state.Ray);
                if (hit is null)
                {
                    termination = NonSequentialTerminationReason.Escaped;
                    escapedPower += state.Ray.Intensity;
                    completed = true;
                    break;
                }

                var incidentMaterial = materials.Resolve(state.MaterialName);
                var distance = hit.Distance;
                var path = state.PathLength + distance;
                var opticalPath = state.OpticalPathLength
                    + distance * incidentMaterial.RefractiveIndex(state.Ray.WavelengthNanometers);
                var incomingIntensity = state.Ray.Intensity;

                if (hit.Item.Kind == NonSequentialObjectKind.DetectorRectangle)
                {
                    var detector = (DetectorRectangleParameters)hit.Item.Parameters;
                    if (!detector.FrontOnly || hit.LocalDirection.Z > 0)
                    {
                        detectors[hit.Item.Id].Add(
                            hit.LocalPoint,
                            hit.LocalDirection,
                            WavelengthNumber(document, state.Ray.WavelengthNanometers),
                            incomingIntensity);
                        state.Segments.Add(Segment(state, hit, path, opticalPath, state.Ray.Direction, null));
                        if (detector.Absorb)
                        {
                            detectorPower += incomingIntensity;
                            termination = NonSequentialTerminationReason.DetectorHit;
                            completed = true;
                            break;
                        }

                        state = state with
                        {
                            Ray = state.Ray with { Origin = hit.WorldPoint + state.Ray.Direction * OriginOffset },
                            PathLength = path,
                            OpticalPathLength = opticalPath
                        };
                        continue;
                    }
                }

                var behavior = Behavior(hit.Item);
                if (behavior == NonSequentialSurfaceBehavior.Absorbing)
                {
                    absorbedPower += incomingIntensity;
                    state.Segments.Add(Segment(state, hit, path, opticalPath, state.Ray.Direction, null));
                    termination = NonSequentialTerminationReason.Absorbed;
                    completed = true;
                    break;
                }

                var normal = hit.WorldNormal;
                if (Dot(state.Ray.Direction, normal) > 0) normal = -normal;
                if (behavior == NonSequentialSurfaceBehavior.Reflective)
                {
                    var reflected = Normalize(Reflect(state.Ray.Direction, normal));
                    state.Segments.Add(Segment(state, hit, path, opticalPath, reflected, RayInteractionKind.Reflected));
                    state = state with
                    {
                        Ray = state.Ray with { Origin = hit.WorldPoint + reflected * OriginOffset, Direction = reflected },
                        PathLength = path,
                        OpticalPathLength = opticalPath
                    };
                    continue;
                }

                var nextMaterialName = hit.IsSolid
                    ? hit.Entering
                        ? SolidMaterial(hit.Item)
                        : OutsideMaterial(document, hit.Item)
                    : PlaneDestination((PlaneRectangleParameters)hit.Item.Parameters, hit.LocalDirection);
                var nextMaterial = materials.Resolve(nextMaterialName);
                var n1 = incidentMaterial.RefractiveIndex(state.Ray.WavelengthNanometers);
                var n2 = nextMaterial.RefractiveIndex(state.Ray.WavelengthNanometers);
                var optical = RefractAndFresnel(state.Ray.Direction, normal, n1, n2);
                if (optical.TotalInternalReflection)
                {
                    state.Segments.Add(Segment(state, hit, path, opticalPath, optical.Reflected, RayInteractionKind.TotalInternalReflection));
                    state = state with
                    {
                        Ray = state.Ray with { Origin = hit.WorldPoint + optical.Reflected * OriginOffset, Direction = optical.Reflected },
                        PathLength = path,
                        OpticalPathLength = opticalPath
                    };
                    continue;
                }

                if (splittingMode == NonSequentialSplittingMode.SimpleStochastic
                    && optical.Reflectance > 0 && optical.Reflectance < 1)
                {
                    var reflect = random.NextDouble() < optical.Reflectance;
                    var direction = reflect ? optical.Reflected : optical.Transmitted;
                    var interaction = reflect ? RayInteractionKind.Reflected : RayInteractionKind.Transmitted;
                    var simpleInside = new HashSet<Guid>(state.InsideObjects);
                    var simpleMaterial = state.MaterialName;
                    if (!reflect)
                    {
                        simpleMaterial = nextMaterialName;
                        if (hit.IsSolid)
                        {
                            if (hit.Entering) simpleInside.Add(hit.Item.Id);
                            else simpleInside.Remove(hit.Item.Id);
                        }
                    }
                    state.Segments.Add(Segment(state, hit, path, opticalPath, direction, interaction));
                    state = state with
                    {
                        Ray = state.Ray with { Origin = hit.WorldPoint + direction * OriginOffset, Direction = direction },
                        MaterialName = simpleMaterial,
                        InsideObjects = simpleInside,
                        PathLength = path,
                        OpticalPathLength = opticalPath
                    };
                    continue;
                }

                if (splittingMode == NonSequentialSplittingMode.FullFresnel
                    && optical.Reflectance > 0 && optical.Reflectance < 1)
                {
                    state.Segments.Add(Segment(state, hit, path, opticalPath, state.Ray.Direction, RayInteractionKind.Transmitted));
                    Emit(Complete(state, NonSequentialTerminationReason.Split));
                    var reflectedPower = incomingIntensity * optical.Reflectance;
                    var transmittedPower = incomingIntensity - reflectedPower;
                    TryEnqueueChild(optical.Reflected, reflectedPower, state.MaterialName, state.InsideObjects, RayInteractionKind.Reflected);
                    var transmittedInside = new HashSet<Guid>(state.InsideObjects);
                    if (hit.IsSolid)
                    {
                        if (hit.Entering) transmittedInside.Add(hit.Item.Id);
                        else transmittedInside.Remove(hit.Item.Id);
                    }
                    TryEnqueueChild(optical.Transmitted, transmittedPower, nextMaterialName, transmittedInside, RayInteractionKind.Transmitted);
                    completed = true;
                    break;

                    void TryEnqueueChild(
                        Vector3D direction,
                        double intensity,
                        string materialName,
                        HashSet<Guid> inside,
                        RayInteractionKind interaction)
                    {
                        if (intensity < settings.MinimumRelativeIntensity || createdBranchCount >= settings.MaximumActiveBranches)
                        {
                            truncatedPower += intensity;
                            return;
                        }

                        var childId = nextBranchId++;
                        createdBranchCount++;
                        queue.Enqueue(new BranchState(
                            childId,
                            state.Id,
                            state.Level + 1,
                            state.SourceObjectId,
                            state.Ray with
                            {
                                Origin = hit.WorldPoint + direction * OriginOffset,
                                Direction = direction,
                                Intensity = intensity
                            },
                            materialName,
                            new HashSet<Guid>(inside),
                            new List<NonSequentialRaySegment>
                            {
                                new(
                                    childId,
                                    hit.Item.Id,
                                    hit.FaceNumber,
                                    hit.WorldPoint,
                                    hit.WorldPoint,
                                    direction,
                                    hit.WorldNormal,
                                    state.Ray.WavelengthNanometers,
                                    intensity,
                                    0,
                                    path,
                                    opticalPath,
                                    interaction)
                            },
                            path,
                            opticalPath));
                    }
                }

                var lostReflection = incomingIntensity * optical.Reflectance;
                truncatedPower += lostReflection;
                var transmittedIntensity = incomingIntensity - lostReflection;
                var insideObjects = new HashSet<Guid>(state.InsideObjects);
                if (hit.IsSolid)
                {
                    if (hit.Entering) insideObjects.Add(hit.Item.Id);
                    else insideObjects.Remove(hit.Item.Id);
                }
                state.Segments.Add(Segment(state, hit, path, opticalPath, optical.Transmitted, RayInteractionKind.Transmitted));
                state = state with
                {
                    Ray = state.Ray with
                    {
                        Origin = hit.WorldPoint + optical.Transmitted * OriginOffset,
                        Direction = optical.Transmitted,
                        Intensity = transmittedIntensity
                    },
                    MaterialName = nextMaterialName,
                    InsideObjects = insideObjects,
                    PathLength = path,
                    OpticalPathLength = opticalPath
                };
            }

            if (!completed)
            {
                truncatedPower += state.Ray.Intensity;
            }
            if (termination != NonSequentialTerminationReason.Split)
            {
                Emit(Complete(state, termination));
            }
        }

        var frames = detectors.Values.Select(item => item.ToFrame(document)).ToArray();
        return new NonSequentialDocumentTraceResult(
            branches,
            frames,
            new NonSequentialEnergyBalance(sourcePower, detectorPower, absorbedPower, escapedPower, truncatedPower),
            totalBranchCount,
            matchedBranchCount,
            totalSegmentCount);

        void Emit(NonSequentialRayBranch branch)
        {
            totalBranchCount++;
            totalSegmentCount += branch.Segments.Count;
            if (!pathFilter.IsMatch(document, branch)) return;
            matchedBranchCount++;
            sink?.OnBranch(branch);
            var retain = request.OutputMode switch
            {
                NonSequentialTraceOutputMode.InMemory => true,
                NonSequentialTraceOutputMode.LayoutSample => branches.Count < request.MaximumRetainedBranches,
                _ => false
            };
            if (retain) branches.Add(branch);
        }
    }

    private static int CountSourceRays(
        NonSequentialDocument document,
        NonSequentialDocumentTraceRequest request)
    {
        if (request.DirectRay is not null) return 1;
        long count = 0;
        foreach (var item in document.Objects.Where(item => item.Enabled && IsSource(item.Kind)))
        {
            if (!IncludesSource(request, item.Id)) continue;
            var source = (SourceParameters)item.Parameters;
            count += item.Parameters is SourceRayParameters
                ? 1
                : request.RayCountOverride is int overrideCount
                    ? overrideCount
                : request.Purpose == NonSequentialTracePurpose.Layout
                    ? source.LayoutRayCount
                    : source.AnalysisRayCount;
            if (count > int.MaxValue) throw new InvalidOperationException("非序列源射线总数超过支持范围。");
        }
        return (int)count;
    }

    private static IEnumerable<GeneratedRay> GenerateSourceRays(
        NonSequentialDocument document,
        NonSequentialDocumentTraceRequest request,
        NonSequentialTraceSettings settings,
        Random random)
    {
        if (request.DirectRay is not null)
        {
            yield return new GeneratedRay(null, request.DirectRay, document.AmbientMaterial);
            yield break;
        }

        foreach (var item in document.Objects.Where(item => item.Enabled && IsSource(item.Kind)))
        {
            if (!IncludesSource(request, item.Id)) continue;
            var source = (SourceParameters)item.Parameters;
            var radialSampler = item.Parameters is SourceRadialParameters radialSource
                ? new RadialDirectionSampler(radialSource.Distribution)
                : null;
            var count = item.Parameters is SourceRayParameters
                ? 1
                : request.RayCountOverride is int overrideCount
                    ? overrideCount
                : request.Purpose == NonSequentialTracePurpose.Layout
                    ? source.LayoutRayCount
                    : source.AnalysisRayCount;
            var wavelength = document.Wavelengths[source.WavelengthNumber - 1].Nanometers;
            var power = source.PowerWatts / count;
            for (var index = 0; index < count; index++)
            {
                var (origin, direction) = item.Parameters switch
                {
                    SourceRayParameters ray => (ray.Origin, Normalize(ray.Direction)),
                    SourcePointParameters point => (Vector3D.Zero, ConeDirection(random, point.ConeHalfAngleDegrees)),
                    SourceRectangleParameters rectangle => (
                        new Vector3D(
                            (random.NextDouble() - 0.5) * rectangle.WidthMillimeters,
                            (random.NextDouble() - 0.5) * rectangle.HeightMillimeters,
                            0),
                        ConeDirection(random, rectangle.AngularHalfAngleDegrees)),
                    SourceGaussianParameters gaussian => (
                        new Vector3D(
                            NextGaussian(random) * gaussian.WaistXMillimeters / 2,
                            NextGaussian(random) * gaussian.WaistYMillimeters / 2,
                            0),
                        GaussianDirection(random, gaussian.DivergenceHalfAngleDegrees)),
                    SourceEllipseParameters ellipse => (
                        EllipsePoint(random, ellipse.WidthMillimeters / 2, ellipse.HeightMillimeters / 2),
                        ConeDirection(random, ellipse.AngularHalfAngleDegrees)),
                    SourceTwoAngleParameters twoAngle => (
                        twoAngle.Shape == NonSequentialSourceApertureShape.Ellipse
                            ? EllipsePoint(random, twoAngle.WidthMillimeters / 2, twoAngle.HeightMillimeters / 2)
                            : BoxPoint(random, twoAngle.WidthMillimeters, twoAngle.HeightMillimeters, 0),
                        AnisotropicDirection(
                            random,
                            twoAngle.AngularHalfAngleXDegrees,
                            twoAngle.AngularHalfAngleYDegrees)),
                    SourceRadialParameters => (
                        Vector3D.Zero,
                        radialSampler!.Next(random)),
                    SourceVolumeRectangleParameters volumeRectangle => (
                        BoxPoint(
                            random,
                            volumeRectangle.WidthMillimeters,
                            volumeRectangle.HeightMillimeters,
                            volumeRectangle.DepthMillimeters),
                        ConeDirection(random, volumeRectangle.AngularHalfAngleDegrees)),
                    SourceVolumeEllipseParameters volumeEllipse => (
                        EllipsoidPoint(
                            random,
                            volumeEllipse.SemiAxisXMillimeters,
                            volumeEllipse.SemiAxisYMillimeters,
                            volumeEllipse.SemiAxisZMillimeters),
                        ConeDirection(random, volumeEllipse.AngularHalfAngleDegrees)),
                    SourceVolumeCylinderParameters volumeCylinder => (
                        CylinderPoint(
                            random,
                            volumeCylinder.RadiusXMillimeters,
                            volumeCylinder.RadiusYMillimeters,
                            volumeCylinder.LengthMillimeters),
                        ConeDirection(random, volumeCylinder.AngularHalfAngleDegrees)),
                    _ => throw new InvalidOperationException("Unknown source object.")
                };
                yield return new GeneratedRay(
                    item.Id,
                    new RealRay(
                        document.ToWorldPoint(item.Id, origin),
                        Normalize(document.ToWorldDirection(item.Id, direction)),
                        wavelength,
                        power),
                    item.ContainingObjectId is Guid containerId
                        ? document.Objects.FirstOrDefault(value => value.Id == containerId) is { } container
                            ? SolidMaterial(container)
                            : document.AmbientMaterial
                        : document.AmbientMaterial);
            }
        }
    }

    private static bool IncludesSource(NonSequentialDocumentTraceRequest request, Guid sourceId)
    {
        if (request.SourceObjectIds is { Count: > 0 }) return request.SourceObjectIds.Contains(sourceId);
        return request.SourceObjectId is not Guid selected || selected == sourceId;
    }

    private static SceneHit? FindNearestHit(NonSequentialDocument document, BvhNode? bvh, RealRay ray)
    {
        SceneHit? nearest = null;
        foreach (var bounded in bvh?.Candidates(ray) ?? Array.Empty<BoundedObject>())
        {
            var hit = Intersect(document, bounded.Item, ray);
            if (hit is not null && hit.Distance > OriginOffset
                && (nearest is null || hit.Distance < nearest.Distance))
            {
                nearest = hit;
            }
        }
        return nearest;
    }

    private static SceneHit? Intersect(NonSequentialDocument document, NonSequentialObjectDefinition item, RealRay ray)
    {
        var origin = document.ToLocalPoint(item.Id, ray.Origin);
        var direction = Normalize(document.ToLocalDirection(item.Id, ray.Direction));
        LocalHit? local = item.Parameters switch
        {
            PlaneRectangleParameters plane => IntersectPlane(origin, direction, plane.WidthMillimeters, plane.HeightMillimeters),
            DetectorRectangleParameters detector when !detector.FrontOnly || direction.Z > 0 =>
                IntersectPlane(origin, direction, detector.WidthMillimeters, detector.HeightMillimeters),
            SphereParameters sphere => IntersectSphere(origin, direction, sphere.RadiusMillimeters),
            CylinderParameters cylinder => IntersectCylinder(origin, direction, cylinder.RadiusMillimeters, cylinder.LengthMillimeters),
            BoxParameters box => IntersectBox(origin, direction, box.WidthMillimeters, box.HeightMillimeters, box.LengthMillimeters),
            StandardLensParameters lens => IntersectLens(origin, direction, lens),
            MeshObjectParameters mesh => IntersectMesh(document.FindMeshAsset(mesh.MeshAssetId), origin, direction, mesh),
            _ => null
        };
        if (local is null) return null;
        var worldPoint = document.ToWorldPoint(item.Id, local.Point);
        var worldNormal = Normalize(document.ToWorldDirection(item.Id, local.Normal));
        var worldDistance = (worldPoint - ray.Origin).Length;
        return new SceneHit(item, worldDistance, worldPoint, worldNormal, local.Point, direction, local.FaceNumber, local.Entering, local.IsSolid);
    }

    private static LocalHit? IntersectPlane(Vector3D origin, Vector3D direction, double width, double height)
    {
        if (Math.Abs(direction.Z) < 1e-15) return null;
        var distance = -origin.Z / direction.Z;
        if (distance <= OriginOffset) return null;
        var point = origin + direction * distance;
        return Math.Abs(point.X) <= width / 2 && Math.Abs(point.Y) <= height / 2
            ? new LocalHit(point, new Vector3D(0, 0, 1), 1, direction.Z > 0, false)
            : null;
    }

    private static LocalHit? IntersectSphere(Vector3D origin, Vector3D direction, double radius)
    {
        var roots = Quadratic(Dot(direction, direction), 2 * Dot(origin, direction), Dot(origin, origin) - radius * radius);
        var distance = PositiveRoot(roots);
        if (distance is null) return null;
        var point = origin + direction * distance.Value;
        var normal = Normalize(point);
        return new LocalHit(point, normal, 1, Dot(direction, normal) < 0, true);
    }

    private static LocalHit? IntersectCylinder(Vector3D origin, Vector3D direction, double radius, double length)
    {
        var hits = new List<(double Distance, Vector3D Normal, int Face)>();
        foreach (var distance in Quadratic(
            direction.X * direction.X + direction.Y * direction.Y,
            2 * (origin.X * direction.X + origin.Y * direction.Y),
            origin.X * origin.X + origin.Y * origin.Y - radius * radius))
        {
            var z = origin.Z + distance * direction.Z;
            if (distance > OriginOffset && Math.Abs(z) <= length / 2)
            {
                var point = origin + direction * distance;
                hits.Add((distance, Normalize(new Vector3D(point.X, point.Y, 0)), 1));
            }
        }
        if (Math.Abs(direction.Z) > 1e-15)
        {
            foreach (var (z, normal, face) in new[]
            {
                (-length / 2, new Vector3D(0, 0, -1), 2),
                (length / 2, new Vector3D(0, 0, 1), 3)
            })
            {
                var distance = (z - origin.Z) / direction.Z;
                var point = origin + direction * distance;
                if (distance > OriginOffset && point.X * point.X + point.Y * point.Y <= radius * radius)
                    hits.Add((distance, normal, face));
            }
        }
        if (hits.Count == 0) return null;
        var nearest = hits.MinBy(item => item.Distance);
        var hitPoint = origin + direction * nearest.Distance;
        return new LocalHit(hitPoint, nearest.Normal, nearest.Face, Dot(direction, nearest.Normal) < 0, true);
    }

    private static LocalHit? IntersectBox(Vector3D origin, Vector3D direction, double width, double height, double length)
    {
        var half = new Vector3D(width / 2, height / 2, length / 2);
        var best = double.PositiveInfinity;
        var normal = Vector3D.Zero;
        var face = 0;
        TestAxis(origin.X, direction.X, half.X, new Vector3D(-1, 0, 0), new Vector3D(1, 0, 0), 1, 2);
        TestAxis(origin.Y, direction.Y, half.Y, new Vector3D(0, -1, 0), new Vector3D(0, 1, 0), 3, 4);
        TestAxis(origin.Z, direction.Z, half.Z, new Vector3D(0, 0, -1), new Vector3D(0, 0, 1), 5, 6);
        if (!double.IsFinite(best)) return null;
        var point = origin + direction * best;
        return new LocalHit(point, normal, face, Dot(direction, normal) < 0, true);

        void TestAxis(double o, double d, double h, Vector3D negative, Vector3D positive, int negativeFace, int positiveFace)
        {
            if (Math.Abs(d) < 1e-15) return;
            Test((-h - o) / d, negative, negativeFace);
            Test((h - o) / d, positive, positiveFace);
        }
        void Test(double distance, Vector3D candidateNormal, int candidateFace)
        {
            if (distance <= OriginOffset || distance >= best) return;
            var p = origin + direction * distance;
            if (Math.Abs(p.X) <= half.X + 1e-9 && Math.Abs(p.Y) <= half.Y + 1e-9 && Math.Abs(p.Z) <= half.Z + 1e-9)
            {
                best = distance; normal = candidateNormal; face = candidateFace;
            }
        }
    }

    private static LocalHit? IntersectLens(Vector3D origin, Vector3D direction, StandardLensParameters lens)
    {
        var hits = new List<(double Distance, Vector3D Normal, int Face)>();
        AddSurface(new StandardGeometry(lens.FrontRadiusMillimeters, lens.FrontConic), 0, front: true, 1);
        AddSurface(new StandardGeometry(lens.BackRadiusMillimeters, lens.BackConic), lens.CenterThicknessMillimeters, front: false, 2);
        var frontEdge = new StandardGeometry(lens.FrontRadiusMillimeters, lens.FrontConic).Sag(lens.SemiDiameterMillimeters, 0);
        var backEdge = lens.CenterThicknessMillimeters
            + new StandardGeometry(lens.BackRadiusMillimeters, lens.BackConic).Sag(lens.SemiDiameterMillimeters, 0);
        foreach (var distance in Quadratic(
            direction.X * direction.X + direction.Y * direction.Y,
            2 * (origin.X * direction.X + origin.Y * direction.Y),
            origin.X * origin.X + origin.Y * origin.Y - lens.SemiDiameterMillimeters * lens.SemiDiameterMillimeters))
        {
            var sidePoint = origin + direction * distance;
            if (distance > OriginOffset
                && sidePoint.Z >= Math.Min(frontEdge, backEdge) - 1e-9
                && sidePoint.Z <= Math.Max(frontEdge, backEdge) + 1e-9)
            {
                hits.Add((distance, Normalize(new Vector3D(sidePoint.X, sidePoint.Y, 0)), 3));
            }
        }
        if (hits.Count == 0) return null;
        var nearest = hits.Where(item => item.Distance > OriginOffset).MinBy(item => item.Distance);
        if (nearest == default) return null;
        var point = origin + direction * nearest.Distance;
        return new LocalHit(point, nearest.Normal, nearest.Face, Dot(direction, nearest.Normal) < 0, true);

        void AddSurface(StandardGeometry geometry, double vertexZ, bool front, int face)
        {
            var shiftedOrigin = origin - new Vector3D(0, 0, vertexZ);
            var intersection = geometry.DistanceToIntersection(shiftedOrigin, direction);
            if (!intersection.IsHit || intersection.Distance <= OriginOffset) return;
            var point = origin + direction * intersection.Distance;
            if (point.X * point.X + point.Y * point.Y > lens.SemiDiameterMillimeters * lens.SemiDiameterMillimeters) return;
            var localOnGeometry = point - new Vector3D(0, 0, vertexZ);
            var sag = geometry.Sag(localOnGeometry.X, localOnGeometry.Y);
            if (!double.IsFinite(sag) || Math.Abs(localOnGeometry.Z - sag) > 1e-7) return;
            var normal = intersection.Normal;
            hits.Add((intersection.Distance, front ? -normal : normal, face));
        }
    }

    private static LocalHit? IntersectMesh(
        NonSequentialMeshAsset asset,
        Vector3D origin,
        Vector3D direction,
        MeshObjectParameters parameters)
    {
        var hit = asset.GetGeometry().Intersect(origin, direction, parameters.TwoSided);
        return hit is null
            ? null
            : new LocalHit(
                hit.Point,
                hit.Normal,
                hit.FaceNumber,
                hit.Entering,
                parameters.Behavior == NonSequentialSurfaceBehavior.Refractive);
    }

    private static NonSequentialSurfaceBehavior Behavior(NonSequentialObjectDefinition item) => item.Parameters switch
    {
        PlaneRectangleParameters value => value.Behavior,
        SphereParameters value => value.Behavior,
        CylinderParameters value => value.Behavior,
        BoxParameters value => value.Behavior,
        StandardLensParameters => NonSequentialSurfaceBehavior.Refractive,
        MeshObjectParameters value => value.Behavior,
        DetectorRectangleParameters => NonSequentialSurfaceBehavior.Absorbing,
        _ => NonSequentialSurfaceBehavior.Absorbing
    };

    private static string SolidMaterial(NonSequentialObjectDefinition item) => item.Parameters switch
    {
        SphereParameters value => value.Material,
        CylinderParameters value => value.Material,
        BoxParameters value => value.Material,
        StandardLensParameters value => value.Material,
        MeshObjectParameters value => value.Material,
        _ => "Air"
    };

    private static string OutsideMaterial(NonSequentialDocument document, NonSequentialObjectDefinition item)
    {
        if (item.ContainingObjectId is not Guid containerId) return document.AmbientMaterial;
        var container = document.Objects.FirstOrDefault(value => value.Id == containerId);
        return container is null ? document.AmbientMaterial : SolidMaterial(container);
    }

    private static string PlaneDestination(PlaneRectangleParameters plane, Vector3D localDirection) =>
        localDirection.Z > 0 ? plane.MaterialAfter : plane.MaterialBefore;

    private static NonSequentialRaySegment Segment(
        BranchState state,
        SceneHit hit,
        double path,
        double opticalPath,
        Vector3D outgoing,
        RayInteractionKind? interaction) => new(
            state.Id,
            hit.Item.Id,
            hit.FaceNumber,
            state.Ray.Origin,
            hit.WorldPoint,
            outgoing,
            hit.WorldNormal,
            state.Ray.WavelengthNanometers,
            state.Ray.Intensity,
            hit.Distance,
            path,
            opticalPath,
            interaction);

    private static NonSequentialRayBranch Complete(BranchState state, NonSequentialTerminationReason reason) => new(
        state.Id,
        state.ParentId,
        state.Level,
        state.SourceObjectId,
        state.Segments.ToArray(),
        reason,
        state.Ray.Intensity,
        state.Ray.WavelengthNanometers);

    private static OpticalResult RefractAndFresnel(Vector3D direction, Vector3D normal, double n1, double n2)
    {
        var eta = n1 / Math.Max(1e-12, n2);
        var cosI = Math.Clamp(-Dot(normal, direction), 0, 1);
        var sinT2 = eta * eta * (1 - cosI * cosI);
        var reflected = Normalize(Reflect(direction, normal));
        if (sinT2 >= 1) return new OpticalResult(reflected, reflected, 1, true);
        var cosT = Math.Sqrt(Math.Max(0, 1 - sinT2));
        var transmitted = Normalize(eta * direction + (eta * cosI - cosT) * normal);
        var rsDenominator = n1 * cosI + n2 * cosT;
        var rpDenominator = n1 * cosT + n2 * cosI;
        var rs = Math.Abs(rsDenominator) < 1e-15 ? 1 : Math.Pow((n1 * cosI - n2 * cosT) / rsDenominator, 2);
        var rp = Math.Abs(rpDenominator) < 1e-15 ? 1 : Math.Pow((n1 * cosT - n2 * cosI) / rpDenominator, 2);
        return new OpticalResult(reflected, transmitted, Math.Clamp((rs + rp) / 2, 0, 1), false);
    }

    private static double[] Quadratic(double a, double b, double c)
    {
        if (Math.Abs(a) < 1e-15) return Math.Abs(b) < 1e-15 ? Array.Empty<double>() : new[] { -c / b };
        var discriminant = b * b - 4 * a * c;
        if (discriminant < 0) return Array.Empty<double>();
        var root = Math.Sqrt(discriminant);
        return new[] { (-b - root) / (2 * a), (-b + root) / (2 * a) }.OrderBy(value => value).ToArray();
    }

    private static double? PositiveRoot(IEnumerable<double> roots) => roots.Where(value => value > OriginOffset).Cast<double?>().FirstOrDefault();
    private static Vector3D Reflect(Vector3D direction, Vector3D normal) => direction - 2 * Dot(direction, normal) * normal;
    private static double Dot(Vector3D left, Vector3D right) => left.X * right.X + left.Y * right.Y + left.Z * right.Z;
    private static Vector3D Normalize(Vector3D value) => value.Length <= 1e-15 ? new Vector3D(0, 0, 1) : value / value.Length;
    private static bool IsSource(NonSequentialObjectKind kind) => kind is NonSequentialObjectKind.SourceRay
        or NonSequentialObjectKind.SourcePoint or NonSequentialObjectKind.SourceRectangle or NonSequentialObjectKind.SourceGaussian
        or NonSequentialObjectKind.SourceEllipse or NonSequentialObjectKind.SourceTwoAngle or NonSequentialObjectKind.SourceRadial
        or NonSequentialObjectKind.SourceVolumeRectangle or NonSequentialObjectKind.SourceVolumeEllipse
        or NonSequentialObjectKind.SourceVolumeCylinder;

    private static Vector3D ConeDirection(Random random, double halfAngleDegrees)
    {
        var max = halfAngleDegrees * Math.PI / 180;
        var cosTheta = 1 - random.NextDouble() * (1 - Math.Cos(max));
        var sinTheta = Math.Sqrt(Math.Max(0, 1 - cosTheta * cosTheta));
        var phi = random.NextDouble() * Math.PI * 2;
        return new Vector3D(sinTheta * Math.Cos(phi), sinTheta * Math.Sin(phi), cosTheta);
    }

    private static Vector3D GaussianDirection(Random random, double halfAngleDegrees)
    {
        var sigma = halfAngleDegrees * Math.PI / 180 / 2;
        return Normalize(new Vector3D(NextGaussian(random) * sigma, NextGaussian(random) * sigma, 1));
    }

    private static Vector3D AnisotropicDirection(Random random, double halfAngleXDegrees, double halfAngleYDegrees)
    {
        var radius = Math.Sqrt(random.NextDouble());
        var azimuth = random.NextDouble() * Math.PI * 2;
        var x = radius * Math.Cos(azimuth) * Math.Tan(halfAngleXDegrees * Math.PI / 180);
        var y = radius * Math.Sin(azimuth) * Math.Tan(halfAngleYDegrees * Math.PI / 180);
        return Normalize(new Vector3D(x, y, 1));
    }

    private sealed class RadialDirectionSampler
    {
        const int binCount = 2_048;
        private readonly double[] _cumulative = new double[binCount];
        private readonly double _maximumAngle;
        private readonly double _total;

        public RadialDirectionSampler(IReadOnlyList<SourceRadialSample> samples)
        {
            _maximumAngle = samples[^1].AngleDegrees * Math.PI / 180;
            for (var index = 0; index < binCount; index++)
            {
                var theta = _maximumAngle * (index + 0.5) / binCount;
                var intensity = RadialIntensity(samples, theta * 180 / Math.PI);
                _total += Math.Max(0, intensity * Math.Sin(theta));
                _cumulative[index] = _total;
            }
        }

        public Vector3D Next(Random random)
        {
            if (_maximumAngle <= 1e-15 || _total <= 1e-30) return new Vector3D(0, 0, 1);
            var target = random.NextDouble() * _total;
            var bin = Array.BinarySearch(_cumulative, target);
            if (bin < 0) bin = ~bin;
            bin = Math.Clamp(bin, 0, binCount - 1);
            var theta = _maximumAngle * (bin + random.NextDouble()) / binCount;
            var azimuth = random.NextDouble() * Math.PI * 2;
            var sinTheta = Math.Sin(theta);
            return new Vector3D(sinTheta * Math.Cos(azimuth), sinTheta * Math.Sin(azimuth), Math.Cos(theta));
        }
    }

    private static double RadialIntensity(IReadOnlyList<SourceRadialSample> samples, double angleDegrees)
    {
        if (angleDegrees <= samples[0].AngleDegrees) return samples[0].RelativeIntensity;
        for (var index = 1; index < samples.Count; index++)
        {
            if (angleDegrees > samples[index].AngleDegrees) continue;
            var left = samples[index - 1];
            var right = samples[index];
            var fraction = (angleDegrees - left.AngleDegrees) / (right.AngleDegrees - left.AngleDegrees);
            return left.RelativeIntensity + fraction * (right.RelativeIntensity - left.RelativeIntensity);
        }
        return samples[^1].RelativeIntensity;
    }

    private static Vector3D EllipsePoint(Random random, double radiusX, double radiusY)
    {
        var radius = Math.Sqrt(random.NextDouble());
        var angle = random.NextDouble() * Math.PI * 2;
        return new Vector3D(radiusX * radius * Math.Cos(angle), radiusY * radius * Math.Sin(angle), 0);
    }

    private static Vector3D BoxPoint(Random random, double width, double height, double depth) => new(
        (random.NextDouble() - 0.5) * width,
        (random.NextDouble() - 0.5) * height,
        (random.NextDouble() - 0.5) * depth);

    private static Vector3D CylinderPoint(Random random, double radiusX, double radiusY, double length)
    {
        var point = EllipsePoint(random, radiusX, radiusY);
        return point with { Z = (random.NextDouble() - 0.5) * length };
    }

    private static Vector3D EllipsoidPoint(Random random, double radiusX, double radiusY, double radiusZ)
    {
        while (true)
        {
            var x = random.NextDouble() * 2 - 1;
            var y = random.NextDouble() * 2 - 1;
            var z = random.NextDouble() * 2 - 1;
            if (x * x + y * y + z * z > 1) continue;
            return new Vector3D(x * radiusX, y * radiusY, z * radiusZ);
        }
    }

    private static double NextGaussian(Random random)
    {
        var u1 = Math.Max(double.Epsilon, random.NextDouble());
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * random.NextDouble());
    }

    private static int WavelengthNumber(NonSequentialDocument document, double wavelengthNanometers)
    {
        var index = document.Wavelengths.ToList().FindIndex(item =>
            Math.Abs(item.Nanometers - wavelengthNanometers) <= 1e-9);
        return Math.Max(1, index + 1);
    }

    private sealed record GeneratedRay(Guid? SourceObjectId, RealRay Ray, string InitialMaterial);
    private sealed record LocalHit(Vector3D Point, Vector3D Normal, int FaceNumber, bool Entering, bool IsSolid);
    private sealed record SceneHit(
        NonSequentialObjectDefinition Item,
        double Distance,
        Vector3D WorldPoint,
        Vector3D WorldNormal,
        Vector3D LocalPoint,
        Vector3D LocalDirection,
        int FaceNumber,
        bool Entering,
        bool IsSolid);
    private sealed record OpticalResult(Vector3D Reflected, Vector3D Transmitted, double Reflectance, bool TotalInternalReflection);
    private sealed record BranchState(
        long Id,
        long? ParentId,
        int Level,
        Guid? SourceObjectId,
        RealRay Ray,
        string MaterialName,
        HashSet<Guid> InsideObjects,
        List<NonSequentialRaySegment> Segments,
        double PathLength,
        double OpticalPathLength);

    private sealed class DetectorAccumulator
    {
        private readonly NonSequentialObjectDefinition _item;
        private readonly DetectorRectangleParameters _parameters;
        private readonly Dictionary<int, double[]> _pixels = new();
        private readonly Dictionary<int, long[]> _hits = new();
        private readonly Dictionary<int, double[]> _angularPixels = new();
        private readonly Dictionary<int, long[]> _angularHits = new();

        public DetectorAccumulator(NonSequentialObjectDefinition item)
        {
            _item = item;
            _parameters = (DetectorRectangleParameters)item.Parameters;
        }

        public void Add(Vector3D localPoint, Vector3D localDirection, int wavelengthNumber, double power)
        {
            var x = (int)Math.Floor((localPoint.X / _parameters.WidthMillimeters + 0.5) * _parameters.PixelsX);
            var y = (int)Math.Floor((localPoint.Y / _parameters.HeightMillimeters + 0.5) * _parameters.PixelsY);
            if (x < 0 || x >= _parameters.PixelsX || y < 0 || y >= _parameters.PixelsY) return;
            if (!_pixels.TryGetValue(wavelengthNumber, out var values))
            {
                values = new double[_parameters.PixelsX * _parameters.PixelsY];
                _pixels[wavelengthNumber] = values;
            }
            values[y * _parameters.PixelsX + x] += power;
            if (!_hits.TryGetValue(wavelengthNumber, out var hitValues))
            {
                hitValues = new long[_parameters.PixelsX * _parameters.PixelsY];
                _hits[wavelengthNumber] = hitValues;
            }
            hitValues[y * _parameters.PixelsX + x]++;

            var normalized = Normalize(localDirection);
            var angleX = Math.Atan2(normalized.X, Math.Abs(normalized.Z)) * 180 / Math.PI;
            var angleY = Math.Atan2(normalized.Y, Math.Abs(normalized.Z)) * 180 / Math.PI;
            var angularX = Math.Clamp((int)Math.Floor((angleX / 180 + 0.5) * _parameters.PixelsX), 0, _parameters.PixelsX - 1);
            var angularY = Math.Clamp((int)Math.Floor((angleY / 180 + 0.5) * _parameters.PixelsY), 0, _parameters.PixelsY - 1);
            if (!_angularPixels.TryGetValue(wavelengthNumber, out var angularValues))
            {
                angularValues = new double[_parameters.PixelsX * _parameters.PixelsY];
                _angularPixels[wavelengthNumber] = angularValues;
            }
            angularValues[angularY * _parameters.PixelsX + angularX] += power;
            if (!_angularHits.TryGetValue(wavelengthNumber, out var angularHitValues))
            {
                angularHitValues = new long[_parameters.PixelsX * _parameters.PixelsY];
                _angularHits[wavelengthNumber] = angularHitValues;
            }
            angularHitValues[angularY * _parameters.PixelsX + angularX]++;
        }

        public NonSequentialDetectorFrame ToFrame(NonSequentialDocument document)
        {
            var byWavelength = new Dictionary<int, IReadOnlyList<double>>();
            foreach (var wavelength in document.Wavelengths.Select((value, index) => (value, index)))
            {
                byWavelength[wavelength.index + 1] = _pixels.TryGetValue(wavelength.index + 1, out var values)
                    ? values.ToArray()
                    : new double[_parameters.PixelsX * _parameters.PixelsY];
            }
            return new NonSequentialDetectorFrame(
                _item.Id,
                _item.Name,
                _parameters.PixelsX,
                _parameters.PixelsY,
                byWavelength,
                byWavelength.Values.Sum(values => values.Sum()),
                document.Wavelengths.Select((_, index) => index + 1).ToDictionary(
                    wavelength => wavelength,
                    wavelength => (IReadOnlyList<long>)(_hits.TryGetValue(wavelength, out var values)
                        ? values.ToArray()
                        : new long[_parameters.PixelsX * _parameters.PixelsY])),
                document.Wavelengths.Select((_, index) => index + 1).ToDictionary(
                    wavelength => wavelength,
                    wavelength => (IReadOnlyList<double>)(_angularPixels.TryGetValue(wavelength, out var values)
                        ? values.ToArray()
                        : new double[_parameters.PixelsX * _parameters.PixelsY])),
                document.Wavelengths.Select((_, index) => index + 1).ToDictionary(
                    wavelength => wavelength,
                    wavelength => (IReadOnlyList<long>)(_angularHits.TryGetValue(wavelength, out var values)
                        ? values.ToArray()
                        : new long[_parameters.PixelsX * _parameters.PixelsY])));
        }
    }

    private sealed record Aabb(Vector3D Minimum, Vector3D Maximum)
    {
        public bool Hits(RealRay ray)
        {
            var minimum = 0.0;
            var maximum = double.PositiveInfinity;
            return Axis(ray.Origin.X, ray.Direction.X, Minimum.X, Maximum.X)
                && Axis(ray.Origin.Y, ray.Direction.Y, Minimum.Y, Maximum.Y)
                && Axis(ray.Origin.Z, ray.Direction.Z, Minimum.Z, Maximum.Z);

            bool Axis(double origin, double direction, double low, double high)
            {
                if (Math.Abs(direction) < 1e-15) return origin >= low && origin <= high;
                var first = (low - origin) / direction;
                var second = (high - origin) / direction;
                if (first > second) (first, second) = (second, first);
                minimum = Math.Max(minimum, first);
                maximum = Math.Min(maximum, second);
                return maximum >= minimum;
            }
        }
    }

    private sealed record BoundedObject(NonSequentialObjectDefinition Item, Aabb Bounds)
    {
        public static BoundedObject Create(NonSequentialDocument document, NonSequentialObjectDefinition item)
        {
            if (item.Parameters is MeshObjectParameters mesh)
            {
                var asset = document.FindMeshAsset(mesh.MeshAssetId);
                return FromLocalBounds(document, item, asset.BoundsMinimum, asset.BoundsMaximum);
            }

            var (halfX, halfY, minimumZ, maximumZ) = item.Parameters switch
            {
                PlaneRectangleParameters value => (value.WidthMillimeters / 2, value.HeightMillimeters / 2, -OriginOffset, OriginOffset),
                DetectorRectangleParameters value => (value.WidthMillimeters / 2, value.HeightMillimeters / 2, -OriginOffset, OriginOffset),
                SphereParameters value => (value.RadiusMillimeters, value.RadiusMillimeters, -value.RadiusMillimeters, value.RadiusMillimeters),
                CylinderParameters value => (value.RadiusMillimeters, value.RadiusMillimeters, -value.LengthMillimeters / 2, value.LengthMillimeters / 2),
                BoxParameters value => (value.WidthMillimeters / 2, value.HeightMillimeters / 2, -value.LengthMillimeters / 2, value.LengthMillimeters / 2),
                StandardLensParameters value => (value.SemiDiameterMillimeters, value.SemiDiameterMillimeters, -value.SemiDiameterMillimeters, value.CenterThicknessMillimeters + value.SemiDiameterMillimeters),
                _ => (0.0, 0.0, 0.0, 0.0)
            };
            return FromLocalBounds(
                document,
                item,
                new Vector3D(-halfX, -halfY, minimumZ),
                new Vector3D(halfX, halfY, maximumZ));
        }

        private static BoundedObject FromLocalBounds(
            NonSequentialDocument document,
            NonSequentialObjectDefinition item,
            Vector3D minimum,
            Vector3D maximum)
        {
            var corners = new List<Vector3D>(8);
            foreach (var x in new[] { minimum.X, maximum.X })
                foreach (var y in new[] { minimum.Y, maximum.Y })
                    foreach (var z in new[] { minimum.Z, maximum.Z })
                        corners.Add(document.ToWorldPoint(item.Id, new Vector3D(x, y, z)));
            return new BoundedObject(item, new Aabb(
                new Vector3D(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Min(p => p.Z)),
                new Vector3D(corners.Max(p => p.X), corners.Max(p => p.Y), corners.Max(p => p.Z))));
        }
    }

    private sealed class BvhNode
    {
        private BvhNode(Aabb bounds, BvhNode? left, BvhNode? right, BoundedObject[]? items)
        {
            Bounds = bounds; Left = left; Right = right; Items = items;
        }
        private Aabb Bounds { get; }
        private BvhNode? Left { get; }
        private BvhNode? Right { get; }
        private BoundedObject[]? Items { get; }

        public static BvhNode? Build(BoundedObject[] items)
        {
            if (items.Length == 0) return null;
            var bounds = Union(items.Select(item => item.Bounds));
            if (items.Length <= 4) return new BvhNode(bounds, null, null, items);
            var extents = bounds.Maximum - bounds.Minimum;
            var axis = extents.X >= extents.Y && extents.X >= extents.Z ? 0 : extents.Y >= extents.Z ? 1 : 2;
            var ordered = items.OrderBy(item => axis switch
            {
                0 => item.Bounds.Minimum.X + item.Bounds.Maximum.X,
                1 => item.Bounds.Minimum.Y + item.Bounds.Maximum.Y,
                _ => item.Bounds.Minimum.Z + item.Bounds.Maximum.Z
            }).ToArray();
            var middle = ordered.Length / 2;
            return new BvhNode(bounds, Build(ordered[..middle]), Build(ordered[middle..]), null);
        }

        public IEnumerable<BoundedObject> Candidates(RealRay ray)
        {
            if (!Bounds.Hits(ray)) yield break;
            if (Items is not null)
            {
                foreach (var item in Items) if (item.Bounds.Hits(ray)) yield return item;
                yield break;
            }
            if (Left is not null) foreach (var item in Left.Candidates(ray)) yield return item;
            if (Right is not null) foreach (var item in Right.Candidates(ray)) yield return item;
        }

        private static Aabb Union(IEnumerable<Aabb> boxes)
        {
            var values = boxes.ToArray();
            return new Aabb(
                new Vector3D(values.Min(v => v.Minimum.X), values.Min(v => v.Minimum.Y), values.Min(v => v.Minimum.Z)),
                new Vector3D(values.Max(v => v.Maximum.X), values.Max(v => v.Maximum.Y), values.Max(v => v.Maximum.Z)));
        }
    }
}
