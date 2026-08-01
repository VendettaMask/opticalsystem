using System.Text.Json;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Tests;

public sealed class ZemaxOpticalPathDifferenceParityTests
{
    [Fact]
    public void PolychromaticFansUsePrimaryWavelengthReferenceSphere()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(
            File.ReadAllText(Path.Combine(fixtureDirectory, "zemax-123456.ZMX")),
            ".zmx");
        using var zemax = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(fixtureDirectory, "zemax-123456-optical-path-difference.json")));

        var current = new AnalysisCatalog(optic)
            .Create("Optical Path Difference")
            .GenerateData();
        var panes = current.PlotPanes
            ?? throw new InvalidOperationException("OPD analysis did not return plot panes.");
        var referenceGroups = zemax.RootElement.GetProperty("dataSeries");
        Assert.Equal(10, panes.Count);
        Assert.Equal(10, referenceGroups.GetArrayLength());

        for (var paneIndex = 0; paneIndex < panes.Count; paneIndex++)
        {
            var pane = panes[paneIndex];
            var referenceRows = referenceGroups[paneIndex].GetProperty("y");
            Assert.Equal(3, pane.Series.Count);
            for (var wavelengthIndex = 0; wavelengthIndex < pane.Series.Count; wavelengthIndex++)
            {
                var actual = pane.Series[wavelengthIndex].Points.Select(point => point.Y).ToArray();
                var expected = referenceRows
                    .EnumerateArray()
                    .Select(row => row[wavelengthIndex].GetDouble())
                    .ToArray();
                var peak = expected.Select(Math.Abs).Max();
                var nrmse = Math.Sqrt(actual.Zip(expected)
                    .Select(pair =>
                    {
                        var error = (pair.First - pair.Second) / peak;
                        return error * error;
                    })
                    .Average());
                Assert.True(
                    nrmse <= 0.0001,
                    $"OPD pane {paneIndex}, wavelength {wavelengthIndex} NRMSE was {nrmse:P8}.");
            }
        }
    }
}
