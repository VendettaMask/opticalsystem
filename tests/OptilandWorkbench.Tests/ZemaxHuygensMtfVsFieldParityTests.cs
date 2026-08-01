using System.Text.Json;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Tests;

public sealed class ZemaxHuygensMtfVsFieldParityTests
{
    [Fact]
    public void SixFrequencyRelativeFieldScanMatchesZemax123456()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(
            File.ReadAllText(Path.Combine(fixtureDirectory, "zemax-123456.ZMX")),
            ".zmx");
        using var zemax = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(fixtureDirectory, "zemax-123456-huygens-mtf-vs-field.json")));
        var frequencies = new[] { 10.0, 20, 30, 40, 50, 60 };
        var settings = new MtfComputationSettings(
            PupilSampling: 64,
            ImageSize: 64,
            PixelPitchMillimeters: 0,
            ZemaxCompatible: true,
            UseZemaxHuygensSemantics: true);

        var current = new MtfVsFieldAnalysis(
            optic,
            MtfComputationMethod.Huygens,
            spatialFrequency: frequencies[0],
            fieldPointCount: 10,
            settings: settings,
            wavelengthNumber: 0,
            spatialFrequencies: frequencies,
            scanType: "+y",
            removeVignettingFactors: true,
            zemaxCompatibleOutput: true).GenerateData();

        var referenceSeries = zemax.RootElement.GetProperty("dataSeries");
        Assert.Equal(6, referenceSeries.GetArrayLength());
        Assert.Equal(12, current.PlotSeries.Count);
        Assert.Equal(11, current.Values["FieldPointCount"]);
        Assert.Equal(300, current.Values["PlotPointCount"]);
        var errors = new List<double>();
        var correlations = new List<double>();
        for (var frequencyIndex = 0; frequencyIndex < referenceSeries.GetArrayLength(); frequencyIndex++)
        {
            var reference = referenceSeries[frequencyIndex];
            var x = reference.GetProperty("x");
            var y = reference.GetProperty("y");
            var tangential = current.PlotSeries[frequencyIndex * 2];
            var sagittal = current.PlotSeries[(frequencyIndex * 2) + 1];
            Assert.Equal(x.GetArrayLength(), tangential.Points.Count);
            Assert.Equal(x.GetArrayLength(), sagittal.Points.Count);
            var referenceTangential = new double[x.GetArrayLength()];
            var referenceSagittal = new double[x.GetArrayLength()];
            for (var index = 0; index < x.GetArrayLength(); index++)
            {
                Assert.Equal(x[index].GetDouble(), tangential.Points[index].X, 12);
                Assert.Equal(x[index].GetDouble(), sagittal.Points[index].X, 12);
                referenceTangential[index] = y[index][0].GetDouble();
                referenceSagittal[index] = y[index][1].GetDouble();
                errors.Add(tangential.Points[index].Y - referenceTangential[index]);
                errors.Add(sagittal.Points[index].Y - referenceSagittal[index]);
            }
            correlations.Add(Correlation(
                tangential.Points.Select(point => point.Y).ToArray(),
                referenceTangential));
            correlations.Add(Correlation(
                sagittal.Points.Select(point => point.Y).ToArray(),
                referenceSagittal));
        }

        var rms = Math.Sqrt(errors.Average(error => error * error));
        var maximum = errors.Select(Math.Abs).Max();
        var minimumCorrelation = correlations.Min();
        Assert.True(
            rms <= 0.01 && maximum <= 0.02 && minimumCorrelation >= 0.95,
            $"Huygens MTF vs Field absolute RMS error is {rms:G8}; max is {maximum:G8}; "
            + $"minimum correlation is {minimumCorrelation:G8}.");
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
