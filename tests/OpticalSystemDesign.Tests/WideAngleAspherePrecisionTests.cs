using System.Numerics;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Geometries;

namespace OpticalSystemDesign.Tests;

public sealed class WideAngleAspherePrecisionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void TwoForwardSagCapIntersectionsChooseTheFirstAcrossScalarAndBatchPaths(int sign)
    {
        // The line y+sign*z=-4 crosses the radius-10 cap at (-8,4)
        // and (-6,2). Both satisfy explicit sag; propagation reaches (-8,4)
        // first, although the other root is nearer the vertex plane.
        var geometry = new StandardGeometry(sign * 10);
        var origin = new Vector3D(0, -10, sign * 6);
        var direction = new Vector3D(0, 1 / Math.Sqrt(2), -sign / Math.Sqrt(2));
        var expected = new Vector3D(0, -8, sign * 4);
        var hit = geometry.DistanceToIntersection(origin, direction);
        Assert.True(hit.IsHit);
        Assert.InRange((hit.Point - expected).Length, 0, 2e-14);
        Assert.InRange(Math.Abs(hit.Distance - 2 * Math.Sqrt(2)), 0, 2e-14);
        Assert.NotNull(hit.IncidenceCosineSquared);
        Assert.InRange(Math.Abs(hit.IncidenceCosineSquared.Value - .02), 0, 1e-15);

        var count = 2 * Vector<double>.Count + 1;
        var zero = new double[count];
        var y = Enumerable.Repeat(origin.Y, count).ToArray();
        var z = Enumerable.Repeat(origin.Z, count).ToArray();
        var dy = Enumerable.Repeat(direction.Y, count).ToArray();
        var dz = Enumerable.Repeat(direction.Z, count).ToArray();
        IBatchedNumericBackend[] backends = [new ManagedCpuBackend(), new ScalarBatchedNumericBackendAdapter(new ManagedCpuBackend())];
        foreach (var backend in backends)
        {
            var distances = new double[count]; var hits = new bool[count];
            backend.IntersectStandard(zero, y, z, zero, dy, dz, geometry.Radius, 0, distances, hits);
            Assert.All(hits, Assert.True);
            Assert.All(distances, distance => Assert.InRange(Math.Abs(distance - hit.Distance), 0, 2e-14));
        }
    }

    [Theory]
    [InlineData(40, 3, 4)]
    [InlineData(-40, 3, -4)]
    [InlineData(40, 0, 0)]
    public void EvenAsphereParaboloidNormalIsOrthogonalToExactPolynomialTangents(double radius, double x, double y)
    {
        // z=(x²+y²)/2R + .001(x²+y²)². Its exact tangent vectors
        // provide an independent geometric oracle, without differencing sag.
        var geometry = new EvenAsphereGeometry(radius, -1, [0, .001]);
        var point = new Vector3D(x, y, geometry.Sag(x, y));
        var normal = geometry.SurfaceNormal(point);
        var slope = 1 / radius + .004 * (x * x + y * y);
        Assert.InRange(Math.Abs(normal.X + normal.Z * x * slope), 0, 2e-16);
        Assert.InRange(Math.Abs(normal.Y + normal.Z * y * slope), 0, 2e-16);
        Assert.InRange(Math.Abs(normal.Length - 1), 0, 2e-16);
        Assert.True(normal.Z > 0);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(-10)]
    public void EvenAsphereConicRimHasFiniteLimitingNormalAndRejectsOutsideDomain(double radius)
    {
        var geometry = new EvenAsphereGeometry(radius, 0, [0, .001]);
        var rim = geometry.SurfaceNormal(new Vector3D(6, 8, radius));
        Assert.InRange(Math.Abs(rim.X + Math.Sign(radius) * .6), 0, 2e-16);
        Assert.InRange(Math.Abs(rim.Y + Math.Sign(radius) * .8), 0, 2e-16);
        Assert.Equal(0, rim.Z);
        var outside = geometry.SurfaceNormal(new Vector3D(6.1, 8, radius));
        Assert.True(double.IsNaN(outside.X));
    }
}
