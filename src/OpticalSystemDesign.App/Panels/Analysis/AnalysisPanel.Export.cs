using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Formatting;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class AnalysisPanel
{
    private ResponsiveAnalysisPlotHost? ImageExportPage()
    {
        if (_view is not { Outcome: AnalysisOutcome.Success }) return null;
        if (_resultHost.Content is ResponsiveAnalysisPlotHost page)
            return _view.PresentationKind == AnalysisPresentationKind.CardinalPoints ? page : null;
        if (_resultHost.Content is not TabControl { SelectedIndex: 0 } tabs
            || tabs.SelectedItem is not TabItem { Content: ResponsiveAnalysisPlotHost plot }) return null;

        // Some plots consume a table rather than series. Text-only reports also
        // have a fallback plot tab, which must not offer an empty image export.
        var hasPlot = _view.Series.Any(series => series.Points.Count > 0)
            || _view.PlotPanes.Any(pane => pane.Series.Any(series => series.Points.Count > 0))
            || (_view.PresentationKind == AnalysisPresentationKind.SeidelDiagram && _view.Table?.Rows.Count > 0);
        return hasPlot ? plot : null;
    }

    private void UpdateImageExportButton()
    {
        _exportImageButton.IsVisible = ImageExportPage() is not null;
        _exportImageButton.IsEnabled = !_isRunning && !_disposed;
        var explanation = _isRunning ? "正在计算，完成后可导出图片" : "导出图片（PNG）";
        ControlAvailability.Explain(_exportImageButton, explanation);
    }

    internal RenderTargetBitmap CapturePlotImage()
    {
        var page = ImageExportPage() ?? throw new InvalidOperationException("请先显示分析绘图页");
        page.UpdateLayout();
        var content = page.Child ?? throw new InvalidOperationException("分析图像尚未显示");
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var size = content.Bounds.Size;
        if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0)
            throw new InvalidOperationException("分析图像尚未显示");

        var pixels = new PixelSize(checked((int)Math.Ceiling(size.Width * scale)), checked((int)Math.Ceiling(size.Height * scale)));
        if (pixels.Width > 32_768 || pixels.Height > 32_768 || (long)pixels.Width * pixels.Height > 33_554_432)
            throw new InvalidOperationException("分析图片超过尺寸上限，请减少同时显示的子图数量后重试");
        var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        try
        {
            // Render the existing page to retain its camera, zoom, legend and
            // complete footer. The toolbar, settings overlay and result tabs
            // are siblings/ancestors and never enter the exported image.
            bitmap.Render(content);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    internal static void SavePlotImage(RenderTargetBitmap bitmap, string path) =>
        BoundedApplicationFile.WriteAtomic(path, BoundedApplicationFile.MaximumImageDataBytes,
            "Analysis image export", stream => bitmap.Save(stream, PngBitmapEncoderOptions.Default));

    private async Task ExportImageAsync()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (_isRunning || _disposed || ImageExportPage() is null || topLevel is null) return;
            // Capture at the click, before a native picker can outlive a result
            // refresh, file switch or closed document.
            using var bitmap = CapturePlotImage();
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = UiLocalization.Text("导出分析图片"),
                SuggestedFileName = $"{_view!.Name}.png",
                DefaultExtension = "png",
                ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType(UiLocalization.Text("PNG 图片"))
                {
                    Patterns = ["*.png"],
                    MimeTypes = ["image/png"]
                }]
            });
            if (file is null) return;
            SavePlotImage(bitmap, file.Path.LocalPath);
            if (!_disposed) _operationStatus.MarkSynced("分析图片已导出");
        }
        catch (Exception exception)
        {
            if (!_disposed) _operationStatus.MarkFailed($"导出失败：{exception.Message}");
        }
    }

    private async Task CopyReportAsync()
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null && !string.IsNullOrWhiteSpace(_view?.ReportText))
            {
                await clipboard.SetTextAsync(_view.ReportText);
                _operationStatus.MarkSynced("报告文本已复制");
            }
        }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                _operationStatus.MarkFailed($"复制失败：{exception.Message}");
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
                    },
                    new FilePickerFileType("CSV 数据")
                    {
                        Patterns = new[] { "*.csv" },
                        MimeTypes = new[] { "text/csv" }
                    }
                }
            });
            if (file is not null)
            {
                var isCsv = string.Equals(
                    Path.GetExtension(file.Path.LocalPath),
                    ".csv",
                    StringComparison.OrdinalIgnoreCase);
                var content = isCsv
                    ? AnalysisCsvFormatter.Format(_view)
                    : _view.ReportText;
                await BoundedApplicationFile.WriteAllTextAtomicAsync(
                    file.Path.LocalPath,
                    content,
                    BoundedApplicationFile.MaximumExportBytes,
                    "Analysis export");
                _operationStatus.MarkSynced(isCsv ? "CSV 数据已导出" : "报告文本已导出");
            }
        }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                _operationStatus.MarkFailed($"导出失败：{exception.Message}");
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
        AutomationProperties.SetName(button, text);
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
        AutomationProperties.SetName(button, tooltip);
        StyleToolbarButton(button, iconOnly: true);
        ToolTip.SetTip(button, tooltip);
        return button;
    }

    private static void StyleToolbarButton(Button button, bool iconOnly)
    {
        button.Height = 28;
        button.MinHeight = 28;
        button.Padding = iconOnly ? new Thickness(0) : new Thickness(7, 2);
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
