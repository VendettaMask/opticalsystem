using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class IconThemeIdentityTests
{
    private static string[] UsedNames()
    {
        using var stream = typeof(LocalIcon).Assembly.GetManifestResourceStream("OpticalSystemDesign.App.Assets.Icons.SemanticManifest.json");
        using var document = JsonDocument.Parse(stream!);
        return document.RootElement.GetProperty("usedDrawings").EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToArray();
    }

    [Fact]
    public async Task LightDarkAndPixelKeepEveryUsedIconPixelIdentical()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            var actual = new SortedDictionary<string, string>();
            foreach (var themeName in new[] { "Light", "Dark", "Pixel" })
            foreach (var name in UsedNames())
            foreach (var size in new[] { 18, 24, 32 })
                actual[$"{themeName}/{name}/{size}"] = RenderHash(name, themeName, size);
            Assert.Equal(2025, actual.Count);
            var location = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_ICON_IDENTITY_EVIDENCE_DIR");
            Assert.False(string.IsNullOrEmpty(location));
            Directory.CreateDirectory(location!);
            var path = Path.Combine(location!, "unchanged-theme-baseline.json");
            if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_WRITE_ICON_BASELINE") == "1")
                File.WriteAllText(path, JsonSerializer.Serialize(actual, new JsonSerializerOptions { WriteIndented = true }));
            else
            {
                var expected = JsonSerializer.Deserialize<SortedDictionary<string, string>>(File.ReadAllText(path))!;
                var added = new[] { "mtf-fourier-map", "mtf-fourier-surface", "mtf-geometric-map", "mtf-huygens-surface" };
                Assert.Equal(expected, actual.Where(sample => expected.ContainsKey(sample.Key)));
                Assert.All(actual.Keys.Except(expected.Keys), key => Assert.Contains(key.Split('/')[1], added));
                File.WriteAllText(Path.Combine(location!, "unchanged-theme-final.json"), JsonSerializer.Serialize(actual));
            }
        }, CancellationToken.None);
    }

    private static string RenderHash(string name, string themeName, int size)
    {
        var icon = new LocalIcon { IconName = name, Width = size, Height = size };
        var scope = new ThemeVariantScope { RequestedThemeVariant = ThemeRegistry.FromSettings(themeName).RequestedVariant,
            Width = size, Height = size, Child = icon };
        scope.Measure(new Size(size, size)); scope.Arrange(new Rect(0, 0, size, size));
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size)); bitmap.Render(scope);
        using var bytes = new MemoryStream(); bitmap.Save(bytes, PngBitmapEncoderOptions.Default);
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }

    [Fact]
    public async Task BothProfilesCoverEveryActualIconAndParseNativeGeometry()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            var names = UsedNames();
            foreach (var theme in new[] { "Lumen", "Isekai" })
            {
                Assert.Equal(225, ScientificIconLibrary.ThemeNames(theme).Count);
                foreach (var name in names)
                {
                    Assert.True(ScientificIconLibrary.HasThemeDrawing(theme, name), $"{theme}/{name}");
                    var drawing = ScientificIconLibrary.Resolve(name, theme);
                    Assert.NotSame(ScientificIconLibrary.Resolve(name), drawing);
                    drawing.Artwork!.EnsureGeometry();
                    Assert.All(drawing.Artwork.Roles, role => Assert.InRange(role, 0, 3));
                }
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Lumen")]
    [InlineData("Isekai")]
    public async Task ThemeSwitchUsesDistinctGeometryAndKeepsMtfDrawingsReadable(string themeName)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            foreach (var size in new[] { 18, 24, 32 })
            {
                var hashes = new HashSet<string>();
                foreach (var name in UsedNames().Where(name => name.StartsWith("mtf-", StringComparison.Ordinal)))
                    Assert.True(hashes.Add(RenderHash(name, themeName, size)), name);
                Assert.Equal(13, hashes.Count);
            }
            Assert.NotEqual(RenderHash("user-guide", "Dark", 32), RenderHash("user-guide", themeName, 32));
            Assert.NotEqual(RenderHash("wavefront-map", "Dark", 32), RenderHash("wavefront-map", themeName, 32));
            Assert.Equal(ScientificIconFinish.Flat, ScientificIconPalette.Monochrome(Avalonia.Media.Colors.Gray).Finish);
        }, CancellationToken.None);
    }
}
