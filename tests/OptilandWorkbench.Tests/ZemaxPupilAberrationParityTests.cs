using System.Text.Json;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Tests;

public sealed class ZemaxPupilAberrationParityTests
{
    [Fact]
    public void CapturedRayAimedSettingsUseFortyOneSamplesAndRemainAtNumericalZero()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(
            File.ReadAllText(Path.Combine(fixtureDirectory, "zemax-123456.ZMX")),
            ".zmx");
        using var zemax = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(fixtureDirectory, "zemax-123456-pupil-aberration.json")));

        var current = new PupilAberrationAnalysis(optic, numPoints: 41).GenerateData();
        var panes = current.PlotPanes
            ?? throw new InvalidOperationException("Pupil Aberration did not return plot panes.");
        Assert.Equal(20, current.Values["NumberOfRaysEachSide"]);
        Assert.Equal(41, current.Values["Samples"]);
        Assert.All(
            panes.SelectMany(pane => pane.Series),
            series => Assert.Equal(41, series.Points.Count));

        var currentMaximum = panes
            .SelectMany(pane => pane.Series)
            .SelectMany(series => series.Points)
            .Select(point => Math.Abs(point.Y))
            .Max();
        var referenceMaximum = zemax.RootElement.GetProperty("dataSeries")
            .EnumerateArray()
            .SelectMany(group => group.GetProperty("y").EnumerateArray())
            .SelectMany(row => row.EnumerateArray())
            .Select(value => Math.Abs(value.GetDouble()))
            .Max();
        Assert.InRange(currentMaximum, 0, 1e-5);
        Assert.InRange(referenceMaximum, 0, 1e-5);
    }
}
