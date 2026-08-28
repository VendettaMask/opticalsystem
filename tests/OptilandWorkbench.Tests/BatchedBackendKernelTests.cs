using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Geometries;

namespace OptilandWorkbench.Tests;

public sealed class BatchedBackendKernelTests
{
    [Fact]
    public void ManagedSimdStandardIntersectionMatchesScalarGeometryIncludingTail()
    {
        var backend = (IBatchedNumericBackend)new ManagedCpuBackend();
        var geometry = new StandardGeometry(radius: 50, conic: -0.25);
        var count = backend.PreferredBatchWidth + 3;
        var originX = Enumerable.Range(0, count)
            .Select(index => -4.0 + (8.0 * index / Math.Max(1, count - 1)))
            .ToArray();
        var originY = Enumerable.Range(0, count)
            .Select(index => -2.0 + (4.0 * index / Math.Max(1, count - 1)))
            .ToArray();
        var originZ = Enumerable.Repeat(-10.0, count).ToArray();
        var directionX = new double[count];
        var directionY = new double[count];
        var directionZ = Enumerable.Repeat(1.0, count).ToArray();
        var distance = new double[count];
        var intersects = new bool[count];

        backend.IntersectStandard(
            originX,
            originY,
            originZ,
            directionX,
            directionY,
            directionZ,
            geometry.Radius,
            geometry.Conic,
            distance,
            intersects);

        for (var index = 0; index < count; index++)
        {
            var expected = geometry.DistanceToIntersection(
                new Vector3D(originX[index], originY[index], originZ[index]),
                new Vector3D(directionX[index], directionY[index], directionZ[index]));
            Assert.Equal(expected.IsHit, intersects[index]);
            Assert.Equal(expected.Distance, distance[index], precision: 11);
        }
    }

    [Fact]
    public void ManagedSimdStandardIntersectionRejectsImplicitBackBranch()
    {
        var backend = (IBatchedNumericBackend)new ManagedCpuBackend();
        var geometry = new StandardGeometry(radius: 1, conic: 0);
        var originX = new[] { 0.0, 1.01 };
        var originY = new[] { 0.0, 0.0 };
        var originZ = new[] { 3.0, -5.0 };
        var directionX = new[] { 0.0, 0.0 };
        var directionY = new[] { 0.0, 0.0 };
        var directionZ = new[] { -1.0, 1.0 };
        var distance = new double[originX.Length];
        var intersects = new bool[originX.Length];

        backend.IntersectStandard(
            originX,
            originY,
            originZ,
            directionX,
            directionY,
            directionZ,
            geometry.Radius,
            geometry.Conic,
            distance,
            intersects);

        Assert.True(intersects[0]);
        Assert.Equal(3, distance[0], precision: 12);
        Assert.False(intersects[1]);
        Assert.True(double.IsNaN(distance[1]));
    }
}
