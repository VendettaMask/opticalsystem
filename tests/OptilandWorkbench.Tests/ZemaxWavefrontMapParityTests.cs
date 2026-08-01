using System.Text.Json;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Tests;

public sealed class ZemaxWavefrontMapParityTests
{
    [Fact]
    public async Task WavefrontMapUsesZemaxEvenGridAndMatchesCapturedOpd()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = await new ZemaxZmxImporter().ImportFileAsync(
            Path.Combine(fixtureDirectory, "zemax-123456.ZMX"));
        var current = new WavefrontAnalysis(
            optic,
            pupilSampling: 64,
            mapSize: 64,
            wavelengthNumber: 2,
            fieldNumber: 1,
            useExitPupilShape: true,
            name: "Wavefront").GenerateData();

        var points = Assert.Single(current.PlotSeries).Points;
        Assert.Equal(3001, points.Count);
        Assert.Equal("64 x 64", current.Values["Sampling"]);
        Assert.InRange(Convert.ToDouble(current.Values["RmsWaves"]), 0.038, 0.040);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(fixtureDirectory, "zemax-123456-wavefront-map.json")));
        var referenceRows = document.RootElement
            .GetProperty("dataGrids")[0]
            .GetProperty("values");
        var currentByIndex = points.ToDictionary(
            point => (
                Row: (int)Math.Round((point.Y * 31) + 32),
                Column: (int)Math.Round((point.X * 31) + 32)),
            point => point.Value!.Value);
        var squaredError = 0.0;
        var referencePeak = 0.0;
        var compared = 0;
        for (var row = 0; row < referenceRows.GetArrayLength(); row++)
        {
            var referenceColumns = referenceRows[row];
            for (var column = 0; column < referenceColumns.GetArrayLength(); column++)
            {
                var element = referenceColumns[column];
                if (element.ValueKind != JsonValueKind.Number
                    || !currentByIndex.TryGetValue((row, column), out var actual))
                {
                    continue;
                }

                var expected = element.GetDouble();
                var error = actual - expected;
                squaredError += error * error;
                referencePeak = Math.Max(referencePeak, Math.Abs(expected));
                compared++;
            }
        }

        Assert.Equal(3001, compared);
        var nrmse = Math.Sqrt(squaredError / compared) / referencePeak;
        Assert.True(nrmse <= 0.003, $"Wavefront Map NRMSE was {nrmse:P6}.");
    }
}
