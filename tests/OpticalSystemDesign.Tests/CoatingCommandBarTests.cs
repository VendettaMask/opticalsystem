using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using CoatingWindow = OpticalSystemDesign.App.Laboratories.CoatingDesign.MainWindow;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class CoatingCommandBarTests
{
    public static AppBuilder BuildAvaloniaApp() => DiagramViewportFitTests.BuildAvaloniaApp();

    [Theory]
    [InlineData("Light", "zh-CN", 1200, 13)]
    [InlineData("Light", "ja-JP", 820, 18)]
    [InlineData("Dark", "en-US", 1200, 13)]
    [InlineData("Lumen", "ja-JP", 1200, 13)]
    [InlineData("Isekai", "en-US", 1200, 13)]
    [InlineData("Pixel", "zh-CN", 1200, 13)]
    public async Task SingleThemeCommandBarKeepsMenuActionsAndBusyCancellation(string theme, string language, int width, int fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CoatingCommandBarTests));
        await session.Dispatch(() =>
        {
            var settingsDirectory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY");
            var captureRoot = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_COATING_CHROME_CAPTURE_DIR")
                ?? Path.Combine(Path.GetTempPath(), "OpticalSystemDesign-coating-chrome");
            Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", Path.Combine(captureRoot, "settings", Guid.NewGuid().ToString("N")));
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            UiLocalization.Apply(language);
            DisplayTypography.Configure(new() { FontSize = fontSize, Theme = theme });
            var window = new CoatingWindow { Width = width, Height = 850 };
            try
            {
                window.Show(); Tick(window);
                var fingerprint = window.CurrentExperiment.Fingerprint();
                var header = window.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "LaboratoryHeader");
                var main = window.GetVisualDescendants().OfType<Grid>().Single(c => c.Name == "CoatingResults");
                Assert.Single(main.Children);
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => button.Content is "1 生成膜系" or "2 优化" or "结构搜索" or "计算 / 复验");
                var menus = header.GetVisualDescendants().OfType<DropDownButton>().Select(button => button.Flyout)
                    .OfType<MenuFlyout>().ToArray();
                var commands = menus.SelectMany(menu => menu.Items.OfType<MenuItem>())
                    .Where(item => item.Name?.StartsWith("CoatingCompute_", StringComparison.Ordinal) == true).ToArray();
                Assert.Equal(new[] { "evaluate", "generate", "optimize", "search" },
                    commands.Select(item => item.Name!["CoatingCompute_".Length..]).Order().ToArray());
                Assert.All(commands, item => Assert.True(item.IsEnabled));
                var status = window.GetVisualDescendants().OfType<OperationStatusBar>().Single();
                Assert.False(status.IsVisible);
                if (theme == "Light")
                {
                    using var image = window.CaptureRenderedFrame(); Assert.NotNull(image);
                    var point = header.TranslatePoint(new Point(header.Bounds.Width - 4, header.Bounds.Height / 2), window)!.Value;
                    var pixel = new byte[4]; var pinned = GCHandle.Alloc(pixel, GCHandleType.Pinned);
                    try { image.CopyPixels(new PixelRect((int)point.X, (int)point.Y, 1, 1), pinned.AddrOfPinnedObject(), 4, 4); }
                    finally { pinned.Free(); }
                    var red = pixel[image.Format == PixelFormat.Rgba8888 ? 0 : 2];
                    var blue = pixel[image.Format == PixelFormat.Rgba8888 ? 2 : 0];
                    Assert.True(blue > red + 20, $"The toolbar should reveal blue: {image.Format}={string.Join(',', pixel)}");
                }
                Capture(window, $"{theme}-{width}");
                var busy = typeof(CoatingWindow).GetMethod("SetBusy", BindingFlags.Instance | BindingFlags.NonPublic)!;
                busy.Invoke(window, [true]); Tick(window);
                Assert.All(commands, item => Assert.False(item.IsEnabled));
                Assert.True(status.IsVisible);
                var cancel = status.GetVisualDescendants().OfType<Button>().Single(button => button.IsVisible);
                var revision = typeof(CoatingWindow).GetField("_uiRevision", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var before = (long)revision.GetValue(window)!;
                cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                Assert.True((long)revision.GetValue(window)! > before);
                busy.Invoke(window, [false]); Tick(window);
                Assert.All(commands, item => Assert.True(item.IsEnabled));
                Assert.False(status.IsVisible);
                foreach (var nextTheme in new[] { "Dark", "Light", theme })
                {
                    ThemeApplicationService.Apply(Avalonia.Application.Current!, nextTheme); Tick(window);
                    Assert.Equal(fingerprint, window.CurrentExperiment.Fingerprint());
                    Assert.Single(main.Children);
                }
            }
            finally
            {
                window.CloseAfterHostApproval();
                Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", settingsDirectory);
                DisplayTypography.Configure(new()); UiLocalization.Apply("zh-CN");
            }
        }, CancellationToken.None);
    }

    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_COATING_CHROME_CAPTURE_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame(); Assert.NotNull(image);
        image.Save(Path.Combine(directory, $"coating-command-bar-{name}.png"), PngBitmapEncoderOptions.Default);
    }
}
