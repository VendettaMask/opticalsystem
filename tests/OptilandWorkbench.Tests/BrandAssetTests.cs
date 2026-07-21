using System.Buffers.Binary;
using System.Text;

namespace OptilandWorkbench.Tests;

public sealed class BrandAssetTests
{
    [Fact]
    public void PackagedBrandAssetsHaveExpectedFormatsAndDimensions()
    {
        var iconPng = File.ReadAllBytes(Asset("AppIcon.png"));
        var splashPng = File.ReadAllBytes(Asset("Splash.png"));
        var windowsIcon = File.ReadAllBytes(Asset("AppIcon.ico"));
        var macIcon = File.ReadAllBytes(Asset("AppIcon.icns"));

        AssertPng(iconPng, 1024, 1024);
        AssertPng(splashPng, 1280, 720);
        Assert.Equal(new byte[] { 0, 0, 1, 0 }, windowsIcon[..4]);
        Assert.True(windowsIcon.Length > 10_000);
        Assert.Equal("icns", Encoding.ASCII.GetString(macIcon, 0, 4));
        Assert.True(macIcon.Length > 10_000);
    }

    private static string Asset(string name) => Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "Brand",
        name);

    private static void AssertPng(byte[] bytes, int width, int height)
    {
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4e, 0x47 }, bytes[..4]);
        Assert.Equal(width, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)));
        Assert.Equal(height, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
    }
}
