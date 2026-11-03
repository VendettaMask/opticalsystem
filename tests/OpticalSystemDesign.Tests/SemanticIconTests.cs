using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class SemanticIconTests
{
    private static JsonDocument Manifest()
    {
        using var stream = typeof(LocalIcon).Assembly.GetManifestResourceStream("OpticalSystemDesign.App.Assets.Icons.SemanticManifest.json");
        Assert.NotNull(stream);
        return JsonDocument.Parse(stream);
    }

    [Fact]
    public void ActualFunctionsUseAuthoredArtworkAndEveryActiveAnalysisNameHasAValidDrawing()
    {
        using var manifest = Manifest();
        var root = manifest.RootElement;
        Assert.Equal(221, root.GetProperty("uniqueSemanticDrawings").GetInt32());
        Assert.Equal(196, root.GetProperty("newlyAuthored").GetArrayLength());
        Assert.Equal(25, root.GetProperty("preservedApproved").GetArrayLength());
        var names = root.GetProperty("newlyAuthored").EnumerateArray().Select(e => e.GetString()!)
            .Concat(root.GetProperty("preservedApproved").EnumerateArray().Select(e => e.GetString()!)).ToHashSet();
        foreach (var name in names)
        {
            Assert.True(ScientificIconLibrary.Contains(name), name);
            Assert.NotNull(ScientificIconLibrary.Resolve(name).Artwork);
        }
        var commandNames = root.GetProperty("commandIcons");
        foreach (var command in commandNames.EnumerateObject())
        {
            var active = WorkbenchAnalysisCatalog.AllRibbonCommands.Single(c => c.Id == command.Name);
            Assert.Equal(command.Value.GetString(), active.IconName);
            Assert.Contains(active.IconName, names);
        }
        Assert.Equal("wavefront-interferogram", WorkbenchAnalysisCatalog.RibbonCommands.Single(c => c.Id == "analysis-interferogram").IconName);
        Assert.Equal("image-geometric", WorkbenchAnalysisCatalog.RibbonCommands.Single(c => c.Id == "analysis-geometric-image").IconName);
    }

    [Fact]
    public void MtfMethodAndIndependentVariableAreBothEncodedInNineDedicatedNames()
    {
        var mtf = WorkbenchAnalysisCatalog.RibbonCommands.Where(c => c.Group == "MTF 曲线").ToArray();
        Assert.Equal(9, mtf.Length);
        Assert.Equal(9, mtf.Select(c => c.IconName).Distinct().Count());
        foreach (var method in new[] { "fourier", "huygens", "geometric" })
        foreach (var variable in new[] { "frequency", "focus", "field" })
            Assert.Contains(mtf, c => c.IconName == $"mtf-{method}-{variable}");
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("Lumen")]
    [InlineData("Isekai")]
    [InlineData("Pixel")]
    public async Task EveryAuthoredIconRendersAndNineMtfDrawingsStayDistinctAtRealUiSizes(string themeName)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            var theme = ThemeRegistry.FromSettings(themeName);
            using var manifest = Manifest();
            var names = manifest.RootElement.GetProperty("newlyAuthored").EnumerateArray().Select(e => e.GetString()!).ToArray();
            var panel = new WrapPanel { Width = 1320, Orientation = Orientation.Horizontal };
            foreach (var name in names)
            {
                ScientificIconLibrary.Resolve(name).Artwork!.EnsureGeometry();
                var row = new StackPanel { Width = 164, Height = 72, Orientation = Orientation.Vertical, Spacing = 3 };
                var samples = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
                foreach (var size in new[] { 18, 24, 32 })
                    samples.Children.Add(new LocalIcon { IconName = name, Width = size, Height = size });
                row.Children.Add(samples);
                row.Children.Add(new TextBlock { Text = name, FontSize = 8, Foreground = theme.IconPalette.Brushes[0] });
                panel.Children.Add(row);
            }
            var window = new Window { Width = 1340, Height = 1860, Content = panel, RequestedThemeVariant = theme.RequestedVariant };
            if (theme.Palette is not null) window.Background = new Avalonia.Media.SolidColorBrush(theme.Palette.Surface);
            window.Show(); Tick(window);
            try
            {
                foreach (var size in new[] { 18, 24, 32 })
                {
                    var hashes = new HashSet<string>();
                    foreach (var name in names.Where(n => n.StartsWith("mtf-", StringComparison.Ordinal)))
                    {
                        var icon = new LocalIcon { IconName = name, Width = size, Height = size };
                        var scope = new ThemeVariantScope { RequestedThemeVariant = theme.RequestedVariant, Width = size, Height = size, Child = icon };
                        scope.Measure(new Size(size, size)); scope.Arrange(new Rect(0, 0, size, size));
                        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size)); bitmap.Render(scope);
                        using var bytes = new MemoryStream(); bitmap.Save(bytes, PngBitmapEncoderOptions.Default);
                        Assert.True(hashes.Add(Convert.ToHexString(SHA256.HashData(bytes.ToArray()))), name);
                    }
                    Assert.Equal(9, hashes.Count);
                }
                foreach (var face in Enum.GetValues<ViewCubeFace>())
                    Assert.True(ScientificIconLibrary.Contains($"view-{face.ToString().ToLowerInvariant()}"));
                var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SEMANTIC_ICON_CAPTURE_DIR");
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var image = window.CaptureRenderedFrame(); Assert.NotNull(image);
                    image.Save(Path.Combine(directory, $"semantic-atlas-{themeName}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static void Tick(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
}
