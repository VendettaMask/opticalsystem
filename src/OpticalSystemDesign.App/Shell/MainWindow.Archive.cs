using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.App;

public sealed partial class MainWindow
{
    private static FilePickerFileType ArchiveFileType => new(UiLocalization.Text("STARPACK 工程归档")) { Patterns = new[] { "*.starpack" } };

    private async Task CreateProjectArchiveAsync()
    {
        var window = new ProjectArchiveWindow(_application.ProjectArchives, CollectArchiveFilesAsync);
        await window.ShowDialog(this);
    }
    private async Task RestoreProjectArchiveAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = UiLocalization.Text("恢复工程归档"), FileTypeFilter = new[] { ArchiveFileType } });
        if (files.Count > 0) await OpenProjectArchiveAsync(files[0].Path.LocalPath);
    }
    internal async Task OpenProjectArchiveAsync(string path)
    {
        var window = new ProjectArchiveWindow(_application.ProjectArchives, archive: path,
            confirm: async () =>
            {
                if (!await ConfirmUnsavedChangesAsync("恢复工程归档")) return false;
                await _panels.SaveCurrentSessionAsync(); return true;
            }, open: OpenArchivedResourceAsync);
        await window.ShowDialog(this);
    }
    private async Task<IReadOnlyList<ProjectArchiveFileDto>> CollectArchiveFilesAsync(string directory, CancellationToken token)
    {
        var revision = _application.Events.Revision;
        var files = new List<ProjectArchiveFileDto>(await _panels.CaptureArchiveAsync(directory, token));
        foreach (var window in ArchiveParticipants(this).ToArray())
            files.AddRange(await window.CaptureArchiveAsync(directory, token));
        if (_application.Events.Revision != revision) throw new OperationCanceledException("工程已变化，请刷新归档清单。", token);
        return files;
    }
    private static IEnumerable<IProjectArchiveParticipant> ArchiveParticipants(Window owner)
    {
        foreach (var window in owner.OwnedWindows.ToArray())
        {
            if (window is IProjectArchiveParticipant participant) yield return participant;
            foreach (var child in ArchiveParticipants(window)) yield return child;
        }
    }
    private async Task OpenArchivedResourceAsync(ProjectArchiveRestoreDto restored, ProjectArchiveFileDto file)
    {
        switch (file.Kind)
        {
            case "camera-input":
                ShowCameraImaging(); _cameraImagingWindow!.OpenArchiveInput(file.SourcePath, restored.Directory); return;
            case "camera-result":
                ShowCameraImaging(); await _cameraImagingWindow!.OpenExperimentFromPathAsync(file.SourcePath); return;
            case "assembly-input":
                ShowOpticalAssembly(); _opticalAssemblyWindow!.OpenArchiveInput(file.SourcePath); return;
            case "assembly-result":
                ShowOpticalAssembly(); _opticalAssemblyWindow!.OpenArchiveResult(file.SourcePath); return;
            case "coating-input":
            case "coating-result":
                await Laboratories.OpenCoatingDesign().OpenArchiveAsync(file.SourcePath); return;
            case "initial-input":
            case "initial-result":
                await Laboratories.OpenInitialStructure().OpenArchiveAsync(file.SourcePath, file.Kind == "initial-result"); return;
            case "autonomous-input":
            case "autonomous-result":
            case "preparation-result":
                await Laboratories.OpenInitialStructure().OpenAutonomousForArchive().OpenArchiveAsync(file.SourcePath, file.Kind != "autonomous-input"); return;
            case "workspace": _panels.ApplyArchiveLayout(file.SourcePath); return;
            case "tolerances":
                _panels.Factory.TolerancingEditor().OpenArchiveInput(file.SourcePath); _panels.Show(WorkspacePanelId.Tolerancing); return;
            case "optimization-input":
                _panels.Show(WorkspacePanelId.Optimization); _panels.Factory.OpenArchiveOptimization(file.SourcePath); return;
            case "ray-database": new NonSequentialRayDatabaseWindow(_application.NonSequentialAnalysis, file.SourcePath).Show(this); return;
        }
        var extension = Path.GetExtension(file.SourcePath).ToLowerInvariant();
        if (extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tif" or ".tiff")
        { new ImageFileViewerWindow(file.SourcePath, false).Show(this); return; }
        if (extension == ".pdf")
        { Process.Start(new ProcessStartInfo(file.SourcePath) { UseShellExecute = true }); return; }
        if (extension is ".json" or ".csv" or ".txt" or ".md" or ".tsv")
        {
            var content = BoundedApplicationFile.ReadAllText(file.SourcePath, BoundedApplicationFile.MaximumSettingsBytes, "Archive text preview");
            var window = new Window
            {
                Title = Path.GetFileName(file.SourcePath),
                Width = 900,
                Height = 650,
                Content = UiLocalization.Preserve(new TextBox { Text = content, IsReadOnly = true, AcceptsReturn = true })
            };
            ThemeChrome.ApplyDialogDecoration(window); window.Show(this); return;
        }
        _statusText.Text = UiLocalization.Text("文件已恢复：") + file.SourcePath;
    }
}
