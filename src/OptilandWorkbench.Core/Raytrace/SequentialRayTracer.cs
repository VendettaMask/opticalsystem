using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Raytrace;

public sealed record SequentialTrace(
    IReadOnlyList<IReadOnlyList<RayTraceSample>> RayHistories,
    SurfaceTraceData SurfaceTraceData);

public sealed partial class SequentialRayTracer
{
    private readonly Optic _optic;
    private RayTraceCache? _traceCache;
    private long _cacheOpticRevision;

    public SequentialRayTracer(Optic optic)
    {
        _optic = optic;
        RayGenerator = new RayGenerator(optic);
    }

    public RayGenerator RayGenerator { get; }

    internal void ConfigureCache(RayTraceCache? cache, long opticRevision)
    {
        _traceCache = cache;
        _cacheOpticRevision = opticRevision;
    }

    public SequentialTrace Trace()
    {
        var bundle = RayGenerator.Generate();
        return Trace(bundle);
    }

    public SequentialTrace TraceNormalized(
        double normalizedFieldX,
        double normalizedFieldY,
        double wavelengthMicrometers,
        int sampleCount = 100,
        string distribution = "hexapolar")
    {
        var bundle = RayGenerator.GenerateNormalized(
            normalizedFieldX,
            normalizedFieldY,
            wavelengthMicrometers,
            sampleCount,
            distribution);
        return Trace(bundle);
    }

    public SequentialTrace TraceGeneric(
        double normalizedFieldX,
        double normalizedFieldY,
        double normalizedPupilX,
        double normalizedPupilY,
        double wavelengthMicrometers)
    {
        var bundle = RayGenerator.GenerateGeneric(
            normalizedFieldX,
            normalizedFieldY,
            normalizedPupilX,
            normalizedPupilY,
            wavelengthMicrometers);
        return Trace(bundle);
    }
    public RayTraceSample? TraceGenericFinalSample(
        double normalizedFieldX,
        double normalizedFieldY,
        double normalizedPupilX,
        double normalizedPupilY,
        double wavelengthMicrometers)
    {
        var bundle = RayGenerator.GenerateGeneric(
            normalizedFieldX,
            normalizedFieldY,
            normalizedPupilX,
            normalizedPupilY,
            wavelengthMicrometers);
        if (_optic.SurfaceGroup.Items.Count == 0)
        {
            return null;
        }

        var surfaceIndex = _optic.SurfaceGroup.Items.Count - 1;
        using var trace = Trace(bundle, TraceRequest.FinalOnly(false));
        return trace.TryGetSample(0, surfaceIndex, out var sample) ? sample.ToRayTraceSample() : null;
    }

    public RayTraceSample? TraceGenericSurfaceSample(
        double normalizedFieldX,
        double normalizedFieldY,
        double normalizedPupilX,
        double normalizedPupilY,
        double wavelengthMicrometers,
        int surfaceIndex,
        bool aimAtStop = false)
    {
        if (surfaceIndex < 0 || surfaceIndex >= _optic.SurfaceGroup.Items.Count)
        {
            return null;
        }

        var bundle = RayGenerator.GenerateGeneric(
            normalizedFieldX,
            normalizedFieldY,
            normalizedPupilX,
            normalizedPupilY,
            wavelengthMicrometers,
            aimAtStop);
        using var trace = Trace(bundle, TraceRequest.Selected(new[] { surfaceIndex }));
        return trace.TryGetSample(0, surfaceIndex, out var sample)
            ? sample.ToRayTraceSample()
            : null;
    }


    public IReadOnlyList<RayTraceSample?> TraceFinalSamples(RealRayBundle bundle)
    {
        if (_optic.SurfaceGroup.Items.Count == 0)
        {
            return new RayTraceSample?[bundle.Rays.Count];
        }

        var finalSurfaceIndex = _optic.SurfaceGroup.Items.Count - 1;
        using var trace = Trace(bundle, TraceRequest.FinalOnly(false));
        var values = trace.GetSurfaceSamples(finalSurfaceIndex);
        var samples = new RayTraceSample?[trace.RayCount];
        for (var index = 0; index < samples.Length; index++)
        {
            if (values[index] is { } value)
            {
                samples[index] = value.ToRayTraceSample();
            }
        }

        return samples;
    }
    internal RayTraceSample? TraceToSurface(RealRay sourceRay, int surfaceIndex)
    {
        if (surfaceIndex < 0 || surfaceIndex >= _optic.SurfaceGroup.Items.Count)
        {
            return null;
        }

        var ray = sourceRay.Normalize();
        var currentMaterial = ResolveMaterial("Air");
        var cumulativePathLength = 0.0;
        var cumulativeOpticalPathLength = 0.0;

        for (var index = 0; index <= surfaceIndex; index++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var surface = _optic.SurfaceGroup.Items[index];
            var result = surface.TraceRayValue(
                ray,
                currentMaterial,
                surface.MaterialAfter,
                cumulativePathLength,
                cumulativeOpticalPathLength,
                ignorePhysicalAperture: true);
            if (index == surfaceIndex)
            {
                return result.Sample.ToRayTraceSample();
            }

            if (result.StopTracing)
            {
                return null;
            }

            ray = result.Ray;
            currentMaterial = result.OutgoingMaterial;
            cumulativePathLength = result.CumulativePathLength;
            cumulativeOpticalPathLength = result.CumulativeOpticalPathLength;
        }

        return null;
    }

