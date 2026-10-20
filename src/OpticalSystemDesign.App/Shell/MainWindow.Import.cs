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
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Formatting;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Manufacturing;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App;

public sealed partial class MainWindow
{
    private async Task ImportZemaxAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入 Zemax 光学系统",
            AllowMultiple = false,
            FileTypeFilter = new[] { ZemaxOpticFileType }
        });
        if (files.Count > 0)
        {
            if (!await ConfirmUnsavedChangesAsync("导入 Zemax 光学系统"))
            {
                return;
            }

            await _panels.SaveCurrentSessionAsync();
            await _application.Documents.OpenAsync(files[0].Path.LocalPath);
        }
    }

    private static string UserGlassCatalogDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpticalSystemDesign",
        "glass-catalogs");

    private static string BundledLensLibraryDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "LensLibrary");

}
