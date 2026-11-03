using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class ZemaxClearApertureTests
{
    [Theory]
    [InlineData("MM", 1, 0)]
    [InlineData("MM", 1, .5)]
    [InlineData("IN", 25.4, 0)]
    [InlineData("IN", 25.4, .5)]
    public void CircularDecenterAndAnnularHoleMatchIndependentMaskAndSurviveFormats(string unit, double scale, double inner)
    {
        var text = Source(unit, $"CLAP {inner.ToString(System.Globalization.CultureInfo.InvariantCulture)} 2 0\nOBDC 1 -2");
        var optic = OpticalFormatCatalog.Import(text, ".zmx");
        foreach (var model in RoundTrips(optic))
        {
            var surface = model.SurfaceGroup.Items[1];
            var aperture = Assert.IsType<OffsetRadialAperture>(surface.PhysicalAperture);
            Assert.Equal(2 * scale, aperture.OuterRadius, 10);
            Assert.Equal(inner * scale, aperture.InnerRadius, 10);
            Assert.Equal(scale, aperture.OffsetX, 10); Assert.Equal(-2 * scale, aperture.OffsetY, 10);
            Assert.False(surface.SemiDiameterDefinesPhysicalAperture);
            for (var i = -9; i <= 9; i++) for (var j = -9; j <= 9; j++)
            {
                // Probe open interior/exterior regions, independently of a
                // unit conversion's rounding at an exact circle boundary.
                var dx = (i + .123) / 4d; var dy = (j + .123) / 4d; var radius = dx * dx + dy * dy;
                Assert.Equal(radius <= 4 && radius >= inner * inner,
                    aperture.Contains(new Vector3D((1 + dx) * scale, (-2 + dy) * scale, 0)));
            }
            surface.SemiDiameter = 9 * scale;
            Assert.Equal(2 * scale, Assert.IsType<OffsetRadialAperture>(surface.PhysicalAperture).OuterRadius, 10);
        }
    }

    [Theory]
    [InlineData("MM", 1)]
    [InlineData("IN", 25.4)]
    public void RectangleRetainsCornersAndDecenterThroughSnapshotsAndZmx(string unit, double scale)
    {
        var optic = OpticalFormatCatalog.Import(Source(unit, "SQAP 2 3 0\nOBDC 1 -2"), ".zmx");
        foreach (var model in RoundTrips(optic))
        {
            var aperture = Assert.IsType<RectangularAperture>(model.SurfaceGroup.Items[1].PhysicalAperture);
            Assert.Equal(2 * scale, aperture.HalfWidth, 10); Assert.Equal(3 * scale, aperture.HalfHeight, 10);
            Assert.Equal(scale, aperture.CenterX, 10); Assert.Equal(-2 * scale, aperture.CenterY, 10);
            foreach (var (dx, dy, inside) in new[] { (1.9, 2.9, true), (-1.9, -2.9, true), (2.01, 0d, false), (0d, 3.01, false) })
                Assert.Equal(inside, aperture.Contains(new Vector3D((1 + dx) * scale, (-2 + dy) * scale, 0)));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfigurationApertureDimensionsAndDecenterRemainIndependent(bool rectangular)
    {
        var source = Source("IN", rectangular ? "SQAP 2 3 0" : "CLAP .5 3 0") + """

            MNUM 2 1
            APMN 1 1 1
            APMN 1 2 2
            APMX 1 1 4
            APMX 1 2 5
            APDX 1 1 3
            APDX 1 2 -3
            APDY 1 1 -2
            APDY 1 2 2
            """;
        var configs = new ZemaxZmxImporter().ImportConfigurationSet(source).Configurations;
        for (var c = 0; c < 2; c++)
        {
            var aperture = configs[c].SurfaceGroup.Items[1].PhysicalAperture;
            if (rectangular)
            {
                var rect = Assert.IsType<RectangularAperture>(aperture);
                Assert.Equal((c + 1) * 25.4, rect.HalfWidth, 10); Assert.Equal((c + 4) * 25.4, rect.HalfHeight, 10);
                Assert.Equal((c == 0 ? 3 : -3) * 25.4, rect.CenterX, 10); Assert.Equal((c == 0 ? -2 : 2) * 25.4, rect.CenterY, 10);
            }
            else
            {
                var radial = Assert.IsType<OffsetRadialAperture>(aperture);
                Assert.Equal((c + 1) * 25.4, radial.InnerRadius, 10); Assert.Equal((c + 4) * 25.4, radial.OuterRadius, 10);
                Assert.Equal((c == 0 ? 3 : -3) * 25.4, radial.OffsetX, 10); Assert.Equal((c == 0 ? -2 : 2) * 25.4, radial.OffsetY, 10);
            }
        }
    }

    [Fact]
    public void FloatingRadialApertureFollowsOwnedRadiusWithoutLosingDecenter()
    {
        var text = "MODE SEQ\nUNIT MM\nENPD 4\nSURF 0\nDISZ INFINITY\nSURF 1\nSTOP\nDIAM 2 1 0 0 1\nDISZ 5\n"
            + "SURF 2\nCURV .02\nGLAS N-BK7\nDIAM 7 1 0 0 1\nFLAP 0 7 0\nOBDC 1 -2\nDISZ 10\nSURF 3\nDISZ 0";
        foreach (var optic in RoundTrips(OpticalFormatCatalog.Import(text, ".zmx")))
        {
            var surface = optic.SurfaceGroup.Items[2]; Assert.True(surface.SemiDiameterDefinesPhysicalAperture);
            surface.SemiDiameter = 4;
            var aperture = Assert.IsType<OffsetRadialAperture>(surface.PhysicalAperture);
            Assert.Equal(4, aperture.OuterRadius); Assert.Equal(1, aperture.OffsetX); Assert.Equal(-2, aperture.OffsetY);
        }
    }

    [Fact]
    public void DormantApertureBoundsAndDecenterDoNotActivateClipping()
    {
        var optic = OpticalFormatCatalog.Import(Source("MM", "APMN .5\nAPMX 2\nOBDC 1 -2"), ".zmx");
        Assert.Null(optic.SurfaceGroup.Items[1].PhysicalAperture);
    }

    [Fact]
    public async Task GregorianRealChiefPassesTheDeclaredDecenteredMirror()
    {
        var optic = (await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "biocular", "Unobscured Gregorian.zmx"))).Configurations[0];
        var aperture = Assert.IsType<OffsetRadialAperture>(optic.SurfaceGroup.Items[3].PhysicalAperture);
        Assert.Equal(55, aperture.OuterRadius); Assert.Equal(-100, aperture.OffsetY);
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, 0, wave.Micrometers, optic.RayAimingEnabled);
        Assert.NotNull(optic.SequentialRayTracer.TraceFinalSamples(bundle).Single());
        Assert.True(optic.SequentialRayTracer.DiagnoseApertures(bundle.Rays.Single()).InsideAllApertures);
    }

    [Fact]
    public void BlockedSingleRayPublishesMarkedDiagnosticRowsWithoutPhysicalThroughput()
    {
        var optic = OpticalFormatCatalog.Import(Source("MM", "SQAP .5 .5 0"), ".zmx");
        var wave = optic.Wavelengths[0];
        var launch = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, .8, 0, wave.Micrometers, false);
        Assert.Null(optic.SequentialRayTracer.TraceFinalSamples(launch).Single());
        var data = new SingleRayTraceAnalysis(optic, px: .8, useRayAiming: false).GenerateData();
        Assert.Equal(true, data.Values["ApertureContinuationUsed"]);
        Assert.Equal(1, data.Values["PhysicalLastSurface"]); Assert.Equal(1, data.Values["VignettedSurface"]);
        Assert.Equal(2, data.Values["LastSurface"]);
        var rows = Assert.IsType<double[][]>(data.Values["RealRayData"]);
        Assert.Equal(2, rows.Length); Assert.Equal(1.6, rows[^1][1], 10);
        Assert.Contains("不计入实际通光", data.ReportText);
        Assert.Null(optic.SequentialRayTracer.TraceFinalSamples(launch).Single());
    }

    [Fact]
    public async Task BoundedOffAxisSagDoesNotReplaceTransmittedChiefWithBlockedAimedChief()
    {
        var optic = (await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "clear-aperture", "oap.zmx"))).Configurations[0];
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var result = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 64, 128,
            zemaxFftSampling: true, cellCenteredPupil: true, imageDelta: -1, aimAtStop: false);
        Assert.False(result.UseRayAiming); Assert.False(result.StopAimingFallbackUsed);
        Assert.Contains(result.Values.Cast<double>(), value => double.IsFinite(value) && value > 0);
        Assert.False(optic.RayAimingEnabled);
    }

    private static IEnumerable<Optic> RoundTrips(Optic optic)
    {
        yield return optic;
        yield return Optic.FromSnapshot(optic.ToSnapshot());
        yield return OpticalFormatCatalog.Import(OpticalFormatCatalog.Export(optic, ".zmx"), ".zmx");
    }

    private static string Source(string unit, string records) => $"MODE SEQ\nUNIT {unit}\nENPD 4\nFTYP 0 0 1 1 0 0 0\nXFLN 0\nYFLN 0\nWAVM 1 .55 1\nPWAV 1\n"
        + $"SURF 0\nDISZ INFINITY\nSURF 1\nSTOP\nDIAM 7 1 0 0 1\n{records}\nDISZ 5\nSURF 2\nDISZ 0\n";
}
