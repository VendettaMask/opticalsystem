using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class LensEditorPanel
{
    private readonly IMultiConfigurationService? _configurations;
    private readonly TextBlock _configurationPosition = new()
    {
        Name = "LensConfigurationPosition",
        FontWeight = Avalonia.Media.FontWeight.Bold,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Button _previousConfiguration = PropertyHeaderButton("PreviousLensConfiguration", "chevron-left", "上一个结构");
    private readonly Button _nextConfiguration = PropertyHeaderButton("NextLensConfiguration", "chevron-right", "下一个结构");
    private readonly TextBlock _configurationMessage = new()
    {
        Name = "LensConfigurationMessage",
        IsVisible = false,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 0)
    };
    private bool _configurationSwitchPending;

    private Control BuildConfigurationNavigation()
    {
        _previousConfiguration.Click += (_, _) => NavigateConfiguration(-1);
        _nextConfiguration.Click += (_, _) => NavigateConfiguration(1);
        _configurationMessage.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextError);
        return new StackPanel
        {
            Name = "LensConfigurationToolbar",
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _previousConfiguration, _configurationPosition, _nextConfiguration }
        };
    }

    private void RefreshConfigurationNavigation()
    {
        if (_configurations is null || _disposed) return;
        // This is read-only navigation metadata; do not calculate every
        // configuration's optical metrics just to paint a toolbar.
        var state = _configurations.GetNavigationState();
        _configurationPosition.Text = $"结构 {state.ActiveIndex + 1}/{state.Count}";
        AutomationProperties.SetName(_configurationPosition, _configurationPosition.Text);
        SetNavigationButton(_previousConfiguration, !_configurationSwitchPending && state.ActiveIndex > 0,
            _configurationSwitchPending ? "正在切换结构" : state.Count == 1 ? "只有一个结构，无需切换" : "已是第一个结构");
        SetNavigationButton(_nextConfiguration, !_configurationSwitchPending && state.ActiveIndex + 1 < state.Count,
            _configurationSwitchPending ? "正在切换结构" : state.Count == 1 ? "只有一个结构，无需切换" : "已是最后一个结构");
    }

    private static void SetNavigationButton(Button button, bool enabled, string unavailable)
    {
        ControlAvailability.Set(button, enabled, unavailable);
        ToolTip.SetTip(button, enabled
            ? button.Name == "PreviousLensConfiguration" ? "上一个结构" : "下一个结构"
            : unavailable);
    }

    private void NavigateConfiguration(int offset)
    {
        if (_disposed || _configurations is null || _configurationSwitchPending) return;
        if (!_grid.CommitEdit(DataGridEditingUnit.Cell, true) || !_grid.CommitEdit(DataGridEditingUnit.Row, true)) return;
        // Commit native numeric LostFocus editors while the old configuration
        // is still active. Their queued refresh must run before navigation.
        _grid.Focus();
        var revision = _events.Revision;
        var state = _configurations.GetNavigationState();
        var target = state.ActiveIndex + offset;
        if (target < 0 || target >= state.Count) return;
        _configurationSwitchPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_disposed || revision != _events.Revision) return;
                if (_grid.GetVisualDescendants().OfType<Control>().Any(DataValidationErrors.GetHasErrors))
                {
                    _configurationMessage.Text = "请先修正当前单元格的输入，或按 Esc 还原后再切换结构。";
                    _configurationMessage.IsVisible = true;
                    return;
                }
                _configurationMessage.IsVisible = false;
                _configurations.Activate(target);
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException)
            {
                _configurationMessage.Text = $"未切换结构：{error.Message}";
                _configurationMessage.IsVisible = true;
            }
            finally
            {
                _configurationSwitchPending = false;
                RefreshConfigurationNavigation();
            }
        });
    }
}
