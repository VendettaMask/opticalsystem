using System.Text.Json;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Tests;

public sealed class ZemaxLateralColorParityTests
{
    [Fact]
    public void CapturedAnalysisSettingsMatchZemaxChiefRayAndAiryCurves()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(
            File.ReadAllText(Path.Combine(fixtureDirectory, "zemax-123456.ZMX")),
            ".zmx");
        using var zemax = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(fixtureDirectory, "zemax-123456-lateral-color.json")));

        var current = new AnalysisCatalog(optic).Create("Lateral Color").GenerateData();
        var reference = zemax.RootElement.GetProperty("dataSeries")[0].GetProperty("y");
        Assert.Equal(3, current.PlotSeries.Count);
        Assert.All(current.PlotSeries, series => Assert.Equal(101, series.Points.Count));

        var currentCurves = new[]
        {
            current.PlotSeries[0].Points.Select(point => point.X).ToArray(),
            current.PlotSeries[2].Points.Select(point => point.X).ToArray(),
            current.PlotSeries[1].Points.Select(point => point.X).ToArray()
        };
        for (var curveIndex = 0; curveIndex < currentCurves.Length; curveIndex++)
        {
            var expected = reference
                .EnumerateArray()
                .Select(row => row[curveIndex].GetDouble())
                .ToArray();
            var referencePeak = expected.Select(Math.Abs).Max();
            var squaredError = currentCurves[curveIndex]
                .Zip(expected)
                .Select(pair =>
                {
                    var error = (pair.First - pair.Second) / referencePeak;
                    return error * error;
                })
                .Average();
            var nrmse = Math.Sqrt(squaredError);
            Assert.True(
                nrmse <= 0.01,
                $"Lateral Color curve {curveIndex} NRMSE was {nrmse:P6}.");
        }
    }
}
