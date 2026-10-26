using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class ViewerPanel
{
    private readonly Button _exportImageButton = CompactButton("camera", "导出图片（PNG）");
    private readonly TextBlock _imageExportFeedback = new()
    {
        Name = "ViewerImageExportFeedback",
        IsVisible = false,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 280,
        Margin = new Thickness(0, 4, 0, 0)
    };
    private bool _isRefreshing;
    private bool _isExportingImage;

    private void InitializeImageExport()
    {
        _exportImageButton.Name = "ExportViewerImage";
        _exportImageButton.BindThemeResource(Button.BackgroundProperty, ThemeResourceBindings.SettingsSurface);
        _exportImageButton.Click += async (_, _) => await ExportImageAsync();
        UpdateImageExportButton();
    }

    private string? ImageExportUnavailableReason()
    {
        if (_disposed) return "视图已关闭";
        if (_isExportingImage) return "正在导出图片";
        if (_isRefreshing) return "视图正在更新，完成后可导出图片";
        var scene = _scene.Scene;
        var hasScene = _dimension == SceneDimension.TwoDimensional
            ? scene?.TwoDimensional is { } two && (two.Surfaces.Count > 0 || two.LensElements.Count > 0 || two.Rays.Count > 0)
            : scene?.ThreeDimensional is { } three && (three.Surfaces.Count > 0 || three.LensElements.Count > 0 || three.Rays.Count > 0);
        if (!hasScene) return "请先生成视图后再导出图片";
        if (scene!.SourceRevision != _events.Revision) return _locked
            ? "视图已过期，请解锁并刷新后再导出图片" : "视图已过期，请刷新后再导出图片";
        return null;
    }

    private void UpdateImageExportButton()
    {
        var reason = ImageExportUnavailableReason();
        _exportImageButton.IsEnabled = reason is null;
        AutomationProperties.SetName(_exportImageButton, UiLocalization.Text("导出图片（PNG）"));
        ControlAvailability.Explain(_exportImageButton, UiLocalization.Text(reason ?? "导出图片（PNG）"));
    }

    internal RenderTargetBitmap CaptureViewerImage(double? renderScale = null)
    {
        if (ImageExportUnavailableReason() is { } reason)
            throw new InvalidOperationException(UiLocalization.Text(reason));
        _scene.UpdateLayout();
        var size = _scene.Bounds.Size;
        var scale = renderScale ?? TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(renderScale));
        if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0)
            throw new InvalidOperationException(UiLocalization.Text("视图尚未显示"));

        // Draw the live scene with its actual viewport/camera. Export-only siblings
        // contain complete legends and wrapped metadata, never settings or navigation.
        var page = new StackPanel { Width = size.Width, FlowDirection = FlowDirection };
        page.Children.Add(new SceneImageCanvas(_scene) { Width = size.Width, Height = size.Height, ClipToBounds = true });
        if (_glassLegend.IsVisible)
        {
            var entries = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var source in _glassLegendItems.Children.OfType<StackPanel>())
            {
                var swatch = (Border)source.Children[0];
                var label = (TextBlock)source.Children[1];
                var copy = CopyImageLabel(label);
                copy.MaxWidth = Math.Max(20, Math.Min(label.MaxWidth,
                    size.Width - _glassLegend.Padding.Left - _glassLegend.Padding.Right
                    - swatch.Width - source.Spacing - source.Margin.Left - source.Margin.Right));
                entries.Children.Add(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = source.Spacing,
                    Margin = source.Margin,
                    Children =
                    {
                        new Border { Width = swatch.Width, Height = swatch.Height, CornerRadius = swatch.CornerRadius,
                            Background = swatch.Background, BorderBrush = swatch.BorderBrush, BorderThickness = swatch.BorderThickness,
                            VerticalAlignment = VerticalAlignment.Center },
                        copy
                    }
                });
            }
            page.Children.Add(new Border { Background = _glassLegend.Background, Padding = _glassLegend.Padding, Child = entries });
        }
        if (_staleLayoutBanner.IsVisible)
            page.Children.Add(new Border
            {
                Background = _staleLayoutBanner.Background,
                Padding = new Thickness(10, 5),
                Child = CopyImageLabel(_staleLayoutMessage)
            });
        if (_summaryBar.IsVisible)
            page.Children.Add(new Border
            {
                Background = _summaryBar.Background,
                BorderBrush = _summaryBar.BorderBrush,
                BorderThickness = _summaryBar.BorderThickness,
                Padding = _summaryBar.Padding,
                Child = CopyImageLabel(_summary)
            });

        page.Measure(new Size(size.Width, double.PositiveInfinity));
        page.Arrange(new Rect(page.DesiredSize));
        page.UpdateLayout();
        var pixels = new PixelSize(checked((int)Math.Ceiling(page.Bounds.Width * scale)),
            checked((int)Math.Ceiling(page.Bounds.Height * scale)));
        var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        try { bitmap.Render(page); return bitmap; }
        catch { bitmap.Dispose(); throw; }
    }

    private static TextBlock CopyImageLabel(TextBlock source) => new()
    {
        Text = source.Text,
        FontFamily = source.FontFamily,
        FontSize = source.FontSize,
        FontWeight = source.FontWeight,
        FontStyle = source.FontStyle,
        Foreground = source.Foreground,
        FlowDirection = source.FlowDirection,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };

    private sealed class SceneImageCanvas(OpticSceneControl scene) : Control
    {
        public override void Render(DrawingContext context) => scene.RenderImageContent(context);
    }

    internal static void SaveViewerImage(RenderTargetBitmap bitmap, string path) =>
        BoundedApplicationFile.WriteAtomic(path, BoundedApplicationFile.MaximumImageDataBytes,
            "Viewer image export", stream => bitmap.Save(stream, PngBitmapEncoderOptions.Default));

    private string SuggestedImageName()
    {
        var snapshot = _scene.Scene!;
        var name = Path.GetFileNameWithoutExtension(string.IsNullOrWhiteSpace(snapshot.Summary.Path)
            ? snapshot.Summary.Name : snapshot.Summary.Path);
        var invalid = Path.GetInvalidFileNameChars();
        name = string.Concat(name.Select(character => invalid.Contains(character) ? '_' : character));
        if (string.IsNullOrWhiteSpace(name)) name = "OpticalSystemDesign";
        var title = _dimension == SceneDimension.TwoDimensional ? "二维图"
            : _presentationMode == ViewerPresentationMode.SolidModel ? "实体模型" : "三维布局";
        var configuration = snapshot.Configuration is { Count: > 1 } item ? $"-{item.Number}" : string.Empty;
        return $"{name}-{UiLocalization.Text(title)}{configuration}.png";
    }

    private async Task ExportImageAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (ImageExportUnavailableReason() is not null || topLevel is null) return;
        try
        {
            // Freeze the image and name before a picker can outlive a refresh/file switch.
            using var bitmap = CaptureViewerImage();
            var suggestedName = SuggestedImageName();
            _isExportingImage = true;
            _imageExportFeedback.IsVisible = false;
            UpdateImageExportButton();
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = UiLocalization.Text("导出视图图片"),
                SuggestedFileName = suggestedName,
                DefaultExtension = "png",
                ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType(UiLocalization.Text("PNG 图片"))
                    { Patterns = ["*.png"], MimeTypes = ["image/png"] }]
            });
            if (file is null) return;
            if (!file.Path.IsFile) throw new IOException(UiLocalization.Text("请选择本地文件位置"));
            SaveViewerImage(bitmap, file.Path.LocalPath);
            ShowImageExportFeedback(UiLocalization.Text("图片已导出"), error: false);
        }
        catch (Exception exception)
        {
            ShowImageExportFeedback(string.Format(UiLocalization.Text("图片导出失败：{0}"), exception.Message), error: true);
        }
        finally
        {
            _isExportingImage = false;
            UpdateImageExportButton();
        }
    }

    private void ShowImageExportFeedback(string message, bool error)
    {
        if (_disposed) return;
        _imageExportFeedback.Text = message;
        _imageExportFeedback.BindThemeResource(TextBlock.ForegroundProperty,
            error ? ThemeResourceBindings.TextError : ThemeResourceBindings.TextSuccess);
        _imageExportFeedback.IsVisible = true;
    }
}
