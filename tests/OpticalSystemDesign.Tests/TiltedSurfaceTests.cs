using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Capabilities;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;

namespace OpticalSystemDesign.Tests;

public sealed class TiltedSurfaceTests
{
    [Theory]
    [InlineData(.25, -.7, 2, -3)]
    [InlineData(-1, 0, -4, 2)]
    [InlineData(0, 0, 3, 1)]
    public void PlaneIntersectionAndNormalSatisfyTheDeclaredEquation(double tx, double ty, double x, double y)
    {
        var geometry = new TiltedPlaneGeometry(tx, ty);
        var target = new Vector3D(x, y, tx * x + ty * y);
        var direction = new Vector3D(.1, -.2, 1); direction /= direction.Length;
        var result = geometry.DistanceToIntersection(target - 10 * direction, direction);
        Assert.True(result.IsHit);
        Assert.InRange((result.Point - target).Length, 0, 1e-13);
        Assert.InRange(Math.Abs(result.Distance - 10), 0, 1e-13);
        var normal = geometry.SurfaceNormal(target);
        Assert.InRange(Math.Abs(normal.X + tx * normal.Z), 0, 1e-14);
        Assert.InRange(Math.Abs(normal.Y + ty * normal.Z), 0, 1e-14);
        Assert.InRange(Math.Abs(normal.Length - 1), 0, 1e-15);
        Assert.False(geometry.DistanceToIntersection(target + direction, direction).IsHit);
    }
    [Theory]
    [InlineData("MM", 1)] [InlineData("CM", 10)] [InlineData("IN", 25.4)]
    public void ImportSnapshotAndZmxRoundtripKeepDimensionlessTangentsAndUnchangedFollowingFrame(string unit, double scale)
    {
        var optic = Import($"""
            MODE SEQ
            UNIT {unit}
            ENPD 1
            SURF 0
             DISZ INFINITY
            SURF 1
             TYPE TILTSURF
             PARM 1 .1234567890123456
             PARM 2 -.7
             DISZ 2
            SURF 2
            """);
        Assert.Empty(OpticCapabilityPreflight.Inspect(optic));
        foreach (var candidate in new[] { optic, Optic.FromSnapshot(optic.ToSnapshot()), Import(OpticalFormatCatalog.Export(optic, ".zmx")) })
        {
            var tilted = Assert.IsType<TiltedPlaneGeometry>(candidate.SurfaceGroup.Items[1].Geometry);
            Assert.Equal(.1234567890123456, tilted.TangentX); Assert.Equal(-.7, tilted.TangentY);
            Assert.Equal(2 * scale, candidate.SurfaceGroup.Items[2].CoordinateSystem.Origin.Z, 11);
            var axis = candidate.SurfaceGroup.Items[2].CoordinateSystem.ToGlobalDirection(new(0, 0, 1));
            Assert.Equal(new Vector3D(0, 0, 1), axis);
        }
    }
    [Fact]
    public void FiniteObjectHeightLiesOnItsTiltedObjectSurface()
    {
        var optic=Import("MODE SEQ\nUNIT MM\nENPD 2\nFTYP 1 0 1 1 0 0 1\nYFLN 3\nSURF 0\nTYPE TILTSURF\nPARM 2 -.5\nDISZ 10\nSURF 1\nSTOP\nDISZ 5\nSURF 2");
        var ray=optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0,1,0,0,.55).Rays.Single();
        Assert.InRange(Math.Abs(ray.Origin.Z-(-10-1.5)),0,1e-12);
    }
    private static Optic Import(string source)=>OpticalFormatCatalog.Import(source,".zmx");
}
