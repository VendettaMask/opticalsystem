using System.Text.Json;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Tests;

public sealed class ZemaxHuygensPsfParityTests
{
    [Fact]
    public void CapturedAnalysisSettingsMatchZemaxHuygensPsf()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(
            File.ReadAllText(Path.Combine(fixtureDirectory, "zemax-123456.ZMX")),
            ".zmx");
        using var zemax = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(fixtureDirectory, "zemax-123456-huygens-psf.json")));

        var current = new HuygensPsfAnalysis(
            optic,
            numRays: 32,
            imageSize: 32,
            pixelPitchMillimeters: 0,
            wavelengthNumber: 0,
            fieldNumber: 1).GenerateData();
        var currentPoints = Assert.Single(current.PlotSeries).Points;
        var referenceGrid = zemax.RootElement.GetProperty("dataGrids")[0];
        var referenceRows = referenceGrid.GetProperty("values");

        Assert.Equal(32, current.Values["PupilSampling"]);
        Assert.Equal(32, current.Values["ImageSize"]);
        Assert.Equal(0, current.Values["WavelengthNumber"]);
        Assert.Equal(1, current.Values["FieldNumber"]);
        Assert.Equal(1024, currentPoints.Count);
        Assert.InRange(
            Convert.ToDouble(current.Values["ImageDeltaMicrometers"]),
            referenceGrid.GetProperty("dx").GetDouble() * 0.999,
            referenceGrid.GetProperty("dx").GetDouble() * 1.001);

        var currentValues = currentPoints.Select(point => point.Value!.Value).ToArray();
        var referenceValues = referenceRows
            .EnumerateArray()
            .SelectMany(row => row.EnumerateArray())
            .Select(value => value.GetDouble())
            .ToArray();
        var currentPeak = currentValues.Max();
        var referencePeak = referenceValues.Max();
        Assert.InRange(currentPeak, referencePeak - 0.01, referencePeak + 0.01);

        var squaredError = currentValues
            .Zip(referenceValues)
            .Select(pair =>
            {
                var error = (pair.First / currentPeak) - (pair.Second / referencePeak);
                return error * error;
            })
            .Average();
        var nrmse = Math.Sqrt(squaredError);
        Assert.True(nrmse <= 0.01, $"Huygens PSF peak-normalized NRMSE was {nrmse:P6}.");
    }
}
