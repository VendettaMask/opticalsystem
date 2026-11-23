using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Services;

internal static class NonSequentialFileImport
{
    internal static async Task PickAndImportAsync(Window owner, INonSequentialDocumentService service)
    {
        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = UiLocalization.Text("导入 Zemax NSC"),
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("ZMX") { Patterns = ["*.zmx"] }]
            });
        }
        catch (OperationCanceledException) { return; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            await CreateUnavailableDialog(string.Empty).ShowDialog(owner);
            return;
        }
        if (files.Count > 0) await ImportAsync(owner, service, files[0].Path.LocalPath);
    }

    internal static async Task ImportAsync(Window owner, INonSequentialDocumentService service, string path)
    {
        using var cancellation = new CancellationTokenSource();
        void OwnerClosed(object? sender, EventArgs args) => cancellation.Cancel();
        owner.Closed += OwnerClosed;
        try
        {
            var identity = service.GetDesignSceneIdentity();
            var result = await service.InspectZemaxNscAsync(path, cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            if (result.CanApply)
            {
                service.ApplyZemaxNscImport(result, identity);
                return;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or NotSupportedException or FormatException or OverflowException)
        {
            // Import diagnostics remain available to the maintenance reader and audit tool.
        }
        finally { owner.Closed -= OwnerClosed; }

        if (!cancellation.IsCancellationRequested)
            await CreateUnavailableDialog(path).ShowDialog(owner);
    }

    internal static Window CreateUnavailableDialog(string path)
    {
        var dialog = new Window
        {
            Title = "导入 Zemax NSC",
            Width = 440,
            Height = 200,
            MinWidth = 320,
            MinHeight = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        AutomationProperties.SetAutomationId(dialog, "nsc-import-unavailable");
        var close = new Button { Content = "关闭", IsDefault = true, IsCancel = true, MinWidth = 88, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => dialog.Close();
        var body = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "无法导入此文件。", TextWrapping = TextWrapping.Wrap },
                UiLocalization.Preserve(new TextBlock { Text = Path.GetFileName(path), TextWrapping = TextWrapping.Wrap })
            }
        };
        var root = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 12 };
        root.Children.Add(new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Grid.SetRow(close, 1);
        root.Children.Add(close);
        dialog.Content = root;
        ThemeChrome.ApplyDialogDecoration(dialog);
        return dialog;
    }
}
