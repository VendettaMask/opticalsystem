using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace OpticalSystemDesign.Tests;

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
    public void CompanyLogoIsOutlinedVectorWithoutRasterOrLiveText()
    {
        using var stream = OpticalSystemDesign.App.BrandAssets.OpenCompanyLogoStream();
        var svg = System.Xml.Linq.XDocument.Load(stream);
        System.Xml.Linq.XNamespace ns = "http://www.w3.org/2000/svg";
        var root = Assert.IsType<System.Xml.Linq.XElement>(svg.Root);
        Assert.Equal(ns + "svg", root.Name);
        var path = Assert.Single(root.Elements(ns + "path"));
        Assert.True(path.Attribute("d")!.Value.Length > 1000);
        Assert.All(root.Descendants(), element => Assert.Contains(element.Name.LocalName, new[] { "title", "desc", "path" }));
        Assert.DoesNotContain("data:", svg.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("font-family", svg.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(typeof(OpticalSystemDesign.App.BrandAssets).Assembly.GetManifestResourceNames(),
            name => name.EndsWith("CompanyLogo.png", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(240)]
    [InlineData(1200)]
    [InlineData(4800)]
    public void VectorLogoRendersAtTargetResolutionWithTransparentMargins(int width)
    {
        var height = (int)Math.Ceiling(width * 104d / 1183);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.Transparent);
        OpticalSystemDesign.App.BrandAssets.DrawCompanyLogo(surface.Canvas, SKRect.Create(width, height));
        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        Assert.Equal((byte)0, bitmap.GetPixel(0, 0).Alpha);
        Assert.Equal((byte)0, bitmap.GetPixel(width - 1, height - 1).Alpha);
        Assert.Contains(bitmap.Pixels, color => color.Alpha == 255 && color.Blue > color.Red && color.Blue > color.Green);
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_BRAND_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(directory, $"vector-logo-{width}.png"), png.ToArray());
        }
    }

    [Fact]
    public void VectorLogoTintPreservesTransparencyAndUsesRequestedColor()
    {
        var requested = new SKColor(246, 176, 116);
        using var surface = SKSurface.Create(new SKImageInfo(1200, 110));
        surface.Canvas.Clear(SKColors.Transparent);
        OpticalSystemDesign.App.BrandAssets.DrawCompanyLogo(surface.Canvas, SKRect.Create(1200, 110), requested);
        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        Assert.Equal((byte)0, bitmap.GetPixel(0, 0).Alpha);
        Assert.Contains(bitmap.Pixels, color => color == requested);
        Assert.All(bitmap.Pixels.Where(color => color.Alpha == 255), color => Assert.Equal(requested, color));
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
