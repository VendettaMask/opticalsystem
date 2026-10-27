using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Services.Updates;

namespace OpticalSystemDesign.App.Panels;

internal sealed class MacSoftwareUpdatePanel : StackPanel, IDisposable
{
    private readonly IMacOtaUpdater _updater;
    private readonly Func<Task<bool>> _prepare;
    private readonly Action _closePrepared;
    private readonly Action? _cancelPreparation;
    private readonly Button _check = new() { Name = "CheckForUpdates", Content = "检查更新" };
    private readonly Button _download = new() { Name = "DownloadUpdate", Content = "下载更新", IsVisible = false };
    private readonly Button _install = new() { Name = "InstallUpdate", Content = "安装更新并重启", IsVisible = false };
    private readonly Button _cancel = new() { Name = "CancelUpdateDownload", Content = "取消更新", IsVisible = false };
    private readonly CheckBox _tests = new() { Name = "IncludeTestUpdates", Content = "包含测试版", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _version = new() { Name = "LatestVersion", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { Name = "OtaUpdateStatus", TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _progress = new() { Name = "UpdateDownloadProgress", Minimum = 0, Maximum = 100, Height = 6, IsVisible = false };
    private bool _preparing;
    private bool _disposed;
    private string? _failure;

    internal MacSoftwareUpdatePanel(IMacOtaUpdater updater, Func<Task<bool>> prepare, Action closePrepared, Action? cancelPreparation = null)
    {
        Name = "AboutUpdates"; Spacing = 10;
        _updater = updater; _prepare = prepare; _closePrepared = closePrepared; _cancelPreparation = cancelPreparation;
        _updater.Changed += Refresh;
        _check.Classes.Add("accent"); _download.Classes.Add("accent"); _install.Classes.Add("accent");
        _check.Click += (_, _) => _updater.Check(_tests.IsChecked == true);
        _download.Click += (_, _) => _updater.Download();
        _install.Click += async (_, _) => await InstallAsync();
        _cancel.Click += (_, _) => _updater.Cancel();
        _tests.IsCheckedChanged += (_, _) => { if (!_preparing) _updater.Cancel(); };
        var buttons = new WrapPanel { ItemSpacing = 10, LineSpacing = 10 };
        foreach (var button in new[] { _check, _download, _install, _cancel }) buttons.Children.Add(button);
        var releases = new Button { Name = "UpdateReleasePage", Content = "打开发布页" };
        releases.Click += async (_, _) =>
        {
            try
            {
                if (TopLevel.GetTopLevel(this) is { } owner) await owner.Launcher.LaunchUriAsync(ProductReleaseSource.ReleasesPage);
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            { _failure = "无法打开浏览器，请稍后重试。"; Refresh(); }
        };
        buttons.Children.Add(releases);
        Children.Add(_tests); Children.Add(_version); Children.Add(buttons); Children.Add(_progress); Children.Add(_status);
        Refresh();
    }

    internal async Task InstallAsync()
    {
        if (_disposed || _preparing || _updater.Status.State != MacOtaState.Ready) return;
        _failure = null; _preparing = true; Refresh();
        var installed = false;
        try
        {
            // No native install reply (or quit request) before all document,
            // module and persistence decisions have succeeded.
            if (await _prepare() && !_disposed)
            {
                installed = _updater.Install();
                if (installed) _closePrepared();
                else _failure = "无法准备更新，未执行安装。";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        { _failure = "无法准备更新，未执行安装。"; }
        finally
        {
            _preparing = false;
            if (!installed) _cancelPreparation?.Invoke();
            if (!_disposed) Refresh();
        }
    }

    private void Refresh()
    {
        if (_disposed) return;
        var status = _updater.Status;
        var busy = _preparing || status.State is MacOtaState.Checking or MacOtaState.Downloading or MacOtaState.Extracting or MacOtaState.Installing;
        _check.IsEnabled = !busy && status.State is not (MacOtaState.Available or MacOtaState.Ready);
        _tests.IsEnabled = _check.IsEnabled;
        _download.IsVisible = status.State == MacOtaState.Available;
        _install.IsVisible = status.State == MacOtaState.Ready;
        _install.IsEnabled = !_preparing;
        _cancel.IsVisible = !_preparing && status.State is MacOtaState.Available or MacOtaState.Ready or MacOtaState.Checking or MacOtaState.Downloading;
        _version.Text = status.Version;
        _progress.IsVisible = status.State is MacOtaState.Checking or MacOtaState.Downloading or MacOtaState.Extracting;
        _progress.IsIndeterminate = status.State == MacOtaState.Checking;
        _progress.Value = status.Progress;
        var message = _preparing ? "正在准备更新，请稍候。" : status.State switch
        {
            MacOtaState.Checking => "正在检查更新，请稍候。",
            MacOtaState.Available => "发现新版本，点击“下载更新”开始 OTA。",
            MacOtaState.Downloading => "正在下载并校验更新…",
            MacOtaState.Extracting => "正在校验并准备更新…",
            MacOtaState.Ready => "更新校验通过。安装前可选择保存或不保存当前修改；取消则不安装。",
            MacOtaState.Installing => "正在安装更新并重启…",
            MacOtaState.UpToDate => "当前已是所选范围内的最新版本和构建，无需更新。",
            MacOtaState.Cancelled => "更新已取消，未执行安装。",
            MacOtaState.Error => "无法完成更新，请稍后重试或从发布页手动安装。",
            _ => "点击“检查更新”获取 Mac 更新；下载完成后可安装并重启。"
        };
        _status.Text = UiLocalization.Text(_failure ?? message);
        foreach (var button in new[] { _check, _install })
        {
            var reason = button.IsEnabled ? null : UiLocalization.Text(message);
            ToolTip.SetTip(button, reason); AutomationProperties.SetHelpText(button, reason);
        }
    }

    public void Dispose() { if (_disposed) return; _disposed = true; _updater.Changed -= Refresh; }
}
