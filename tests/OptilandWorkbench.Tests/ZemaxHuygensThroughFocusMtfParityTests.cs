using System.Text.Json;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Tests;

public sealed class ZemaxHuygensThroughFocusMtfParityTests
{
    [Fact]
    public void AxisFieldMatchesZemax123456AtTheFiveConfiguredFocusPlanes()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(
            File.ReadAllText(Path.Combine(fixtureDirectory, "zemax-123456.ZMX")),
            ".zmx");
        using var zemax = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(fixtureDirectory, "zemax-123456-huygens-through-focus-mtf.json")));
        var settings = new MtfComputationSettings(
            PupilSampling: 64,
            ImageSize: 32,
            PixelPitchMillimeters: 0,
            ZemaxCompatible: true,
            UseZemaxHuygensSemantics: true);

        var current = new MtfThroughFocusAnalysis(
            optic,
            MtfComputationMethod.Huygens,
            spatialFrequency: 20,
            deltaFocus: 0.1,
            focusPlaneCount: 5,
            settings: settings,
            wavelengthNumber: 0,
            fieldNumber: 1).GenerateData();

        var reference = zemax.RootElement.GetProperty("dataSeries")[0];
        var referenceY = reference.GetProperty("y");
        var rawTangential = Assert.Single(Assert.IsType<double[][]>(current.Values["RawTangential"]));
        var rawSagittal = Assert.Single(Assert.IsType<double[][]>(current.Values["RawSagittal"]));
        var referenceIndices = new[] { 0, 25, 50, 75, 100 };
        var errors = new List<double>();
        for (var focusIndex = 0; focusIndex < referenceIndices.Length; focusIndex++)
        {
            var referenceIndex = referenceIndices[focusIndex];
            errors.Add(rawTangential[focusIndex] - referenceY[referenceIndex][0].GetDouble());
            errors.Add(rawSagittal[focusIndex] - referenceY[referenceIndex][1].GetDouble());
        }

        var rms = Math.Sqrt(errors.Average(error => error * error));
        var maximum = errors.Select(Math.Abs).Max();
        Assert.True(
            rms <= 0.02 && maximum <= 0.04,
            $"Huygens through-focus MTF absolute RMS error is {rms:G8}; max is {maximum:G8}; "
            + $"current T=[{string.Join(", ", rawTangential.Select(value => value.ToString("G8")))}], "
            + $"S=[{string.Join(", ", rawSagittal.Select(value => value.ToString("G8")))}].");
    }
}
