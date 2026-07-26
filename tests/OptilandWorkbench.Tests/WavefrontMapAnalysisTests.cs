using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.Tests;

public sealed class WavefrontMapAnalysisTests
{
    [Fact]
    public void GeneratesSurfaceReadyWavefrontSamples()
    {
        var result = new WavefrontAnalysis(
            Optic.CreateCookeTriplet(),
            pupilSampling: 16,
            wavelengthNumber: 1,
            fieldNumber: 1,
            rotationDegrees: 90,
            displayScale: 1.5,
            removeTilt: true,
            pupilSx: 0.1,
            pupilSy: -0.2,
            pupilSr: 0.8,
            name: "Wavefront Map").GenerateData();

        Assert.Equal("Wavefront Map", result.Name);
        var series = Assert.Single(result.PlotSeries);
        Assert.Equal(AnalysisSeriesKind.Heatmap, series.Kind);
        Assert.NotEmpty(series.Points);
        Assert.All(series.Points, point => Assert.True(point.Value >= -1e-12));
        Assert.Equal(90.0, result.Values["RotationDegrees"]);
        Assert.Equal(1.5, result.Values["DisplayScale"]);
        Assert.Equal("16 x 16", result.Values["Sampling"]);
        Assert.Equal(0.1, result.Values["PupilSx"]);
        Assert.Equal(-0.2, result.Values["PupilSy"]);
        Assert.Equal(0.8, result.Values["PupilSr"]);
    }
}
