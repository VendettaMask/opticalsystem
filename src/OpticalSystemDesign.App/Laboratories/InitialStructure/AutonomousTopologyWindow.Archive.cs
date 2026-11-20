using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.App.Laboratories.InitialStructure;

public sealed partial class AutonomousTopologyWindow : IProjectArchiveParticipant
{
    public async Task<IReadOnlyList<ProjectArchiveFileDto>> CaptureArchiveAsync(string directory, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(directory, "labs/initial/autonomous"); Directory.CreateDirectory(folder);
        var plan = Path.Combine(folder, "plan.json");
        await AutonomousDesignFiles.SavePlanAsync(ReadPlan(), plan, cancellationToken);
        var files = new List<ProjectArchiveFileDto> { ProjectArchiveArtifacts.File("labs/initial/autonomous/plan.json", "autonomous-input", plan) };
        foreach (var (source, relative, kind) in new[]
        {
            (_session.RecordPath, "record.json", "autonomous-result"),
            (_session.PreparationCheckpointPath, "record.preparation.json", "preparation-result"),
            (_session.PreparationReportPath, "preparation-report.json", "report")
        })
            if (source is not null)
                files.Add(new("labs/initial/autonomous/" + relative, kind, source, File.Exists(source) ? new FileInfo(source).Length : 0, false, true));
        return files;
    }
    internal async Task OpenArchiveAsync(string path, bool result)
    {
        if (_session.IsBusy) throw new InvalidOperationException("请先停止当前计算。");
        if (result)
        {
            await _session.LoadAsync(path, ApplyPlan);
            _draft = _session.Checkpoint?.Plan ?? _session.PreparationCheckpoint!.Plan;
            ApplyPlan(_draft);
            if (_session.Checkpoint is { } main) Publish(main);
        }
        else
        {
            var plan = await AutonomousDesignFiles.LoadPlanAsync(path);
            ApplyPlan(plan); _session.Reset(); _draft = plan;
        }
        _generation++; Refresh();
    }
}
