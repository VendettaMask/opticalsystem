using System.Text.Json;
using OptilandWorkbench.Application.Legacy;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;
using Xunit.Abstractions;

namespace OptilandWorkbench.Tests;

public sealed class ZemaxRmsWavefrontVsFieldParityTests
{
    private readonly ITestOutputHelper _output;

    public ZemaxRmsWavefrontVsFieldParityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void WorkbenchProductPresetUsesTheCaptured123456RmsFieldSettings()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(
            File.ReadAllText(Path.Combine(fixtureDirectory, "zemax-123456.ZMX")),
            ".zmx");

        var workspace = new OpticalWorkspaceModel(optic);
        var settings = workspace.MergeAnalysisSettings("RMS vs Field", null);
        Assert.Equal("wavefront", settings["Data"]);
        Assert.Equal("chief", settings["Reference"]);
        Assert.Equal("15", settings["FieldDensity"]);
        Assert.Equal("+y", settings["ScanDirection"]);
        var current = workspace.BuildAnalysisView("RMS vs Field", settings);
        var validated = new RmsWavefrontVsFieldAnalysis(
            optic,
            numRings: 6,
            fieldDensity: 15,
            method: "GQ",
            reference: "chief",
            wavelengthNumber: 0,
            scanType: "+y",
            removeVignettingFactors: true,
            zemaxCompatibleOutput: true).GenerateData();

        Assert.Equal(validated.PlotSeries.Count, current.SeriesList.Count);
        for (var seriesIndex = 0; seriesIndex < validated.PlotSeries.Count; seriesIndex++)
        {
            var expected = validated.PlotSeries[seriesIndex];
            var actual = current.SeriesList[seriesIndex];
            Assert.Equal(expected.Points.Count, actual.Points.Count);
            for (var pointIndex = 0; pointIndex < expected.Points.Count; pointIndex++)
            {
                Assert.Equal(expected.Points[pointIndex].X, actual.Points[pointIndex].X, 12);
                Assert.Equal(expected.Points[pointIndex].Y, actual.Points[pointIndex].Y, 12);
            }
        }
    }

    [Fact]
    public void ChiefRayGaussianQuadratureFieldScanMatchesZemax123456()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(
            File.ReadAllText(Path.Combine(fixtureDirectory, "zemax-123456.ZMX")),
            ".zmx");
        using var zemax = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(fixtureDirectory, "zemax-123456-rms-wavefront-vs-field.json")));

        var current = new RmsWavefrontVsFieldAnalysis(
            optic,
            numRings: 6,
            fieldDensity: 15,
            method: "GQ",
            reference: "chief",
            wavelengthNumber: 0,
            scanType: "+y",
            removeVignettingFactors: true,
            zemaxCompatibleOutput: true).GenerateData();

        var reference = zemax.RootElement.GetProperty("dataSeries")[0];
        var x = reference.GetProperty("x");
        var y = reference.GetProperty("y");
        Assert.Equal(4, current.PlotSeries.Count);
        Assert.All(current.PlotSeries, series => Assert.Equal(16, series.Points.Count));

        var errors = new List<double>();
        var expectedValues = new List<double>();
        var correlations = new List<double>();
        var seriesMetrics = new List<string>();
        for (var seriesIndex = 0; seriesIndex < 4; seriesIndex++)
        {
            var actual = current.PlotSeries[seriesIndex];
            var expected = Enumerable.Range(0, x.GetArrayLength())
                .Select(index => y[index][seriesIndex].GetDouble())
                .ToArray();
            for (var index = 0; index < expected.Length; index++)
            {
                Assert.Equal(x[index].GetDouble(), actual.Points[index].X, 12);
                errors.Add(actual.Points[index].Y - expected[index]);
                expectedValues.Add(expected[index]);
            }
            var seriesErrors = actual.Points.Select((point, index) => point.Y - expected[index]).ToArray();
            var correlation = Correlation(actual.Points.Select(point => point.Y).ToArray(), expected);
            correlations.Add(correlation);
            seriesMetrics.Add($"{actual.Name}: RMSE={Math.Sqrt(seriesErrors.Average(error => error * error)):G8}, "
                + $"max={seriesErrors.Max(Math.Abs):G8}, corr={correlation:G10}");
        }

        var rms = Math.Sqrt(errors.Average(error => error * error));
        var normalizedRms = rms / expectedValues.Max(Math.Abs);
        var maximum = errors.Select(Math.Abs).Max();
        var minimumCorrelation = correlations.Min();
        _output.WriteLine(
            $"RMSE={rms:G8} waves; NRMSE={normalizedRms:P6}; max absolute={maximum:G8} waves; "
            + $"minimum correlation={minimumCorrelation:G10}");
        _output.WriteLine(string.Join(Environment.NewLine, seriesMetrics));
        Assert.True(
            normalizedRms <= 0.005 && maximum <= 0.001 && minimumCorrelation >= 0.999,
            $"RMSE={rms:G8} waves; NRMSE={normalizedRms:P6}; max absolute={maximum:G8} waves; "
            + $"minimum correlation={minimumCorrelation:G10}");
    }

    private static double Correlation(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        var leftMean = left.Average();
        var rightMean = right.Average();
        var numerator = 0.0;
        var leftSquared = 0.0;
        var rightSquared = 0.0;
        for (var index = 0; index < left.Count; index++)
        {
            var leftDelta = left[index] - leftMean;
            var rightDelta = right[index] - rightMean;
            numerator += leftDelta * rightDelta;
            leftSquared += leftDelta * leftDelta;
            rightSquared += rightDelta * rightDelta;
        }
        return numerator / Math.Sqrt(leftSquared * rightSquared);
    }
}
