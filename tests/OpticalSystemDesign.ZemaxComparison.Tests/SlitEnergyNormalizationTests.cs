using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison.Normalization;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class SlitEnergyNormalizationTests
{
    [Theory]
    [InlineData("X")]
    [InlineData("Y")]
    [InlineData("ensquared")]
    public void NonradialEnergyMatchesTheNativeFieldRatherThanItsIdealCurve(string type)
    {
        var result = Normalize(type);
        var actual = Assert.Single(result.Series);
        Assert.Equal("curve:0", actual.Id);
        Assert.Equal(new[] { .2, .5 }, actual.Y);
    }

    [Fact]
    public void RadialEnergyRetainsBothIndependentCurves()
    {
        var result = Normalize("encircled");
        Assert.Equal(2, result.Series.Count);
        Assert.Equal(new[] { .8, .9 }, result.Series[0].Y);
        Assert.Equal(new[] { .2, .5 }, result.Series[1].Y);
    }

    private static NumericResult Normalize(string type)
    {
        var path = Path.Combine(Path.GetTempPath(), "dee-normalization-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                dataSeries = new[]
                {
                    new { x = new[] { 1d, 2d }, y = new[] { new[] { .8 }, new[] { .9 } } },
                    new { x = new[] { 1d, 2d }, y = new[] { new[] { .2 }, new[] { .5 } } }
                }
            }));
            return ExtendedResultNormalizer.Zemax(path, new CanonicalAnalysisRequest
            {
                CanonicalAnalysisKey = "Diffraction Encircled Energy",
                Apodization = "{}",
                WorkbenchSettings = new() { ["Type"] = type }
            });
        }
        finally
        {
            File.Delete(path);
        }
    }
}
