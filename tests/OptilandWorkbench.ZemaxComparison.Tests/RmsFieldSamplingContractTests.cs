using System.Text.Json;
using OptilandWorkbench.ZemaxComparison;
using OptilandWorkbench.ZemaxComparison.Configuration;

namespace OptilandWorkbench.ZemaxComparison.Tests;

public sealed class RmsFieldSamplingContractTests
{
    [Theory]
    [InlineData("GQ", 6, true)]
    [InlineData("GQ", 12, false)]
    [InlineData("RA", 64, true)]
    [InlineData("RA", 128, false)]
    public void SamplingAndVignettingStayAlignedAcrossTheSerializedRequest(string method, int density, bool remove)
    {
        var entry = AnalysisComparisonRegistry.Get("RMS vs Field");
        var original = ExtendedAnalysisContracts.Configure(entry, new CanonicalAnalysisRequest
        { CanonicalAnalysisKey = entry.CanonicalAnalysisKey, Apodization = "none", WorkbenchSettings = [] }, 1);
        var configured = ExtendedAnalysisContracts.ConfigureRmsFieldSampling(original, new()
        { RmsFieldMethod = method, RmsFieldRayDensity = density, RmsFieldRemoveVignettingFactors = remove });
        var restored = JsonSerializer.Deserialize<CanonicalAnalysisRequest>(JsonSerializer.Serialize(configured, JsonFiles.Options), JsonFiles.Options)!;
        var mapped = restored with { WorkbenchSettings = AnalysisComparisonRegistry.MapWorkbench(entry, restored) };
        Assert.Equal(method, mapped.WorkbenchSettings["Method"]);
        Assert.Equal(density.ToString(), mapped.WorkbenchSettings["NumRings"]);
        Assert.Equal(remove.ToString(), mapped.WorkbenchSettings["RemoveVignetting"]);
        Assert.Equal("GaussQuad", original.ZemaxSettings["Method"]);
        Assert.Equal("RayDens_6", original.ZemaxSettings["RayDensity"]);
        Assert.Equal(configured.Fingerprint, restored.Fingerprint);
        if (method != "GQ" || density != 6 || !remove) Assert.NotEqual(original.Fingerprint, configured.Fingerprint);
    }

    [Fact]
    public void DefaultsKeepTheExistingFrozenRequestIdentity()
    {
        var entry = AnalysisComparisonRegistry.Get("RMS vs Field");
        var original = ExtendedAnalysisContracts.Configure(entry, new CanonicalAnalysisRequest
        { CanonicalAnalysisKey = entry.CanonicalAnalysisKey, Apodization = "none", WorkbenchSettings = [] }, 1);
        original = original with { WorkbenchSettings = AnalysisComparisonRegistry.MapWorkbench(entry, original) };
        var configured = ExtendedAnalysisContracts.ConfigureRmsFieldSampling(original, new());
        configured = configured with { WorkbenchSettings = AnalysisComparisonRegistry.MapWorkbench(entry, configured) };
        Assert.Equal(original.Fingerprint, configured.Fingerprint);
    }

    [Theory]
    [InlineData("RMS vs Field", "GQ", 0)]
    [InlineData("RMS vs Field", "GQ", 21)]
    [InlineData("RMS vs Field", "RA", 48)]
    [InlineData("RMS vs Field", "RA", 1024)]
    [InlineData("RMS vs Field", "Unknown", 6)]
    [InlineData("RMS vs Wavelength", "RA", 64)]
    [InlineData("Ray Fan", "RA", 64)]
    public void UnsupportedOrUnalignedSamplingIsRejected(string key, string method, int density)
        => Assert.Throws<ArgumentException>(() => ExtendedAnalysisContracts.ValidateRmsFieldSampling(key,
            new() { RmsFieldMethod = method, RmsFieldRayDensity = density }));
}