    public SequentialTrace Trace(RealRayBundle bundle)
    {
        using var requested = Trace(bundle, TraceRequest.FullHistory(recordSurfaceData: false));
        var histories = MaterializeHistories(requested);
        var surfaceTraceData = BuildSurfaceTraceData(_optic.SurfaceGroup.Items, histories);
        _optic.SurfaceGroup.RecordTrace(surfaceTraceData);
        return new SequentialTrace(histories, surfaceTraceData);
    }

    public RequestedTrace Trace(RealRayBundle bundle, TraceRequest request)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(request);

        if (request.MaxDegreeOfParallelism is 0 or < -1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "MaxDegreeOfParallelism must be -1 or a positive value.");
        }
        request = request with
        {
            MaxDegreeOfParallelism = ComputationParallelism.ResolveMaxDegreeOfParallelism(request.MaxDegreeOfParallelism)
        };
        var surfaces = _optic.SurfaceGroup.Items
            .Select(surface => surface.Clone())
            .ToArray();
        var retainedSurfaceIndices = request.ResolveSurfaceIndices(surfaces.Length);
        if (request.RecordSurfaceData && request.Retention != TraceRetention.FullHistory)
        {
            throw new ArgumentException(
                "Surface trace recording requires FullHistory retention.",
                nameof(request));
        }

        var rayCount = bundle.Rays.Count;
        var retainedCount = retainedSurfaceIndices.Length;
        var sampleCount = checked(rayCount * retainedCount);
        var samples = System.Buffers.ArrayPool<RayTraceSampleValue>.Shared.Rent(Math.Max(1, sampleCount));
        var hasSamples = System.Buffers.ArrayPool<bool>.Shared.Rent(Math.Max(1, sampleCount));
        Array.Clear(hasSamples, 0, Math.Max(1, sampleCount));
        var requestedTrace = new RequestedTrace(rayCount, retainedSurfaceIndices, samples, hasSamples);

        if (rayCount == 0 || surfaces.Length == 0 || retainedCount == 0)
        {
            return requestedTrace;
        }

        RayTraceCacheKey? cacheKey = null;
        if (!request.RecordSurfaceData && _traceCache is not null)
        {
            cacheKey = RayTraceCacheKey.Create(
                _cacheOpticRevision,
                _optic.Backend.Current.Name,
                request,
                retainedSurfaceIndices,
                bundle.Rays);
            if (_traceCache.TryCopyTo(cacheKey, samples, hasSamples))
            {
                return requestedTrace;
            }
        }

        var surfaceSlots = new int[surfaces.Length];
        Array.Fill(surfaceSlots, -1);
        for (var slot = 0; slot < retainedCount; slot++)
        {
            surfaceSlots[retainedSurfaceIndices[slot]] = slot;
        }

        var finalOpticalPaths = request.NormalizeOpticalPathDifference
            ? System.Buffers.ArrayPool<double>.Shared.Rent(rayCount)
            : null;
        var hasFinalOpticalPath = request.NormalizeOpticalPathDifference
            ? System.Buffers.ArrayPool<bool>.Shared.Rent(rayCount)
            : null;
        if (hasFinalOpticalPath is not null)
        {
            Array.Clear(hasFinalOpticalPath, 0, rayCount);
        }

        var ambientMaterial = ResolveMaterial("Air");
        var maximumRequiredSurface = request.NormalizeOpticalPathDifference
            ? surfaces.Length - 1
            : retainedSurfaceIndices[^1];

        using var initialDirections = PooledDirectionBatch.Create(
            bundle,
            _optic.Backend.CurrentBatched,
            request.UseBatchedBackend);


        try
        {
            TraceSurfaceMajor(
                bundle,
                request,
                surfaces,
                surfaceSlots,
                maximumRequiredSurface,
                samples,
                hasSamples,
                finalOpticalPaths,
                hasFinalOpticalPath,
                initialDirections,
                ambientMaterial);

            if (request.NormalizeOpticalPathDifference)
            {
                NormalizeOpticalPathDifference(
                    samples,
                    hasSamples,
                    sampleCount,
                    finalOpticalPaths!,
                    hasFinalOpticalPath!,
                    rayCount);
            }

            if (request.RecordSurfaceData)
            {
                var histories = MaterializeHistories(requestedTrace);
                _optic.SurfaceGroup.RecordTrace(BuildSurfaceTraceData(surfaces, histories));
            }

            if (cacheKey is not null)
            {
                _traceCache?.Store(cacheKey, samples, hasSamples, sampleCount);
            }

            return requestedTrace;
        }
        catch
        {
            requestedTrace.Dispose();
            throw;
        }
        finally
        {
            if (finalOpticalPaths is not null)
            {
                System.Buffers.ArrayPool<double>.Shared.Return(finalOpticalPaths, clearArray: true);
            }

            if (hasFinalOpticalPath is not null)
            {
                System.Buffers.ArrayPool<bool>.Shared.Return(hasFinalOpticalPath, clearArray: true);
            }
        }
    }

    private static IReadOnlyList<IReadOnlyList<RayTraceSample>> MaterializeHistories(RequestedTrace trace)
    {
        var histories = new IReadOnlyList<RayTraceSample>[trace.RayCount];
        for (var rayIndex = 0; rayIndex < trace.RayCount; rayIndex++)
        {
            var history = new List<RayTraceSample>(trace.RetainedSurfaceIndices.Count);
            foreach (var surfaceIndex in trace.RetainedSurfaceIndices)
            {
                if (trace.TryGetSample(rayIndex, surfaceIndex, out var sample))
                {
                    history.Add(sample.ToRayTraceSample());
                }
            }

            histories[rayIndex] = history.ToArray();
        }

        return histories;
    }

    private static void NormalizeOpticalPathDifference(
        RayTraceSampleValue[] samples,
        bool[] hasSamples,
        int sampleCount,
        double[] finalOpticalPaths,
        bool[] hasFinalOpticalPath,
        int rayCount)
    {
        var sum = 0.0;
        var count = 0;
        for (var rayIndex = 0; rayIndex < rayCount; rayIndex++)
        {
            if (hasFinalOpticalPath[rayIndex])
            {
                sum += finalOpticalPaths[rayIndex];
                count++;
            }
        }

        if (count == 0)
        {
            return;
        }

        var reference = sum / count;
        for (var index = 0; index < sampleCount; index++)
        {
            if (hasSamples[index])
            {
                samples[index] = samples[index] with
                {
                    OpticalPathDifference = samples[index].CumulativeOpticalPathLength - reference
                };
            }
        }
    }

    private IMaterial ResolveMaterial(string material)
    {
        return _optic.Materials.Resolve(material);
    }

    private static SurfaceTraceData BuildSurfaceTraceData(
        IReadOnlyList<OpticalSurface> surfaces,
        IReadOnlyList<IReadOnlyList<RayTraceSample>> histories)
    {
        if (surfaces.Count == 0)
        {
            return SurfaceTraceData.Empty;
        }

        var records = surfaces
            .Select(surface =>
            {
                var samples = histories
                    .Select(history => history.FirstOrDefault(sample => sample.SurfaceNumber == surface.Number))
                    .ToArray();

                return new SurfaceTraceRecord(
                    surface.Number,
                    surface.Label,
                    samples.Select(sample => sample?.Position.X ?? double.NaN).ToArray(),
                    samples.Select(sample => sample?.Position.Y ?? double.NaN).ToArray(),
                    samples.Select(sample => sample?.Position.Z ?? double.NaN).ToArray(),
                    samples.Select(sample => sample?.Direction.X ?? double.NaN).ToArray(),
                    samples.Select(sample => sample?.Direction.Y ?? double.NaN).ToArray(),
                    samples.Select(sample => sample?.Direction.Z ?? double.NaN).ToArray(),
                    samples.Select(sample => sample?.Intensity ?? 0.0).ToArray(),
                    samples.Select(sample => sample?.OpticalPathDifference ?? double.NaN).ToArray(),
                    samples.Select(sample => sample?.CumulativeOpticalPathLength ?? double.NaN).ToArray());
            })
            .ToArray();

        return new SurfaceTraceData(records);
    }
}
