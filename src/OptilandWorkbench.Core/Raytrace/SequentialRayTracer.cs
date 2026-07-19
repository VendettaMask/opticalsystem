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

    public SequentialTrace Trace(RealRayBundle bundle)
    {
        var histories = new List<IReadOnlyList<RayTraceSample>>();

        foreach (var sourceRay in bundle.Rays)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var ray = sourceRay.Normalize();
            var currentMaterial = ResolveMaterial("Air");
            var cumulativePathLength = 0.0;
            var cumulativeOpticalPathLength = 0.0;
            var history = new List<RayTraceSample>();

            foreach (var surface in _optic.SurfaceGroup.Items)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
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
