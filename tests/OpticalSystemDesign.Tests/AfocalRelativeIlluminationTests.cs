using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class AfocalRelativeIlluminationTests
{
    private static Optic Cone()
    {
        var optic = new Optic();
        optic.Aperture.Value = 2;
        optic.Fields.Add(new FieldPoint { Y = 2 });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550 });
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Label = "Object", Thickness = 10 },
            new OpticalSurface { IsStop = true, Thickness = 10, SemiDiameter = 1 },
            new OpticalSurface { Label = "Image" }
        ]);
        return optic;
    }

    [Theory]
    [InlineData(IlluminationSamplingKind.AdaptivePupil)]
    [InlineData(IlluminationSamplingKind.UniformImageCosine)]
    [InlineData(IlluminationSamplingKind.FiniteImageCosine)]
    public void AfocalApplicabilityIsIndependentOfQuadratureAndPolarization(IlluminationSamplingKind sampling)
    {
        var optic = Cone(); optic.ImageSpaceAfocal = true;
        foreach (var polarization in new[] { false, true })
        {
            var data = new RelativeIlluminationAnalysis(optic, fieldDensity: 2,
                sampling: sampling, usePolarization: polarization).GenerateData();
            Assert.Equal(AnalysisOutcome.NotApplicable, data.Outcome);
            Assert.Equal(IlluminationMetrics.RelativeIlluminationNotApplicableReason(optic), data.OutcomeReason);
            Assert.Empty(data.PlotSeries);
            Assert.False(data.Values.ContainsKey("RelativeIllumination"));
            Assert.False(data.Values.ContainsKey("EffectiveFNumbers"));
            var scalar = Assert.Throws<AnalysisDataUnavailableException>(() =>
                IlluminationMetrics.RelativeToAxis(optic, (0, 1), .55, sampling: sampling, usePolarization: polarization));
            Assert.Equal(data.OutcomeReason, scalar.Reason);
        }
    }

    [Fact]
    public void AfocalImageWeightingRejectsTheSameQuantityBeforeChangingPixels()
    {
        var optic = Cone(); optic.ImageSpaceAfocal = true;
        double[,] pixels = { { 1, .5 }, { .25, .75 } };
        var original = pixels.Cast<double>().ToArray();
        var failure = Assert.Throws<AnalysisDataUnavailableException>(() =>
            ImageSimulationEngine.ApplyRelativeIllumination(pixels, optic, optic.Wavelengths[0], .1, .1, 10, 0, 0));
        Assert.Equal(IlluminationMetrics.RelativeIlluminationNotApplicableReason(optic), failure.Reason);
        Assert.Equal(original, pixels.Cast<double>());
        var data = new ImageSimulationAnalysis(optic, new ImageSimulationConfig { ImageWidth = 16, ImageHeight = 16 }).GenerateData();
        Assert.Equal(AnalysisOutcome.Unavailable, data.Outcome);
        Assert.Contains(failure.Reason, data.OutcomeReason);
        Assert.DoesNotContain("reduce the image field", data.OutcomeReason);
        Assert.Empty(data.PlotSeries);
    }

    [Fact]
    public void RawProjectedAreaIsNotRenamedAsAfocalRelativeIllumination()
    {
        var optic = Cone();
        var focal = IlluminationMetrics.Evaluate(optic, (0, 0), .55);
        Assert.True(focal.ProjectedCosineArea > 0);
        Assert.Null(IlluminationMetrics.RelativeIlluminationNotApplicableReason(optic));
        optic.ImageSpaceAfocal = true;
        // This synthetic finite cone isolates the meaning of the raw integration
        // API; its finite geometry is not a physical afocal lens control.
        Assert.Equal(focal, IlluminationMetrics.Evaluate(optic, (0, 0), .55));
    }
}
