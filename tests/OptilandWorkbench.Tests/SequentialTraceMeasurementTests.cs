using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class SequentialTraceMeasurementTests
{
    [Fact]
    public void ApertureDiagnosticsCountEveryUncachedPropagation()
    {
        var optic = Optic.CreateCookeTriplet();
        var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, .5, .5876).Rays[0];
        using var measurement = SequentialTraceMeasurement.Begin();
        var first = optic.SequentialRayTracer.DiagnoseCircularApertures(ray);
        Assert.NotNull(first.UnclippedImage);
        Assert.Equal(1, measurement.RayCount);
        Assert.Equal(first, optic.SequentialRayTracer.DiagnoseCircularApertures(ray));
        Assert.Equal(2, measurement.RayCount);
    }

    [Fact]
    public async Task NestedAndParallelScopesCountActualRequestsAndKeepSeparateRunsIsolated()
    {
        using var outer = SequentialTraceMeasurement.Begin();
        var counts = await Task.WhenAll(Enumerable.Range(1, 3).Select(count => Task.Run(() =>
        {
            using var inner = SequentialTraceMeasurement.Begin();
            var optic = Optic.CreateCookeTriplet();
            for (var index = 0; index < count; index++)
                optic.SequentialRayTracer.TraceGenericFinalSample(0, 0, 0, 0, .5876);
            return inner.RayCount;
        })));
        Assert.Equal(new long[] { 1, 2, 3 }, counts);
        Assert.Equal(6, outer.RayCount);
    }

    [Fact]
    public void EnablingMeasurementDoesNotChangeTraceResults()
    {
        var optic = Optic.CreateCookeTriplet();
        var expected = optic.SequentialRayTracer.TraceGenericFinalSample(0, .5, 0, .4, .5876, aimAtStop: true);
        using var measurement = SequentialTraceMeasurement.Begin();
        var actual = optic.SequentialRayTracer.TraceGenericFinalSample(0, .5, 0, .4, .5876, aimAtStop: true);
        Assert.Equal(expected, actual);
        Assert.True(measurement.RayCount > 1); // Includes real stop-aiming probes.
    }

    [Fact]
    public void SharedCacheHitsDoNotCountAsRepeatedComputation()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.ConfigureRayTraceCache(new RayTraceCache(maximumEntries: 4, maximumSamples: 1_000), opticRevision: 1);
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, .5, .5876);
        using var measurement = SequentialTraceMeasurement.Begin();
        using var first = optic.SequentialRayTracer.Trace(bundle, TraceRequest.FinalOnly(false));
        Assert.Equal(1, measurement.RayCount);
        using var second = optic.SequentialRayTracer.Trace(bundle, TraceRequest.FinalOnly(false));
        Assert.Equal(1, measurement.RayCount);
    }
}
