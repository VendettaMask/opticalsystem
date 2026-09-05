using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class PsfWorkingFNumberRegressionTests
{
    [Fact]
    public void ImportedHighNaLensCompletesApplicationDefaultPolychromaticFftPsf()
    {
        var optic = Import("zemax-ms-l7-high-na.ZMX");
        var before = optic.SurfaceGroup.Items.Select(surface => (surface.SemiDiameter, surface.Thickness, surface.PhysicalAperture)).ToArray();
        var view = new WorkbenchRuntime(optic).BuildAnalysisView("PSF", new Dictionary<string, string>());
        var series = Assert.IsType<AnalysisSeries>(view.Series);
        Assert.Equal(AnalysisSeriesKind.Heatmap, series.Kind);
        Assert.Equal(128 * 128, series.Points.Count);
        Assert.All(series.Points, point => Assert.True(double.IsFinite(point.Value!.Value)));
        Assert.Contains(series.Points, point => point.Value > 0);
        Assert.Equal(before, optic.SurfaceGroup.Items.Select(surface => (surface.SemiDiameter, surface.Thickness, surface.PhysicalAperture)).ToArray());
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(-10.6 / 15, false)]
    [InlineData(-10.6 / 15, true)]
    [InlineData(-1, false)]
    [InlineData(-1, true)]
    public void MissingParaxialEdgeLaunchRetriesWholePupilWithStopAiming(double hy, bool polarized)
    {
        var optic = Import("zemax-ms-l7-high-na.ZMX");
        foreach (var wave in optic.Wavelengths)
        {
            Assert.Throws<InvalidOperationException>(() => DiffractionEngine.WorkingFNumber(optic, (0, hy), wave));
            var expectedFNumber = DiffractionEngine.WorkingFNumber(optic, (0, hy), wave, aimAtStop: true);
            var actual = DiffractionEngine.ComputeFftPsf(optic, (0, hy), wave, 16, 32, usePolarization: polarized);
            var expected = DiffractionEngine.ComputeFftPsf(optic, (0, hy), wave, 16, 32,
                usePolarization: polarized, aimAtStop: true);
            Assert.InRange(actual.WorkingFNumber, 0.5, 2);
            Assert.Equal(expectedFNumber, actual.WorkingFNumber, 12);
            Assert.Equal(wave.Micrometers * expectedFNumber * 15 / 32, actual.SampleSpacingMicrometers, 12);
            Assert.Equal(expected.Values.Cast<double>(), actual.Values.Cast<double>());
            Assert.All(actual.Values.Cast<double>(), value => Assert.True(double.IsFinite(value)));
            Assert.True(actual.PeakStrehlRatio > 0);
        }
    }

    [Fact]
    public void PreparedStopAimedPupilIsPreservedIncludingItsPhase()
    {
        var optic = Import("zemax-ms-l7-high-na.ZMX");
        var wave = optic.Wavelengths.First(item => item.IsPrimary);
        var pupil = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, 16,
            cellCentered: true, aimAtStop: true);
        // A prepared wavefront can contain caller-supplied defocus: do not silently regenerate it.
        pupil = pupil with
        {
            Samples = pupil.Samples.Select(sample => sample with
            {
                OpdWaves = sample.OpdWaves + sample.NormalizedPupilX * 2
            }).ToArray()
        };
        var actual = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 16, 32,
            cellCenteredPupil: true, preparedWavefront: pupil);
        var expected = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 16, 32,
            cellCenteredPupil: true, preparedWavefront: pupil, aimAtStop: true);
        Assert.Equal(expected.Values.Cast<double>(), actual.Values.Cast<double>());
        var regenerated = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 16, 32,
            cellCenteredPupil: true, aimAtStop: true);
        Assert.NotEqual(regenerated.Values.Cast<double>().ToArray(), actual.Values.Cast<double>().ToArray());
    }

    [Fact]
    public void UnaimedPreparedPupilIsNotSilentlyCombinedWithAimedScale()
    {
        var optic = Import("zemax-ms-l7-high-na.ZMX");
        var wave = optic.Wavelengths.First(item => item.IsPrimary);
        var pupil = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, 16);
        var error = Assert.Throws<InvalidOperationException>(() => DiffractionEngine.ComputeFftPsf(
            optic, (0, 0), wave, 16, 32, preparedWavefront: pupil));
        Assert.Contains("Regenerate the prepared wavefront", error.Message);
    }

    [Fact]
    public void PhysicallyBlockedMarginalRaysAreNotIgnored()
    {
        var optic = Import("zemax-ms-l7-high-na.ZMX");
        optic.SurfaceGroup.Items[10].PhysicalAperture = new CircularAperture(0.01);
        var wave = optic.Wavelengths.First(item => item.IsPrimary);
        Assert.Throws<InvalidOperationException>(() => DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 16, 32));
    }

    [Fact]
    public void CancellationIsNotSwallowedByStopAimingRetry()
    {
        var optic = Import("zemax-ms-l7-high-na.ZMX");
        using var scope = ComputationCancellation.Push(new CancellationToken(canceled: true));
        Assert.Throws<OperationCanceledException>(() => DiffractionEngine.ComputeFftPsf(
            optic, (0, 0), optic.Wavelengths[0], 16, 32));
    }

    [Fact]
    public void Primary123456FixtureStillProducesFiniteFftPsfWithoutChangingItsScale()
    {
        var optic = Import("zemax-123456.ZMX");
        var wave = optic.Wavelengths.First(item => item.IsPrimary);
        var expectedFNumber = DiffractionEngine.WorkingFNumber(optic, (0, 0), wave);
        var result = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 16, 32);
        Assert.Equal(expectedFNumber, result.WorkingFNumber, 12);
        Assert.All(result.Values.Cast<double>(), value => Assert.True(double.IsFinite(value)));
        Assert.True(result.PeakStrehlRatio > 0);
    }

    private static Optic Import(string name) => OpticalFormatCatalog.Import(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)), ".zmx");
}
