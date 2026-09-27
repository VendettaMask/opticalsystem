namespace OptilandWorkbench.CoatingDesign.Engine;

/// <summary>A laboratory-owned revision gate. No Optic, document manager or global optimizer state.</summary>
public sealed class ExperimentSession : IDisposable
{
    private readonly object _gate = new();
    private Experiment _current;
    private long _generation;
    private CancellationTokenSource? _active;
    public ExperimentSession(Experiment experiment) => _current = experiment.Snapshot();
    public Experiment Current { get { lock (_gate) return _current.Snapshot(); } }

    public void Replace(Experiment experiment)
    {
        lock (_gate)
        {
            InvalidateTask();
            _current = experiment.Snapshot();
        }
    }
    public void Invalidate()
    {
        lock (_gate)
        {
            InvalidateTask();
            _current = _current with { Result = null, Candidates = [], Tolerance = null };
        }
    }
    public void Cancel() { lock (_gate) InvalidateTask(); }

    public async Task<bool> RunAsync(Func<Experiment, CancellationToken, Task<Experiment>> work)
    {
        Experiment snapshot; long generation; CancellationTokenSource source;
        lock (_gate)
        {
            InvalidateTask();
            source = _active = new CancellationTokenSource();
            generation = _generation;
            snapshot = _current.Snapshot();
        }
        try
        {
            var result = await work(snapshot, source.Token).ConfigureAwait(false);
            lock (_gate)
            {
                if (source.IsCancellationRequested || generation != _generation || snapshot.Id != _current.Id) return false;
                _current = result.Snapshot();
                return true;
            }
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { return false; }
        finally
        {
            lock (_gate) { if (ReferenceEquals(_active, source)) _active = null; }
            source.Dispose();
        }
    }
    private void InvalidateTask() { _generation++; _active?.Cancel(); }
    public void Dispose() => Cancel();
}
