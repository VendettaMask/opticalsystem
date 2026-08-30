using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Controls;

internal enum OperationStatusKind
{
    Idle,
    Running,
    Synced,
    Stale,
    Failed
}

internal sealed class OperationStatusBar : DockPanel, IDisposable
{
    private static readonly TimeSpan ProgressDelay = TimeSpan.FromMilliseconds(500);

    private readonly TextBlock _message = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap,
        FontSize = DisplayTypography.CompactBody
    };
    private readonly ProgressBar _progress = new()
    {
        IsIndeterminate = true,
        IsVisible = false,
        Width = 96,
        Height = 4,
        Margin = new Thickness(8, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Button _cancel = new()
    {
        Content = "取消",
        IsVisible = false,
        MinWidth = 58,
        Height = 28,
        Margin = new Thickness(8, 0, 0, 0),
        Padding = new Thickness(8, 2),
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _longRunningHint = new()
    {
        Text = "当前操作不可取消",
        IsVisible = false,
        Margin = new Thickness(8, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = DisplayTypography.Caption
    };
    private readonly DispatcherTimer _progressTimer = new()
    {
        Interval = ProgressDelay
    };
    private readonly DispatcherTimer _longRunningTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2)
    };
    private Action? _cancelAction;
    private bool _disposed;

    public OperationStatusBar()
    {
        LastChildFill = false;
        VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_message);
        Children.Add(_progress);
        Children.Add(_cancel);
        Children.Add(_longRunningHint);
        _message.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextMuted);
        _longRunningHint.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextMuted);
        _cancel.Click += (_, _) =>
        {
            _cancel.IsEnabled = false;
            _message.Text = "正在取消…";
            _cancelAction?.Invoke();
        };
        _progressTimer.Tick += (_, _) =>
        {
            _progressTimer.Stop();
            if (Kind == OperationStatusKind.Running)
            {
                _progress.IsVisible = true;
            }
        };
        _longRunningTimer.Tick += (_, _) =>
        {
            _longRunningTimer.Stop();
            if (Kind == OperationStatusKind.Running && _cancelAction is null)
            {
                _longRunningHint.IsVisible = true;
            }
        };
    }

    public OperationStatusKind Kind { get; private set; } = OperationStatusKind.Idle;

    public void Start(string message, Action? cancelAction)
    {
        Kind = OperationStatusKind.Running;
        _cancelAction = cancelAction;
        _message.Text = message;
        _message.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextAccent);
        _progress.IsVisible = false;
        _longRunningHint.IsVisible = false;
        _cancel.IsEnabled = true;
        _cancel.IsVisible = cancelAction is not null;
        _progressTimer.Stop();
        _progressTimer.Start();
        _longRunningTimer.Stop();
        _longRunningTimer.Start();
    }

    public void MarkSynced(string message)
    {
        SetFinalState(OperationStatusKind.Synced, message, ThemeResourceBindings.TextSuccess);
    }

    public void MarkStale(string message)
    {
        SetFinalState(OperationStatusKind.Stale, message, ThemeResourceBindings.TextWarning);
    }

    public void MarkFailed(string message)
    {
        SetFinalState(OperationStatusKind.Failed, message, ThemeResourceBindings.TextError);
    }

    public void MarkIdle(string message)
    {
        SetFinalState(OperationStatusKind.Idle, message, ThemeResourceBindings.TextMuted);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _progressTimer.Stop();
        _longRunningTimer.Stop();
        _cancelAction = null;
    }

    private void SetFinalState(OperationStatusKind kind, string message, string foregroundResource)
    {
        Kind = kind;
        _cancelAction = null;
        _message.Text = message;
        _message.BindThemeResource(TextBlock.ForegroundProperty, foregroundResource);
        _progressTimer.Stop();
        _longRunningTimer.Stop();
        _progress.IsVisible = false;
        _longRunningHint.IsVisible = false;
        _cancel.IsVisible = false;
        _cancel.IsEnabled = true;
    }
}
