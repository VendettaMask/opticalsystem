using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.App.Panels;

namespace OpticalSystemDesign.App.Services;

public sealed partial class PanelManager
{
    internal async Task<IReadOnlyList<ProjectArchiveFileDto>> CaptureArchiveAsync(string directory, CancellationToken token)
    {
        var files = new List<ProjectArchiveFileDto>
        {
            await ProjectArchiveArtifacts.WriteAsync(directory, "settings/workspace.json", "workspace", CaptureSession(), token: token)
        };
        files.AddRange(await Factory.CaptureArchiveAsync(directory, token));
        return files;
    }
    internal bool ApplyArchiveLayout(string path) => TryRestore(ProjectArchiveArtifacts.Read<WorkspaceSession>(path));
}

public sealed partial class WorkspaceDockFactory
{
    internal async Task<IReadOnlyList<ProjectArchiveFileDto>> CaptureArchiveAsync(string directory, CancellationToken token)
    {
        var files = new List<ProjectArchiveFileDto>();
        var panel = TolerancingPanels().FirstOrDefault();
        if (panel is not null) files.AddRange(await panel.CaptureArchiveAsync(directory, token));
        var optimization = _content.Values.OfType<OptimizationPanel>().FirstOrDefault();
        if (optimization is not null) files.AddRange(await optimization.CaptureArchiveAsync(directory, token));
        return files;
    }
    internal void OpenArchiveOptimization(string path) => (_content.Values.OfType<OptimizationPanel>().FirstOrDefault()
        ?? throw new InvalidOperationException("Optimization editor is not open.")).OpenArchiveInput(path);
}
