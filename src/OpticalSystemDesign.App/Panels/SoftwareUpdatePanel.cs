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
    private readonly Button _check = new() { Name = "CheckForUpdates", Content = "检查更新", MinWidth = 100 };
    private readonly TextBlock _latest = new() { Name = "LatestVersion", Text = "—", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _installer = new() { Name = "ReleaseInstaller", Text = string.Empty, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private Uri _releasePage = ProductReleaseSource.ReleasesPage;
    private bool _checking;
    private bool _disposed;

    internal SoftwareUpdatePanel(IReleaseUpdateService? service = null, Func<Uri, Task<bool>>? openPage = null)
    {
        if (service is null)
        {
            _client = GitHubReleaseUpdateService.CreateClient();
            service = new GitHubReleaseUpdateService(_client, ProductVersionInfo.Current, ProductReleaseSource.CurrentRuntime);
        }
        _service = service;
        _openPage = openPage ?? (uri => TopLevel.GetTopLevel(this) is { } owner
            ? owner.Launcher.LaunchUriAsync(uri) : Task.FromResult(false));
        Name = "AboutUpdates";
        Spacing = 10;
        _check.Classes.Add("accent");
        _check.Click += async (_, _) => await CheckAsync();
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
        buttons.Children.Add(releases);
        Children.Add(version);
        Children.Add(_installer);
        Children.Add(buttons);
    }

    internal async Task CheckAsync()
    {
        if (_checking || _disposed) return;
        _checking = true;
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
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _client?.Dispose();
    }

    private void SetStatus(string text)
    {
        ToolTip.SetTip(_latest, text);
        AutomationProperties.SetHelpText(_latest, text);
    }
}
