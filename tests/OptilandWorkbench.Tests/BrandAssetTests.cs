using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

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
        AssertRoundedAlpha(iconPng);
        Assert.Equal(new byte[] { 0, 0, 1, 0 }, windowsIcon[..4]);
        Assert.True(windowsIcon.Length > 10_000);
        Assert.Equal("icns", Encoding.ASCII.GetString(macIcon, 0, 4));
        Assert.True(macIcon.Length > 10_000);
    }

    [Fact]
    public void EmbeddedCompanyLogoCanBeLoadedByTheAnalysisFooter()
    {
        using var stream = OptilandWorkbench.App.BrandAssets.OpenCompanyLogoStream();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);

        AssertPng(memory.ToArray(), 2172, 724);
    }

    [Fact]
    public void PreparedCompanyLogoHasTransparentBackgroundAndVisibleArtwork()
    {
        var png = OptilandWorkbench.App.BrandAssets.GetPreparedCompanyLogoPng();
        using var logo = SKBitmap.Decode(png);

        Assert.NotNull(logo);
        Assert.Equal((byte)0, logo.GetPixel(0, 0).Alpha);
        Assert.Contains(
            Enumerable.Range(0, logo.Height),
            y => Enumerable.Range(0, logo.Width).Any(x => logo.GetPixel(x, y).Alpha == 255));
    }

    [Fact]
    public void PreparedCompanyLogoKeepsOriginalBrandColor()
    {
        var png = OptilandWorkbench.App.BrandAssets.GetPreparedCompanyLogoPng();
        using var logo = SKBitmap.Decode(png);
        Assert.NotNull(logo);

        var visibleColors = Enumerable.Range(0, logo.Height)
            .SelectMany(y => Enumerable.Range(0, logo.Width).Select(x => logo.GetPixel(x, y)))
            .Where(pixel => pixel.Alpha == 255)
            .Select(pixel => (pixel.Red, pixel.Green, pixel.Blue))
            .Distinct()
            .Take(8)
            .ToArray();

        Assert.Contains(visibleColors, color => color.Blue > color.Red && color.Blue > color.Green);
        Assert.DoesNotContain(visibleColors, color => color is ((byte)32, (byte)34, (byte)38));
    }

    [Fact]
    public void ThemeColoredCompanyLogoPreservesTransparencyAndUsesRequestedColor()
    {
        var requested = Avalonia.Media.Color.FromRgb(32, 34, 38);
        var png = OptilandWorkbench.App.BrandAssets.GetThemeColoredCompanyLogoPng(requested);
        using var logo = SKBitmap.Decode(png);
        Assert.NotNull(logo);
        Assert.Equal((byte)0, logo.GetPixel(0, 0).Alpha);
        var visible = Enumerable.Range(0, logo.Height)
            .SelectMany(y => Enumerable.Range(0, logo.Width).Select(x => logo.GetPixel(x, y)))
            .First(pixel => pixel.Alpha == 255);
        Assert.Equal((requested.R, requested.G, requested.B), (visible.Red, visible.Green, visible.Blue));
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

    private static void AssertRoundedAlpha(byte[] bytes)
    {
        using var icon = SKBitmap.Decode(bytes);
        Assert.NotNull(icon);
        Assert.Equal((byte)0, icon.GetPixel(0, 0).Alpha);
        Assert.Equal((byte)0, icon.GetPixel(icon.Width - 1, 0).Alpha);
        Assert.Equal((byte)0, icon.GetPixel(0, icon.Height - 1).Alpha);
        Assert.Equal((byte)0, icon.GetPixel(icon.Width - 1, icon.Height - 1).Alpha);
        Assert.Equal((byte)255, icon.GetPixel(icon.Width / 2, icon.Height / 2).Alpha);
    }
}
