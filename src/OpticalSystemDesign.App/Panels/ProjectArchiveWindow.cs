using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Panels;

internal sealed class ProjectArchiveWindow : Window
{
    private readonly IProjectArchiveService _service;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<ProjectArchiveFileDto>>>? _collect;
    private readonly Func<ProjectArchiveRestoreDto, ProjectArchiveFileDto, Task>? _open;
    private readonly Func<Task<bool>>? _confirm;
    private readonly string? _archive;
    private readonly string _temporary = Path.Combine(Path.GetTempPath(), "starpack-ui-" + Guid.NewGuid().ToString("N"));
    private readonly ListBox _files = new() { Name = "archive-files", MinHeight = 160 };
    private readonly TextBlock _status = new() { Name = "archive-status", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, IsVisible = false };
    private readonly TextBox _target = new() { Name = "archive-target", MinWidth = 120 };
    private readonly CheckBox _layout = new() { Content = "恢复后应用窗口布局", IsChecked = false };
    private readonly ComboBox _preset = new() { Name = "archive-preset", ItemsSource = new[] { "设计归档", "完整归档" }, SelectedIndex = 0 };
    private readonly Button _execute;
    private readonly List<Control> _commands = [];
    private readonly CancellationTokenSource _lifetime = new();
    private ProjectArchiveCapture? _capture;
    private ProjectArchiveRestoreDto? _restored;
    private bool _closed;
    private bool _busy;
    private Task _pending = Task.CompletedTask;
    internal Task InitialLoad { get; }

