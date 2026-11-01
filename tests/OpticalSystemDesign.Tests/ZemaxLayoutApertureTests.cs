using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Visualization;

namespace OpticalSystemDesign.Tests;

public sealed class ZemaxLayoutApertureTests
{
    [Theory]
    [InlineData("DIAM 0 1 0 0 1", false)]
    [InlineData("DIAM 4 1 0 0 1", false)]
    [InlineData("DIAM 4 1 0 0 1\n  APMN .5\n  APMX 2", false)]
    [InlineData("DIAM 4 1 0 0 1\n  CLAP .5 2 0", true)]
    public void CurvedDummyDoesNotAcquireAnImplicitFloatingAperture(string apertureRecords, bool explicitAperture)
    {
        var source = """
            MODE SEQ
            UNIT MM
            ENPD 6
            FTYP 0 0 1 1 0 0 0
            XFLN 0
            YFLN 0
            WAVM 1 .55 1
            PWAV 1
            SURF 0
              DISZ INFINITY
            SURF 1
              CURV .01
              DISZ 20
              GLAS AIR
            """ + "\n  " + apertureRecords + "\nSURF 2\n  STOP\n  DISZ 1\n  DIAM 3 1 0 0 1\nSURF 3\n  DISZ 0";
        var optic = OpticalFormatCatalog.Import(source, ".zmx");
        Assert.Equal(explicitAperture, optic.SurfaceGroup.Items[1].PhysicalAperture is not null);
        var stop = optic.SurfaceGroup.Items[2];
        Assert.True(stop.IsStop);
        Assert.Equal(3, stop.SemiDiameter);
        Assert.True(stop.SemiDiameterFixed);
        // A plane air-to-air dummy stop establishes the pupil, not an implicit
        // clipping aperture. Native replay and explicit FLAP controls are kept
        // in MultiLensEncircledEnergyTests.PlaneStopAcceptanceMatchesItsExplicitNativeAperture.
        Assert.Null(stop.PhysicalAperture);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("FLAP 0 0 0 0 0 0", true)]
    public void ExplicitFloatingApertureRemainsDistinctFromAFixedDummyStop(string record, bool clips)
    {
        var source = $"MODE SEQ\nENPD 6\nSURF 0\nDISZ INFINITY\nSURF 1\nSTOP\nDIAM 3 1 0 0 1\n{record}\nDISZ 10\nSURF 2\nDISZ 0";
        var optic = OpticalFormatCatalog.Import(source, ".zmx");
        var stop = optic.SurfaceGroup.Items.Single(surface => surface.IsStop);
        Assert.Equal(clips, stop.PhysicalAperture is not null);
        if (clips) Assert.Equal(3, Assert.IsType<CircularAperture>(stop.PhysicalAperture).Radius);
    }

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
    public void UserDefinedStopDiamClipsEvenWhenTheStopSurfaceIsPlane()
    {
        var optic = OpticalFormatCatalog.Import(
            Source.Replace("CURV 0.02", "CURV 0\n  STOP", StringComparison.Ordinal),
            ".zmx");

        var aperture = Assert.IsType<CircularAperture>(optic.SurfaceGroup.Items[1].PhysicalAperture);
        Assert.Equal(4, aperture.Radius, precision: 12);
    }

    [Fact]
    public void AutomaticStopDiamDoesNotClipTheEstimatedRayFootprint()
    {
        var optic = OpticalFormatCatalog.Import(
            Source.Replace("CURV 0.02", "CURV 0\n  STOP", StringComparison.Ordinal)
                .Replace("DIAM 4 1 0 0 1", "DIAM 4 0 0 0 1", StringComparison.Ordinal),
            ".zmx");

        var stop = optic.SurfaceGroup.Items[1];
        Assert.True(stop.IsStop);
        Assert.False(stop.SemiDiameterFixed);
        Assert.Equal(4, stop.SemiDiameter);
        Assert.Equal(6.5, stop.MechanicalSemiDiameter);
        Assert.Null(stop.PhysicalAperture);
        var rays = new Layout2DBuilder(optic).Build(options: new LayoutBuildOptions(
            RayCount: 5, LowerPupil: -1, UpperPupil: 1, DeleteVignetted: false)).Rays;
        Assert.Equal(5, rays.Count);
        Assert.All(rays, ray => Assert.False(ray.Vignetted));
    }

    [Fact]
    public void SpotDiagramAiryDiskUsesWorkingFNumberWithoutApplyingSurfaceApertures()
    {
        var optic = OpticalFormatCatalog.Import(Source, ".zmx");

        var data = new SpotDiagramAnalysis(
            optic,
            new SpotDiagramSettings(
                RayDensity: 3,
                FieldNumber: 1,
                ShowAiryDisk: true)).GenerateData();

        Assert.True((int)data.Values["VignettedRayCount"] > 0);
        Assert.True((double)data.Values["AiryRadius"] > 0);
        var pane = Assert.Single(data.PlotPanes!);
        Assert.Contains(pane.Series, series => series.Name == "艾里斑");
    }
}
