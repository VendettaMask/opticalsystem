using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class PupilLaunchCacheTests
{
    private static readonly PupilSample[] Pupils = [new(0, 0, 1), new(.2, -.3, .5), new(-.4, .1, .75)];

    [Fact]
    public void IdenticalFrozenOpticsReuseExactLaunchesWithoutRenormalizingOrSharingContainers()
    {
        var optic = Optic.CreateCookeTriplet(); var cache = new RayTraceCache();
        optic.ConfigureRayTraceCache(cache, 1);
        var first = Generate(optic);
        var same = optic.Clone(); same.Name = "a different display name";
        same.ConfigureRayTraceCache(cache, 1);
        using var rays = SequentialTraceMeasurement.Begin();
        var second = Generate(same);
        Assert.Equal(first.Rays, second.Rays);
        Assert.Equal(0, rays.RayCount);
        Assert.Equal(1, cache.PupilLaunchStatistics.Hits);
        Assert.NotSame(first.Rays, second.Rays);
        ((IList<RealRay>)second.Rays)[0] = second.Rays[0] with { Intensity = 42 };
        Assert.Equal(first.Rays, Generate(optic).Rays);
    }

    [Fact]
    public void CompletePupilWeightFieldWavelengthAndVignettingOptionsHaveDistinctKeys()
    {
        var optic = Optic.CreateCookeTriplet(); var cache = new RayTraceCache();
        optic.ConfigureRayTraceCache(cache, 1);
        Generate(optic);
        var requests = new (double Hx, double Hy, double Wave, PupilSample[] Samples, bool Vignetting)[]
        {
            (0, .5, .5876, Pupils, true),
            (0, 0, .4861, Pupils, true),
            (0, 0, .5876, [Pupils[0], Pupils[1] with { Weight = .25 }, Pupils[2]], true),
            (0, 0, .5876, [Pupils[0], Pupils[1] with { X = .25 }, Pupils[2]], true),
            (0, 0, .5876, Pupils, false)
        };
        foreach (var request in requests)
        {
            var actual = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(request.Hx, request.Hy,
                request.Wave, request.Samples, true, applyVignettingFactors: request.Vignetting);
            var expected = optic.Clone().SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(request.Hx, request.Hy,
                request.Wave, request.Samples, true, applyVignettingFactors: request.Vignetting);
            Assert.Equal(expected.Rays, actual.Rays);
        }
        Assert.Equal(0, cache.PupilLaunchStatistics.Hits);
        Assert.Equal(6, cache.PupilLaunchStatistics.EntryCount);
        var explicitTargets = new[] { (0d, 0d), (.2, -.3), (-.4, .1) };
        optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 0, .5876, Pupils, true, stopTargets: explicitTargets);
        Assert.Equal(6, cache.PupilLaunchStatistics.EntryCount);
    }

    [Theory]
    [InlineData("aperture")]
    [InlineData("coordinate")]
    [InlineData("material")]
    [InlineData("field")]
    [InlineData("wavelength")]
    public void PhysicalEditsDetachOldIdentityAndRebindingCannotReadItsOldLaunches(string edit)
    {
        var optic = Optic.CreateCookeTriplet(); var cache = new RayTraceCache();
        optic.ConfigureRayTraceCache(cache, 1); Generate(optic);
        switch (edit)
        {
            case "aperture": optic.Aperture.Value *= .9; break;
            case "coordinate": optic.SurfaceGroup.Items[2].CoordinateSystem = optic.SurfaceGroup.Items[2].CoordinateSystem with { Origin = new Vector3D(0, 0, 5.25) }; break;
            case "material": optic.SurfaceGroup.Items[1].MaterialAfter = new ConstantIndexMaterial("changed-index", 1.6); break;
            case "field": optic.Fields[0].Y += .1; break;
            case "wavelength": optic.Wavelengths[0].Nanometers += 1; break;
        }
        var hits = cache.PupilLaunchStatistics.Hits;
        var uncached = Generate(optic.Clone());
        Assert.Equal(uncached.Rays, Generate(optic).Rays);
        Assert.Equal(hits, cache.PupilLaunchStatistics.Hits);
        optic.ConfigureRayTraceCache(cache, 1);
        Assert.Equal(uncached.Rays, Generate(optic).Rays);
        Assert.Equal(hits, cache.PupilLaunchStatistics.Hits);
        Assert.Equal(uncached.Rays, Generate(optic).Rays);
        Assert.Equal(hits + 1, cache.PupilLaunchStatistics.Hits);
    }

    [Fact]
    public void CapacityRevisionClearAndCancellationRemainBounded()
    {
        var optic = Optic.CreateCookeTriplet(); var cache = new RayTraceCache(maximumEntries: 1, maximumSamples: 4);
        optic.ConfigureRayTraceCache(cache, 1); Generate(optic);
        optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, .1, .5876, Pupils, true);
        Assert.Equal(1, cache.PupilLaunchStatistics.EntryCount);
        Assert.Equal(3, cache.PupilLaunchStatistics.RayCount);
        using var source = new CancellationTokenSource(); source.Cancel();
        using (ComputationCancellation.Push(source.Token))
            Assert.ThrowsAny<OperationCanceledException>(() => Generate(optic));
        cache.SetCurrentRevision(2);
        Assert.Equal(0, cache.PupilLaunchStatistics.EntryCount);
        optic.ConfigureRayTraceCache(cache, 2); Generate(optic);
        cache.Clear();
        Assert.Equal(0, cache.PupilLaunchStatistics.RayCount);
    }

    static RealRayBundle Generate(Optic optic) => optic.SequentialRayTracer.RayGenerator
        .GenerateNormalizedPupilSamples(0, 0, .5876, Pupils, aimAtStop: true);
}
