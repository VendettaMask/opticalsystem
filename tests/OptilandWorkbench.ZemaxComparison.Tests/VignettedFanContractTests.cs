using System.Text.Json;
using OptilandWorkbench.ZemaxComparison;
using OptilandWorkbench.ZemaxComparison.Configuration;
using OptilandWorkbench.ZemaxComparison.Normalization;
using OptilandWorkbench.ZemaxComparison.Metrics;

namespace OptilandWorkbench.ZemaxComparison.Tests;

public sealed class VignettedFanContractTests
{
    [Theory]
    [InlineData("Ray Fan", "ray-fan")]
    [InlineData("Optical Path Difference", "optical-path-difference")]
    public void ClippedNativeFanSamplesRemainInvalidAndFailTheComparisonGate(string key, string file)
    {
        var normalized = ResultNormalizer.Zemax(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "tessar-vignetting", file + "-native.json"), AnalysisComparisonRegistry.Get(key),
            new CanonicalAnalysisRequest { CanonicalAnalysisKey = key, Apodization = "none", WorkbenchSettings = [] });
        var sagittal = normalized.Series.Single(s => s.Id == "sagittal");
        Assert.Equal(41, sagittal.X.Length);
        Assert.Equal(2, sagittal.Y.Count(y => !double.IsFinite(y)));
        Assert.True(double.IsFinite(sagittal.Y[38]));
        Assert.True(double.IsNaN(sagittal.Y[39]) && double.IsNaN(sagittal.Y[40]));
        Assert.Throws<InvalidDataException>(() => ComparisonMetrics.Align(sagittal, sagittal, []));
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
