using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class AboutWindowTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    [Theory]
    [InlineData("Light", 13)]
    [InlineData("Dark", 13)]
    [InlineData("Light", 18)]
    [InlineData("Dark", 18)]
    public async Task AboutShowsCurrentVersionBelowProductNameAndCloses(string theme, double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AboutWindowTests));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            DisplayTypography.Configure(new AppSettings { FontSize = fontSize });
            using var stream = AssetLoader.Open(new Uri("avares://OpticalSystemDesign.App/Assets/Author.jpg"));
            using var portrait = new Bitmap(stream);
            var owner = new Window { Width = 800, Height = 600 };
            var dialog = global::OpticalSystemDesign.App.MainWindow.CreateAboutDialog(portrait);
            DisplayTypography.Apply(dialog);
            owner.Show();
            try
            {
                var completion = dialog.ShowDialog(owner);
                Dispatcher.UIThread.RunJobs();
                dialog.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();

                var texts = dialog.GetVisualDescendants().OfType<TextBlock>().ToArray();
                var version = Assert.Single(texts, text => text.Name == "AboutVersion");
                var title = Assert.Single(texts, text => text.Name == "AboutProductName");
                var publisher = Assert.Single(texts, text => text.Name == "AboutPublisher");
                var runtime = Assert.Single(texts, text => text.Name == "AboutRuntime");
                var close = Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "关闭"));

                Assert.Equal($"版本 {ProductVersionInfo.Current}", version.Text);
                Assert.True(version.IsEffectivelyVisible);
                var position = version.TranslatePoint(default, dialog)!.Value;
                Assert.True(position.Y >= title.TranslatePoint(default, dialog)!.Value.Y + title.Bounds.Height);
                Assert.True(position.Y + version.Bounds.Height <= publisher.TranslatePoint(default, dialog)!.Value.Y);
                Assert.True(runtime.TranslatePoint(default, dialog)!.Value.Y + runtime.Bounds.Height <= close.TranslatePoint(default, dialog)!.Value.Y);
                Assert.True(position.X >= 0 && position.X + version.Bounds.Width <= dialog.ClientSize.Width);
                Assert.True(version.Bounds.Height > 0);

                var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_ABOUT_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var bitmap = new RenderTargetBitmap(new PixelSize((int)dialog.Width * 2, (int)dialog.Height * 2), new Vector(192, 192));
                    bitmap.Render(dialog);
                    bitmap.Save(Path.Combine(directory, $"about-{theme}-{fontSize:0}.png"), PngBitmapEncoderOptions.Default);
                }

                close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await completion;
                Assert.False(dialog.IsVisible);
            }
            finally
            {
                dialog.Close();
                owner.Close();
                DisplayTypography.Configure(new AppSettings());
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SplashUsesTheSameProductVersion()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AboutWindowTests));
        await session.Dispatch(() =>
        {
            var splash = new global::OpticalSystemDesign.App.SplashWindow();
            try
            {
                splash.Show();
                splash.UpdateLayout();
                Assert.Contains(splash.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == $"Version {ProductVersionInfo.Current}");
            }
            finally { splash.Close(); }
        }, CancellationToken.None);
    }
}
