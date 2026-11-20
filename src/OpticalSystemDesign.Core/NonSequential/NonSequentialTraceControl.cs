namespace OpticalSystemDesign.Core.NonSequential;

public sealed record NonSequentialTraceProgress(
    long LaunchedRays,
    long TotalSourceRays,
    long CompletedBranches,
    long Segments,
    TimeSpan Elapsed,
    bool IsPaused,
    bool IsCompleted = false);

/// <summary>Cooperative pause gate shared by the caller and its trace.</summary>
public sealed class NonSequentialTraceControl
{
    private readonly object _gate = new();
    private TaskCompletionSource? _resume;

    public bool IsPaused
    {
        get { lock (_gate) return _resume is not null; }
    }

    public void Pause()
    {
        lock (_gate)
            _resume ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Resume()
    {
        TaskCompletionSource? resume;
        lock (_gate)
        {
            resume = _resume;
            _resume = null;
        }
        resume?.TrySetResult();
    }

    internal void Wait(CancellationToken cancellationToken)
    {
        Task? resume;
        lock (_gate) resume = _resume?.Task;
        resume?.WaitAsync(cancellationToken).GetAwaiter().GetResult();
        cancellationToken.ThrowIfCancellationRequested();
    }
}
