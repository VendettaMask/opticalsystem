using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class VignettedFanContractTests
{
    [Theory]
    [InlineData("Ray Fan", "ray-fan")]
    [InlineData("Optical Path Difference", "optical-path-difference")]
    public void ClippedNativeFanSamplesRequireIdenticalPhysicalAcceptanceMasks(string key, string file)
    {
        var normalized = ResultNormalizer.Zemax(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "tessar-vignetting", file + "-native.json"), AnalysisComparisonRegistry.Get(key),
            new CanonicalAnalysisRequest { CanonicalAnalysisKey = key, Apodization = "none", WorkbenchSettings = new() { ["CheckApertures"] = "True" } });
        var sagittal = normalized.Series.Single(s => s.Id == "sagittal");
        Assert.Equal(41, sagittal.X.Length);
        Assert.Equal(2, sagittal.Y.Count(y => !double.IsFinite(y)));
        Assert.True(double.IsFinite(sagittal.Y[38]));
        Assert.True(double.IsNaN(sagittal.Y[39]) && double.IsNaN(sagittal.Y[40]));
        var values = ComparisonMetrics.Align(sagittal, sagittal, []);
        Assert.Equal(39, values.X.Length);
        Assert.Equal(1, values.Coverage);
        Assert.All(values.Workbench, value => Assert.True(double.IsFinite(value)));
        var differentMask = sagittal with { Y = sagittal.Y.Select((y, i) => i == 39 ? 0 : y).ToArray() };
        Assert.Throws<InvalidDataException>(() => ComparisonMetrics.Align(differentMask, sagittal, []));
        Assert.Throws<InvalidDataException>(() => ComparisonMetrics.Align(sagittal with { HasClippingMask = false }, sagittal, []));
        Assert.Throws<InvalidDataException>(() => ComparisonMetrics.Align(sagittal with { X = sagittal.X.Select(x => x + .001).ToArray() }, sagittal, []));
        var empty = sagittal with { Y = sagittal.Y.Select(_ => double.NaN).ToArray() };
        Assert.Throws<InvalidDataException>(() => ComparisonMetrics.Align(empty, empty, []));
        var singleton = sagittal with { Y = sagittal.Y.Select((y, i) => i == 20 ? y : double.NaN).ToArray() };
        Assert.Throws<InvalidDataException>(() => ComparisonMetrics.Align(singleton, singleton, []));
    }

    [Theory]
    [InlineData("Ray Fan", true)]
    [InlineData("Ray Fan", false)]
    [InlineData("Optical Path Difference", true)]
    [InlineData("Optical Path Difference", false)]
    public void TypedPupilDisplaySettingSurvivesConfigurationAndRequestIdentity(string key, bool vignetted)
    {
        var original = new AnalysisConfiguration { VignettedPupil = vignetted };
        var restored = JsonSerializer.Deserialize<AnalysisConfiguration>(JsonSerializer.Serialize(original, JsonFiles.Options), JsonFiles.Options)!;
        Assert.Equal(vignetted, restored.VignettedPupil);
        var request = new CanonicalAnalysisRequest { CanonicalAnalysisKey = key, Apodization = "none", WorkbenchSettings = [] };
        var entry = AnalysisComparisonRegistry.Get(key);
        var mapped = request with { WorkbenchSettings = AnalysisComparisonRegistry.MapWorkbench(entry, request, restored.VignettedPupil) };
        Assert.Equal(vignetted.ToString(), mapped.WorkbenchSettings["VignettedPupil"]);
        Assert.Equal("True", mapped.WorkbenchSettings["CheckApertures"]);
        var other = request with { WorkbenchSettings = AnalysisComparisonRegistry.MapWorkbench(entry, request, !vignetted) };
        Assert.NotEqual(mapped.Fingerprint, other.Fingerprint);
        Assert.Equal("True", AnalysisComparisonRegistry.MapWorkbench(entry, request)["VignettedPupil"]);
    }
}
