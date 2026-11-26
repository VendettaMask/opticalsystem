using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Tests;

public sealed class RadiometricParaxialTraceTests
{
    [Theory]
    [InlineData(false, true, 0)]
    [InlineData(true, true, .4)]
    [InlineData(false, false, 0)]
    [InlineData(true, false, .4)]
    public void PowerTraceKeepsGeometryAperturesAndJonesPowerWithoutPhaseAiming(
        bool polarized, bool infinite, double field)
    {
        var optic = OpticalFormatCatalog.Import(Source(infinite), ".zmx");
        // Match the private image-simulation optic's ideal-lens power convention.
        optic.SequentialRayTracer.ImageSimulationCoatings = new Dictionary<int, SurfaceCoatingResponse>();
        var source = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, field, .3, .4, .55).Rays[0];
        long phaseRays;
        ApertureRayDiagnostic complete;
        using (var measurement = SequentialTraceMeasurement.Begin())
        {
            complete = optic.SequentialRayTracer.DiagnoseApertures(source,
                stopAtIncidentImage: true, usePolarization: polarized);
            phaseRays = measurement.RayCount;
        }
        ApertureRayDiagnostic power;
        using (var measurement = SequentialTraceMeasurement.Begin())
        {
            power = optic.SequentialRayTracer.DiagnoseAperturesForPower(source,
                stopAtIncidentImage: true, usePolarization: polarized);
            Assert.Equal(1, measurement.RayCount);
            if (infinite || field != 0) Assert.True(phaseRays > measurement.RayCount);
        }
        Assert.Equal(complete.InsideAllApertures, power.InsideAllApertures);
        Assert.Equal(complete.FirstBlockedSurfaceIndex, power.FirstBlockedSurfaceIndex);
        Assert.Equal(complete.UnpolarizedIntensity, power.UnpolarizedIntensity);
        Assert.Equal(complete.PolarizedIntensity, power.PolarizedIntensity);
        Assert.NotNull(complete.UnclippedImage); Assert.NotNull(power.UnclippedImage);
        var a = complete.UnclippedImage;
        var b = power.UnclippedImage;
        Assert.True(Math.Abs(a.PhaseInclusiveOpticalPathLength!.Value - b.PhaseInclusiveOpticalPathLength!.Value) > 1e-4);
        Assert.Equal(a with { OpticalPathDifference = b.OpticalPathDifference,
            PhaseInclusiveOpticalPathLength = b.PhaseInclusiveOpticalPathLength }, b);
        // A later phase-aware request still computes the complete original phase.
        Assert.Equal(complete, optic.SequentialRayTracer.DiagnoseApertures(source,
            stopAtIncidentImage: true, usePolarization: polarized));
    }

    [Fact]
    public void PowerTraceChecksEveryPhysicalApertureAndCancellation()
    {
        var optic = OpticalFormatCatalog.Import(Source(true), ".zmx");
        var source = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, .5, .4, .55).Rays[0];
        optic.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(.1);
        var result = optic.SequentialRayTracer.DiagnoseAperturesForPower(source, stopAtIncidentImage: true);
        Assert.False(result.InsideAllApertures);
        Assert.Equal(1, result.FirstBlockedSurfaceIndex);
        Assert.NotNull(result.UnclippedImage);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            optic.SequentialRayTracer.DiagnoseAperturesForPower(source, cancellation.Token));
    }

    private static string Source(bool infinite) =>
        $"MODE SEQ\nUNIT MM\nENPD 10\nFTYP 0 0 1 1 0 0 0\nXFLN 0\nYFLN 1\nWAVM 1 .55 1\nPWAV 1\nSURF 0\nTYPE STANDARD\nCURV 0\nDISZ {(infinite ? "INFINITY" : "150")}\nSURF 1\nSTOP\nTYPE PARAXIAL\nCURV 0\nPARM 1 100\nPARM 2 1\nDISZ 100\nDIAM 5 1 0 0 1 \"\"\nSURF 2\nTYPE STANDARD\nCURV 0";
}
