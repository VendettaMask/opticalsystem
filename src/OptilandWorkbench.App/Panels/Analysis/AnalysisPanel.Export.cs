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

    private static Button CommandButton(string iconName, string text, double minWidth) => new()
    {
        Content = new LocalIconLabel(iconName, text),
        MinWidth = minWidth
    };

    private static Button IconButton(string iconName, string tooltip)
    {
        var button = new Button
        {
            Content = new LocalIcon { IconName = iconName, Width = 18, Height = 18 },
            Width = 34,
            Height = 30,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0)
        };
        ToolTip.SetTip(button, tooltip);
        return button;
    }
}
