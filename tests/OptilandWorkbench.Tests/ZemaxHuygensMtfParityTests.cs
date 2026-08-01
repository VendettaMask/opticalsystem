using System.Text.Json;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Tests;

public sealed class ZemaxHuygensMtfParityTests
{
    [Fact]
    public void CapturedPolychromaticSettingsMatchZemax123456()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(
            File.ReadAllText(Path.Combine(fixtureDirectory, "zemax-123456.ZMX")),
            ".zmx");
        using var zemax = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(fixtureDirectory, "zemax-123456-huygens-mtf.json")));

        var current = new HuygensMtfAnalysis(
            optic,
            numRays: 32,
            imageSize: 32,
            pixelPitchMillimeters: 0,
            wavelengthNumber: 0,
            fieldNumber: 0,
            zemaxCompatible: true).GenerateData();
        var referenceSeries = zemax.RootElement.GetProperty("dataSeries");
        Assert.Equal(5, referenceSeries.GetArrayLength());
        Assert.Equal(10, current.PlotSeries.Count);
        Assert.Equal(32, current.Values["NumRays"]);
        Assert.Equal(32, current.Values["ImageSize"]);

        var normalizedErrors = new List<double>();
        var correlations = new List<double>();
        for (var fieldIndex = 0; fieldIndex < referenceSeries.GetArrayLength(); fieldIndex++)
        {
            var reference = referenceSeries[fieldIndex];
            var x = reference.GetProperty("x");
            var y = reference.GetProperty("y");
            var tangential = current.PlotSeries[fieldIndex * 2];
            var sagittal = current.PlotSeries[(fieldIndex * 2) + 1];
            Assert.Equal(300, tangential.Points.Count);
            Assert.Equal(300, sagittal.Points.Count);
            var referenceTangential = new double[300];
            var referenceSagittal = new double[300];
            for (var index = 0; index < 300; index++)
            {
                var referenceFrequency = x[index].GetDouble();
                var frequencyScale = Math.Max(1, referenceFrequency);
                Assert.InRange(
                    Math.Abs(tangential.Points[index].X - referenceFrequency) / frequencyScale,
                    0,
                    0.001);
                Assert.InRange(
                    Math.Abs(sagittal.Points[index].X - referenceFrequency) / frequencyScale,
                    0,
                    0.001);
                referenceTangential[index] = y[index][0].GetDouble();
                referenceSagittal[index] = y[index][1].GetDouble();
            }

            AddNormalizedErrors(tangential, referenceTangential, normalizedErrors);
            AddNormalizedErrors(sagittal, referenceSagittal, normalizedErrors);
            correlations.Add(Correlation(
                tangential.Points.Select(point => point.Y).ToArray(),
                referenceTangential));
            correlations.Add(Correlation(
                sagittal.Points.Select(point => point.Y).ToArray(),
                referenceSagittal));
        }

        var nrmse = Math.Sqrt(normalizedErrors.Average(error => error * error));
        var minimumCorrelation = correlations.Min();
        Assert.True(
            nrmse <= 0.03 && minimumCorrelation >= 0.98,
            $"Huygens MTF NRMSE is {nrmse:G8}; minimum correlation is {minimumCorrelation:G8}.");
    }

    private static void AddNormalizedErrors(
        AnalysisSeries current,
        IReadOnlyList<double> reference,
        ICollection<double> errors)
    {
        var range = Math.Max(1e-12, reference.Max() - reference.Min());
        for (var index = 0; index < reference.Count; index++)
        {
            errors.Add((current.Points[index].Y - reference[index]) / range);
        }
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