    internal ProjectArchiveWindow(IProjectArchiveService service,
        Func<string, CancellationToken, Task<IReadOnlyList<ProjectArchiveFileDto>>>? collect = null,
        string? archive = null, Func<Task<bool>>? confirm = null,
        Func<ProjectArchiveRestoreDto, ProjectArchiveFileDto, Task>? open = null)
    {
        _service = service; _collect = collect; _archive = archive; _confirm = confirm; _open = open;
        Directory.CreateDirectory(_temporary);
        var restoring = archive is not null;
        Title = restoring ? "恢复工程归档" : "创建工程归档";
        Width = 880; Height = 650; MinWidth = 500; MinHeight = 460;
        _execute = new Button { Content = restoring ? "恢复归档" : "创建归档", Name = restoring ? "archive-restore" : "archive-create", Classes = { "accent" } };
        _execute.Click += async (_, _) => await TrackAsync(ExecuteAsync);
        _commands.Add(_execute);
        var browse = Command("浏览…", "archive-browse", BrowseTargetAsync);
        var add = Command("添加文件", "archive-add", AddAsync);
        var locate = Command("重新定位", "archive-locate", LocateAsync);
        var refresh = Command("刷新清单", "archive-refresh", RefreshAsync);
        var inspect = Command("打开所选资料", "archive-open-item", OpenSelectedAsync);
        add.IsVisible = locate.IsVisible = refresh.IsVisible = !restoring;
        inspect.IsVisible = restoring;
        _preset.IsVisible = !restoring; _layout.IsVisible = restoring;
        _preset.SelectionChanged += (_, _) => ApplyPreset();
        _commands.Add(_preset); _commands.Add(_target); _commands.Add(_layout);
        var cancel = new Button { Content = "取消", Name = "archive-cancel" };
        cancel.Click += (_, _) => { if (_busy) _lifetime.Cancel(); else Close(); };
        var targetRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8, Children = { _target, browse } };
        Grid.SetColumn(browse, 1);
        var tools = new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Children = { _preset, add, locate, refresh, inspect, _layout } };
        _files.ItemTemplate = new FuncDataTemplate<ArchiveRow>((row, _) => row is null ? new Border() : RowControl(row, !restoring));
        var top = new StackPanel { Spacing = 10, Margin = new(0, 0, 0, 12), Children =
        {
            new TextBlock { Text = restoring ? "选择新的工程文件夹，恢复后打开项目。" : "必要输入随工程收集，计算结果可按需选择。", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            targetRow, tools
        } };
        var bottom = new StackPanel { Spacing = 8, Margin = new(0, 12, 0, 0), Children =
        {
            _status, _progress, new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, ItemSpacing = 8, LineSpacing = 6, Children = { cancel, _execute } }
        } };
        DockPanel.SetDock(top, Avalonia.Controls.Dock.Top); DockPanel.SetDock(bottom, Avalonia.Controls.Dock.Bottom);
        Content = new DockPanel { Margin = new(18), Children = { top, bottom, _files } };
        ThemeChrome.ApplyDialogDecoration(this);
        Closed += async (_, _) =>
        {
            _closed = true; _lifetime.Cancel();
            try { await _pending; } catch (Exception) { /* The operation already reports its failure. */ }
            _capture?.Dispose();
            if (Directory.Exists(_temporary) && Path.GetFileName(_temporary).StartsWith("starpack-ui-", StringComparison.Ordinal))
                Directory.Delete(_temporary, recursive: true);
            _lifetime.Dispose();
        };
        InitialLoad = TrackAsync(RefreshAsync);
    }

    private Button Command(string text, string name, Func<Task> run)
    {
        var button = new Button { Content = text, Name = name };
        button.Click += async (_, _) => await TrackAsync(run); _commands.Add(button); return button;
    }
    private async Task TrackAsync(Func<Task> run)
    {
        if (_busy || _closed) return;
        _busy = true; foreach (var command in _commands) command.IsEnabled = false;
        _progress.IsVisible = true; _progress.IsIndeterminate = true;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = completion.Task;
        try { await run(); }
        catch (OperationCanceledException) { if (!_closed) _status.Text = UiLocalization.Text("归档操作已取消。"); }
        catch (Exception error) { if (!_closed) _status.Text = error.Message; }
        finally
        {
            _busy = false;
            if (!_closed)
            {
                _progress.IsVisible = false;
                foreach (var command in _commands) command.IsEnabled = !_lifetime.IsCancellationRequested;
            }
            completion.TrySetResult();
        }
    }
    private async Task RefreshAsync()
    {
        if (_archive is not null)
        {
            var info = await _service.InspectAsync(_archive, _lifetime.Token);
            if (_closed) return;
            _files.ItemsSource = info.Entries.Select(entry => new ArchiveRow(new(entry.RelativePath, entry.Kind, "", entry.Bytes, entry.Required))).ToArray();
            _target.Text = Path.Combine(Path.GetDirectoryName(_archive)!, SafeFolder(info.Name));
            Summary(info.Name, info.Entries.Count, info.Entries.Sum(entry => entry.Bytes));
        }
        else
        {
            var directory = Path.Combine(_temporary, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            var extras = _collect is null ? [] : await _collect(directory, _lifetime.Token);
            var capture = await _service.CaptureAsync(extras, _lifetime.Token);
            if (_closed) { capture.Dispose(); return; }
            _capture?.Dispose(); _capture = capture;
            _files.ItemsSource = capture.Files.Select(file => new ArchiveRow(file)).ToArray();
            if (string.IsNullOrWhiteSpace(_target.Text)) _target.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), SafeFolder(capture.Name) + ".starpack");
            ApplyPreset(); Summary(capture.Name, capture.Files.Count, capture.Files.Sum(file => file.Bytes));
        }
    }
    private void Summary(string name, int count, long bytes) => _status.Text = name + " · "
        + string.Format(UiLocalization.Text("{0} 个文件 · {1:N2} MiB"), count, bytes / 1048576d);
    private void ApplyPreset()
    {
        if (_archive is not null) return;
        foreach (var row in Rows()) row.Include = row.File.Required || !row.File.IsResult || _preset.SelectedIndex == 1;
    }
    private IEnumerable<ArchiveRow> Rows() => (_files.ItemsSource ?? Array.Empty<object>()).Cast<ArchiveRow>();
    private async Task AddAsync()
    {
        var paths = await StorageProvider.OpenFilePickerAsync(new() { Title = UiLocalization.Text("添加归档文件"), AllowMultiple = true });
        foreach (var path in paths)
        {
            var kind = Kind(path.Path.LocalPath);
            _service.LinkFile(path.Path.LocalPath, kind, isResult: kind == "ray-database" || kind.EndsWith("-result", StringComparison.Ordinal));
        }
        await RefreshAsync();
    }
    private async Task LocateAsync()
    {
        if (_files.SelectedItem is not ArchiveRow row || _archive is not null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = UiLocalization.Text("重新定位归档文件") });
        if (files.Count == 0) return;
        var path = files[0].Path.LocalPath;
        _capture?.RelocateFile(row.File.RelativePath, path);
        var updated = Rows().Select(item => item == row ? new ArchiveRow(item.File with { SourcePath = path, Bytes = new FileInfo(path).Length }) : item).ToArray();
        _files.ItemsSource = updated;
    }
    private async Task BrowseTargetAsync()
    {
        if (_archive is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new() { Title = UiLocalization.Text("创建工程归档"), DefaultExtension = "starpack",
                SuggestedFileName = Path.GetFileName(_target.Text), FileTypeChoices = new[] { new FilePickerFileType("STARPACK") { Patterns = new[] { "*.starpack" } } } });
            if (file is not null) _target.Text = file.Path.LocalPath;
        }
        else
        {
            var directories = await StorageProvider.OpenFolderPickerAsync(new() { Title = UiLocalization.Text("选择恢复位置") });
            if (directories.Count > 0) _target.Text = Path.Combine(directories[0].Path.LocalPath, Path.GetFileName(_target.Text ?? ""));
        }
    }
    internal async Task ExecuteAsync()
    {
        var path = _target.Text;
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(UiLocalization.Text("请选择归档目标。"));
        var progress = new Progress<ProjectArchiveProgressDto>(value =>
        {
            if (_closed || !_busy) return;
            _progress.IsIndeterminate = false;
            _progress.Value = value.TotalBytes == 0 ? 0 : Math.Clamp(100d * value.CompletedBytes / value.TotalBytes, 0, 100);
            _status.Text = UiLocalization.Text(value.Phase switch { "collect" => "正在收集文件", "pack" => "正在创建归档", _ => "正在恢复归档" }) + " · " + value.File;
        });
        if (_archive is null)
        {
            if (_capture is null) return;
            if (!path.EndsWith(".starpack", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(UiLocalization.Text("归档文件请使用 .starpack 后缀。"));
            var rows = Rows().ToArray();
            await _service.CreateAsync(path, _capture, rows.Where(row => row.Include).Select(row => row.File.RelativePath).ToHashSet(StringComparer.Ordinal), progress, _lifetime.Token);
            _status.Text = UiLocalization.Text("工程归档已创建。");
        }
        else
        {
            if (_restored is not null) return;
            if (_confirm is not null && !await _confirm()) return;
            _restored = await _service.RestoreAsync(_archive, path, progress, _lifetime.Token);
            _files.ItemsSource = _restored.Files.Select(file => new ArchiveRow(file)).ToArray();
            if (_layout.IsChecked == true && _open is not null && _restored.Files.FirstOrDefault(file => file.Kind == "workspace") is { } workspace)
                await _open(_restored, workspace);
            _status.Text = UiLocalization.Text("工程归档已恢复，可打开所选资料。");
        }
    }
    private async Task OpenSelectedAsync()
    {
        if (_restored is not null && _files.SelectedItem is ArchiveRow row && _open is not null)
            await _open(_restored, row.File);
    }
    private static Control RowControl(ArchiveRow row, bool creating)
    {
        var check = new CheckBox { IsChecked = row.Include, IsEnabled = !row.File.Required, IsVisible = creating, VerticalAlignment = VerticalAlignment.Center };
        check.IsCheckedChanged += (_, _) => row.Include = check.IsChecked == true;
        row.PropertyChanged += (_, _) => check.IsChecked = row.Include;
        var name = UiLocalization.Preserve(new TextBlock { Text = row.File.RelativePath, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var status = new TextBlock { Text = UiLocalization.Text(row.File.Required ? "必需" : "可选") };
        if (creating && !row.File.Exists) status.Text = UiLocalization.Text("文件缺失，请重新定位");
        var detail = new StackPanel { Spacing = 3, Children = { name, status } };
        var size = UiLocalization.Preserve(new TextBlock { Text = (row.File.Bytes / 1048576d).ToString("N2") + " MiB", VerticalAlignment = VerticalAlignment.Center });
        var grid = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 10, Margin = new(4, 6), Children = { check, detail, size } };
        Grid.SetColumn(detail, 1); Grid.SetColumn(size, 2); return grid;
    }
    private static string SafeFolder(string value)
    {
        var name = new string(value.Take(100).Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray()).TrimEnd(' ', '.');
        return "project-" + (name.Length == 0 ? "archive" : name);
    }
    private static string Kind(string path) => path.ToLowerInvariant() switch
    {
        var value when value.EndsWith(".camera-experiment.zip") => "camera-result",
        var value when value.EndsWith(".coating.json") => "coating-input",
        var value when value.EndsWith(".flat-start.json") => "initial-result",
        var value when value.EndsWith(".startol.json") => "tolerances",
        var value when value.EndsWith(".starrdb") => "ray-database",
        _ => "attachment"
    };
    private sealed class ArchiveRow(ProjectArchiveFileDto file) : INotifyPropertyChanged
    {
        public ProjectArchiveFileDto File { get; } = file;
        private bool _include = true;
        public bool Include { get => _include; set { _include = value; PropertyChanged?.Invoke(this, new(nameof(Include))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
