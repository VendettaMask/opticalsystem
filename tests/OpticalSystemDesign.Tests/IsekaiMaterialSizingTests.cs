using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Mvvm.Controls;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using SkiaSharp;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class IsekaiMaterialSizingTests
{
    [Theory]
    [InlineData(13, 1)]
    [InlineData(18, 1)]
    [InlineData(13, 1.5)]
    [InlineData(18, 1.5)]
    [InlineData(13, 2)]
    [InlineData(18, 2)]
    public async Task NativeDocumentTabsKeepReadableMaterialsAcrossStatesAndThemeRoundTrips(double fontSize, double scaling)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Isekai");
            DisplayTypography.Configure(new AppSettings { FontSize = fontSize });
            var strip = new DocumentTabStrip
            {
                ItemsSource = new[] { "镜头数据", "二维视图", "瞳孔像差", "网格畸变" }
                    .Select((title, n) => new Document { Id = "tab-" + n, Title = title, CanClose = n != 0 }).ToArray(),
                IconTemplate = new FuncDataTemplate<Document>((_, _) => new Border()),
                HeaderTemplate = new FuncDataTemplate<Document>((d, _) => new TextBlock { Text = d?.Title, FontSize = fontSize }),
                SelectedIndex = 3, IsActive = true, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
            };
            var window = new Window { Width = 1000, Height = 160,
                Background = new SolidColorBrush(IsekaiTheme.Palette.Workspace),
                Content = new StackPanel { Margin = new Thickness(12), Children = { strip } } };
            DisplayTypography.Apply(window);
            try
            {
                window.Show(); window.SetRenderScaling(scaling); Tick(window);
                var prefix = $"tabs-{fontSize}-{scaling}";
                Save(window, $"{prefix}-idle.png");
                var tabs = strip.GetVisualDescendants().OfType<DocumentTabStripItem>().ToArray();
                Assert.Equal(4, tabs.Length);
                var geometry = tabs.Select(t => t.Bounds).ToArray();
                Write($"{prefix}-geometry.json", tabs.Select(t => new
                {
                    t.IsSelected, Bounds = t.Bounds.ToString(),
                    Parts = t.GetVisualDescendants().OfType<Control>()
                        .Where(c => c.Name is "PART_TabBody" or "PART_HeaderPresenter" or "PART_ClosePresenter")
                        .Select(c => new { c.Name, Bounds = c.Bounds.ToString(), Origin = c.TranslatePoint(default, t)?.ToString() }),
                    Ink = Header(t).Foreground?.ToString()
                }));
                CheckTabs(tabs, scaling);
                var hover = tabs[1];
                window.MouseMove(hover.TranslatePoint(new Point(hover.Bounds.Width / 2, hover.Bounds.Height / 2), window)!.Value);
                Tick(window); Save(window, $"{prefix}-hover.png"); CheckTabs(tabs, scaling);
                strip.SelectedIndex = 1; Tick(window); CheckTabs(tabs, scaling);
                window.MouseMove(new Point(900, 140)); Tick(window);
                Assert.Equal(geometry, tabs.Select(t => t.Bounds));
                foreach (var theme in new[] { "Light", "Dark", "Lumen", "Pixel", "Isekai" })
                {
                    ThemeApplicationService.Apply(Avalonia.Application.Current!, theme); Tick(window);
                    if (theme != "Isekai")
                        Assert.All(tabs, tab => Assert.IsNotType<DrawingBrush>(Body(tab).Background));
                }
                CheckTabs(tabs, scaling); Save(window, $"{prefix}-roundtrip.png");
                var enabledInk = Header(tabs[1]).Foreground;
                tabs[1].IsEnabled = false; Tick(window);
                Assert.False(tabs[1].IsEffectivelyEnabled);
                Assert.NotEqual(enabledInk, Header(tabs[1]).Foreground);
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(600, 360, 1)]
    [InlineData(1200, 740, 1.5)]
    [InlineData(1740, 1000, 2)]
    public async Task SceneCornerArtworkFollowsAllFourEdgesWhenResized(double width, double height, double scaling)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Isekai");
            var sourceSize = new Size(1200, 740);
            var scene = new OpticSceneControl { Width = sourceSize.Width, Height = sourceSize.Height };
            var scaled = new Viewbox { Child = scene, Stretch = Stretch.Uniform };
            var window = new Window { Width = width, Height = height, Content = scaled };
            try
            {
                window.Show(); window.SetRenderScaling(scaling); Tick(window);
                foreach (var size in new[] { new Size(width, height), new Size(width + 80, height + 60), new Size(width, height) })
                {
                    window.Width = size.Width; window.Height = size.Height; Tick(window);
                    var prefix = $"scene-{size.Width}x{size.Height}-{scaling}";
                    Save(window, $"{prefix}.png");
                    using var actual = CapturePixels(window);
                    var area = new Rect(scene.Bounds.Size).TransformToAABB(scene.TransformToVisual(window)!.Value);
                    area = area.TransformToAABB(Matrix.CreateScale(scaling, scaling));
                    var paper = new Border { Width = sourceSize.Width, Height = sourceSize.Height,
                        Background = IsekaiDrawingMaterial.Instance.CreateResources(false)[ThemeResourceBindings.SceneBackground] as IBrush };
                    scaled.Child = paper; Tick(window);
                    using var plain = CapturePixels(window);
                    scaled.Child = scene; Tick(window);
                    var counts = new List<int>();
                    for (var row = 0; row < 2; row++) for (var column = 0; column < 2; column++)
                    {
                        var count = 0;
                        var x0 = (int)(area.Left + area.Width * (column == 0 ? .01 : .8));
                        var x1 = (int)(area.Left + area.Width * (column == 0 ? .2 : .99));
                        var y0 = (int)(area.Top + area.Height * (row == 0 ? .01 : .75));
                        var y1 = (int)(area.Top + area.Height * (row == 0 ? .25 : .98));
                        for (var y = y0; y < y1; y++) for (var x = x0; x < x1; x++)
                            if (plain.GetPixel(x, y).Red - actual.GetPixel(x, y).Red > 20) count++;
                        counts.Add(count);
                    }
                    Write($"{prefix}-corners.json", counts);
                    Assert.All(counts, count => Assert.True(count > 100, $"Missing edge-anchored corner artwork: {string.Join(",", counts)}"));
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static void CheckTabs(DocumentTabStripItem[] tabs, double scaling)
    {
        foreach (var tab in tabs)
        {
            var body = Body(tab);
            var header = Header(tab);
            var ink = Assert.IsAssignableFrom<ISolidColorBrush>(header.GetVisualDescendants().OfType<TextBlock>().Single().Foreground).Color;
            var background = new Border { Width = body.Bounds.Width, Height = body.Bounds.Height, Background = body.Background };
            background.Measure(body.Bounds.Size); background.Arrange(new Rect(body.Bounds.Size));
            using var pixels = Render(background, scaling);
            foreach (var xFraction in new[] { .3, .5, .7 })
            foreach (var yFraction in new[] { .3, .5, .7 })
            {
                var sample = pixels.GetPixel((int)(pixels.Width * xFraction), (int)(pixels.Height * yFraction));
                var opacity = sample.Alpha / 255d;
                var colour = Color.FromRgb((byte)(sample.Red * opacity + IsekaiTheme.Palette.Workspace.R * (1 - opacity)),
                    (byte)(sample.Green * opacity + IsekaiTheme.Palette.Workspace.G * (1 - opacity)),
                    (byte)(sample.Blue * opacity + IsekaiTheme.Palette.Workspace.B * (1 - opacity)));
                Assert.True(ScientificIconPalette.Contrast(ink, colour) >= 4.5,
                    $"Unreadable native tab (selected={tab.IsSelected}): {ink} on {sample} at {xFraction},{yFraction}");
            }
            var close = tab.GetVisualDescendants().OfType<ContentPresenter>()
                .Where(p => p.Name == "PART_ClosePresenter" && p.IsEffectivelyVisible)
                .SelectMany(p => p.GetVisualDescendants().OfType<Button>()).SingleOrDefault();
            if (close is not null)
            {
                Assert.True(close.IsHitTestVisible);
                var closeStart = close.TranslatePoint(default, tab)!.Value.X;
                var headerStart = header.TranslatePoint(default, tab)!.Value.X;
                Assert.True(headerStart + header.Bounds.Width <= closeStart + 1);
            }
        }
    }

    private static Border Body(DocumentTabStripItem tab) => tab.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_TabBody");
    private static ContentPresenter Header(DocumentTabStripItem tab) => tab.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_HeaderPresenter");
    private static SKBitmap Render(Control control, double scaling)
    {
        using var target = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(control.Bounds.Width * scaling), (int)Math.Ceiling(control.Bounds.Height * scaling)), new Vector(96 * scaling, 96 * scaling));
        target.Render(control);
        using var stream = new MemoryStream(); target.Save(stream, PngBitmapEncoderOptions.Default); stream.Position = 0;
        return SKBitmap.Decode(stream);
    }
    private static SKBitmap CapturePixels(Window window)
    {
        using var target = window.CaptureRenderedFrame(); Assert.NotNull(target);
        using var stream = new MemoryStream(); target.Save(stream, PngBitmapEncoderOptions.Default); stream.Position = 0;
        return SKBitmap.Decode(stream);
    }
    private static void Tick(Window window)
    {
        for (var n = 0; n < 3; n++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    }
    private static string? DirectoryPath => Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_MATERIAL_SIZING_CAPTURE_DIR");
    private static void Save(Window window, string name)
    {
        if (DirectoryPath is not { } dir) return;
        Directory.CreateDirectory(dir);
        using var image = window.CaptureRenderedFrame(); Assert.NotNull(image);
        image.Save(Path.Combine(dir, name), PngBitmapEncoderOptions.Default);
    }
    private static void Write(string name, object value)
    {
        if (DirectoryPath is not { } dir) return;
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }
}
