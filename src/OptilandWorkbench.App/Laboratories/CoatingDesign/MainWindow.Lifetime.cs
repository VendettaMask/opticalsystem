using Avalonia.Controls;

namespace OptilandWorkbench.App.Laboratories.CoatingDesign;

public sealed partial class MainWindow
{
    private bool _hasUnsavedChanges;
    private bool _closeApproved;
    private bool _closeInProgress;
    private bool _preparingHostClose;
    private readonly HashSet<Task> _pendingOperations = [];
    internal bool HasUnsavedChanges => _hasUnsavedChanges;
    internal Func<string, Task<UnsavedChangesChoice>>? ChooseUnsavedChanges { get; init; }

    private Task TrackOperation(Func<Task> action)
    {
        if (_preparingHostClose || _closeApproved) return Task.CompletedTask;
        var task = Guard(action);
        _pendingOperations.Add(task);
        _ = RemoveWhenCompleteAsync(task);
        return PendingOperation = task;
    }

    private async Task RemoveWhenCompleteAsync(Task task)
    {
        try { await task; }
        finally { _pendingOperations.Remove(task); }
    }

    private async Task<bool> ConfirmUnsavedChangesAsync(string operation)
    {
        _table.CommitEdit();
        try
        {
            return await UnsavedChangesGuard.CanContinueAsync(_hasUnsavedChanges,
                () => ChooseUnsavedChanges is { } choose ? choose(operation)
                    : new UnsavedChangesWindow(operation, "当前镀膜实验").ShowDialog<UnsavedChangesChoice>(this),
                TrySaveAsync);
        }
        catch (Exception error)
        {
            _status.Text = "镀膜实验尚未保存，操作已取消：" + error.Message;
            return false;
        }
    }

    internal async Task<bool> PrepareForHostCloseAsync()
    {
        if (_preparingHostClose) return false;
        _preparingHostClose = true;
        try
        {
            _session.Cancel();
            while (_pendingOperations.Count > 0)
            {
                var pending = _pendingOperations.ToArray();
                await Task.WhenAll(pending);
                _pendingOperations.ExceptWith(pending);
            }
            return await ConfirmUnsavedChangesAsync("关闭镀膜设计");
        }
        finally { _preparingHostClose = false; }
    }

    internal void CloseAfterHostApproval()
    {
        _closeApproved = true;
        Close();
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_closeApproved) return;
        args.Cancel = true;
        if (_closeInProgress) return;
        _closeInProgress = true;
        try
        {
            if (await PrepareForHostCloseAsync()) CloseAfterHostApproval();
        }
        catch (Exception error) { _status.Text = "关闭未完成：" + error.Message; }
        finally { _closeInProgress = false; }
    }
}
