using System.Text.Json;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class NativePhysicalConventionTests
{
    [Fact]
    public void ReflectionParityRetainsPhysicalDirectionsNormalsAndSignedReportDistances()
    {
        using var model = JsonDocument.Parse("""{"surfaces":[{"Material":""},{"Material":""},{"Material":"MIRROR"},{"Material":"MIRROR"},{"Material":""}]}""");
        var signs = NativeReflectionConvention.PropagationSigns(model.RootElement);
        Assert.Equal(new[] { 1, 1, -1, 1, 1 }, signs);
        double[][] rows = [
            [1, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, 0],
            [2, 0, 1, 2, .1, .2, .97, 0, .3, -.95, 12, 10],
            [3, 0, 2, 3, .2, .1, .97, 0, .2, -.98, 13, -9]
        ];
        NativeReflectionConvention.ConvertRayReport(rows, signs);
        Assert.Equal(new[] { -.1, -.2, -.97 }, rows[1][4..7]);
        Assert.Equal(new[] { 0d, .3, -.95 }, rows[1][7..10]);
        Assert.Equal(new[] { 0d, -.2, .98 }, rows[2][7..10]);
        Assert.Equal(10, rows[1][11]);
        Assert.Equal(9, rows[2][11]);
        Assert.Equal(12, rows[1][10]);
    }

    [Theory]
    [InlineData("CM")]
    [InlineData("IN")]
    [InlineData("M")]
    public void NonMillimeterCaptureRequiresExplicitConversionAndMatchingNativeReadback(string unit)
    {
        Assert.NotNull(AfocalAnalysisContracts.BlockReason(unit, false, "MTF"));
        Assert.Null(AfocalAnalysisContracts.BlockReason(unit, false, "MTF", normalizeNativeLength: true));
        Assert.NotNull(AfocalAnalysisContracts.BlockReason("UNKNOWN", false, "MTF", normalizeNativeLength: true));
    }
}
