using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Zemax;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class NativeConfigurationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CanonicalHuygensContractsUseTheNumberedNativeSelector(int configuration)
    {
        foreach (var key in new[] { "Huygens PSF Cross Section", "Huygens Through Focus MTF" })
        {
            var request = ExtendedAnalysisContracts.Configure(AnalysisComparisonRegistry.Get(key),
                new() { CanonicalAnalysisKey = key, Configuration = configuration, Apodization = "none", WorkbenchSettings = [] }, 1);
            Assert.Equal(configuration + 1, request.ZemaxSettings["Configuration"]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidConfigurationCannotSelectAReservedNativeMode(int configuration) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeConfigurationSelector.Numbered(configuration));

    [Fact]
    public void OverflowCannotWrapIntoAReservedNativeMode() =>
        Assert.Throws<OverflowException>(() => NativeConfigurationSelector.Numbered(int.MaxValue));

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void StaleNativeConfigurationIsRejectedBeforeItsSpotNumbersAreCompared(int configuration)
    {
        var directory = Path.Combine(Path.GetTempPath(), "OpticalSystemDesign-native-config-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var data = Path.Combine(directory, "data.json");
            var settings = Path.Combine(directory, "captured-settings.json");
            var request = new CanonicalAnalysisRequest { CanonicalAnalysisKey = "Spot Diagram", Configuration = configuration, Apodization = "none", WorkbenchSettings = [] };
            var entry = AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey);
            JsonFiles.Write(data, new { spot = new { rms = 10d, geo = 15d } });
            Assert.Throws<InvalidDataException>(() => ResultNormalizer.Zemax(data, entry, request));
            JsonFiles.Write(settings, new { properties = new Dictionary<string, int> { ["Configuration"] = configuration } });
            Assert.Throws<InvalidDataException>(() => ResultNormalizer.Zemax(data, entry, request));
            JsonFiles.Write(settings, new { properties = new Dictionary<string, int> { ["Configuration"] = NativeConfigurationSelector.Numbered(configuration) } });
            Assert.Equal(10, ResultNormalizer.Zemax(data, entry, request).Scalars[0].Value);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
