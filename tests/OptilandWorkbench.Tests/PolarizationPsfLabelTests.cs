using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.Tests;

public sealed class PolarizationPsfLabelTests
{
    [Fact]
    public void PolarizationWeightedFftPsfIsExplicitlyLabeledAsScalarApproximation()
    {
        var data = new PsfAnalysis(
            Optic.CreateCookeTriplet(),
            numRays: 8,
            gridSize: 16,
            usePolarization: true).GenerateData();

        Assert.Contains("Polarization-weighted scalar FFT PSF", data.PlotOptions!.Title);
        Assert.Equal("Polarization-weighted scalar approximation", data.Values["PolarizationModel"]);
        Assert.Contains("cross-polarization", data.Values["PolarizationLimit"]!.ToString());
    }

    [Fact]
    public void PolarizationWeightedHuygensPsfIsExplicitlyLabeledAsScalarApproximation()
    {
        var data = new HuygensPsfAnalysis(
            Optic.CreateCookeTriplet(),
            numRays: 4,
            imageSize: 4,
            pixelPitchMillimeters: 0.005,
            usePolarization: true).GenerateData();

        Assert.Contains("Polarization-weighted scalar Huygens PSF", data.PlotOptions!.Title);
        Assert.Equal("Polarization-weighted scalar approximation", data.Values["PolarizationModel"]);
        Assert.Contains("longitudinal", data.Values["PolarizationLimit"]!.ToString());
    }
}
