using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App;

public sealed partial class MainWindow
{
    private DocumentRecoveryStore? _recovery;
    private DispatcherTimer? _recoveryTimer;
    private bool _checkpointRunning;
    private bool _checkpointRequested;

    internal async Task StartDocumentRecoveryAsync()
    {
        if (_closed || _recovery is not null) return;
        try
        {
            _recovery = new(_application.Documents, DocumentRecoveryStore.DefaultDirectory);
            _recoveryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _recoveryTimer.Tick += OnRecoveryTick;
            _recoveryTimer.Start();
            await ShowDocumentRecoveryAsync();
        }
        catch (Exception error) { _statusText.Text = $"工程恢复保护未启动：{error.Message}"; }
    }

    private async void OnRecoveryTick(object? sender, EventArgs args)
    {
        if (_closed || _closeInProgress || _recovery is null) return;
        if (_checkpointRunning) { _checkpointRequested = true; return; }
        _checkpointRunning = true;
        try { await Task.Run(() => _recovery.CheckpointAsync()); }
        catch (Exception error) { if (!_closed) _statusText.Text = $"工程恢复副本未保存：{error.Message}"; }
        finally
        {
            _checkpointRunning = false;
            if (_checkpointRequested && !_closed)
            {
                _checkpointRequested = false;
                Dispatcher.UIThread.Post(() => OnRecoveryTick(null, EventArgs.Empty));
            }
        }
    }

    private async Task ShowDocumentRecoveryAsync()
    {
        if (_recovery is null) { await StartDocumentRecoveryAsync(); return; }
        var entries = await Task.Run(_recovery.ListCandidates);
        if (entries.Count == 0) { _statusText.Text = "没有需要恢复的工程副本。"; return; }
        var dialog = new Window
        {
            Title = "恢复未保存工程",
            Width = 680,
            Height = 440,
            MinWidth = 480,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var list = new ListBox { ItemsSource = entries, SelectedIndex = 0 };
        var restore = new Button { Content = "恢复所选工程", MinWidth = 120 };
        restore.Classes.Add("accent");
        var later = new Button { Content = "保留并关闭", MinWidth = 110 };
        restore.Click += (_, _) => dialog.Close(list.SelectedItem as DocumentRecoveryCandidate);
        later.Click += (_, _) => dialog.Close(null);
        var content = new Grid { Margin = new Avalonia.Thickness(20), RowDefinitions = new("Auto,*,Auto"), RowSpacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = "检测到上次未正常结束时的工程副本。恢复后作为未保存工程打开，请另存；原文件保持不变。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        Grid.SetRow(list, 1); content.Children.Add(list);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12,
            Children = { restore, later }
        };
        Grid.SetRow(buttons, 2); content.Children.Add(buttons); dialog.Content = content;
        var chosen = await dialog.ShowDialog<DocumentRecoveryCandidate?>(this);
        if (chosen is null || !await ConfirmUnsavedChangesAsync("恢复未保存工程")) return;
        await _recovery.RestoreAsync(chosen);
        _statusText.Text = "工程已恢复；请另存为 STAROPT 工程。原文件未被覆盖。";
    }
}
