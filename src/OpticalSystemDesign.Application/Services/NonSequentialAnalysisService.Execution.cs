using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.NonSequential;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class NonSequentialAnalysisService
{
    private readonly object _executionGate = new();
    private NonSequentialTraceControl? _activeTraceControl;
    private NonSequentialTraceProgressDto? _traceProgress;
    private (DatabaseCacheKey Key, NonSequentialBranchIndex Index)? _branchIndex;

    public NonSequentialRayBranchDetailsDto GetRayDatabaseBranch(
        string path, long branchId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        path = Path.GetFullPath(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new NonSequentialRayDatabaseReader(stream);
        var key = DatabaseCacheKey.Create(path, "", reader.Header.SceneHash, stream.Length,
            reader.Header, reader.BranchCount);
        NonSequentialBranchIndex? index;
        lock (_cacheGate) index = _branchIndex is { } cached && cached.Key == key ? cached.Index : null;
        if (index is null || !reader.IsBranchIndexCompatible(index))
        {
            index = reader.BuildBranchIndex(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_cacheGate) _branchIndex = (key, index);
        }
        var branch = reader.ReadBranchById(branchId, index, cancellationToken)
            ?? throw new KeyNotFoundException($"光线数据库没有分支 {branchId}。");
        var parent = branch.ParentId is { } id ? reader.ReadBranchById(id, index, cancellationToken) : null;
        var children = reader.ReadChildBranches(branchId, index, count: 1000, cancellationToken: cancellationToken);
        var objects = reader.Header.Objects.ToDictionary(item => item.Id);
        return new(MapBranch(branch, objects), parent is null ? null : MapBranch(parent, objects),
            children.Branches.Select(child => MapBranch(child, objects)).ToArray(), children.Total);
    }

    public NonSequentialTraceProgressDto? GetTraceProgress()
    {
        lock (_executionGate)
            return _activeTraceControl is null ? null : _traceProgress is { } progress
                ? progress with { IsPaused = _activeTraceControl.IsPaused } : null;
    }

    public void PauseTrace()
    {
        lock (_executionGate) _activeTraceControl?.Pause();
    }

    public void ResumeTrace()
    {
        lock (_executionGate) _activeTraceControl?.Resume();
    }

    private sealed class TraceProgressObserver(Action<NonSequentialTraceProgress> report)
        : IProgress<NonSequentialTraceProgress>
    {
        public void Report(NonSequentialTraceProgress value) => report(value);
    }
}
