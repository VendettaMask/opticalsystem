using System.Text.Json;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;
using Xunit.Abstractions;

namespace OptilandWorkbench.Tests;

public sealed class ZemaxRmsWavefrontVsFocusParityTests
{
    private readonly ITestOutputHelper _output;

    public ZemaxRmsWavefrontVsFocusParityTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void CapturedChiefRayFocusScanSettingsMatchZemax123456()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(
            File.ReadAllText(Path.Combine(fixtureDirectory, "zemax-123456.ZMX")),
            ".zmx");
        using var zemax = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(fixtureDirectory, "zemax-123456-rms-wavefront-vs-focus.json")));

        var current = new RmsVsFocusAnalysis(
            optic,
            focusDensity: 16,
            minimumFocus: -0.01,
            maximumFocus: 0.01,
            numRings: 6,
            distribution: "hexapolar",
            wavelengthNumber: 0,
            reference: "chief",
            method: "GQ",
            data: "wavefront").GenerateData();
        var reference = zemax.RootElement.GetProperty("dataSeries")[0];
        var x = reference.GetProperty("x");
        var y = reference.GetProperty("y");
        Assert.Equal(5, current.PlotSeries.Count);
        Assert.All(current.PlotSeries, series => Assert.Equal(16, series.Points.Count));

        var errors = new List<double>();
        var expectedValues = new List<double>();
        for (var seriesIndex = 0; seriesIndex < 5; seriesIndex++)
        {
            var actualSeries = current.PlotSeries[seriesIndex];
            var expectedSeries = Enumerable.Range(0, x.GetArrayLength())
                .Select(index => y[index][seriesIndex].GetDouble())
                .ToArray();
            _output.WriteLine($"S{seriesIndex + 1} actual: {string.Join(",", actualSeries.Points.Select(point => point.Y.ToString("G8")))}");
            _output.WriteLine($"S{seriesIndex + 1} zemax:  {string.Join(",", expectedSeries.Select(value => value.ToString("G8")))}");
            for (var index = 0; index < x.GetArrayLength(); index++)
            {
                var actual = actualSeries.Points[index];
                var expected = y[index][seriesIndex].GetDouble();
                Assert.Equal(x[index].GetDouble(), actual.X, 12);
                errors.Add(actual.Y - expected);
                expectedValues.Add(expected);
            }
        }

        var rmse = Math.Sqrt(errors.Average(error => error * error));
        var nrmse = rmse / expectedValues.Max(Math.Abs);
        var maximum = errors.Max(Math.Abs);
        _output.WriteLine($"RMSE={rmse:G8} waves; NRMSE={nrmse:P6}; max={maximum:G8} waves");
        Assert.True(nrmse <= 0.01 && maximum <= 0.002,
            $"RMSE={rmse:G8} waves; NRMSE={nrmse:P6}; max={maximum:G8} waves");
    }
}
