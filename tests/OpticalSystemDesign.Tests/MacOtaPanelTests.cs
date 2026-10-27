using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Services.Updates;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class MacOtaPanelTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });

    [Theory]
    [InlineData("zh-CN", false)]
    [InlineData("en-US", true)]
    [InlineData("ja-JP", true)]
    public async Task UpdateNeedsExplicitDownloadAndSuccessfulClosePreparation(string language, bool approve)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MacOtaPanelTests));
        await session.Dispatch(async () =>
        {
            UiLocalization.Apply(language);
            var engine = new Engine(); var closes = 0; var prompts = 0;
            using var panel = new MacSoftwareUpdatePanel(engine, () => { prompts++; return Task.FromResult(approve); }, () => closes++);
            var window = new Window { Width = 680, Height = 400, Content = panel };
            window.Show();
            try
            {
                Assert.Equal(0, engine.Checks); Assert.Equal(0, engine.Downloads); Assert.Equal(0, engine.Installs);
                Click(panel, "CheckForUpdates"); Assert.Equal(1, engine.Checks); Assert.True(engine.IncludeTests);
                engine.Set(MacOtaState.Available);
                Click(panel, "DownloadUpdate"); Assert.Equal(1, engine.Downloads);
                Assert.Equal(0, engine.Installs);
                engine.Set(MacOtaState.Ready);
                await panel.InstallAsync();
                Assert.Equal(1, prompts); Assert.Equal(approve ? 1 : 0, engine.Installs); Assert.Equal(approve ? 1 : 0, closes);
                Assert.Equal(approve ? MacOtaState.Installing : MacOtaState.Ready, engine.Status.State);
                Assert.Equal(UiLocalization.Text(approve ? "正在安装更新并重启…" : "更新校验通过。安装前可选择保存或不保存当前修改；取消则不安装。"), Find<TextBlock>(panel,"OtaUpdateStatus").Text);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData((int)MacOtaState.Checking)]
    [InlineData((int)MacOtaState.Downloading)]
    [InlineData((int)MacOtaState.Ready)]
    public async Task CancelNeverStartsInstallation(int state)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MacOtaPanelTests));
        await session.Dispatch(() =>
        {
            var engine = new Engine();
            using var panel = new MacSoftwareUpdatePanel(engine, () => throw new Exception("Must not prompt"), () => throw new Exception("Must not close"));
            engine.Set((MacOtaState)state); Click(panel,"CancelUpdateDownload");
            Assert.Equal(1, engine.Cancels); Assert.Equal(0, engine.Installs); Assert.Equal(MacOtaState.Cancelled, engine.Status.State);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AChangedOrFailedNativeUpdateCannotCloseTheApplicationAfterSave()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MacOtaPanelTests));
        await session.Dispatch(async () =>
        {
            var engine = new Engine(); var closes = 0; var recovered = 0;
            using var panel = new MacSoftwareUpdatePanel(engine, () => { engine.Set(MacOtaState.Error); return Task.FromResult(true); }, () => closes++, () => recovered++);
            engine.Set(MacOtaState.Ready); await panel.InstallAsync();
            Assert.Equal(0, closes); Assert.Equal(0, engine.Installs);
            Assert.Equal(1, recovered);
        }, CancellationToken.None);
    }

    private static T Find<T>(Control panel, string name) where T : Control => panel.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Control panel, string name) => Find<Button>(panel,name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private sealed class Engine : IMacOtaUpdater
    {
        public MacOtaStatus Status { get; private set; } = new(MacOtaState.Idle);
        public event Action? Changed;
        public int Checks, Downloads, Installs, Cancels;
        public bool IncludeTests;
        public void Set(MacOtaState state) { Status = new(state,"1.0.4 (3.1.1)",42); Changed?.Invoke(); }
        public void Check(bool includeTests) { Checks++; IncludeTests = includeTests; Set(MacOtaState.Checking); }
        public void Download() { Downloads++; Set(MacOtaState.Downloading); }
        public bool Install() { if (Status.State != MacOtaState.Ready) return false; Installs++; Set(MacOtaState.Installing); return true; }
        public void Cancel() { Cancels++; Set(MacOtaState.Cancelled); }
    }
}
