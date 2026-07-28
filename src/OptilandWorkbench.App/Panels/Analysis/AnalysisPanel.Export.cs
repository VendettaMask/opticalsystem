using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Panels;

public sealed partial class AnalysisPanel
{
    private async Task CopyReportAsync()
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null && !string.IsNullOrWhiteSpace(_view?.ReportText))
            {
                await clipboard.SetTextAsync(_view.ReportText);
                _stateText.Text = "报告文本已复制";
            }
        }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                _stateText.Text = $"复制失败：{exception.Message}";
            }
        }
    }

    private async Task ExportReportAsync()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (_view is null || topLevel is null)
            {
                return;
            }

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出分析报告",
                SuggestedFileName = $"{_view.Name}.txt",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("文本报告")
                    {
                        Patterns = new[] { "*.txt" },
                        MimeTypes = new[] { "text/plain" }
                    }
                }
            });
            if (file is not null)
            {
                await File.WriteAllTextAsync(file.Path.LocalPath, _view.ReportText);
                _stateText.Text = "报告文本已导出";
            }
        }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                _stateText.Text = $"导出失败：{exception.Message}";
            }
        }
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(10, 0, 4, 0)
    };

    private static Button CommandButton(string iconName, string text, double minWidth)
    {
        var button = new Button
        {
            Content = new LocalIconLabel(iconName, text),
            MinWidth = minWidth
        };
        StyleToolbarButton(button, iconOnly: false);
        return button;
    }

    private static Button IconButton(string iconName, string tooltip)
    {
        var button = new Button
        {
            Content = new LocalIcon { IconName = iconName, Width = 17, Height = 17 },
            MinWidth = 0,
            MinHeight = 0
        };
        StyleToolbarButton(button, iconOnly: true);
        ToolTip.SetTip(button, tooltip);
        return button;
    }

    private static void StyleToolbarButton(Button button, bool iconOnly)
    {
        button.Height = 28;
        button.MinHeight = 28;
        button.Padding = iconOnly ? new Thickness(0) : new Thickness(7, 2);
        button.CornerRadius = new CornerRadius(4);
        button.BorderThickness = new Thickness(1);
        if (iconOnly)
        {
            button.Width = 30;
        }

        button.Background = Brushes.Transparent;
        button.BorderBrush = Brushes.Transparent;
        button.PointerEntered += (_, _) =>
        {
            button.Background = ToolbarBrush(button, ThemeResourceBindings.Hover);
            button.BorderBrush = ToolbarBrush(button, ThemeResourceBindings.HoverBorder);
        };
        button.PointerExited += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
        };
    }

    private static IBrush ToolbarBrush(Control control, string resourceKey) =>
        control.TryFindResource(resourceKey, control.ActualThemeVariant, out var value)
        && value is IBrush brush
            ? brush
            : Brushes.Transparent;
}
