using System.Net.Http;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Services.Updates;

namespace OpticalSystemDesign.App.Panels;

internal sealed class SoftwareUpdatePanel : StackPanel, IDisposable
{
    private readonly IReleaseUpdateService _service;
    private readonly HttpClient? _client;
    private readonly Func<Uri, Task<bool>> _openPage;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HttpClient? _downloadClient;
    private readonly IOtaDownloader _downloader;
    private readonly Func<PreparedOtaUpdate, Task<bool>>? _handoff;
    private readonly Func<string?> _unavailableReason;
    private readonly Button _download = new() { Name = "DownloadUpdate", Content = "下载更新" };
    private readonly Button _install = new() { Name = "InstallUpdate", Content = "保存并重启更新", IsVisible = false };
    private readonly Button _cancel = new() { Name = "CancelUpdateDownload", Content = "取消下载", IsVisible = false };
    private readonly ProgressBar _progress = new() { Name = "UpdateDownloadProgress", Minimum = 0, Maximum = 100, Height = 6, IsVisible = false };
    private readonly TextBlock _otaStatus = new() { Name = "OtaUpdateStatus", TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private CancellationTokenSource? _downloadCancellation;
    private SignedOtaUpdate? _verifiedUpdate;
    private PreparedOtaUpdate? _prepared;
    private bool _downloading;
    private bool _installing;
    private readonly Button _check = new() { Name = "CheckForUpdates", Content = "检查更新", MinWidth = 100 };
    private readonly TextBlock _latest = new() { Name = "LatestVersion", Text = "—", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _installer = new() { Name = "ReleaseInstaller", Text = string.Empty, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private Uri _releasePage = ProductReleaseSource.ReleasesPage;
    private bool _checking;
    private bool _disposed;

    internal SoftwareUpdatePanel(IReleaseUpdateService? service = null, Func<Uri, Task<bool>>? openPage = null,
        Func<PreparedOtaUpdate, Task<bool>>? handoff = null, IOtaDownloader? downloader = null, Func<string?>? unavailableReason = null)
    {
        if (service is null)
        {
            _client = GitHubReleaseUpdateService.CreateClient();
            service = new GitHubReleaseUpdateService(_client, ProductVersionInfo.Current, ProductReleaseSource.CurrentRuntime);
        }
        _service = service;
        _handoff = handoff;
        _unavailableReason = unavailableReason ?? WindowsOtaHandoff.UnavailableReason;
        if (downloader is null)
        {
            _downloadClient = OtaDownloader.CreateClient();
            downloader = new OtaDownloader(_downloadClient);
        }
        _downloader = downloader;
        _openPage = openPage ?? (uri => TopLevel.GetTopLevel(this) is { } owner
            ? owner.Launcher.LaunchUriAsync(uri) : Task.FromResult(false));
        Name = "AboutUpdates";
        Spacing = 10;
        _check.Classes.Add("accent");
        _check.Click += async (_, _) => await CheckAsync();
        _download.Classes.Add("accent");
        _install.Classes.Add("accent");
        _download.Click += async (_, _) => await DownloadAsync();
        _install.Click += async (_, _) => await InstallAsync();
        _cancel.Click += (_, _) => _downloadCancellation?.Cancel();
        var releases = new Button { Name = "UpdateReleasePage", Content = "打开发布页" };
        releases.Click += async (_, _) =>
        {
            try
            {
                if (!await _openPage(_releasePage) && !_disposed) SetStatus("无法打开浏览器，请稍后重试。");
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                if (!_disposed) SetStatus("无法打开浏览器，请稍后重试。");
            }
        };
        var version = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 16 };
        version.Children.Add(new TextBlock { Text = "最新正式版本", TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(_latest, 1);
        version.Children.Add(_latest);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 10, LineSpacing = 10 };
        buttons.Children.Add(_check);
        buttons.Children.Add(_download);
        buttons.Children.Add(_install);
        buttons.Children.Add(_cancel);
        buttons.Children.Add(releases);
        Children.Add(version);
        Children.Add(_installer);
        Children.Add(buttons);
        Children.Add(_progress);
        Children.Add(_otaStatus);
        RefreshOtaActions();
    }

    internal async Task CheckAsync()
    {
        if (_checking || _downloading || _installing || _disposed) return;
        _prepared?.Dispose();
        _prepared = null;
        _verifiedUpdate = null;
        _otaStatus.IsVisible = false;
        _checking = true;
        RefreshOtaActions();
        _check.IsEnabled = false;
        const string reason = "正在检查更新，请稍候。";
        ToolTip.SetTip(_check, reason);
        AutomationProperties.SetHelpText(_check, reason);
        SetStatus(reason);
        _latest.Text = "—";
        _installer.Text = string.Empty;
        _installer.IsVisible = false;
        _releasePage = ProductReleaseSource.ReleasesPage;
        try
        {
            var result = await _service.CheckAsync(_lifetime.Token);
            if (_disposed) return;
            _latest.Text = result.Release?.Version ?? "—";
            _releasePage = result.Release?.PageUri ?? ProductReleaseSource.ReleasesPage;
            _installer.Text = result.Release?.Installer?.Name ?? string.Empty;
            _installer.IsVisible = !string.IsNullOrEmpty(_installer.Text);
            _verifiedUpdate = result.Status == UpdateCheckStatus.UpdateAvailable ? result.Release?.Ota : null;
            SetStatus(result.Status switch
            {
                UpdateCheckStatus.UpdateAvailable => "发现新版本，可在 GitHub 发布页下载对应安装包。",
                UpdateCheckStatus.UpToDate => "当前版本无需升级（没有更高的正式版本）。",
                UpdateCheckStatus.NoPublishedRelease => "暂未找到公开的正式版本（也可能仓库不可访问）。",
                UpdateCheckStatus.NoCompatibleInstaller => "发现新版本，但尚无匹配的完整安装包。",
                UpdateCheckStatus.UnsupportedPlatform => "发现新版本，此平台暂不提供受支持的安装包检测。",
                UpdateCheckStatus.RateLimited => "GitHub 访问受限或请求过于频繁，请稍后重试。",
                UpdateCheckStatus.TimedOut => "检查更新超时，请检查网络后重试。",
                UpdateCheckStatus.NetworkError => "无法连接 GitHub，请检查网络后重试。",
                UpdateCheckStatus.InvalidCurrentVersion => "当前软件版本无效，无法比较版本。",
                _ => "发布信息不完整或不可信，已拒绝此更新信息。"
            });
        }
        catch (OperationCanceledException) when (_disposed) { }
        finally
        {
            _checking = false;
            if (!_disposed)
            {
                _check.IsEnabled = true;
                ToolTip.SetTip(_check, null);
                AutomationProperties.SetHelpText(_check, null);
                RefreshOtaActions();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _downloadCancellation?.Cancel();
        _prepared?.Dispose();
        _lifetime.Dispose();
        _client?.Dispose();
        _downloadClient?.Dispose();
    }

    internal async Task DownloadAsync()
    {
        if (_disposed || _checking || _downloading || !_download.IsEnabled || _verifiedUpdate is null) return;
        _downloading = true;
        _check.IsEnabled = false;
        ToolTip.SetTip(_check, "正在下载更新，请稍候。");
        AutomationProperties.SetHelpText(_check, "正在下载更新，请稍候。");
        _downloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _progress.Value = 0;
        _progress.IsVisible = true;
        ShowOtaStatus("正在下载并校验更新…");
        RefreshOtaActions();
        try
        {
            _prepared?.Dispose();
            _prepared = null;
            var progress = new Progress<double>(value => { if (!_disposed) _progress.Value = value; });
            var prepared = await _downloader.DownloadAsync(_verifiedUpdate, ProductVersionInfo.Current, progress, _downloadCancellation.Token);
            if (_disposed) { prepared.Dispose(); return; }
            _prepared = prepared;
            ShowOtaStatus("校验通过。点击保存并重启更新；取消保存则不安装。");
        }
        catch (OperationCanceledException) { if (!_disposed) ShowOtaStatus("下载已取消，未执行安装。"); }
        catch (TimeoutException) { if (!_disposed) ShowOtaStatus("下载更新超时，请检查网络后重试。"); }
        catch (Exception exception) when (exception is IOException or HttpRequestException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or System.Security.SecurityException)
        { if (!_disposed) ShowOtaStatus("下载或校验失败，未执行安装。请重试或使用发布页。"); }
        finally
        {
            _downloadCancellation.Dispose();
            _downloadCancellation = null;
            _downloading = false;
            if (!_disposed)
            {
                _check.IsEnabled = true;
                ToolTip.SetTip(_check, null);
                AutomationProperties.SetHelpText(_check, null);
                _progress.IsVisible = false;
                RefreshOtaActions();
            }
        }
    }

    internal async Task InstallAsync()
    {
        if (_disposed || _prepared is null || _handoff is null || !_install.IsEnabled) return;
        _installing = true;
        _check.IsEnabled = false;
        ToolTip.SetTip(_check, "正在准备更新，请稍候。");
        AutomationProperties.SetHelpText(_check, "正在准备更新，请稍候。");
        SetDisabledReason(_install, "正在准备更新，请稍候。");
        try
        {
            if (!await _handoff(_prepared) && !_disposed)
                ShowOtaStatus("更新已取消，未执行安装。");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or System.Security.SecurityException)
        { if (!_disposed) ShowOtaStatus("无法准备更新，未执行安装。"); }
        finally
        {
            _installing = false;
            if (!_disposed)
            {
                _check.IsEnabled = true;
                ToolTip.SetTip(_check, null);
                AutomationProperties.SetHelpText(_check, null);
                RefreshOtaActions();
            }
        }
    }

    private void RefreshOtaActions()
    {
        var reason = _installing ? "正在准备更新，请稍候。" : _downloading ? "正在下载更新，请稍候。" : _checking ? "正在检查更新，请稍候。" :
            _unavailableReason() ?? (_handoff is null ? "在线更新仅支持已安装的 Windows 版本。" :
                _verifiedUpdate is null ? "请先检查更新；仅接受具有可信签名的新正式版本。" : null);
        SetDisabledReason(_download, reason);
        _download.IsVisible = _prepared is null;
        _install.IsVisible = _prepared is not null;
        SetDisabledReason(_install, reason);
        _cancel.IsVisible = _downloading;
    }

    private void ShowOtaStatus(string text) { _otaStatus.Text = text; _otaStatus.IsVisible = true; }
    private static void SetDisabledReason(Button button, string? reason)
    {
        button.IsEnabled = reason is null;
        ToolTip.SetTip(button, reason);
        AutomationProperties.SetHelpText(button, reason);
    }

    private void SetStatus(string text)
    {
        ToolTip.SetTip(_latest, text);
        AutomationProperties.SetHelpText(_latest, text);
    }
}
