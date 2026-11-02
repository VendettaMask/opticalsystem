using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class AnalysisToolbarArtworkTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseHarfBuzz();

    [Theory]
    [InlineData("Light", 13, "zh-CN")]
    [InlineData("Dark", 13, "zh-CN")]
    [InlineData("Lumen", 13, "zh-CN")]
    [InlineData("Pixel", 13, "zh-CN")]
    [InlineData("Isekai", 13, "zh-CN")]
    [InlineData("Light", 18, "en-US")]
    [InlineData("Dark", 18, "ja-JP")]
    public async Task ApprovedVectorsKeepMenusReachableAcrossThemesAndNarrowWidths(string theme, double fontSize, string language)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisToolbarArtworkTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            DisplayTypography.Configure(new AppSettings { FontSize = fontSize });
            UiLocalization.Apply(language);
            var executed = new List<string>();
            var actions = new ActionManager();
            foreach (var command in WorkbenchAnalysisCatalog.AllRibbonCommands)
                actions.Register(command.Id, command.Label, command.Group, () => executed.Add(command.Id));
            var page = Assert.IsType<ScrollViewer>(MainWindow.BuildSequentialAnalysisRibbonPage(actions));
            var window = new Window { Width = 1280, Height = 100, Content = page };
            UiLocalization.Attach(window); DisplayTypography.Apply(window);
            try
            {
                window.Show(); Tick(window);
                var buttons = page.GetVisualDescendants().OfType<DropDownButton>().ToArray();
                Assert.Equal(9, buttons.Length);
                Assert.Equal(3, page.GetVisualDescendants().OfType<Border>().Count(b => b.Name == "RibbonGroupDivider"));
                Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, page.HorizontalScrollBarVisibility);
                for (var index = 0; index < buttons.Length; index++)
                {
                    var button = buttons[index]; var menu = WorkbenchAnalysisCatalog.RibbonMenus[index];
                    var grid = Assert.IsType<Grid>(button.Content);
                    var icon = Assert.Single(grid.Children.OfType<AnalysisCategoryIcon>());
                    Assert.Equal(menu.IconName, icon.SemanticName);
                    Assert.Empty(grid.Children.OfType<LocalIcon>());
                    Assert.Single(grid.Children.OfType<Polygon>());
                    Assert.Equal(DisplayTypography.Scale(22), icon.Width, 12);
                    Assert.InRange(Math.Abs(icon.Bounds.Width - icon.Width), 0, 1); // Native layout rounds to display pixels.
                    Assert.True(icon.Bounds.Right <= grid.Children.OfType<TextBlock>().Single().Bounds.Left);
                    AssertVectorDrawn(icon);
                    var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
                    Assert.Equal(menu.CommandIds.Count, flyout.Items.Count);
                    Assert.Equal(menu.CommandIds.Where(id => id != "-").Select(id => "ribbon-" + id),
                        flyout.Items.OfType<MenuItem>().Select(AutomationProperties.GetAutomationId));
                    Assert.Equal(menu.CommandIds.Count(id => id == "-"), flyout.Items.OfType<Separator>().Count());
                }
                Capture(window, $"analysis-toolbar-{theme}-{language}-{fontSize:0}");
                window.Width = 480; Tick(window);
                Assert.True(page.Extent.Width > page.Viewport.Width);
                page.Offset = new Vector(page.Extent.Width, 0); Tick(window);
                var last = buttons[^1].TranslatePoint(new Point(buttons[^1].Bounds.Width / 2, buttons[^1].Bounds.Height / 2), window)!.Value;
                Assert.InRange(last.X, 0, window.ClientSize.Width);
                window.Width = 1280; Tick(window);
                foreach (var button in buttons)
                {
                    button.BringIntoView(); Tick(window);
                    var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                    window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Tick(window);
                    var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
                    Assert.True(flyout.IsOpen);
                    Assert.Empty(executed);
                    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                    window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null); Tick(window);
                    Assert.False(flyout.IsOpen);
                }
                // Exercise the retained command binding, independently of menu opening.
                var mtf = Assert.IsType<MenuFlyout>(buttons[5].Flyout);
                mtf.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.Equal(new[] { "analysis-mtf" }, executed);
                var nsc = MainWindow.BuildAnalysisMenuButton(WorkbenchAnalysisCatalog.NonSequentialRibbonMenus[0], actions);
                Assert.Single(Assert.IsType<Grid>(nsc.Content).Children.OfType<LocalIcon>());
                Assert.Empty(Assert.IsType<Grid>(nsc.Content).Children.OfType<AnalysisCategoryIcon>());
            }
            finally
            {
                window.Close(); UiLocalization.Apply(UiLocalization.Chinese); DisplayTypography.Configure(new AppSettings());
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RuntimeThemeSwitchRecolorsTheSameVectorWithoutChangingGeometry()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisToolbarArtworkTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            var icon = new AnalysisCategoryIcon("chart-no-axes-combined") { Width = 32, Height = 32 };
            var window = new Window { Width = 100, Height = 100, Content = icon };
            try
            {
                window.Show(); Tick(window);
                var light = VectorPixels(icon); var bounds = icon.Bounds;
                ThemeApplicationService.Apply(Avalonia.Application.Current!, "Dark"); Tick(window);
                var dark = VectorPixels(icon);
                Assert.False(light.SequenceEqual(dark));
                Assert.Equal(bounds, icon.Bounds);
                Assert.Equal(light.Where((_, index) => index % 4 == 3), dark.Where((_, index) => index % 4 == 3));
                ThemeApplicationService.Apply(Avalonia.Application.Current!, "Lumen"); Tick(window);
                Assert.Equal(dark, VectorPixels(icon));
                ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light"); Tick(window);
                Assert.Equal(light, VectorPixels(icon));
                Assert.Same(icon, window.Content);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static void AssertVectorDrawn(Control icon)
    {
        var pixels = VectorPixels(icon);
        Assert.True(Enumerable.Range(0, pixels.Length / 4).Count(index => pixels[index * 4 + 3] != 0) > 20,
            "Approved SVG must render actual nonempty vector geometry.");
    }
    private static byte[] VectorPixels(Control icon)
    {
        using var image = new RenderTargetBitmap(new PixelSize(64, 64)); image.Render(icon);
        var pixels = new byte[64 * 64 * 4]; var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { image.CopyPixels(new PixelRect(0, 0, 64, 64), handle.AddrOfPinnedObject(), pixels.Length, 64 * 4); }
        finally { handle.Free(); }
        return pixels;
    }

    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
    }
    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_ANALYSIS_TOOLBAR_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame(); image!.Save(System.IO.Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
