using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Rays;

namespace OpticalSystemDesign.Tests;

public sealed class PolygonPlanePrecisionTests
{
    [Theory]
    [InlineData(1e-10, -1e-10, false)]
    [InlineData(1e-10, -1e-10, true)]
    [InlineData(.2, .1, false)]
    [InlineData(.2, .1, true)]
    [InlineData(.9, .1, false)]
    [InlineData(.9, .1, true)]
    [InlineData(.1, .9, false)]
    [InlineData(.1, .9, true)]
    public void ParallelPlatePreservesDirectionAndPhysicalPathForEachDominantComponent(
        double l, double m, bool reverseWinding)
    {
        Vector3D[] front = [new(-10, -10, -3), new(10, -10, -3), new(10, 10, -3), new(-10, 10, -3)];
        var back = front.Select(p => p + new Vector3D(0, 0, 2)).ToArray();
        var faces = new List<PolygonGroupGeometry.Face> { new(front, false, false), new(back, false, false) };
        for (var i = 0; i < 4; i++)
            faces.Add(new([front[i], front[(i + 1) % 4], back[(i + 1) % 4], back[i]], false, false));
        if (reverseWinding) faces = faces.Select(f => f with { Vertices = f.Vertices.Reverse().ToArray() }).ToList();
        var frame = new CoordinateSystem(new(4, 5, 6), 20, 10, 30);
        var port = new OpticalSurface
        {
            Geometry = new PolygonGroupGeometry(faces, new ConstantIndexMaterial("plate", 1.5)),
            CoordinateSystem = frame
        };
        var n = Math.Sqrt(1 - l * l - m * m);
        var direction = new Vector3D(l, m, n);
        var ray = new RayState(frame.ToGlobalPoint(new(.2, .1, -4)), frame.ToGlobalDirection(direction), 550, 1, 0, null, true);
        var air = new ConstantIndexMaterial("ambient", 1);
        var result = port.TraceRayState(ray, air, air, 0, 0);
        Assert.False(result.StopTracing);
        Assert.False(result.Sample.Vignetted);
        Assert.InRange((result.Ray.Direction - frame.ToGlobalDirection(direction)).Length, 0, 5e-15);
        Assert.InRange(Math.Abs(result.Ray.Direction.Length - 1), 0, 4e-15);
        // Independent parallel-plate Snell solution: two millimeters in each
        // medium, and the two external rays are parallel.
        var glassN = Math.Sqrt(1 - (l * l + m * m) / (1.5 * 1.5));
        var expected = new Vector3D(.2 + 2 * l / n + 2 * (l / 1.5) / glassN,
            .1 + 2 * m / n + 2 * (m / 1.5) / glassN, 0);
        Assert.InRange((result.Ray.Origin - frame.ToGlobalPoint(expected)).Length, 0, 1e-11);
        Assert.InRange(Math.Abs(result.CumulativePathLength - (2 / n + 2 / glassN)), 0, 1e-11);
        Assert.InRange(Math.Abs(result.CumulativeOpticalPathLength - (2 / n + 3 / glassN)), 0, 1e-11);
    }
}
