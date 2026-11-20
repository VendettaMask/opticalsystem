using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

public sealed partial class MainWindow : IProjectArchiveParticipant
{
    public async Task<IReadOnlyList<ProjectArchiveFileDto>> CaptureArchiveAsync(string directory, CancellationToken cancellationToken)
    {
        var design = ReadInputs().Snapshot();
        var folder = Path.Combine(directory, "labs/coating"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "input.coating.json");
        await ExperimentStore.SaveAsync(design with { Result = null, Candidates = null, Tolerance = null }, path, cancellationToken);
        var files = new List<ProjectArchiveFileDto> { ProjectArchiveArtifacts.File("labs/coating/input.coating.json", "coating-input", path) };
        if (design.Result is not null || design.Tolerance is not null || design.Candidates is { Length: > 0 })
        {
            var result = Path.Combine(folder, "result.coating.json");
            await ExperimentStore.SaveAsync(design, result, cancellationToken);
            files.Add(ProjectArchiveArtifacts.File("labs/coating/result.coating.json", "coating-result", result, true));
        }
        return files;
    }
    internal async Task<bool> OpenArchiveAsync(string path)
    {
        if (!await ConfirmUnsavedChangesAsync("打开另一份镀膜实验")) return false;
        var value = await ExperimentStore.OpenAsync(path);
        _session.Invalidate(); _historyCurrent = null; _undo.Clear(); _redo.Clear(); _historyKey = null;
        Load(value, markDirty: false); return true;
    }
}
