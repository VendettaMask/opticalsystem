using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;

namespace OpticalSystemDesign.Tests;

public sealed class StreamedFourierPupilTests
{
    [Fact]
    public void FoldedEnergyOnlyUsesSagittalSymmetryForItsVerifiedMeridionalTrain()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "optical-testing", "Twyman Green Interferometer.zmx");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx");
        Assert.True(DiffractionEngine.HasSagittalEnergySymmetry(optic, (0, 0)));
        Assert.False(DiffractionEngine.HasSagittalEnergySymmetry(optic, (.1, 0)));
        var surface = optic.SurfaceGroup.Items.First(item => item.Geometry is TiltedPlaneGeometry);
        var plane = Assert.IsType<TiltedPlaneGeometry>(surface.Geometry);
        surface.Geometry = new TiltedPlaneGeometry(.01, plane.TangentY);
        Assert.False(DiffractionEngine.HasSagittalEnergySymmetry(optic, (0, 0)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmallStreamedWindowMatchesTheSameDensePupilAtEveryImageNode(bool physicalWindow)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "optical-testing", "Twyman Green Interferometer.zmx");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx");
        var wave = optic.Wavelengths[0];
        const int sampling = 256;
        const int display = 16;
        const double pitch = .25;
        var dense = physicalWindow
            ? DiffractionEngine.ComputePhysicalPsfWindowSpectrum(optic, (0, 0), [wave], sampling, display, pitch).Single().Result
            : DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, sampling, 2 * sampling,
                cellCenteredPupil: true, zemaxFftSampling: true, imageDelta: pitch,
                ignoreScalarTransmission: true, interpolateScalarPupilAmplitude: true, croppedOutputSize: display);
        var (streamed, diagnostics) = DiffractionEngine.ComputeStreamedFocalFftPsf(optic, (0, 0), wave, sampling,
            display, pitch, sampling * sampling, physicalWindow: physicalWindow);
        Assert.Equal(dense.CroppedImageCenterIndex, streamed.CroppedImageCenterIndex);
        Assert.Equal(dense.SampleSpacingMicrometers, streamed.SampleSpacingMicrometers, 12);
        Assert.Equal(dense.PupilPhaseSampling!.Status, streamed.PupilPhaseSampling!.Status);
        Assert.Equal(dense.PupilPhaseSampling.ConnectedEdges, streamed.PupilPhaseSampling.ConnectedEdges);
        Assert.Equal(dense.PupilPhaseSampling.MaximumStepWaves, streamed.PupilPhaseSampling.MaximumStepWaves, 10);
        Assert.InRange(diagnostics.TracedNodes, 1, (long)sampling * sampling);
        Assert.True(diagnostics.FourierWorkingCells < 2 * sampling * sampling);
        foreach (var (expected, actual) in dense.Values.Cast<double>().Zip(streamed.Values.Cast<double>()))
            Assert.InRange(Math.Abs(expected / dense.UnaberratedPeakRatio - actual / streamed.UnaberratedPeakRatio), 0, 1e-10);
        Assert.Throws<AnalysisDataUnavailableException>(() => DiffractionEngine.ComputeStreamedFocalFftPsf(
            optic, (0, 0), wave, sampling, display, pitch, maximumTraceNodes: 1));
    }

    [Fact]
    public void StreamedMtfMatchesDenseComplexCorrelationAcrossBlockBoundaries()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "optical-testing", "Twyman Green Interferometer.zmx");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx");
        var wave = optic.Wavelengths[0];
        const int sampling = 512;
        const double maximumFrequency = 50;
        var dense = DiffractionEngine.ComputeFftMtfFromPupil(optic, (0, 0), wave, sampling, 2 * sampling,
            cellCenteredPupil: true, zemaxFftSampling: true);
        var (streamed, phase, diagnostics) = DiffractionEngine.ComputeStreamedFocalFftMtf(optic, (0, 0), wave,
            sampling, maximumFrequency, (long)sampling * sampling);

        Assert.Equal(FftPhaseSamplingStatus.UndersampledPhase, phase.Status);
        Assert.True(phase.ConnectedEdges > diagnostics.TracedNodes);
        Assert.InRange(diagnostics.TracedNodes, 1, (long)sampling * sampling);
        Assert.Equal(dense.CutoffFrequency, streamed.CutoffFrequency, 12);
        Assert.True(streamed.TangentialFrequency![^1] > maximumFrequency);
        Assert.True(streamed.SagittalFrequency![^1] > maximumFrequency);
        Assert.Equal(dense.TangentialFrequency!.Take(streamed.TangentialFrequency.Count), streamed.TangentialFrequency);
        Assert.Equal(dense.SagittalFrequency!.Take(streamed.SagittalFrequency.Count), streamed.SagittalFrequency);
        foreach (var (expected, actual) in dense.TangentialOtf!.Zip(streamed.TangentialOtf!))
            Assert.InRange((expected - actual).Magnitude, 0, 1e-12);
        foreach (var (expected, actual) in dense.SagittalOtf!.Zip(streamed.SagittalOtf!))
            Assert.InRange((expected - actual).Magnitude, 0, 1e-12);

        Assert.Throws<AnalysisDataUnavailableException>(() => DiffractionEngine.ComputeStreamedFocalFftMtf(
            optic, (0, 0), wave, sampling, maximumFrequency, maximumTraceNodes: 1));
        Assert.Throws<AnalysisDataUnavailableException>(() => DiffractionEngine.ComputeStreamedFocalFftMtf(
            optic, (0, 0), wave, sampling, 2 * dense.CutoffFrequency, (long)sampling * sampling));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StreamedAxisMatchesTheCentralImageCutAtEveryPhysicalCoordinate(bool horizontal)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "optical-testing", "Twyman Green Interferometer.zmx");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx");
        var wave = optic.Wavelengths[0];
        const int sampling = 256;
        const int display = 16;
        const int center = (display - 1) / 2;
        const double pitch = .25;
        var axis = Enumerable.Range(-center, display).Select(index => index * pitch).ToArray();
        var dense = DiffractionEngine.ComputePhysicalPsfWindowSpectrum(optic, (0, 0), [wave], sampling, display, pitch).Single().Result;
        var streamed = DiffractionEngine.ComputeStreamedFocalFftAxis(optic, (0, 0), wave, sampling, axis,
            horizontal, sampling * sampling);
        Assert.Equal(dense.PupilPhaseSampling!.ConnectedEdges, streamed.Phase.ConnectedEdges);
        Assert.Equal(dense.PupilPhaseSampling.MaximumStepWaves, streamed.Phase.MaximumStepWaves, 10);
        Assert.InRange(streamed.Diagnostics.TracedNodes, 1, (long)sampling * sampling);
        for (var i = 0; i < display; i++)
        {
            var expected = horizontal ? dense.Values[center, i] : dense.Values[i, center];
            Assert.InRange(Math.Abs(expected / (100 * dense.UnaberratedPeakRatio) - streamed.Values[i]), 0, 1e-12);
        }
        Assert.Throws<AnalysisDataUnavailableException>(() => DiffractionEngine.ComputeStreamedFocalFftAxis(
            optic, (0, 0), wave, sampling, [-10_000d, 0, 10_000d], horizontal, sampling * sampling));
    }
}
