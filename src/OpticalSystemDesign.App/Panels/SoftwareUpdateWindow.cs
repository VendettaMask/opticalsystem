using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Services.Updates;

namespace OpticalSystemDesign.App.Panels;

internal sealed class SoftwareUpdateWindow : Window
{
    private readonly IReleaseUpdateService _service;
    private readonly Func<Uri, Task<bool>> _openPage;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Button _check = new() { Name = "CheckForUpdates", Content = "检查更新", MinWidth = 100 };
    private readonly TextBlock _status = new() { Name = "UpdateStatus", Text = "点击检查以查询 GitHub 上的正式版本。", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _latest = new() { Name = "LatestVersion", Text = "—" };
    private readonly TextBlock _installer = new() { Name = "ReleaseInstaller", TextWrapping = TextWrapping.Wrap };
    private Uri _releasePage = ProductReleaseSource.ReleasesPage;
    private bool _checking;
    private bool _closed;

    internal SoftwareUpdateWindow(IReleaseUpdateService service, Func<Uri, Task<bool>>? openPage = null)
    {
        _service = service;
        _openPage = openPage ?? (uri => Launcher.LaunchUriAsync(uri));
        Title = "软件版本与更新";
        Width = 680;
        Height = 470;
        MinWidth = 600;
        MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _check.Classes.Add("accent");
        _check.Click += async (_, _) => await CheckAsync();
        var releases = new Button { Name = "UpdateReleasePage", Content = "打开 GitHub 发布页" };
        releases.Click += async (_, _) =>
        {
            try
            {
                if (!await _openPage(_releasePage) && !_closed) _status.Text = "无法打开浏览器，请稍后重试。";
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                if (!_closed) _status.Text = "无法打开浏览器，请稍后重试。";
            }
        };
        var upgrade = new Button { Name = "OnlineUpgradeReserved", Content = "在线升级（预留）", IsEnabled = new ReservedOnlineUpgradeInstaller().IsAvailable };
        const string reason = "在线自动安装尚未开放。请在 GitHub 下载完整安装包，关闭软件后手动安装。";
        ToolTip.SetTip(upgrade, reason);
        AutomationProperties.SetHelpText(upgrade, reason);
        var close = new Button { Name = "CloseUpdateWindow", Content = "关闭", MinWidth = 88 };
        close.Click += (_, _) => Close();
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 10, LineSpacing = 10 };
        buttons.Children.Add(_check);
        buttons.Children.Add(releases);
        buttons.Children.Add(upgrade);
        buttons.Children.Add(close);
        var body = new StackPanel
        {
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = "Optical System Design", FontSize = DisplayTypography.EmptyStateTitle, FontWeight = FontWeight.SemiBold },
                VersionRow("当前版本", new TextBlock { Name = "InstalledVersion", Text = ProductVersionInfo.Current }),
                VersionRow("最新正式版本", _latest),
                new TextBlock { Text = $"GitHub · {ProductReleaseSource.Repository}", TextWrapping = TextWrapping.Wrap },
                _status,
                _installer,
                new TextBlock { Text = reason, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "仅手动检查；不会后台联网、自动下载或安装。未签名安装包不提供发布者身份认证。", TextWrapping = TextWrapping.Wrap }
            }
        };
        var root = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 20 };
        root.Children.Add(new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
        Content = root;
        Closed += (_, _) =>
        {
            _closed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();
        };
        DisplayTypography.Apply(this);
        UiLocalization.Attach(this);
    }

    internal async Task CheckAsync()
    {
        if (_checking || _closed) return;
        _checking = true;
        _check.IsEnabled = false;
        const string reason = "正在检查更新，请稍候。";
        ToolTip.SetTip(_check, reason);
        AutomationProperties.SetHelpText(_check, reason);
        _status.Text = reason;
        _latest.Text = "—";
        _installer.Text = string.Empty;
        _releasePage = ProductReleaseSource.ReleasesPage;
        try
        {
            var result = await _service.CheckAsync(_lifetime.Token);
            if (_closed) return;
            _latest.Text = result.Release?.Version ?? "—";
            _releasePage = result.Release?.PageUri ?? ProductReleaseSource.ReleasesPage;
            _installer.Text = result.Release?.Installer?.Name ?? string.Empty;
            _status.Text = result.Status switch
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
            };
        }
        catch (OperationCanceledException) when (_closed) { }
        finally
        {
            _checking = false;
            if (!_closed)
            {
                _check.IsEnabled = true;
                ToolTip.SetTip(_check, null);
                AutomationProperties.SetHelpText(_check, null);
                UiLocalization.Attach(this);
            }
        }
    }

    private static Grid VersionRow(string label, TextBlock version)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*"), ColumnSpacing = 16 };
        version.TextWrapping = TextWrapping.Wrap;
        Grid.SetColumn(version, 1);
        row.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        row.Children.Add(version);
        return row;
    }
}
