using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class FocusedHuygensSamplingTests
{
    private const string SlowSinglet = """
        MODE SEQ
        UNIT MM
        ENPD 2
        GCAT SCHOTT
        WAVM 1 .55 1
        PWAV 1
        SURF 0
          DISZ INFINITY
        SURF 1
          STOP
          CURV .02
          DISZ 1
          GLAS BK7
          DIAM 2
        SURF 2
          DISZ 95.76931699190447
          DIAM 2
        SURF 3
        """;

    [Fact]
    public void SlowAxialSingletConvergesToItsParaxialAiryControl()
    {
        const double pitch = .003;
        const int size = 16;
        var optic = OpticalFormatCatalog.Import(SlowSinglet, ".zmx");
        var wave = optic.Wavelengths[0];
        var psf = DiffractionEngine.ComputeCheckedFocusedHuygensPsf(optic, (0, 0), wave, 16, size, pitch);
        Assert.NotNull(psf.FocusedHuygensSampling);
        Assert.InRange(psf.StrehlRatio, .999, 1.000000000001);
        const double focalLength = 96.42785189935934;
        for (var row = 0; row < size; row++)
        {
            var x = (row - size / 2) * pitch;
            var t = Math.PI * x / (wave.Micrometers * .001 * focalLength);
            // Series for 2 J1(2t) / (2t), including the on-axis limit.
            var term = 1d;
            var amplitude = 1d;
            for (var order = 1; order <= 20; order++)
            {
                term *= -t * t / (order * (order + 1));
                amplitude += term;
            }
            Assert.InRange(Math.Abs(psf.Values[size / 2, row] / 100 - amplitude * amplitude), 0, .001);
            Assert.Equal(psf.Values[size / 2, row], psf.Values[row, size / 2]);
        }
    }

    [Fact]
    public void OffAxisAndDecenteredPrescriptionsRejectTheRadialReadout()
    {
        var optic = OpticalFormatCatalog.Import(SlowSinglet, ".zmx");
        var wave = optic.Wavelengths[0];
        Assert.Throws<AnalysisDataUnavailableException>(() =>
            DiffractionEngine.ComputeCheckedFocusedHuygensPsf(optic, (0, 1), wave, 16, 16, .003));
        var surface = optic.SurfaceGroup.Items[1];
        var coordinates = surface.CoordinateSystem;
        surface.CoordinateSystem = coordinates with { Origin = coordinates.Origin with { X = .01 } };
        var profile = new HuygensPsfCrossSectionAnalysis(optic, 16, 16, .003, wavelengthNumber: 1)
            { UseCheckedFocusedPupil = true }.GenerateData();
        Assert.Equal(AnalysisOutcome.Unavailable, profile.Outcome);
        Assert.Empty(profile.PlotSeries);
    }
}
