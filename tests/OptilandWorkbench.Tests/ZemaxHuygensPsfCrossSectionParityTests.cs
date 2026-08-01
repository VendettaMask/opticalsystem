using System.Text.Json;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;
using Xunit.Abstractions;

namespace OptilandWorkbench.Tests;

public sealed class ZemaxHuygensPsfCrossSectionParityTests
{
    private readonly ITestOutputHelper _output;

    public ZemaxHuygensPsfCrossSectionParityTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void CapturedCrossSectionSettingsUseZemaxPsfCenterRowAndScale()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(
            File.ReadAllText(Path.Combine(fixtureDirectory, "zemax-123456.ZMX")),
            ".zmx");
        using var zemax = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(fixtureDirectory, "zemax-123456-huygens-psf.json")));

        var current = new HuygensPsfCrossSectionAnalysis(
            optic,
            numRays: 32,
            imageSize: 32,
            pixelPitchMillimeters: 0,
            wavelengthNumber: 0,
            fieldNumber: 1,
            profileType: "X").GenerateData();
        var actual = Assert.Single(current.PlotSeries).Points;
        var grid = zemax.RootElement.GetProperty("dataGrids")[0];
        var expected = grid.GetProperty("values")[16];
        var dx = grid.GetProperty("dx").GetDouble();
        var minx = grid.GetProperty("minX").GetDouble();
        Assert.Equal(32, actual.Count);

        var peak = expected.EnumerateArray().Max(value => value.GetDouble());
        var squaredError = 0.0;
        for (var index = 0; index < actual.Count; index++)
        {
            var expectedX = minx + (index * dx);
            var tolerance = Math.Max(dx * 0.001, Math.Abs(expectedX) * 0.001);
            Assert.InRange(actual[index].X, expectedX - tolerance, expectedX + tolerance);
            var error = (actual[index].Y / actual.Max(point => point.Y))
                - (expected[index].GetDouble() / peak);
            squaredError += error * error;
        }

        var normalizedRmse = Math.Sqrt(squaredError / actual.Count);
        _output.WriteLine($"normalized RMSE={normalizedRmse:P6}; peak={actual.Max(point => point.Y):G10}; dx={dx:G10} µm");
        Assert.True(normalizedRmse <= 0.01);
        Assert.InRange(current.PlotOptions!.XMinimum!.Value, -16 * dx * 1.001, -16 * dx * 0.999);
        Assert.InRange(current.PlotOptions.XMaximum!.Value, 16 * dx * 0.999, 16 * dx * 1.001);
    }
}
