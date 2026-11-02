using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

public sealed partial class MainWindow
{
    private CoatingRecoverySession? _recovery;
    private readonly DispatcherTimer _recoveryTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private long _recoveryRevision;
    private Task _recoveryWrite = Task.CompletedTask;
    private Window? _recoveryWindow;
    internal Task RecoveryWrite => _recoveryWrite;
    internal string? RecoveryPath => _recovery?.Path;
    private static string RecoveryDirectory => Path.Combine(Path.GetDirectoryName(CoatingPlotDefaults.DefaultPath)!, "coating-recovery");
    private void InitializeRecovery()
    {
        try { _recovery = CoatingRecoverySession.Create(RecoveryDirectory); }
        catch (Exception error) { ShowFailure("无法创建镀膜恢复草稿：" + error.Message); }
        _recoveryTimer.Tick += OnRecoveryTick;
    }
    private void ScheduleRecovery()
    {
        if (_recovery is null || _closeApproved) return;
        _recoveryTimer.Stop(); _recoveryTimer.Start();
    }
    private async void OnRecoveryTick(object? sender, EventArgs args)
    {
        _recoveryTimer.Stop();
        try { await FlushRecoveryAsync(); }
        catch (Exception error) { ShowFailure("恢复草稿尚未写入，原文件已保留：" + error.Message); }
    }
    internal Task FlushRecoveryAsync()
    {
        if (_recovery is null) return Task.CompletedTask;
        var revision = ++_recoveryRevision;
        return _recoveryWrite = _hasUnsavedChanges
            ? _recovery.WriteAsync(_name.Text ?? "镀膜实验", JsonSerializer.Serialize(CaptureEditorState(), Experiment.JsonOptions), revision)
            : _recovery.ClearAsync(revision);
    }
    private async Task FinishRecoveryAsync()
    {
        _recoveryTimer.Stop();
        if (_recovery is not null) await _recovery.CompleteAsync().ConfigureAwait(false);
    }
    private void DisposeRecovery()
    {
        _recoveryTimer.Stop(); _recoveryTimer.Tick -= OnRecoveryTick;
        _recovery?.Dispose();
    }
    private void ValidateEditorState(EditorState s)
    {
        if (s.Base is null || s.Numbers is null || s.Texts is not { Length: 7 } || s.Indices is not { Length: 9 }
            || s.Toggles is not { Length: 4 } || s.Rows is not { Length: <= 200 } || s.BackRows is not { Length: <= 200 }
            || s.Selected is not { Length: <= 200 } || s.Numbers.Count != _numbers.Count || s.Numbers.Keys.Any(k => !_numbers.ContainsKey(k))
            || s.Numbers.Values.Concat(s.Texts).Any(v => v?.Length > 65536))
            throw new InvalidDataException("恢复草稿的编辑器字段或数量无效；文件已保留。");
        s.Base.Validate();
        var ids = s.Base.Materials.Select(m => m.Id).ToHashSet();
        if (s.Rows.Concat(s.BackRows).Any(r => r is null || r.Original is null || !ids.Contains(r.Original.MaterialId)
            || !double.IsFinite(r.Original.ThicknessNm) || r.Original.ThicknessNm <= 0 || r.Original.ThicknessNm > 100000
            || r.PhysicalText is null || r.OriginalText is null || r.PhysicalText.Length > 65536 || r.InvalidText?.Length > 65536
            || r.InvalidUnit is not (null or "OT" or "QW" or "FW")))
            throw new InvalidDataException("恢复草稿包含无效膜层；文件已保留。");
        var lengths = new[] { 3, 3, AlgorithmNames.Length, 2, 3, 3, 3, 3, 2 };
        if (s.Indices.Where((v, i) => v < 0 || v >= lengths[i]).Any()) throw new InvalidDataException("恢复草稿的选项索引无效。");
    }
    private async Task OfferRecoveryAsync()
    {
        var sessions = CoatingRecoverySession.AcquireAvailable(RecoveryDirectory);
        try
        {
            foreach (var session in sessions)
            {
                try
                {
                    var draft = session.Read();
                    var state = JsonSerializer.Deserialize<EditorState>(draft.EditorJson, Experiment.JsonOptions)
                        ?? throw new InvalidDataException("恢复草稿缺少编辑状态。");
                    ValidateEditorState(state);
                    var choice = await RecoveryChoiceAsync(draft);
                    if (choice is not ("restore" or "delete")) break;
                    if (choice == "delete") { await session.CompleteAsync(); continue; }
                    if (!await ConfirmUnsavedChangesAsync("恢复另一份镀膜草稿")) break;
                    ApplyEditorState(state);
                    _undo.Clear(); _redo.Clear(); _historyCurrent = CaptureEditorState(); _historyKey = null; RefreshHistoryButtons();
                    // Never delete the old crash draft until its replacement is safely on disk.
                    if (_recovery is null) throw new IOException("新的恢复会话不可用，旧草稿已保留。");
                    await FlushRecoveryAsync(); await session.CompleteAsync();
                    break;
                }
                catch (Exception error) { ShowFailure("草稿无法恢复，已保留以便检查：" + Path.GetFileName(session.Path) + " · " + error.Message); }
            }
        }
        finally { foreach (var session in sessions) session.Dispose(); }
    }
    private async Task<string> RecoveryChoiceAsync(CoatingRecoveryDraft draft)
    {
        var window = new Window
        {
            Name = "CoatingRecoveryWindow",
            Title = "发现未正常关闭的镀膜草稿",
            Width = 600,
            Height = 250,
            FontFamily = FontFamily,
            FontSize = FontSize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var (label, value) in new[] { ("恢复草稿", "restore"), ("删除这份草稿", "delete"), ("稍后处理", "later") })
        {
            var button = new Button { Content = label }; AutomationProperties.SetName(button, label);
            button.Click += (_, _) => window.Close(value); buttons.Children.Add(button);
        }
        window.Content = new StackPanel
        {
            Margin = new(20),
            Spacing = 20,
            Children =
        {
            new TextBlock { Text = $"{draft.Name}\n{draft.UpdatedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n恢复到编辑器，不覆盖原实验文件。未完成的厚度输入也会保留。", TextWrapping = Avalonia.Media.TextWrapping.Wrap }, buttons
        }
        };
        _recoveryWindow = window;
        try { return await window.ShowDialog<string>(this); }
        finally { _recoveryWindow = null; }
    }
}
