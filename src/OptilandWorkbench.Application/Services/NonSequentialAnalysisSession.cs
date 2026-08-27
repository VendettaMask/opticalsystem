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

    public event EventHandler? Changed;

    public void Publish(NonSequentialTraceSessionDto session, bool ownsDatabase)
    {
        ArgumentNullException.ThrowIfNull(session);
        Selection? previous;
        lock (_gate)
        {
            previous = _selection;
            _selection = new Selection(
                Path.GetFullPath(session.RayDatabasePath),
                session.FilterExpression,
                session,
                ownsDatabase);
        }
        DeleteOwned(previous, session.RayDatabasePath);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Set(string path, string? filterExpression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        Selection? previous = null;
        lock (_gate)
        {
            if (_selection is { } existing
                && fullPath.Equals(existing.Path, StringComparison.OrdinalIgnoreCase))
            {
                _selection = existing with
                {
                    FilterExpression = filterExpression,
                    Session = existing.Session with { FilterExpression = filterExpression }
                };
            }
            else
            {
                previous = _selection;
                using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new NonSequentialRayDatabaseReader(stream);
                var splitting = reader.Header.SplittingMode is { } storedSplitting
                    ? (OptilandWorkbench.Application.Contracts.NonSequentialSplittingMode)(int)storedSplitting
                    : reader.Header.TraceSettings.SplitFresnelRays
                        ? OptilandWorkbench.Application.Contracts.NonSequentialSplittingMode.FullFresnel
                        : OptilandWorkbench.Application.Contracts.NonSequentialSplittingMode.None;
                var session = new NonSequentialTraceSessionDto(
                    Guid.NewGuid(), NonSequentialTraceSessionState.Completed,
                    reader.Header.SceneHash, reader.Header.SourceRevision,
                    reader.Header.CreatedUtc, DateTimeOffset.UtcNow, 1,
                    reader.Header.RandomSeed, splitting, Array.Empty<Guid>(),
                    reader.BranchCount, 0, 0, 0, 0, 0, 0, 0, 0,
                    TimeSpan.Zero, fullPath, false, false, filterExpression,
                    Array.Empty<string>());
                _selection = new Selection(fullPath, filterExpression, session, false);
            }
        }
        DeleteOwned(previous, fullPath);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public NonSequentialTraceSessionDto? Snapshot(NonSequentialDocument document, long revision)
    {
        lock (_gate)
        {
            if (_selection is null || !File.Exists(_selection.Path)) return null;
            var stale = !_selection.Session.SceneHash.Equals(
                NonSequentialSceneHasher.Compute(document),
                StringComparison.OrdinalIgnoreCase);
            return _selection.Session with
            {
                IsStale = stale,
                FilterExpression = _selection.FilterExpression
            };
        }
    }

    public string? SelectedPath
    {
        get { lock (_gate) return _selection?.Path; }
    }

    public string? SelectedFilter
    {
        get { lock (_gate) return _selection?.FilterExpression; }
    }

    public IReadOnlyList<NonSequentialRayBranch>? LoadLayoutBranches(
        NonSequentialDocument document,
        int maximumCount = 2_000)
    {
        var selection = SelectionSnapshot();
        if (selection is null || !File.Exists(selection.Path)) return null;
        using var stream = new FileStream(selection.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new NonSequentialRayDatabaseReader(stream);
        var filter = NonSequentialPathFilter.Parse(selection.FilterExpression);
        return reader.ReadBranches(filter, maximumCount).ToArray();
    }

    public IReadOnlyList<NonSequentialDetectorFrame>? ReconstructDetectors(NonSequentialDocument document)
    {
        var selection = SelectionSnapshot();
        if (selection is null || !File.Exists(selection.Path)) return null;
        using var stream = new FileStream(selection.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new NonSequentialRayDatabaseReader(stream);
        var filter = NonSequentialPathFilter.Parse(selection.FilterExpression);
        return NonSequentialDetectorReconstruction.Reconstruct(document, reader.ReadBranches(filter));
    }

    public void Clear()
    {
        Selection? previous;
        lock (_gate)
        {
            previous = _selection;
            _selection = null;
        }
        DeleteOwned(previous);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _events.Changed -= OnWorkspaceChanged;
        Clear();
    }

    private Selection? SelectionSnapshot()
    {
        lock (_gate) return _selection;
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args)
    {
        if (args.FileSwitched) Clear();
        else Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void DeleteOwned(Selection? selection, string? exceptPath = null)
    {
        if (selection is not { OwnsDatabase: true }) return;
        if (exceptPath is not null
            && Path.GetFullPath(exceptPath).Equals(selection.Path, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            if (File.Exists(selection.Path)) File.Delete(selection.Path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record Selection(
        string Path,
        string? FilterExpression,
        NonSequentialTraceSessionDto Session,
        bool OwnsDatabase);
}
