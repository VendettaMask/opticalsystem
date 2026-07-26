using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Manufacturing;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App;

public sealed partial class MainWindow
{
private async void OnWindowKeyDown(object? sender, KeyEventArgs args)
    {
        var commandModifier = args.KeyModifiers.HasFlag(KeyModifiers.Control)
            || args.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (args.Key == Key.K && commandModifier)
        {
            args.Handled = true;
            try
            {
                await ShowCommandPaletteAsync();
            }
            catch (Exception exception)
            {
                if (!_closed)
                {
                    _statusText.Text = $"命令面板打开失败：{exception.Message}";
                }
            }
        }
        else if (args.Key == Key.S && commandModifier)
        {
            args.Handled = true;
            try
            {
                await SaveProjectAsync();
            }
            catch (Exception exception)
            {
                if (!_closed)
                {
                    _statusText.Text = $"保存项目失败：{exception.Message}";
                }
            }
        }
    }

    private void RefreshStatus()
    {
        var snapshot = _application.Documents.GetSnapshot();
        Title = $"{snapshot.Name} - Optical System Design";
        _statusText.Text = $"{snapshot.Status}   |   {snapshot.SurfaceCount} 个表面   |   {snapshot.FieldCount} 个视场   |   {snapshot.WavelengthCount} 个波长";
        _eflText.Text = $"EFFL: {FormatMetric(snapshot.EffectiveFocalLength)}";
        _fNumberText.Text = $"F/#: {FormatMetric(snapshot.FNumber)}";
        _apertureText.Text = $"APER: {NumericDisplayFormatter.Format(snapshot.ApertureValue)}";
        _trackText.Text = $"TOTR: {FormatMetric(snapshot.TotalTrack)}";
    }

    private async Task SwitchDocumentAsync(Action createDocument)
    {
        await _panels.SaveCurrentSessionAsync();
        createDocument();
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args)
    {
        Dispatcher.UIThread.Post(RefreshStatus);
    }

    private void OnWorkspacePersistenceFailed(object? sender, WorkspacePersistenceFailedEventArgs args)
    {
        Dispatcher.UIThread.Post(() =>
            _statusText.Text = $"工作区自动保存失败：{args.Exception.Message}");
    }

    private static string FormatMetric(double value)
    {
        return NumericDisplayFormatter.Format(value);
    }

    private void ConfigureDisplaySettings()
    {
        _settings.NormalizeDisplaySettings();
        NumericDisplayFormatter.Configure(new NumericDisplayOptions(
            _settings.DecimalPlaces,
            _settings.UpperScientificExponent,
            _settings.LowerScientificExponent));
        DisplayTypography.Configure(_settings);
    }

    private async Task ShowDisplaySettingsAsync()
    {
        var dialog = new DisplaySettingsWindow(_settings);
        if (!await dialog.ShowDialog<bool>(this))
        {
            return;
        }

        ConfigureDisplaySettings();
        ApplyTheme(save: false);
        DisplayTypography.Apply(this);
        _panels.ApplyDisplaySettings();
        RefreshStatus();
        InvalidateVisual();
    }

    private void ApplyTheme(bool save = true)
    {
        Avalonia.Application.Current!.RequestedThemeVariant = _settings.Theme switch
        {
            "Dark" => ThemeVariant.Dark,
            "System" => ThemeVariant.Default,
            _ => ThemeVariant.Light
        };
        if (save)
        {
            _settings.Save();
        }
    }

    private static IBrush ThemeBrush(Control control, string key) =>
        control.TryFindResource(key, control.ActualThemeVariant, out var value)
        && value is IBrush brush
            ? brush
            : Brushes.Transparent;

    private void ResetLayout()
    {
        Width = 1440;
        Height = 900;
        _panels.ResetLayout();
        SaveLayout();
    }

    private Task SaveLayoutSlot(int slot)
    {
        return _panels.SaveLayoutSlotAsync(slot);
    }

    private async Task LoadLayoutSlot(int slot)
    {
        await _panels.LoadLayoutSlotAsync(slot);
        SaveLayout();
    }

    private async Task ShowCommandPaletteAsync()
    {
        await new CommandPaletteWindow(_actions).ShowDialog(this);
    }

    private void SaveLayout()
    {
        _settings.WindowWidth = Math.Max(MinWidth, Width);
        _settings.WindowHeight = Math.Max(MinHeight, Height);
        _settings.ApplyLayout(_panels.CaptureLayout());
        _settings.Save();
    }

    private MenuItem MenuItem(AppAction action)
    {
        var item = new MenuItem { Header = action.Text };
        item.Click += async (_, _) => await _actions.ExecuteAsync(action);
        return item;
    }

    private async void OnActionExecutionFailed(object? sender, ActionExecutionFailedEventArgs args)
    {
        if (_closed || _closeInProgress)
        {
            return;
        }

        var dialog = new Window
        {
            Title = "操作失败",
            Width = 560,
            Height = 260,
            MinWidth = 420,
            MinHeight = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var closeButton = new Button
        {
            Content = "关闭",
            MinWidth = 88,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        closeButton.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = args.Action.Text,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBox
                {
                    Text = args.Exception.Message,
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    MinHeight = 120
                },
                closeButton
            }
        };
        try
        {
            await dialog.ShowDialog(this);
        }
        catch (Exception exception)
        {
            if (!_closed)
            {
                _statusText.Text = $"操作失败：{args.Exception.Message}；错误窗口未能显示：{exception.Message}";
            }
        }
    }
}
