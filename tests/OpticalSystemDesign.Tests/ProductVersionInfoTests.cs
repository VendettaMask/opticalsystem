using System.Reflection;
using System.Xml.Linq;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.Tests;

public sealed class ProductVersionInfoTests
{
    [Fact]
    public void OtaBuildSequenceIsEmbeddedByTheCloudBuild()
    {
        Assert.True(ProductVersionInfo.OtaBuildSequence >= 0);
        var cloudId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID");
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" && long.TryParse(cloudId, out var expected))
            Assert.Equal(expected, ProductVersionInfo.OtaBuildSequence);
    }
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

    [Fact]
    public void ProductVersionAuthorityMatchesAssemblyAndAllDefaultPackagingEntrypoints()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpticalSystemDesign.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var root = directory.FullName;
        var version = Assert.Single(XDocument.Load(Path.Combine(root, "ProductVersion.props")).Descendants("ProductVersion")).Value;
        Assert.Equal(version, ProductVersionInfo.Current);
        Assert.Contains("ProductVersion.props", File.ReadAllText(Path.Combine(root, "Directory.Build.props")));
        foreach (var script in new[] { "build-installer.ps1", "publish-windows.ps1" })
        {
            var text = File.ReadAllText(Path.Combine(root, "scripts", script));
            Assert.Contains("get-product-version.ps1", text);
            Assert.DoesNotContain("$AppVersion = '1.0.3'", text);
        }
        var workflow = File.ReadAllText(Path.Combine(root, ".github/workflows/release.yml"));
        Assert.Contains("$tag -cne \"v$version\"", workflow);
        Assert.Contains("options: [all, win-x64, win-arm64, osx-arm64]", workflow);
        Assert.Contains("needs: [plan, package]", workflow);
        var publisher = File.ReadAllText(Path.Combine(root, "scripts/publish-github-exe.ps1"));
        Assert.Contains("git/ref/tags/$Tag", publisher);
        Assert.Contains("draft = $true", publisher);
        Assert.Contains("draft = $false", publisher);
        Assert.Contains("-PublishDirectory $payload -OutputDirectory", workflow);
    }
}
