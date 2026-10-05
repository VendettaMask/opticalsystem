using System.Text.Json;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;
using Xunit.Abstractions;

namespace OptilandWorkbench.Tests;

public sealed class ZemaxRelativeIlluminationParityTests
{
    private readonly ITestOutputHelper _output;

    public ZemaxRelativeIlluminationParityTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ChiefRayTangentPupilProjectionMatchesZemax123456()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(
            File.ReadAllText(Path.Combine(fixtureDirectory, "zemax-123456.ZMX")),
            ".zmx");
        using var zemax = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(fixtureDirectory, "zemax-123456-relative-illumination.json")));

        var current = new RelativeIlluminationAnalysis(
            optic,
            rayDensity: 10,
            fieldDensity: 21,
            wavelengthNumber: 0,
            scanDirection: "+y",
            removeVignettingFactors: true).GenerateData();
        var actual = Assert.Single(current.PlotSeries).Points;
        var reference = zemax.RootElement.GetProperty("dataSeries")[0];
        var x = reference.GetProperty("x");
        var y = reference.GetProperty("y");
        Assert.Equal(21, actual.Count);

        var errors = new List<double>();
        for (var index = 0; index < actual.Count; index++)
        {
            Assert.Equal(x[index].GetDouble(), actual[index].X, 12);
            errors.Add(actual[index].Y - y[index][0].GetDouble());
        }

        var rmse = Math.Sqrt(errors.Average(error => error * error));
        var maximum = errors.Max(Math.Abs);
        _output.WriteLine($"RMSE={rmse:G8}; max={maximum:G8}; edge={actual[^1].Y:G10}");
        _output.WriteLine("EffectiveFNumbers=" + JsonSerializer.Serialize(current.Values["EffectiveFNumbers"]));
        _output.WriteLine("SampledPupilNodeCounts=" + JsonSerializer.Serialize(current.Values["SampledPupilNodeCounts"]));
        Assert.True(rmse <= 0.002 && maximum <= 0.004,
            $"RMSE={rmse:G8}; max={maximum:G8}; edge={actual[^1].Y:G10}");

        // Observational density study, not an EFNO compatibility gate: only the
        // original capture exists, so these are current Workbench recalculations.
        foreach (var density in new[] { 5, 20, 40 })
        {
            var scan = new RelativeIlluminationAnalysis(optic, rayDensity: density, fieldDensity: 3,
                wavelengthNumber: 0, scanDirection: "+y", removeVignettingFactors: true).GenerateData();
            _output.WriteLine("DensityStudy=" + JsonSerializer.Serialize(new
            {
                Density = density,
                FieldMillimeters = scan.PlotSeries.Single().Points.Select(point => point.X).ToArray(),
                EffectiveFNumbers = scan.Values["EffectiveFNumbers"],
                RelativeIllumination = scan.Values["RelativeIllumination"],
                SampledPupilNodeCounts = scan.Values["SampledPupilNodeCounts"]
            }));
        }
    }
}
