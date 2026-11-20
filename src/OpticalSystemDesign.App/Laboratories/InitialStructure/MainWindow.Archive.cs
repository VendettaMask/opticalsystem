using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.App.Laboratories.InitialStructure;

internal sealed record InitialStructureArchiveInput(int Version, InitialStructureSpecification Specification, FlatStartSearchOptions Options);

public sealed partial class MainWindow : IProjectArchiveParticipant
{
    internal AutonomousTopologyWindow OpenAutonomousForArchive()
    {
        if (_autonomousWindow is null)
        {
            var window = new AutonomousTopologyWindow(); _autonomousWindow = window;
            window.Closed += (_, _) => _autonomousWindow = null; window.Show(this);
        }
        return _autonomousWindow;
    }
    public async Task<IReadOnlyList<ProjectArchiveFileDto>> CaptureArchiveAsync(string directory, CancellationToken cancellationToken)
    {
        var files = new List<ProjectArchiveFileDto>
        {
            await ProjectArchiveArtifacts.WriteAsync(directory, "labs/initial/input.json", "initial-input",
                new InitialStructureArchiveInput(1, BuildSpecification(), BuildOptions()), token: cancellationToken)
        };
        if (_current is { } current)
        {
            var path = Path.Combine(directory, "labs/initial/search.flat-start.json");
            await new FlatStartSearchCheckpointStore().SaveAsync(current, path, cancellationToken);
            files.Add(ProjectArchiveArtifacts.File("labs/initial/search.flat-start.json", "initial-result", path, true));
        }
        return files;
    }
    internal async Task OpenArchiveAsync(string path, bool result)
    {
        if (_busy || _running) throw new InvalidOperationException("请先停止当前计算。");
        if (result) ShowLoaded(await new FlatStartSearchCheckpointStore().LoadAsync(path));
        else
        {
            var value = ProjectArchiveArtifacts.Read<InitialStructureArchiveInput>(path);
            if (value.Version != 1) throw new InvalidDataException("Unsupported initial-structure archive input.");
            ApplySpecification(value.Specification, value.Options);
        }
    }
}
