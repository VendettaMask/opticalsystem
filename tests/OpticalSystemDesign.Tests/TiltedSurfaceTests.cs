using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Capabilities;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;

namespace OpticalSystemDesign.Tests;

public sealed class TiltedSurfaceTests
{
    [Fact]
    public void FocalFftRequiresAFiniteImageScaleAfterPlanarRefraction()
    {
        var optic = Import("""
            MODE SEQ
            UNIT MM
            ENPD 2
            SURF 0
             DISZ INFINITY
            SURF 1
             TYPE TILTSURF
             PARM 2 .2
             DISZ 5
            SURF 2
            """);
        foreach (var analysis in new BaseAnalysis[] { new PsfAnalysis(optic), new MtfAnalysis(optic) })
        {
            var data = analysis.GenerateData();
            Assert.Equal(AnalysisOutcome.NotApplicable, data.Outcome);
            Assert.Equal(FocalImageDomain.CollimatedPlaneTrain, data.Values[FocalImageDomain.ResultKey]);
        }

        optic.SurfaceGroup.Items[0].Thickness = 25;
        Assert.False(FocalImageDomain.IsCollimatedPlaneTrain(optic));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CompoundCoordinateBreakUsesTheSelectedLocalAxisOrder(int order)
    {
        var optic = Import(FormattableString.Invariant($"MODE SEQ\nUNIT MM\nENPD 2\nSURF 0\nDISZ INFINITY\nSURF 1\nTYPE COORDBRK\nPARM 3 30\nPARM 4 20\nPARM 6 {order}\nDISZ 1\nSURF 2"));
        var axis = optic.SurfaceGroup.Items[^1].CoordinateSystem.ToGlobalDirection(new(0, 0, 1));
        var x = Math.PI / 6;
        var y = Math.PI / 9;
        var expected = order == 0
            ? new Vector3D(Math.Sin(y), -Math.Sin(x) * Math.Cos(y), Math.Cos(x) * Math.Cos(y))
            : new Vector3D(Math.Cos(x) * Math.Sin(y), -Math.Sin(x), Math.Cos(x) * Math.Cos(y));
        Assert.InRange((axis - expected).Length, 0, 1e-13);
    }

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

    [Theory]
    [InlineData(.8)]
    [InlineData(.02)]
    public void TiltedObjectLaunchSurvivesCoordinateRoundoffWithoutAcceptingBackwardHits(double normalizedHeight)
    {
        var optic = Import("MODE SEQ\nUNIT MM\nENPD 2\nFTYP 1 0 1 1 0 0 1\nYFLN 10\nSURF 0\nTYPE TILTSURF\nPARM 2 -.577\nDISZ 100\nSURF 1\nSTOP\nDISZ 5\nSURF 2");
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 1, 0, normalizedHeight, .55, false);
        var trace = optic.SequentialRayTracer.Trace(bundle);
        var history = trace.RayHistories.Single();
        Assert.Equal(2, history.Last().SurfaceNumber);
        Assert.All(history, sample => Assert.False(sample.Vignetted));

        var surface = optic.SurfaceGroup.Items[0];
        var point = surface.CoordinateSystem.ToLocalPoint(bundle.Rays[0].Origin);
        Assert.False(surface.Geometry.DistanceToIntersection(point + new Vector3D(0, 0, 1e-8), new(0, 0, 1)).IsHit);
    }

    private static Optic Import(string source)=>OpticalFormatCatalog.Import(source,".zmx");
}
