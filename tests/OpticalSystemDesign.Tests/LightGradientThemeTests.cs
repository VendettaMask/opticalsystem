using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;
using SkiaSharp;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class LightGradientThemeTests
{
    [Fact]
    public void BackdropIsRelativeAndHeaderInkReadableWhileDataSurfacesStayWhite()
    {
        var resources = ThemeRegistry.FromSettings("Light").BuildResources();
        var gradient = Assert.IsAssignableFrom<ILinearGradientBrush>(resources[ThemeResourceBindings.WorkbenchBackdrop]);
        Assert.Equal(RelativeUnit.Relative, gradient.StartPoint.Unit);
        Assert.Equal(RelativeUnit.Relative, gradient.EndPoint.Unit);
        Assert.Equal(new Point(0, 1), gradient.EndPoint.Point);
        Assert.Equal(new[] { 0d, .23, .48, .62, 1 }, gradient.GradientStops.Select(stop => stop.Offset));
        Assert.InRange(BlueThemeTokens.BackdropFadeFraction, .45, .5);
        foreach (var stop in gradient.GradientStops)
            Assert.True(Contrast(BlueThemeTokens.TitleText, stop.Color) >= 4.5);
        Assert.True(IconBackgroundSampling.TrySample(gradient, new Point(20, 20), new Size(100, 100), out var compact));
        Assert.True(IconBackgroundSampling.TrySample(gradient, new Point(60, 100), new Size(300, 500), out var large));
        Assert.Equal(compact, large);
        Assert.True(Contrast(BlueThemeTokens.HeaderMenuText, compact) >= 4.5);
        foreach (var key in new[] { ThemeResourceBindings.Surface, ThemeResourceBindings.PlotBackground, ThemeResourceBindings.SceneBackground })
            Assert.Equal(Colors.White, Colour(resources[key]));
        var glass = Assert.IsAssignableFrom<ILinearGradientBrush>(resources[ThemeResourceBindings.SidebarSectionSurface]);
        Assert.All(glass.GradientStops, stop => Assert.InRange(stop.Color.A, (byte)1, (byte)254));
        Assert.Equal(Colors.Transparent, Colour(resources[ThemeResourceBindings.SidebarSectionContent]));
        foreach (var theme in ThemeRegistry.ConcreteThemes.Where(theme => theme.SettingsValue != "Light"))
        {
            var other = theme.BuildResources();
            Assert.Equal(theme.Palette!.Workspace, Colour(other[ThemeResourceBindings.WorkbenchBackdrop]));
            Assert.Equal(theme.Palette.Workspace, Colour(other[ThemeResourceBindings.ShellBackground]));
            Assert.Equal(theme.Palette.Surface, Colour(other[ThemeResourceBindings.SidebarSectionSurface]));
            Assert.Equal(theme.Palette.Surface, Colour(other[ThemeResourceBindings.SidebarSectionContent]));
        }
    }

    [Theory]
    [InlineData(1600, 1000, 13)]
    [InlineData(900, 700, 18)]
    [InlineData(640, 500, 13)]
    public async Task RealWindowKeepsOneContinuousBackdropAcrossResizeAndThemeChanges(
        double width, double height, double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(async () =>
        {
            using var workbench = WorkbenchApplication.Create();
            await workbench.Documents.OpenAsync(DoubleGaussSfrTests.StandardLens);
            var original = workbench.Documents.GetRecoveryState();
            var settings = new AppSettings { Theme = "Light", FontSize = fontSize, WindowWidth = width, WindowHeight = height };
            var store = new WorkspaceSessionStore(Path.Combine(Path.GetTempPath(), "light-gradient", Guid.NewGuid().ToString("N")));
            var window = new MainWindow(settings, workbench, store);
            var ready = false;
            window.StartupCompleted += (_, _) => ready = true;
            try
            {
                window.Show();
                for (var attempt = 0; attempt < 200 && !ready; attempt++) { await Task.Delay(10); Tick(window); }
                Assert.True(ready);
                var root = Assert.IsType<DockPanel>(window.Content);
                foreach (var size in new[] { (width, height), (width + 140, height + 100), (width, height) })
                {
                    window.Width = size.Item1; window.Height = size.Item2; Tick(window);
                    Assert.Same(BlueThemeTokens.WorkbenchGradient, root.Background);
                    Assert.Equal(original, workbench.Documents.GetRecoveryState());
                    using var image = window.CaptureRenderedFrame();
                    Assert.NotNull(image);
                    using var stream = new MemoryStream(); image.Save(stream, PngBitmapEncoderOptions.Default);
                    stream.Position = 0;
                    using var pixels = SKBitmap.Decode(stream);
                    if (size == (width, height)) Capture(image, $"workbench-{width}x{height}-{fontSize}.png");
                    AssertSidebarBackdrop(window, root, pixels);
                    AssertNoBrightHeaderSeams(window, pixels);
                    var upper = Sample(.2); var lower = Sample(.72);
                    Assert.True(upper.Blue > upper.Red + 10, $"Expected blue upper backdrop, got {upper}.");
                    Assert.True(lower.Red >= 245 && lower.Green >= 245 && lower.Blue >= 245,
                        $"Expected white lower backdrop, got {lower}.");
                    SKColor Sample(double fraction)
                    {
                        // The existing five-DIP outer gutter reveals the window backdrop.
                        var point = root.TranslatePoint(new Point(2, root.Bounds.Height * fraction), window)!.Value;
                        return pixels.GetPixel((int)point.X, (int)point.Y);
                    }
                }
                foreach (var theme in new[] { "Dark", "Lumen", "Light" })
                {
                    ThemeApplicationService.Apply(Avalonia.Application.Current!, theme); Tick(window);
                    Assert.Equal(original, workbench.Documents.GetRecoveryState());
                    if (theme == "Light") Assert.Same(BlueThemeTokens.WorkbenchGradient, root.Background);
                    else Assert.IsAssignableFrom<ISolidColorBrush>(root.Background);
                }
                var sidebar = Assert.Single(window.GetVisualDescendants().OfType<SystemPropertiesPanel>());
                var sidebarScroll = Assert.IsType<ScrollViewer>(sidebar.Content);
                foreach (var offset in new[] { Math.Min(96, Math.Max(0, sidebarScroll.Extent.Height - sidebarScroll.Viewport.Height)), 0 })
                {
                    sidebarScroll.Offset = new Vector(0, offset); Tick(window);
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    using var data = new MemoryStream(); frame.Save(data, PngBitmapEncoderOptions.Default); data.Position = 0;
                    using var pixels = SKBitmap.Decode(data);
                    AssertSidebarBackdrop(window, root, pixels);
                    AssertNoBrightHeaderSeams(window, pixels);
                    Assert.Equal(original, workbench.Documents.GetRecoveryState());
                    if (offset > 0) Capture(frame, $"workbench-{width}x{height}-{fontSize}-scrolled.png");
                }
                var title = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Name == "MainWindowTitle");
                Assert.Equal(BlueThemeTokens.TitleText, Colour(title.Foreground));
                Assert.NotEmpty(window.GetVisualDescendants().OfType<LensEditorPanel>());
                Assert.All(window.GetVisualDescendants().Where(visual => visual is TextBlock or DataGrid),
                    visual => Assert.Null(visual.Effect));
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    private static void AssertNoBrightHeaderSeams(Window window, SKBitmap pixels)
    {
        var tabs = Assert.Single(window.GetVisualDescendants().OfType<TabControl>(),
            control => control.Classes.Contains("ribbon-tabs"));
        var ribbon = tabs.GetVisualAncestors().OfType<Border>().First();
        var title = Assert.Single(window.GetVisualDescendants().OfType<MainWindowTitleBar>());
        foreach (var surface in new[] { ribbon, title })
        foreach (var fraction in new[] { .25, .55, .85 })
        {
            // Compare actual rendered rows above the text/icons. A hard white
            // stroke makes the first row much brighter than the glass below it.
            var top = surface.TranslatePoint(new Point(surface.Bounds.Width * fraction, .5), window)!.Value;
            var below = surface.TranslatePoint(new Point(surface.Bounds.Width * fraction, 3.5), window)!.Value;
            var edge = pixels.GetPixel((int)top.X, (int)top.Y);
            var fill = pixels.GetPixel((int)below.X, (int)below.Y);
            Assert.True(edge.Red - fill.Red <= 8 && edge.Green - fill.Green <= 8 && edge.Blue - fill.Blue <= 8,
                $"Bright header seam on {surface.GetType().Name} at {top}: {edge} over {fill}.");
        }
    }

    private static void AssertSidebarBackdrop(Window window, DockPanel root, SKBitmap pixels)
    {
        var sidebar = Assert.Single(window.GetVisualDescendants().OfType<SystemPropertiesPanel>());
        var scroll = Assert.IsType<ScrollViewer>(sidebar.Content);
        var sections = Assert.IsType<StackPanel>(scroll.Content);
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_LIGHT_GRADIENT_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            var chain = sidebar.GetVisualAncestors().Prepend(sidebar).OfType<Control>().Select(control => new
            {
                Type = control.GetType().Name, control.Name,
                control.Bounds.X, control.Bounds.Y, control.Bounds.Width, control.Bounds.Height,
                Template = (control.TemplatedParent as Control)?.GetType().Name,
                Background = (control switch
                {
                    Border border => border.Background,
                    Panel panel => panel.Background,
                    TemplatedControl templated => templated.Background,
                    _ => null
                })?.ToString()
            });
            File.WriteAllText(Path.Combine(directory, $"sidebar-layers-{window.Width}x{window.Height}.json"),
                System.Text.Json.JsonSerializer.Serialize(chain, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        foreach (var fraction in new[] { .15, .4, .8 })
            Check(sidebar, new Point(sections.Margin.Left / 2, sidebar.Bounds.Height * fraction));
        // The section gap is inside the sidebar, rather than the window's outer gutter.
        var first = Assert.IsType<Border>(sections.Children[0]);
        var gap = first.TranslatePoint(new Point(first.Bounds.Width / 2, first.Bounds.Height + sections.Spacing / 2), sidebar)!.Value;
        if (gap.Y > 0 && gap.Y < sidebar.Bounds.Height) Check(sidebar, gap);
        // Check the large section body too: opaque white here would hide the
        // blue everywhere except the thin gaps and recreate the reported defect.
        var body = Assert.IsType<Border>(Assert.IsType<StackPanel>(first.Child).Children[1]);
        var paddingPoint = body.TranslatePoint(new Point(body.Padding.Left / 2, body.Padding.Top / 2), sidebar)!.Value;
        if (paddingPoint.Y > 0 && paddingPoint.Y < sidebar.Bounds.Height)
        {
            var pointInCard = sidebar.TranslatePoint(paddingPoint, first)!.Value;
            Check(sidebar, paddingPoint, SampleTint(Assert.IsAssignableFrom<ILinearGradientBrush>(first.Background), pointInCard, first.Bounds.Size));
        }
        var input = sidebar.GetVisualDescendants().OfType<NumericUpDown>().First(control => control.IsEffectivelyVisible);
        var textBox = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        var inputPoint = textBox.TranslatePoint(new Point(textBox.Bounds.Width - 5, textBox.Bounds.Height - 5), window)!.Value;
        var localInputPoint = window.TranslatePoint(inputPoint, sidebar)!.Value;
        if (localInputPoint.Y > 0 && localInputPoint.Y < sidebar.Bounds.Height)
        {
            var white = pixels.GetPixel((int)inputPoint.X, (int)inputPoint.Y);
            Assert.True(white.Red >= 250 && white.Green >= 250 && white.Blue >= 250, $"Input background became {white}.");
        }

        void Check(Control host, Point point, Color? overlay = null)
        {
            var windowPoint = host.TranslatePoint(point, window)!.Value;
            var x = (int)windowPoint.X; var y = (int)windowPoint.Y;
            var backdropPoint = window.TranslatePoint(new Point(x + .5, y + .5), root)!.Value;
            Assert.True(IconBackgroundSampling.TrySample(root.Background, backdropPoint, root.Bounds.Size, out var expected));
            if (overlay is { } tint)
            {
                var alpha = tint.A / 255d;
                expected = Color.FromRgb(Blend(tint.R, expected.R), Blend(tint.G, expected.G), Blend(tint.B, expected.B));
                byte Blend(byte foreground, byte background) => (byte)Math.Round(foreground * alpha + background * (1 - alpha));
            }
            var actual = pixels.GetPixel(x, y);
            Assert.True(Math.Abs(actual.Red - expected.R) <= 3 && Math.Abs(actual.Green - expected.G) <= 3 &&
                Math.Abs(actual.Blue - expected.B) <= 3,
                $"Sidebar backdrop at ({x}, {y}) is {actual}; expected shared window gradient {expected}.");
        }
    }

    private static Color SampleTint(ILinearGradientBrush brush, Point point, Size size)
    {
        var start = brush.StartPoint.ToPixels(size); var vector = brush.EndPoint.ToPixels(size) - start;
        var delta = point - start;
        var t = Math.Clamp((delta.X * vector.X + delta.Y * vector.Y) / (vector.X * vector.X + vector.Y * vector.Y), 0, 1);
        var stops = brush.GradientStops.OrderBy(stop => stop.Offset).ToArray();
        var index = Math.Clamp(Array.FindIndex(stops, stop => stop.Offset >= t), 1, stops.Length - 1);
        var first = stops[index - 1]; var last = stops[index];
        var amount = (t - first.Offset) / (last.Offset - first.Offset);
        byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return Color.FromArgb(Mix(first.Color.A, last.Color.A), Mix(first.Color.R, last.Color.R),
            Mix(first.Color.G, last.Color.G), Mix(first.Color.B, last.Color.B));
    }

    private static void Capture(Bitmap image, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_LIGHT_GRADIENT_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        image.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
    private static void Tick(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
    private static Color Colour(object? value) => Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    private static double Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double Linear(byte b) { var v = b / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
            return .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        }
        var first = L(a); var second = L(b);
        return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
    }
}
