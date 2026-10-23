using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class DenseCircularEnergyTests
{
    [Theory]
    [InlineData("1634-052-double-gauss-28-degree-field-c1-primary-f1-w2-diffraction-encircled-energy-p256")]
    [InlineData("1664-053-double-gauss-5-degree-field-c1-primary-f1-w3-diffraction-encircled-energy-p256")]
    [InlineData("1814-058-gaussian-quadrature-over-wavelength-c1-primary-f1-w2-diffraction-encircled-energy-p256")]
    [InlineData("1874-060-petzval-c1-primary-f1-w2-diffraction-encircled-energy-p256")]
    [InlineData("1934-062-tessar-lens-using-vignetting-factors-c1-primary-f1-w2-diffraction-encircled-energy-p256")]
    [InlineData("1964-063-wide-angle-lens-100-degree-field-c1-primary-f1-w2-diffraction-encircled-energy-p256")]
    [InlineData("2024-065-wide-angle-lens-210-degree-field-c1-primary-f1-w2-diffraction-encircled-energy-p256")]
    public async Task OriginalNativeDenseEnergyRetainsTheCircularPupilBoundary(string id)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures/dense-energy");
        var files = JsonFiles.Read<JsonElement>(Path.Combine(root, "manifest.json"));
        foreach (var file in files.EnumerateArray().Where(f => f.GetProperty("path").GetString()!.StartsWith(id + "/", StringComparison.Ordinal)))
            Assert.Equal(file.GetProperty("sha256").GetString(), JsonFiles.Hash(File.ReadAllBytes(Path.Combine(root, file.GetProperty("path").GetString()!))));
        var request = JsonFiles.Read<CanonicalAnalysisRequest>(Path.Combine(root, id, "request.json"));
        var optic = (await WorkbenchExecutor.Import(Path.Combine(root, id, "source.zmx"))).Configurations[0];
        var data = new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey, request.WorkbenchSettings);
        var actual = ExtendedResultNormalizer.Workbench(data, request);
        var expected = ResultNormalizer.Zemax(Path.Combine(root, id, "data.json"), AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey), request);
        var policy = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json")).Analyses[request.CanonicalAnalysisKey];
        Assert.Equal(2, expected.Series.Count);
        foreach (var reference in expected.Series)
        {
            var values = ComparisonMetrics.Align(actual.Series.Single(s => s.Id == reference.Id), reference, []);
            Assert.Equal(1, values.Coverage);
            Assert.Equal(Conclusion.Pass, ComparisonMetrics.Calculate(reference.Id, reference.YAxis.Unit, values, policy.Quantities[reference.YAxis.Quantity]).Conclusion);
        }
    }
}
