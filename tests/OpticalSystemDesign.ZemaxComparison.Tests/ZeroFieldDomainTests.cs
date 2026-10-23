using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class ZeroFieldDomainTests
{
    [Theory]
    [InlineData("1418-047-apochromat-c1-primary-f1-w4-relative-illumination")]
    [InlineData("1419-047-apochromat-c1-primary-f1-w4-rms-vs-field")]
    [InlineData("1420-047-apochromat-c1-primary-f1-w4-field-curvature-and-distortion")]
    public async Task FreshCoreOnAxisValuesMatchIndependentNativeCapture(string id)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures/zero-field");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
        Assert.Equal(manifest.RootElement.GetProperty("sourceSha256").GetString(), JsonFiles.Hash(File.ReadAllBytes(Path.Combine(root, "source.zmx"))));
        foreach (var file in manifest.RootElement.GetProperty("files").EnumerateArray())
            Assert.Equal(file.GetProperty("sha256").GetString(), JsonFiles.Hash(File.ReadAllBytes(Path.Combine(root, file.GetProperty("path").GetString()!))));
        var request = JsonFiles.Read<CanonicalAnalysisRequest>(Path.Combine(root, id, "request.json"));
        Assert.Equal(0, request.MaximumFieldRadius);
        var optic = (await WorkbenchExecutor.Import(Path.Combine(root, "source.zmx"))).Configurations[0];
        var data = new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey, request.WorkbenchSettings);
        var actual = ExtendedResultNormalizer.Workbench(data, request);
        var expected = ResultNormalizer.Zemax(Path.Combine(root, id, "data.json"), AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey), request);
        var policy = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json")).Analyses[request.CanonicalAnalysisKey];
        Assert.Equal(expected.Series.Count, actual.Series.Count);
        foreach (var reference in expected.Series)
        {
            var series = actual.Series.Single(s => s.Id == reference.Id);
            Assert.True(series.IsPointDomain && reference.IsPointDomain);
            var values = ComparisonMetrics.Align(series, reference, []);
            Assert.Single(values.X);
            Assert.Equal(0, values.X[0]);
            Assert.Equal(1, values.Coverage);
            Assert.Equal(Conclusion.Pass, ComparisonMetrics.Calculate(reference.Id, reference.YAxis.Unit, values, policy.Quantities[reference.YAxis.Quantity]).Conclusion);
        }
    }

    [Fact]
    public void PointDomainCannotHideMissingOnAxisSamplesOrCreateAnInterval()
    {
        var axis = new Axis("NormalizedField", "Dimensionless");
        var value = new Axis("Irradiance", "Dimensionless");
        var repeated = new Series1DResult("on-axis", "", [0, 0], [1, 1], axis, value);
        var point = PhysicalNormalization.ZeroFieldPoint(repeated, false, []);
        var native = PhysicalNormalization.ZeroFieldPoint(repeated with { X = [0, 1e-6], Y = [1, 9] }, true, []);
        Assert.Equal(1, ComparisonMetrics.Align(point, native, []).Coverage);
        Assert.Throws<InvalidDataException>(() => PhysicalNormalization.ZeroFieldPoint(repeated with { Y = [1, 2] }, false, []));
        Assert.Throws<InvalidDataException>(() => PhysicalNormalization.ZeroFieldPoint(repeated with { X = [1e-6, 2e-6] }, true, []));
        Assert.Throws<InvalidDataException>(() => PhysicalNormalization.ZeroFieldPoint(repeated with { X = [0, 1e-6] }, false, []));
        Assert.Throws<InvalidDataException>(() => ComparisonMetrics.Align(point, point with { X = [1e-6] }, []));
        Assert.Throws<InvalidDataException>(() => ComparisonMetrics.Align(point, repeated, []));
        // The ordinary interval contract still rejects duplicates and singleton curves.
        Assert.Throws<InvalidDataException>(() => ComparisonMetrics.Align(repeated, repeated, []));
        Assert.Throws<InvalidDataException>(() => ComparisonMetrics.Align(point with { IsPointDomain = false }, point with { IsPointDomain = false }, []));
    }
}
