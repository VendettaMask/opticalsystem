using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Services.Updates;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class SoftwareUpdateWindowTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_UPDATE_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData("Light", 13, "zh-CN")]
    [InlineData("Dark", 18, "en-US")]
    [InlineData("Light", 18, "ja-JP")]
    public async Task WindowShowsVersionAndManualResultsWithoutAnyAutomaticCheck(string theme, double fontSize, string language)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SoftwareUpdateWindowTests));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            UiLocalization.InstallGlobalHooks();
            UiLocalization.Apply(language);
            DisplayTypography.Configure(new AppSettings { FontSize = fontSize });
            var release = GitHubReleaseUpdateService.Parse(Encoding.UTF8.GetBytes(ReleaseUpdateTests.Release("99.0.0").ToJsonString()), ProductVersionInfo.Current, "win-x64");
            var service = new FixedService(release);
            Uri? opened = null;
            var window = new SoftwareUpdateWindow(service, uri => { opened = uri; return Task.FromResult(true); });
            try
            {
                window.Show();
                Tick(window);
                Assert.Equal(0, service.Calls);
                Assert.Equal(ProductVersionInfo.Current, Named<TextBlock>(window, "InstalledVersion").Text);
                var upgrade = Named<Button>(window, "OnlineUpgradeReserved");
                Assert.False(upgrade.IsEnabled);
                Assert.NotNull(ToolTip.GetTip(upgrade));
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(upgrade)));
                var check = Named<Button>(window, "CheckForUpdates");
                Assert.Contains("accent", check.Classes);
                Assert.Equal(UiLocalization.Text("检查更新"), check.Content);
                await window.CheckAsync();
                Tick(window);
                Assert.Equal(1, service.Calls);
                Assert.Equal("99.0.0", Named<TextBlock>(window, "LatestVersion").Text);
                Assert.Equal(UiLocalization.Text("发现新版本，可在 GitHub 发布页下载对应安装包。"), Named<TextBlock>(window, "UpdateStatus").Text);
                Assert.True(check.IsEnabled);
                Assert.Null(AutomationProperties.GetHelpText(check));
                Named<Button>(window, "UpdateReleasePage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(release.Release!.PageUri, opened);
                Assert.DoesNotContain(".exe", opened!.AbsoluteUri);
                foreach (var name in new[] { "CheckForUpdates", "UpdateReleasePage", "OnlineUpgradeReserved", "CloseUpdateWindow" })
                {
                    var button = Named<Button>(window, name);
                    var position = button.TranslatePoint(default, window)!.Value;
                    Assert.True(button.Bounds.Height > 0);
                    Assert.InRange(position.X + button.Bounds.Width, 0, window.ClientSize.Width);
                    Assert.InRange(position.Y + button.Bounds.Height, 0, window.ClientSize.Height);
                }
                var capture = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_UPDATE_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(capture))
                {
                    Directory.CreateDirectory(capture);
                    using var bitmap = new RenderTargetBitmap(new((int)window.Width * 2, (int)window.Height * 2), new(192, 192));
                    bitmap.Render(window);
                    var path = Path.Combine(capture, $"update-{theme}-{language}.png");
                    bitmap.Save(path, PngBitmapEncoderOptions.Default);
                    Assert.True(new FileInfo(path).Length > 100);
                }
            }
            finally
            {
                window.Close();
                DisplayTypography.Configure(new AppSettings());
                UiLocalization.Apply(UiLocalization.Chinese);
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DuplicateChecksAreSuppressedAndClosingCancelsTheRequest()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SoftwareUpdateWindowTests));
        await session.Dispatch(async () =>
        {
            var service = new PendingService();
            var window = new SoftwareUpdateWindow(service);
            window.Show();
            var pending = window.CheckAsync();
            Assert.False(Named<Button>(window, "CheckForUpdates").IsEnabled);
            await window.CheckAsync();
            Assert.Equal(1, service.Calls);
            window.Close();
            await pending;
            Assert.True(service.Token.IsCancellationRequested);
            await window.CheckAsync();
            Assert.Equal(1, service.Calls);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FailedRecheckClearsOldVersionAndReleaseTarget()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SoftwareUpdateWindowTests));
        await session.Dispatch(async () =>
        {
            var service = new FixedService(new(UpdateCheckStatus.UpdateAvailable,
                new("99.0.0", new($"https://github.com/{ProductReleaseSource.Repository}/releases/tag/v99.0.0"), new("installer.exe", new("https://example.invalid"), 1))));
            Uri? opened = null;
            var window = new SoftwareUpdateWindow(service, uri => { opened = uri; return Task.FromResult(true); });
            try
            {
                window.Show();
                await window.CheckAsync();
                service.Result = new(UpdateCheckStatus.NetworkError);
                await window.CheckAsync();
                Assert.Equal("—", Named<TextBlock>(window, "LatestVersion").Text);
                Assert.Equal(string.Empty, Named<TextBlock>(window, "ReleaseInstaller").Text);
                Named<Button>(window, "UpdateReleasePage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(ProductReleaseSource.ReleasesPage, opened);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private sealed class FixedService(UpdateCheckResult result) : IReleaseUpdateService
    {
        public int Calls { get; private set; }
        public UpdateCheckResult Result { get; set; } = result;
        public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken) { Calls++; return Task.FromResult(Result); }
    }

    private sealed class PendingService : IReleaseUpdateService
    {
        public int Calls { get; private set; }
        public CancellationToken Token { get; private set; }
        public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
        {
            Calls++;
            Token = cancellationToken;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new(UpdateCheckStatus.NetworkError);
        }
    }

    private static T Named<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
    private static void Tick(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
}
