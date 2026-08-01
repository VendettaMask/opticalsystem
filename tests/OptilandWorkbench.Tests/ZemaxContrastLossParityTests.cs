using System.Text.Json;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Tests;

public sealed class ZemaxContrastLossParityTests
{
    [Fact]
    public void MooreElliottLossMapsMatchCapturedZemax123456Settings()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(
            File.ReadAllText(Path.Combine(fixtureDirectory, "zemax-123456.ZMX")),
            ".zmx");
        using var zemax = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(fixtureDirectory, "zemax-123456-contrast-loss.json")));

        var current = new ContrastLossMapAnalysis(
            optic,
            sampling: 13,
            frequency: 100,
            normalize: false,
            wavelengthNumber: 2,
            fieldNumber: 1,
            showOpd: false).GenerateData();

        Assert.Equal(2, current.Values["WavelengthNumber"]);
        Assert.Equal(100.0, current.Values["Frequency"]);
        Assert.InRange(Convert.ToDouble(current.Values["PupilSeparation"]), 0.23, 0.25);

        var referenceGrids = zemax.RootElement.GetProperty("dataGrids");
        var comparisons = new[]
        {
            (Current: current.PlotSeries[0], Reference: referenceGrids[3]),
            (Current: current.PlotSeries[1], Reference: referenceGrids[1])
        };
        var errors = new List<double>();
        var referenceValues = new List<double>();
        foreach (var (series, reference) in comparisons)
        {
            var values = reference.GetProperty("values");
            Assert.Equal(13 * 13, series.Points.Count);
            for (var row = 0; row < 13; row++)
            {
                for (var column = 0; column < 13; column++)
                {
                    var expectedElement = values[row][column];
                    if (expectedElement.ValueKind == JsonValueKind.String)
                    {
                        continue;
                    }

                    var actual = series.Points[(row * 13) + column].Value;
                    Assert.True(actual.HasValue && double.IsFinite(actual.Value));
                    var expected = expectedElement.GetDouble();
                    errors.Add(actual.Value - expected);
                    referenceValues.Add(expected);
                }
            }
        }

        var normalizedRootMeanSquareError = Math.Sqrt(
            errors.Average(error => error * error)) / referenceValues.Max(Math.Abs);
        Assert.True(
            normalizedRootMeanSquareError <= 0.03,
            $"Contrast-loss NRMSE against Zemax is {normalizedRootMeanSquareError:P6}; "
            + $"maximum absolute error is {errors.Max(Math.Abs):G8}.");
    }
}
