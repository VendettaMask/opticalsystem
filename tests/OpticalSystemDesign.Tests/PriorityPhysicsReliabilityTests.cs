using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;
using Ns = OpticalSystemDesign.Core.NonSequential;

namespace OpticalSystemDesign.Tests;

public sealed class PriorityPhysicsReliabilityTests
{
    [Theory]
    [InlineData(32, 16, .08)]
    [InlineData(16, 32, .16)]
    public void RectangularSensorUsesItsVerticalPhysicalHalfExtent(int width, int height, double expected)
    {
        var optic = Optic.CreateCookeTriplet();
        CameraImagingService.ConfigureSensorField(optic, new CameraImagingRequest(Width: width, Height: height), 10);
        Assert.Equal(expected, Assert.Single(optic.Fields).Y, 12);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CachedTracesRemainIsolatedAcrossDifferentOpticsAndSnapshotReplacement(bool replaceState)
    {
        var first = Optic.CreateCookeTriplet();
        var second = Optic.CreateCookeTriplet();
        second.SurfaceGroup.Items[1].Radius += 5;
        second.SurfaceGroup.Renumber();
        var cache = new RayTraceCache(8, 10000);
        first.ConfigureRayTraceCache(cache, 42);
        var bundle = first.SequentialRayTracer.RayGenerator.GenerateNormalized(.2, .3, .5876, 32, "hexapolar");
        var request = TraceRequest.FinalOnly(normalizeOpticalPathDifference: false);
        using var original = first.SequentialRayTracer.Trace(bundle, request);
        if (replaceState) first.ApplySnapshot(second.ToSnapshot());
        else second.ConfigureRayTraceCache(cache, 42);
        var tested = replaceState ? first : second;
        var before = cache.Statistics.Hits;
        using var actual = tested.SequentialRayTracer.Trace(bundle, request);
        Assert.Equal(before, cache.Statistics.Hits);
        tested.InvalidateRayTraceCache();
        using var expected = tested.SequentialRayTracer.Trace(bundle, request);
        var surface = tested.SurfaceGroup.Items.Count - 1;
        var differences = 0;
        for (var ray = 0; ray < bundle.Rays.Count; ray++)
        {
            Assert.Equal(expected.TryGetSample(ray, surface, out var wanted), actual.TryGetSample(ray, surface, out var found));
            Assert.Equal(wanted, found);
            if (original.TryGetSample(ray, surface, out var old) && old != found) differences++;
        }
        Assert.True(differences > 0);
    }

    [Fact]
    public void SceneHashSeparatesAdjacentStringsVisibilityAndMeshMetadata()
    {
        var first = Scene("ab", ambient: "c");
        var second = Scene("a", ambient: "bc");
        Assert.NotEqual(Ns.NonSequentialSceneHasher.Compute(first), Ns.NonSequentialSceneHasher.Compute(second));
        var source = Ns.NonSequentialObjectDefinition.Create(Ns.NonSequentialObjectKind.SourceRay);
        Assert.NotEqual(Ns.NonSequentialSceneHasher.Compute(Scene(objects: [source])),
            Ns.NonSequentialSceneHasher.Compute(Scene(objects: [source with { Visible = false }])));
        var id = Guid.NewGuid();
        Ns.NonSequentialMeshAsset Mesh(double scale) => new(id, "mesh.stl", "stl", new string('A', 64), scale,
            4, 4, Vector3D.Zero, new(1, 1, 1), true, true, true, true, false, 1);
        Assert.NotEqual(Ns.NonSequentialSceneHasher.Compute(Scene(meshes: [Mesh(1)])),
            Ns.NonSequentialSceneHasher.Compute(Scene(meshes: [Mesh(2)])));
        Assert.Equal(Ns.NonSequentialSceneHasher.Compute(first), Ns.NonSequentialSceneHasher.Compute(first.Clone()));
    }

    [Fact]
    public void RayStartingInsideGlassUsesGlassOpticalPathAndTotalInternalReflection()
    {
        var sphere = Ns.NonSequentialObjectDefinition.Create(Ns.NonSequentialObjectKind.Sphere) with
        { Parameters = new Ns.SphereParameters(RadiusMillimeters: 1, Material: "N-BK7") };
        var ray = new RealRay(new(0, 0, .9), new(1, 0, 0), 587.6);
        var materials = new MaterialRegistry();
        var result = new Ns.NonSequentialDocumentTracer().Trace(Scene(objects: [sphere]), materials,
            new(DirectRay: ray, SplittingMode: Ns.NonSequentialSplittingMode.None, MaximumSegmentsPerRay: 3));
        var segment = result.Branches[0].Segments[0];
        Assert.Equal(RayInteractionKind.TotalInternalReflection, segment.InteractionKind);
        Assert.Equal(segment.CumulativePathLength * materials.Resolve("N-BK7").RefractiveIndex(587.6), segment.CumulativeOpticalPathLength, 10);
    }

    [Theory]
    [InlineData(-1, -1)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    [InlineData(1, 1)]
    public void DetectorPhysicalEdgesAgreeWithLiveAndDatabaseReconstructedPixels(double x, double y)
    {
        var detector = Ns.NonSequentialObjectDefinition.Create(Ns.NonSequentialObjectKind.DetectorRectangle) with
        {
            LocalCoordinateSystem = new CoordinateSystem(new(0, 0, 5)),
            Parameters = new Ns.DetectorRectangleParameters(WidthMillimeters: 2, HeightMillimeters: 2, PixelsX: 2, PixelsY: 2)
        };
        var scene = Scene(objects: [detector]);
        var result = new Ns.NonSequentialDocumentTracer().Trace(scene, new MaterialRegistry(),
            new(DirectRay: new RealRay(new(x, y, 0), new(0, 0, 1), 587.6)));
        var live = Assert.Single(result.Detectors);
        var replay = Assert.Single(Ns.NonSequentialDetectorReconstruction.Reconstruct(scene, result.Branches));
        Assert.Equal(1, result.EnergyBalance.DetectorPowerWatts, 12);
        Assert.Equal(1, live.PowerByWavelength.Values.SelectMany(values => values).Sum(), 12);
        Assert.Equal(live.PowerByWavelength[1], replay.PowerByWavelength[1]);
    }

    [Fact]
    public void LeavingInnerSolidRestoresTheOuterMedium()
    {
        var outer = Ns.NonSequentialObjectDefinition.Create(Ns.NonSequentialObjectKind.Sphere) with
        { Parameters = new Ns.SphereParameters(3, "outer") };
        var inner = Ns.NonSequentialObjectDefinition.Create(Ns.NonSequentialObjectKind.Sphere) with
        { Parameters = new Ns.SphereParameters(1, "inner") };
        var materials = new MaterialRegistry();
        materials.Register(new ConstantIndexMaterial("outer", 1.5));
        materials.Register(new ConstantIndexMaterial("inner", 1.7));
        var result = new Ns.NonSequentialDocumentTracer().Trace(Scene(objects: [outer, inner]), materials,
            new(DirectRay: new RealRay(Vector3D.Zero, new(0, 0, 1), 587.6), SplittingMode: Ns.NonSequentialSplittingMode.None));
        var segments = Assert.Single(result.Branches).Segments;
        Assert.Equal(inner.Id, segments[0].ObjectId);
        Assert.Equal(outer.Id, segments[1].ObjectId);
        Assert.Equal(segments[0].CumulativePathLength * 1.7, segments[0].CumulativeOpticalPathLength, 12);
        Assert.Equal((segments[1].CumulativePathLength - segments[0].CumulativePathLength) * 1.5,
            segments[1].CumulativeOpticalPathLength - segments[0].CumulativeOpticalPathLength, 12);
    }

    [Fact]
    public void NelderMeadUsesInsideContractionWhenReflectionIsWorseThanWorst()
    {
        var x = 0d;
        var evaluations = new List<double>();
        var problem = new OptimizationProblem();
        problem.AddVariable(new DelegateVariable("x", () => x, value => x = value, -10, 10, 1));
        problem.AddOperand(new Operand("target", 0, 1, () => { evaluations.Add(x); return x - .4; }));
        new NelderMeadOptimizer().Optimize(problem, 1);
        Assert.Equal(.5, x, 12);
        Assert.DoesNotContain(-.5, evaluations);
        Assert.Contains(.5, evaluations);
    }

    [Fact]
    public void NelderMeadStartsInwardAtUpperBoundAndExcludesFixedCoordinates()
    {
        var x = 1d;
        var fixedValue = 2d;
        var problem = new OptimizationProblem();
        problem.AddVariable(new DelegateVariable("x", () => x, value => x = value, 0, 1, .3));
        problem.AddVariable(new DelegateVariable("fixed", () => fixedValue, value => fixedValue = value, 2, 2, 1));
        problem.AddOperand(new Operand("target", 0, 1, () => x - .2));
        new NelderMeadOptimizer().Optimize(problem, 100);
        Assert.InRange(Math.Abs(x - .2), 0, 1e-4);
        Assert.Equal(2, fixedValue);
    }

    private static Ns.NonSequentialDocument Scene(string name = "test", string ambient = "Air",
        Ns.NonSequentialObjectDefinition[]? objects = null, Ns.NonSequentialMeshAsset[]? meshes = null) =>
        new(name, [new("d", 587.6, 1, true)], objects, ambient, meshAssets: meshes);
}
