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
            await _panels.SaveCurrentSessionAsync();
            await _application.Documents.OpenAsync(files[0].Path.LocalPath);
            if (_application.MultiConfiguration.GetRows().Count > 1)
            {
                _panels.Show(WorkspacePanelId.MultiConfiguration);
            }
        }
    }

    private static string UserGlassCatalogDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptilandWorkbench",
        "glass-catalogs");

    private static string BundledLensLibraryDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "LensLibrary");

    private sealed record AnalysisRibbonCommand(
        string Id,
        string Name,
        string Label,
        string IconName,
        string Group,
        AnalysisRibbonCommandKind Kind = AnalysisRibbonCommandKind.Analysis);

    private enum AnalysisRibbonCommandKind
    {
        Analysis,
        ImaBimViewer,
        BitmapViewer
    }

    private sealed record AnalysisRibbonMenu(
        string Group,
        string Label,
        string IconName,
        IReadOnlyList<string> CommandIds);
}
