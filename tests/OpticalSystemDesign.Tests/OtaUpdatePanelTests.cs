using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Services.Updates;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class OtaUpdatePanelTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());

    [Theory]
    [InlineData("zh-CN", "Light")][InlineData("en-US", "Dark")][InlineData("ja-JP", "Light")]
    public async Task ManualDownloadAndCanceledSaveAreLocalizedAndNeverAutoInstall(string language, string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(OtaUpdatePanelTests));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            UiLocalization.InstallGlobalHooks(); UiLocalization.Apply(language);
            var service = new FixedService(); var downloader = new FakeDownloader(); var installs = 0;
            using var panel = new SoftwareUpdatePanel(service, handoff: _ => { installs++; return Task.FromResult(false); },
                downloader: downloader, unavailableReason: () => null);
            var window = Show(panel);
            try
            {
                Assert.Equal(0, service.Calls);
                Assert.False(Named<Button>(panel, "DownloadUpdate").IsEnabled);
                Assert.False(string.IsNullOrEmpty(AutomationProperties.GetHelpText(Named<Button>(panel, "DownloadUpdate"))));
                await panel.CheckAsync(); Tick(window);
                var download = Named<Button>(panel, "DownloadUpdate");
                Assert.Equal(UiLocalization.Text("下载更新"), download.Content);
                Assert.True(download.IsEnabled); Assert.Null(AutomationProperties.GetHelpText(download));
                await panel.DownloadAsync(); Tick(window);
                Assert.Equal(1, downloader.Calls); Assert.Equal(0, installs);
                var install = Named<Button>(panel, "InstallUpdate");
                Assert.True(install.IsVisible); Assert.True(install.IsEnabled); Assert.Contains("accent", install.Classes);
                Assert.Equal(UiLocalization.Text("保存并重启更新"), install.Content);
                await panel.InstallAsync(); Tick(window);
                Assert.Equal(1, installs);
                Assert.Equal(UiLocalization.Text("更新已取消，未执行安装。"), Named<TextBlock>(panel, "OtaUpdateStatus").Text);
                Assert.True(File.Exists(downloader.Prepared!.InstallerPath));
            }
            finally
            {
                window.Close(); panel.Dispose();
                Assert.False(File.Exists(downloader.Prepared?.InstallerPath));
                UiLocalization.Apply(UiLocalization.Chinese);
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClosingCancelsDownloadsAndDuplicateChecksAreIgnored()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(OtaUpdatePanelTests));
        await session.Dispatch(async () =>
        {
            var service = new FixedService(); var downloader = new FakeDownloader { Pending = true };
            using var panel = new SoftwareUpdatePanel(service, handoff: _ => Task.FromResult(true), downloader: downloader, unavailableReason: () => null);
            var window = Show(panel);
            await panel.CheckAsync();
            var pending = panel.DownloadAsync();
            await panel.DownloadAsync(); await panel.CheckAsync();
            Assert.Equal(1, downloader.Calls); Assert.Equal(1, service.Calls);
            Assert.False(Named<Button>(panel, "CheckForUpdates").IsEnabled);
            panel.Dispose(); window.Close(); await pending;
            Assert.True(downloader.Token.IsCancellationRequested);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CancelDownloadEnablesRetryWithoutAnInstallAction()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(OtaUpdatePanelTests));
        await session.Dispatch(async () =>
        {
            var downloader = new FakeDownloader { Pending = true };
            using var panel = new SoftwareUpdatePanel(new FixedService(), handoff: _ => Task.FromResult(true), downloader: downloader, unavailableReason: () => null);
            var window = Show(panel);
            try
            {
                await panel.CheckAsync(); var pending = panel.DownloadAsync();
                Named<Button>(panel, "CancelUpdateDownload").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await pending; Tick(window);
                Assert.True(Named<Button>(panel, "DownloadUpdate").IsEnabled);
                Assert.False(Named<Button>(panel, "InstallUpdate").IsVisible);
                Assert.False(Named<ProgressBar>(panel, "UpdateDownloadProgress").IsVisible);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InstallationPreparationSuppressesRechecksAndDuplicateInstalls()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(OtaUpdatePanelTests));
        await session.Dispatch(async () =>
        {
            var service = new FixedService(); var completion = new TaskCompletionSource<bool>(); var calls = 0;
            using var panel = new SoftwareUpdatePanel(service, handoff: _ => { calls++; return completion.Task; }, downloader: new FakeDownloader(), unavailableReason: () => null);
            var window = Show(panel);
            try
            {
                await panel.CheckAsync(); await panel.DownloadAsync();
                var pending = panel.InstallAsync(); await panel.InstallAsync(); await panel.CheckAsync();
                Assert.Equal(1, calls); Assert.Equal(1, service.Calls);
                completion.SetResult(false); await pending;
                Assert.True(Named<Button>(panel, "CheckForUpdates").IsEnabled);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MissingSignatureOrUnsupportedInstallationNeverDownloads()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(OtaUpdatePanelTests));
        await session.Dispatch(async () =>
        {
            var service = new FixedService { HasSignature = false }; var downloader = new FakeDownloader();
            using var panel = new SoftwareUpdatePanel(service, handoff: _ => Task.FromResult(true), downloader: downloader, unavailableReason: () => null);
            var window = Show(panel);
            try
            {
                await panel.CheckAsync(); await panel.DownloadAsync(); Assert.Equal(0, downloader.Calls);
                Assert.False(Named<Button>(panel, "DownloadUpdate").IsEnabled);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private sealed class FixedService : IReleaseUpdateService
    {
        internal int Calls; internal bool HasSignature = true;
        public Task<UpdateCheckResult> CheckAsync(CancellationToken token)
        {
            Calls++; var update = OtaTestData.Update();
            return Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, new("99.0.0", ProductReleaseSource.ReleasesPage,
                new(update.Payload.Name, OtaManifest.DownloadUri(update.Payload), update.Payload.Size), HasSignature ? update : null)));
        }
    }
    private sealed class FakeDownloader : IOtaDownloader
    {
        internal int Calls; internal bool Pending; internal CancellationToken Token; internal PreparedOtaUpdate? Prepared;
        public async Task<PreparedOtaUpdate> DownloadAsync(SignedOtaUpdate update, string installedVersion, IProgress<double> progress, CancellationToken token)
        {
            Calls++; Token = token;
            if (Pending) await Task.Delay(Timeout.Infinite, token);
            var directory = Path.Combine(Path.GetTempPath(), "optical-ota-panel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory); Prepared = new(directory, update);
            await File.WriteAllBytesAsync(Prepared.InstallerPath, OtaTestData.InstallerBytes, token);
            return Prepared;
        }
    }
    private static Window Show(Control content)
    {
        var window = new Window { Width = 680, Height = 600, Content = content };
        UiLocalization.Attach(window); window.Show(); Tick(window); return window;
    }
    private static T Named<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
    private static void Tick(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
}

public sealed class OtaShutdownGuardTests
{
    [Theory]
    [InlineData(false, true, false, "save")][InlineData(true, false, false, "save,windows")]
    [InlineData(true, true, true, "save,windows,persist")]
    public async Task SaveAndWorkspaceCancellationStopBeforeClose(bool saved, bool windows, bool expected, string order)
    {
        var calls = new List<string>();
        var actual = await OtaShutdownGuard.PrepareAsync(
            () => { calls.Add("save"); return Task.FromResult(saved); },
            () => { calls.Add("windows"); return Task.FromResult(windows); },
            () => { calls.Add("persist"); return Task.CompletedTask; });
        Assert.Equal(expected, actual); Assert.Equal(order, string.Join(',', calls));
    }
    [Fact]
    public async Task FailedSessionPersistenceDoesNotApproveClose() =>
        await Assert.ThrowsAsync<IOException>(() => OtaShutdownGuard.PrepareAsync(() => Task.FromResult(true),
            () => Task.FromResult(true), () => Task.FromException(new IOException("fixture"))));
}
