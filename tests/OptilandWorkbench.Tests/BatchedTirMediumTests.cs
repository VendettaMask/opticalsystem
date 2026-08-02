using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Tests;

public sealed class BatchedTirMediumTests
{
    [Fact]
    public void BatchedTotalInternalReflectionKeepsIncidentIndexForReturnPath()
    {
        var optic = Optic.CreateBlank();
        optic.Materials.Register(new ConstantIndexMaterial("Air", 1.5));
        optic.SurfaceGroup.Replace(
            new[]
            {
                Surface(
                    "TIR",
                    new AirMaterial(),
                    new SimpleCoatingModel(transmittance: 0.2, reflectance: 0.9)),
                Surface("Return", new AirMaterial(), new NoneCoatingModel())
            });
        optic.SurfaceGroup.Items[0].CoordinateSystem =
            new CoordinateSystem(new Vector3D(0, 0, 0));
        optic.SurfaceGroup.Items[1].CoordinateSystem =
            new CoordinateSystem(new Vector3D(0, 0, -2));

        var angle = Math.PI / 3;
        var direction = new Vector3D(Math.Sin(angle), 0, Math.Cos(angle));
        var rays = Enumerable.Range(0, 257)
            .Select(index => new RealRay(
                new Vector3D(0, index * 1e-6, -1),
                direction,
                587.6))
            .ToArray();
        var bundle = new RealRayBundle(rays);

        using var scalar = optic.SequentialRayTracer.Trace(
            bundle,
            TraceRequest.FullHistory(false) with
            {
                UseBatchedBackend = false,
                MaxDegreeOfParallelism = 1
            });
        using var batched = optic.SequentialRayTracer.Trace(
            bundle,
            TraceRequest.FullHistory(false) with
            {
                UseBatchedBackend = true,
                ParallelThreshold = 1,
                MaxDegreeOfParallelism = 4
            });

        for (var index = 0; index < rays.Length; index++)
        {
            Assert.True(scalar.TryGetSample(index, 0, out var expectedTir));
            Assert.True(batched.TryGetSample(index, 0, out var actualTir));
            Assert.Equal(RayInteractionKind.TotalInternalReflection, expectedTir.InteractionKind);
            Assert.Equal(RayInteractionKind.TotalInternalReflection, actualTir.InteractionKind);
            Assert.True(scalar.TryGetSample(index, 1, out var expected));
            Assert.True(batched.TryGetSample(index, 1, out var actual));
            Assert.Equal(9.0, actual.CumulativeOpticalPathLength, precision: 11);
            Assert.Equal(0.9, actual.Intensity, precision: 12);
            Assert.Equal(expected.Position.X, actual.Position.X, precision: 11);
            Assert.Equal(expected.Direction.Z, actual.Direction.Z, precision: 12);
            Assert.Equal(
                expected.CumulativeOpticalPathLength,
                actual.CumulativeOpticalPathLength,
                precision: 11);
        }
    }

    private static OpticalSurface Surface(
        string label,
        IMaterial materialAfter,
        ICoatingModel coating) =>
        new()
        {
            Label = label,
            Geometry = new PlaneGeometry(),
            MaterialAfter = materialAfter,
            InteractionModel = new RefractiveReflectiveInteractionModel(),
            CoatingModel = coating
        };
}
