using System.Buffers;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Raytrace;

public sealed partial class SequentialRayTracer
{
    private void TraceSurfaceMajor(
        RealRayBundle bundle,
        TraceRequest request,
        OpticalSurface[] surfaces,
        int[] surfaceSlots,
        int maximumRequiredSurface,
        RayTraceSampleValue[] samples,
        bool[] hasSamples,
        double[]? finalOpticalPaths,
        bool[]? hasFinalOpticalPath,
        PooledDirectionBatch initialDirections,
        IMaterial ambientMaterial)
    {
        var rayCount = bundle.Rays.Count;
        var states = new PooledRayStateBuffer(rayCount);
        var materials = states.Materials;
        var cumulativePaths = states.CumulativePath;
        var cumulativeOpticalPaths = states.CumulativeOpticalPath;
        var active = states.Active;
        try
        {
            states.Initialize(bundle, initialDirections, ambientMaterial);

            var parallelOptions = new ParallelOptions
            {
                CancellationToken = ComputationCancellation.Current,
                MaxDegreeOfParallelism = request.MaxDegreeOfParallelism
            };
            for (var surfaceIndex = 0; surfaceIndex <= maximumRequiredSurface; surfaceIndex++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                var surface = surfaces[surfaceIndex];
                NormalizePendingDirections(
                    states,
                    active,
                    rayCount,
                    _optic.Backend.CurrentBatched,
                    request.UseBatchedBackend);

                if (TryTraceSurfaceBatched(
                        surface,
                        surfaceIndex,
                        surfaces.Length,
                        request,
                        surfaceSlots,
                        states,
                        materials,
                        cumulativePaths,
                        cumulativeOpticalPaths,
                        active,
                        rayCount,
                        samples,
                        hasSamples,
                        finalOpticalPaths,
                        hasFinalOpticalPath))
                {
                    continue;
                }

                void TraceAtSurface(int rayIndex)
                {
                    if (!active[rayIndex])
                    {
                        return;
                    }

                    ComputationCancellation.ThrowIfCancellationRequested();
                    var result = TraceSequentialSurface(
                        surface,
                        surfaceIndex,
                        states[rayIndex],
                        materials[rayIndex],
                        cumulativePaths[rayIndex],
                        cumulativeOpticalPaths[rayIndex]);
                    var slot = surfaceSlots[surfaceIndex];
                    if (slot >= 0)
                    {
                        var offset = (slot * rayCount) + rayIndex;
                        samples[offset] = result.Sample;
                        hasSamples[offset] = true;
                    }

                    states[rayIndex] = result.Ray;
                    materials[rayIndex] = result.OutgoingMaterial;
                    cumulativePaths[rayIndex] = result.CumulativePathLength;
                    cumulativeOpticalPaths[rayIndex] = result.CumulativeOpticalPathLength;

                    if (request.NormalizeOpticalPathDifference
                        && surfaceIndex == surfaces.Length - 1
                        && result.Sample.Intensity > 0
                        && !result.Sample.Vignetted)
                    {
                        finalOpticalPaths![rayIndex] = result.Sample.CumulativeOpticalPathLength;
                        hasFinalOpticalPath![rayIndex] = true;
                    }

                    if (result.StopTracing)
                    {
                        active[rayIndex] = false;
                    }
                }

                if (rayCount >= Math.Max(1, request.ParallelThreshold)
                    && request.MaxDegreeOfParallelism != 1)
                {
                    Parallel.For(0, rayCount, parallelOptions, TraceAtSurface);
                }
                else
                {
                    for (var rayIndex = 0; rayIndex < rayCount; rayIndex++)
                    {
                        TraceAtSurface(rayIndex);
                    }
                }
            }
        }
        finally
        {
            states.Dispose();
        }
    }

    private static void NormalizePendingDirections(
        PooledRayStateBuffer states,
        bool[] active,
        int rayCount,
        IBatchedNumericBackend backend,
        bool useBatchedBackend)
    {
        var needsNormalization = false;
        for (var index = 0; index < rayCount; index++)
        {
            if (active[index] && !states[index].IsNormalized)
            {
                needsNormalization = true;
                break;
            }
        }

        if (!needsNormalization)
        {
            return;
        }

        if (!useBatchedBackend)
        {
            for (var index = 0; index < rayCount; index++)
            {
                if (active[index] && !states[index].IsNormalized)
                {
                    states[index] = states[index].Normalize();
                }
            }

            return;
        }

        var x = ArrayPool<double>.Shared.Rent(rayCount);
        var y = ArrayPool<double>.Shared.Rent(rayCount);
        var z = ArrayPool<double>.Shared.Rent(rayCount);
        try
        {
            for (var index = 0; index < rayCount; index++)
            {
                var direction = states[index].Direction;
                x[index] = direction.X;
                y[index] = direction.Y;
                z[index] = direction.Z;
            }

            backend.NormalizeDirections(
                x.AsSpan(0, rayCount),
                y.AsSpan(0, rayCount),
                z.AsSpan(0, rayCount),
                x.AsSpan(0, rayCount),
                y.AsSpan(0, rayCount),
                z.AsSpan(0, rayCount));
            for (var index = 0; index < rayCount; index++)
            {
                if (active[index] && !states[index].IsNormalized)
                {
                    states[index] = states[index] with
                    {
                        Direction = new Vector3D(x[index], y[index], z[index]),
                        IsNormalized = true
                    };
                }
            }
        }
        finally
        {
            ArrayPool<double>.Shared.Return(x, clearArray: true);
            ArrayPool<double>.Shared.Return(y, clearArray: true);
            ArrayPool<double>.Shared.Return(z, clearArray: true);
        }
    }
}
