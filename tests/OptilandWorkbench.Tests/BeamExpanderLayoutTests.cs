using OptilandWorkbench.Core;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Visualization;

namespace OptilandWorkbench.Tests;

public sealed class BeamExpanderLayoutTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InfiniteObjectDoesNotBlockTheConcaveEntranceSurface(bool batched)
    {
        var optic = await LoadAsync();
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, 1, 0.266);
        using var trace = optic.SequentialRayTracer.Trace(bundle,
            TraceRequest.FullHistory() with { UseBatchedBackend = batched });
        for (var surface = 0; surface < 6; surface++)
        {
            Assert.True(trace.TryGetSample(0, surface, out var sample), $"Missing surface {surface}");
            Assert.False(sample.Vignetted, $"Ray stopped at surface {surface}: {sample.Position}");
        }

        Assert.True(trace.TryGetSample(0, 5, out var output));
        Assert.InRange(output.Position.Y / 0.6, 5.99, 6.01);
        Assert.InRange(Math.Abs(output.Direction.Y), 0, 2e-6);
        Assert.Equal(3.6000008296163113, output.Position.Y, 9);
        Assert.True(trace.TryGetSample(0, 0, out var launch));
        Assert.Equal(bundle.Rays[0].Origin, launch.Position);
        Assert.Equal(0, launch.SegmentLength);
        Assert.Null(launch.InteractionKind);
    }

    [Fact]
    public async Task LayoutShowsAnExpandedBundleAndPreservesGaussianIntensity()
    {
        var optic = await LoadAsync();
        Assert.True(optic.ImageSpaceAfocal);
        var scene = new Layout2DBuilder(optic).Build(options: new LayoutBuildOptions(RayCount: 7));
        Assert.Equal(7, scene.Rays.Count);
        var center = scene.Rays.Single(ray => ray.PupilIndex == 3);
        var edge = scene.Rays.Single(ray => ray.PupilIndex == 6);
        var incident = edge.Segments.Single(segment => segment.SourceSurfaceNumber == 0 && segment.TargetSurfaceNumber == 1);
        Assert.Equal(LayoutRaySegmentType.Incident, incident.SegmentType);
        Assert.Equal(LayoutRayInteractionType.None, incident.InteractionType);
        Assert.InRange(edge.Points[^1].Y / edge.Points[0].Y, 5.99, 6.01);
        Assert.Equal(Math.Exp(-2 * 1.44 * 0.85 * 0.85), edge.FinalIntensity / center.FinalIntensity, 10);
    }

    [Fact]
    public async Task CompactCorningNameUsesCatalogDispersionAndSurvivesSaving()
    {
        var optic = await LoadAsync();
        var material = Assert.IsType<CatalogGlassMaterial>(optic.SurfaceGroup.Items[3].MaterialAfter);
        Assert.Equal("C79-80", material.Name);
        Assert.InRange(material.RefractiveIndex(266), 1.4997, 1.4998);
        var restored = Optic.FromSnapshot(optic.ToSnapshot());
        Assert.Equal(material.RefractiveIndex(266), restored.SurfaceGroup.Items[3].MaterialAfter.RefractiveIndex(266), 12);
    }

    [Fact]
    public async Task RayAimingCanReachConcaveFirstSurfaceFromInfiniteObject()
    {
        var optic = await LoadAsync();
        var sample = optic.SequentialRayTracer.TraceGenericSurfaceSample(0, 0, 0, 1, 0.266, 1, aimAtStop: true);
        Assert.NotNull(sample);
        Assert.False(sample.Vignetted);
        Assert.Equal(0.6, sample.Position.Y, 8);
        Assert.True(sample.Position.Z < 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ZeroObjectDistanceStillTracesTheFiniteObjectPlane(bool batched)
    {
        var optic = await LoadAsync();
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, 1, 0.266);
        optic.SurfaceGroup.Items[0].Thickness = 0;
        using var trace = optic.SequentialRayTracer.Trace(bundle,
            TraceRequest.FullHistory() with { UseBatchedBackend = batched });
        Assert.True(trace.TryGetSample(0, 0, out var obj));
        Assert.Equal(0, obj.Position.Z);
        Assert.True(obj.SegmentLength > 0);
        Assert.True(trace.TryGetSample(0, 1, out var firstFace));
        Assert.True(firstFace.Vignetted);
    }

    private static Task<Optic> LoadAsync() => new ZemaxZmxImporter().ImportFileAsync(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "beam-expander-266nm-6x.zmx"));
}
