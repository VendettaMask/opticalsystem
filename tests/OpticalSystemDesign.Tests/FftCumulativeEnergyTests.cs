using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class FftCumulativeEnergyTests
{
    [Theory]
    [InlineData("ms-l7-32", 32, 5, 0)]
    [InlineData("ms-l7-32", 32, 5, 1)]
    [InlineData("ms-l7-64", 64, 5, 0)]
    [InlineData("ms-l7-64", 64, 5, 1)]
    [InlineData("ms-l7-128", 128, 5, 0)]
    [InlineData("ms-l7-128", 128, 5, 1)]
    [InlineData("range2p5", 32, 2.5, 0)]
    [InlineData("range2p5", 32, 2.5, 1)]
    [InlineData("range10", 32, 10, 0)]
    [InlineData("range10", 32, 10, 1)]
    public void CapturedIdealAndActualCurvesPassAcrossSamplingAndRange(string name, int sampling, double maximum, int series)
        => Compare(name, sampling, maximum, "encircled", series);

    [Theory]
    [InlineData("x-tiny", .15)]
    [InlineData("x-half", .5)]
    [InlineData("x-one", 1)]
    public void IndependentSlitControlsValidateDistanceBinsAndOrigin(string name, double maximum)
        => Compare(name, 32, maximum, "X", 1);

    private static void Compare(string name, int sampling, double maximum, string type, int series)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Validation", "Zemax", "FftEnergyIntegration", name);
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(folder, "source.ZMX")), ".zmx");
        optic.RayAimingEnabled = true;
        var data = new DiffractionEncircledEnergyAnalysis(optic, sampling, 2 * sampling, 401, 1, 1,
            type, "centroid", maximum).GenerateData();
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "data.json")));
        var expected = native.RootElement.GetProperty("dataSeries")[series].GetProperty("y")
            .EnumerateArray().Select(v => v[0].GetDouble()).ToArray();
        var actual = data.PlotSeries[type == "encircled" ? series : 0].Points.Select(p => p.Y).ToArray();
        Assert.Equal(expected.Length, actual.Length);
        var nrmse = Math.Sqrt(actual.Zip(expected, (a, b) => (a - b) * (a - b)).Average()) / (expected.Max() - expected.Min());
        Assert.True(nrmse < .003, $"{name}/{series}: original energy NRMSE limit .003, measured {nrmse:R}");
        Assert.Equal("FFT cumulative distance-bin integration", data.Values["Method"]);
    }

    [Theory]
    [InlineData("X", 1)]
    [InlineData("Y", 1)]
    [InlineData("ensquared", 1)]
    [InlineData("encircled", 1.4142135623730951)]
    public void IsolatedPixelHasAnIndependentLinearDistanceBin(string type, double distance)
    {
        var samples = Enumerable.Range(-1, 3).SelectMany(y => Enumerable.Range(-1, 3)
            .Select(x => new EnergySample(x, y, x == 1 && y == 1 ? 1 : 0))).ToArray();
        var grid = new FftCumulativeEnergyGrid(samples, (0, 0), type);
        Assert.Equal(0, grid.Fraction(0));
        Assert.Equal(0, grid.Fraction(distance - .5), 12);
        Assert.Equal(.6, grid.Fraction(distance + .1), 12);
        Assert.Equal(1, grid.Fraction(distance + .5), 12);
    }
}
