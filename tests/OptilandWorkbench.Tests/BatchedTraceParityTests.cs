using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Tests;

public sealed class BatchedTraceParityTests
{
    [Theory]
    [InlineData(TraceRetention.FinalOnly)]
    [InlineData(TraceRetention.SelectedSurfaces)]
    [InlineData(TraceRetention.FullHistory)]
    public void BatchedAndScalarTraceRetentionModesAreNumericallyEquivalent(
        TraceRetention retention)
    {
        var optic = Optic.CreateDemo();
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalized(
            0.35,
            0.65,
            0.5876,
            257,
            "hexapolar");
        var finalSurface = optic.SurfaceGroup.Items.Count - 1;
        var request = retention switch
        {
            TraceRetention.FinalOnly => TraceRequest.FinalOnly(),
            TraceRetention.SelectedSurfaces => TraceRequest.Selected(
                new[] { 1, finalSurface },
                normalizeOpticalPathDifference: true),
            TraceRetention.FullHistory => TraceRequest.FullHistory(),
            _ => throw new ArgumentOutOfRangeException(nameof(retention))
        };

        using var scalar = optic.SequentialRayTracer.Trace(
            bundle,
            request with
            {
                UseBatchedBackend = false,
                ParallelThreshold = int.MaxValue,
                MaxDegreeOfParallelism = 1
            });
        using var batched = optic.SequentialRayTracer.Trace(
            bundle,
            request with
            {
                UseBatchedBackend = true,
                ParallelThreshold = 1,
                MaxDegreeOfParallelism = 4
            });

        Assert.Equal(scalar.RetainedSurfaceIndices, batched.RetainedSurfaceIndices);
        foreach (var surfaceIndex in scalar.RetainedSurfaceIndices)
        {
            for (var rayIndex = 0; rayIndex < scalar.RayCount; rayIndex++)
            {
                var hasScalar = scalar.TryGetSample(
                    rayIndex,
                    surfaceIndex,
                    out var scalarSample);
                var hasBatched = batched.TryGetSample(
                    rayIndex,
                    surfaceIndex,
                    out var batchedSample);
                Assert.Equal(hasScalar, hasBatched);
                if (hasScalar)
                {
                    AssertEquivalent(scalarSample, batchedSample);
                }
            }
        }
    }

    [Fact]
    public void RayAndSurfaceViewsShareTheRequestedTraceLifetimeAndSamples()
    {
        var optic = Optic.CreateDemo();
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalized(
            0, 0, 0.5876, 8, "hexapolar");
        var trace = optic.SequentialRayTracer.Trace(
            bundle,
            TraceRequest.FullHistory());
        var ray = trace.GetRaySamples(3);
        Assert.Equal(trace.RetainedSurfaceIndices.Count, ray.Count);
        for (var slot = 0; slot < ray.Count; slot++)
        {
            var surfaceIndex = trace.RetainedSurfaceIndices[slot];
            Assert.True(trace.TryGetSample(3, surfaceIndex, out var surfaceSample));
            Assert.Equal(surfaceSample, ray[slot]);
        }

        trace.Dispose();
        Assert.Throws<ObjectDisposedException>(() => trace.GetRaySamples(0));
        Assert.Throws<ObjectDisposedException>(() => trace.GetSurfaceSamples(0));
    }

    private static void AssertEquivalent(
        RayTraceSampleValue expected,
        RayTraceSampleValue actual)
    {
        Assert.Equal(expected.SurfaceNumber, actual.SurfaceNumber);
        Assert.Equal(expected.SurfaceLabel, actual.SurfaceLabel);
        Assert.Equal(expected.Position.X, actual.Position.X, 11);
        Assert.Equal(expected.Position.Y, actual.Position.Y, 11);
        Assert.Equal(expected.Position.Z, actual.Position.Z, 11);
        Assert.Equal(expected.Direction.X, actual.Direction.X, 11);
        Assert.Equal(expected.Direction.Y, actual.Direction.Y, 11);
        Assert.Equal(expected.Direction.Z, actual.Direction.Z, 11);
        Assert.Equal(expected.Intensity, actual.Intensity, 11);
        Assert.Equal(expected.SegmentLength, actual.SegmentLength, 11);
        Assert.Equal(expected.SegmentOpticalPathLength, actual.SegmentOpticalPathLength, 11);
        Assert.Equal(expected.CumulativePathLength, actual.CumulativePathLength, 11);
        Assert.Equal(
            expected.CumulativeOpticalPathLength,
            actual.CumulativeOpticalPathLength,
            11);
        Assert.Equal(expected.OpticalPathDifference, actual.OpticalPathDifference, 11);
        Assert.Equal(expected.Vignetted, actual.Vignetted);
    }
}
