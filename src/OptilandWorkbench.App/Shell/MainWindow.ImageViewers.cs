using Avalonia.Platform.Storage;
using OptilandWorkbench.App.Panels;

namespace OptilandWorkbench.App;

public sealed partial class MainWindow
{
    private static readonly FilePickerFileType ZemaxImageFileType = new("Zemax IMA/BIM 图像")
    {
        Patterns = new[] { "*.ima", "*.IMA", "*.bim", "*.BIM" },
        MimeTypes = new[] { "application/octet-stream", "text/plain" }
    };

    private static readonly FilePickerFileType BitmapImageFileType = new("位图图像")
    {
        Patterns = new[]
        {
            "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.tif", "*.tiff", "*.webp"
        },
        MimeTypes = new[] { "image/*" }
    };

    private async Task OpenImaBimViewerAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开 IMA 或 BIM 图像",
            AllowMultiple = false,
            FileTypeFilter = new[] { ZemaxImageFileType }
        });
        if (files.Count == 0)
        {
            return;
        }

        try
        {
            new ImageFileViewerWindow(files[0].Path.LocalPath, zemaxImage: true).Show(this);
        }
        catch (Exception exception)
        {
            _statusText.Text = $"无法打开 IMA/BIM 图像：{exception.Message}";
        }
    }

    private async Task OpenBitmapViewerAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开位图图像",
            AllowMultiple = false,
            FileTypeFilter = new[] { BitmapImageFileType }
        });
        if (files.Count == 0)
        {
            return;
        }

        try
        {
            new ImageFileViewerWindow(files[0].Path.LocalPath, zemaxImage: false).Show(this);
        }
        catch (Exception exception)
        {
            _statusText.Text = $"无法打开位图：{exception.Message}";
        }
    }
}
