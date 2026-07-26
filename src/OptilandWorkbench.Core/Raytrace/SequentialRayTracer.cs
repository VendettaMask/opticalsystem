using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Raytrace;

public sealed record SequentialTrace(
    IReadOnlyList<IReadOnlyList<RayTraceSample>> RayHistories,
    SurfaceTraceData SurfaceTraceData);

public sealed class SequentialRayTracer
{
    private readonly Optic _optic;

    public SequentialRayTracer(Optic optic)
    {
        _optic = optic;
        RayGenerator = new RayGenerator(optic);
    }

    public RayGenerator RayGenerator { get; }

    public IRayAimer RayAimer { get; private set; } = new ParaxialRayAimer();

    public void SetAiming(string mode)
    {
        RayAimer = mode.ToLowerInvariant() switch
        {
            "iterative" => new IterativeRayAimer(),
            "robust" => new RobustRayAimer(),
            "cached" => new CachedRayAimer(new ParaxialRayAimer()),
            _ => new ParaxialRayAimer()
        };
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

    public IReadOnlyList<RayTraceSample?> TraceFinalSamples(RealRayBundle bundle)
    {
        var finalSamples = new RayTraceSample?[bundle.Rays.Count];
        var ambientMaterial = ResolveMaterial("Air");

        void TraceRay(int rayIndex)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var ray = bundle.Rays[rayIndex].Normalize();
            var currentMaterial = ambientMaterial;
            var cumulativePathLength = 0.0;
            var cumulativeOpticalPathLength = 0.0;

            foreach (var surface in _optic.SurfaceGroup.Items)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                if (surface.Label.Equals("Object", StringComparison.OrdinalIgnoreCase)
                    && !double.IsFinite(surface.CoordinateSystem.Origin.Z))
                {
                    continue;
                }

                var nextMaterial = surface.MaterialAfter;
                var result = surface.TraceRay(
                    ray,
                    currentMaterial,
                    nextMaterial,
                    cumulativePathLength,
                    cumulativeOpticalPathLength);

                ray = result.Ray;
                currentMaterial = nextMaterial;
                cumulativePathLength = result.CumulativePathLength;
                cumulativeOpticalPathLength = result.CumulativeOpticalPathLength;
                finalSamples[rayIndex] = result.Sample;

                if (result.StopTracing)
                {
                    break;
                }
            }
        }

        if (bundle.Rays.Count >= 64)
        {
            Parallel.For(
                0,
                bundle.Rays.Count,
                new ParallelOptions { CancellationToken = ComputationCancellation.Current },
                TraceRay);
        }
        else
        {
            for (var rayIndex = 0; rayIndex < bundle.Rays.Count; rayIndex++)
            {
                TraceRay(rayIndex);
            }
        }

        return finalSamples;
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
            if (surface.Label.Equals("Object", StringComparison.OrdinalIgnoreCase)
                && !double.IsFinite(surface.CoordinateSystem.Origin.Z))
            {
                continue;
            }

            var result = surface.TraceRay(
                ray,
                currentMaterial,
                surface.MaterialAfter,
                cumulativePathLength,
                cumulativeOpticalPathLength);
            if (index == surfaceIndex)
            {
                return result.Sample;
            }

            if (result.StopTracing)
            {
                return null;
            }

            ray = result.Ray;
            currentMaterial = surface.MaterialAfter;
            cumulativePathLength = result.CumulativePathLength;
            cumulativeOpticalPathLength = result.CumulativeOpticalPathLength;
        }

        return null;
    }

    public SequentialTrace Trace(RealRayBundle bundle)
    {
        var histories = new List<IReadOnlyList<RayTraceSample>>();
        var ambientMaterial = ResolveMaterial("Air");

        foreach (var sourceRay in bundle.Rays)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var ray = sourceRay.Normalize();
            var currentMaterial = ambientMaterial;
            var cumulativePathLength = 0.0;
            var cumulativeOpticalPathLength = 0.0;
            var history = new List<RayTraceSample>();

            foreach (var surface in _optic.SurfaceGroup.Items)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                if (surface.Label.Equals("Object", StringComparison.OrdinalIgnoreCase)
                    && !double.IsFinite(surface.CoordinateSystem.Origin.Z))
                {
                    continue;
                }

                var nextMaterial = surface.MaterialAfter;
                var result = surface.TraceRay(
                    ray,
                    currentMaterial,
                    nextMaterial,
                    cumulativePathLength,
                    cumulativeOpticalPathLength);

                ray = result.Ray;
                currentMaterial = nextMaterial;
                cumulativePathLength = result.CumulativePathLength;
                cumulativeOpticalPathLength = result.CumulativeOpticalPathLength;
                history.Add(result.Sample);

                if (result.StopTracing)
                {
                    break;
                }
            }

            histories.Add(history);
        }

        var normalizedHistories = NormalizeOpticalPathDifference(histories);
        var surfaceTraceData = BuildSurfaceTraceData(_optic.SurfaceGroup.Items, normalizedHistories);
        _optic.SurfaceGroup.RecordTrace(surfaceTraceData);
        return new SequentialTrace(normalizedHistories, surfaceTraceData);
    }

    private IMaterial ResolveMaterial(string material)
    {
        return _optic.Materials.Resolve(material);
    }

    private static IReadOnlyList<IReadOnlyList<RayTraceSample>> NormalizeOpticalPathDifference(
        IReadOnlyList<IReadOnlyList<RayTraceSample>> histories)
    {
        var finalSamples = histories
            .Where(history => history.Count > 0)
            .Select(history => history[^1])
            .Where(sample => !sample.Vignetted && sample.Intensity > 0)
            .ToArray();
        if (finalSamples.Length == 0)
        {
            return histories;
        }

        var referenceOpticalPathLength = finalSamples.Average(sample => sample.CumulativeOpticalPathLength);
        return histories
            .Select(history => history
                .Select(sample => sample with
                {
                    OpticalPathDifference = sample.CumulativeOpticalPathLength - referenceOpticalPathLength
                })
                .ToArray())
            .ToArray();
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
