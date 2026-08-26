using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Core.NonSequential;

namespace OptilandWorkbench.Application.Services;

internal sealed class NonSequentialAnalysisSession : IDisposable
{
    private readonly object _gate = new();
    private readonly IWorkspaceEventStream _events;
    private Selection? _selection;

    public NonSequentialAnalysisSession(IWorkspaceEventStream events)
    {
        _events = events;
        _events.Changed += OnWorkspaceChanged;
    }

    public void Set(string path, string? filterExpression)
    {
        lock (_gate)
        {
            _selection = new Selection(Path.GetFullPath(path), filterExpression);
        }
    }

    public IReadOnlyList<NonSequentialRayBranch>? LoadLayoutBranches(
        NonSequentialDocument document,
        int maximumCount = 2_000)
    {
        var selection = Snapshot();
        if (selection is null || !File.Exists(selection.Path)) return null;
        using var stream = new FileStream(selection.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new NonSequentialRayDatabaseReader(stream);
        var filter = NonSequentialPathFilter.Parse(selection.FilterExpression);
        return reader.ReadBranches(filter, maximumCount).ToArray();
    }

    public IReadOnlyList<NonSequentialDetectorFrame>? ReconstructDetectors(NonSequentialDocument document)
    {
        var selection = Snapshot();
        if (selection is null || !File.Exists(selection.Path)) return null;
        using var stream = new FileStream(selection.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new NonSequentialRayDatabaseReader(stream);
        var filter = NonSequentialPathFilter.Parse(selection.FilterExpression);
        return NonSequentialDetectorReconstruction.Reconstruct(document, reader.ReadBranches(filter));
    }

    public void Dispose()
    {
        _events.Changed -= OnWorkspaceChanged;
    }

    private Selection? Snapshot()
    {
        lock (_gate) return _selection;
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args)
    {
        if (!args.FileSwitched) return;
        lock (_gate) _selection = null;
    }

    private sealed record Selection(string Path, string? FilterExpression);
}
