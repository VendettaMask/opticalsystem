using System.Reflection;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.Tests;

public sealed class ProductVersionInfoTests
{
    [Theory]
    [InlineData("1.0.3", "1.0.3")]
    [InlineData("1.0.3+abcdef012345", "1.0.3")]
    [InlineData("2.1.0-rc.2+abcdef012345", "2.1.0-rc.2")]
    [InlineData("  3.2.1  ", "3.2.1")]
    public void InformationalVersionPreservesReleaseSuffixWithoutBuildMetadata(string input, string expected)
    {
        Assert.Equal(expected, ProductVersionInfo.Format(input, new Version(9, 9, 9)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+abcdef")]
    public void MissingInformationalVersionFallsBackToActualAssemblyVersion(string? input)
    {
        Assert.Equal("1.2.3", ProductVersionInfo.Format(input, new Version(1, 2, 3, 4)));
    }

    [Fact]
    public void TwoPartAssemblyVersionUsesZeroPatch()
    {
        Assert.Equal("1.2.0", ProductVersionInfo.Format(null, new Version(1, 2)));
    }

    [Fact]
    public void MissingBothSourcesDoesNotInventAReleaseVersion()
    {
        Assert.Equal("未知", ProductVersionInfo.Format(null, null));
    }

    [Fact]
    public void CurrentReadsProductAssemblyRatherThanTestHost()
    {
        var assembly = typeof(global::OpticalSystemDesign.App.App).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.False(string.IsNullOrWhiteSpace(informational));
        Assert.Equal(informational.Split('+', 2)[0], ProductVersionInfo.Current);
    }
}
