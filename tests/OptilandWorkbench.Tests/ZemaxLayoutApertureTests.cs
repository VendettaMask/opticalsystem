using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Visualization;

namespace OptilandWorkbench.Tests;

public sealed class ZemaxLayoutApertureTests
{
    private const string Source = """
        MODE SEQ
        NAME "Clear aperture is independent from the part edge"
        UNIT MM
        ENPD 12
        FTYP 0 0 1 1 0 0 0
        XFLN 0
        YFLN 0
        WAVM 1 0.55 1
        PWAV 1
        SURF 0
          DISZ INFINITY
          DIAM 0 0 0 0 1 ""
          MEMA 0 0 0 0 1 ""
        SURF 1
          CURV 0.02
          DISZ 4
          GLAS N-BK7
          DIAM 4 1 0 0 1 ""
          MEMA 6.5 1 0 0 1 ""
        SURF 2
          CURV -0.02
          DISZ 20
          GLAS AIR
          DIAM 4 1 0 0 1 ""
          MEMA 6.5 1 0 0 1 ""
        SURF 3
          DISZ 0
          DIAM 3 0 0 0 1 ""
          MEMA 3 0 0 0 1 ""
        """;

    [Fact]
    public void UserDefinedDiamOnPoweredSurfaceBecomesZemaxFloatingAperture()
    {
        var optic = OpticalFormatCatalog.Import(Source, ".zmx");

        var front = optic.SurfaceGroup.Items[1];
        var aperture = Assert.IsType<CircularAperture>(front.PhysicalAperture);
        Assert.Equal(4, aperture.Radius, precision: 12);
        Assert.Equal(6.5, front.MechanicalSemiDiameter, precision: 12);

        var lens = Assert.Single(new Layout2DBuilder(optic).Build().LensElements);
        Assert.Equal(6.5, lens.Boundary.Max(point => Math.Abs(point.Y)), precision: 10);
    }

    [Fact]
    public void LayoutSamplesTheEntrancePupilAndClipsAtDiamInsteadOfMema()
    {
        var optic = OpticalFormatCatalog.Import(Source, ".zmx");
        var options = new LayoutBuildOptions(
            RayCount: 5,
            LowerPupil: -1,
            UpperPupil: 1,
            DeleteVignetted: false);

        var scene = new Layout2DBuilder(optic).Build(options: options);

        Assert.Equal(5, scene.Rays.Count);
        var blocked = scene.Rays.Where(ray => ray.Vignetted).ToArray();
        Assert.Equal(2, blocked.Length);
        Assert.All(blocked, ray =>
        {
            Assert.True(ray.Points.Count >= 2);
            Assert.Equal(1, ray.Segments[^1].TargetSurfaceNumber);
            Assert.Equal(6, Math.Abs(ray.Points[0].Y), precision: 10);
        });

        var transmitted = scene.Rays.Where(ray => !ray.Vignetted).ToArray();
        Assert.Equal(3, transmitted.Length);
        Assert.All(transmitted, ray => Assert.True(Math.Abs(ray.Points[1].Y) <= 4 + 1e-9));

        var withoutBlockedRays = new Layout2DBuilder(optic).Build(
            options: options with { DeleteVignetted = true });
        Assert.Equal(3, withoutBlockedRays.Rays.Count);
        Assert.All(withoutBlockedRays.Rays, ray => Assert.False(ray.Vignetted));
    }

    [Fact]
    public void AutomaticDiamDoesNotBecomeAClippingAperture()
    {
        var optic = OpticalFormatCatalog.Import(
            Source.Replace("DIAM 4 1 0 0 1", "DIAM 4 0 0 0 1", StringComparison.Ordinal),
            ".zmx");

        Assert.Null(optic.SurfaceGroup.Items[1].PhysicalAperture);
        Assert.Null(optic.SurfaceGroup.Items[2].PhysicalAperture);
    }

    [Fact]
    public void StopDiamClipsEvenWhenTheStopSurfaceIsPlane()
    {
        var optic = OpticalFormatCatalog.Import(
            Source.Replace("CURV 0.02", "CURV 0\n  STOP", StringComparison.Ordinal)
                .Replace("DIAM 4 1 0 0 1", "DIAM 4 0 0 0 1", StringComparison.Ordinal),
            ".zmx");

        var aperture = Assert.IsType<CircularAperture>(optic.SurfaceGroup.Items[1].PhysicalAperture);
        Assert.Equal(4, aperture.Radius, precision: 12);
    }
}
