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
            Assert.Equal(expected.HasValue, intersects[index]);
            Assert.Equal(expected!.Value, distance[index], precision: 11);
        }
    }
}
