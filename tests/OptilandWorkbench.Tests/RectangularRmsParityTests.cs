using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Tests;

public sealed class RectangularRmsParityTests
{
    [Theory]
    [InlineData("cooke-40-degree-field", 64)]
    [InlineData("cooke-40-degree-field", 128)]
    [InlineData("double-gauss-28-degree-field", 64)]
    [InlineData("double-gauss-28-degree-field", 128)]
    [InlineData("relay-lens", 64)]
    [InlineData("relay-lens", 128)]
    [InlineData("even-asphere", 64)]
    [InlineData("even-asphere", 128)]
    public void RectangularSpotFieldScanMatchesIndependentNativeCaptures(string lens, int density)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "rms-field-sampling", $"{lens}-ra-{density}-remove");
        var optic = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(File.ReadAllText(Path.Combine(root, "snapshot.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals })!);
        using var request = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "canonical-request.json")));
        var wavelength = request.RootElement.GetProperty("wavelength").GetInt32();
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data.json")));
        var curve = native.RootElement.GetProperty("dataSeries")[0];
        var current = new RmsVsFieldAnalysis(optic, fieldDensity: 15, numRings: density, method: "RA", data: "spot",
            reference: "centroid", wavelengthNumber: wavelength, removeVignetting: true).GenerateData().Series!;
        Assert.Equal(16, current.Points.Count);
        for (var i = 0; i < current.Points.Count; i++)
        {
            Assert.Equal(curve.GetProperty("x")[i].GetDouble(), current.Points[i].X, 10);
            Assert.InRange(Math.Abs(current.Points[i].Y * 1000 - curve.GetProperty("y")[i][0].GetDouble()), 0, 3e-5);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(64)]
    public void RectangularCellIntegrationIsCenteredAndKeepsGeneralEndpointSamplingDistinct(int density)
    {
        var samples = ApertureSampler.GenerateRectangularArray(density);
        Assert.NotEmpty(samples);
        Assert.All(samples, p => { Assert.InRange(p.X * p.X + p.Y * p.Y, 0, 1); Assert.Equal(1, p.Weight); });
        Assert.InRange(Math.Abs(samples.Sum(p => p.X)), 0, 1e-10);
        Assert.InRange(Math.Abs(samples.Sum(p => p.Y)), 0, 1e-10);
        Assert.All(samples, p => { Assert.True(Math.Abs(p.X) < 1); Assert.True(Math.Abs(p.Y) < 1); });
        if (density == 64)
        {
            Assert.InRange(Math.Abs(samples.Count * 4d / (density * density) - Math.PI), 0, .015);
            Assert.InRange(Math.Abs(samples.Sum(p => p.X * p.X) * 4d / (density * density) - Math.PI / 4), 0, .01);
            var general = ApertureSampler.Generate(9, PupilSampling.UniformGrid);
            Assert.Equal(5, general.Count);
            Assert.Contains(general, p => p.X == 1 && p.Y == 0);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1025)]
    public void InvalidRectangularDensityIsRejected(int density)
        => Assert.Throws<ArgumentOutOfRangeException>(() => ApertureSampler.GenerateRectangularArray(density));
}
