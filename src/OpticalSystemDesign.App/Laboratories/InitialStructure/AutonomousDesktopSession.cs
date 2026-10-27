using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.App.Laboratories.InitialStructure;

/// <summary>In-process desktop lifetime. Loading is read-only; only RunAsync dispatches optical work.</summary>
internal sealed class AutonomousDesktopSession(string directory)
{
    private readonly AutonomousSearchCheckpointStore _store = new();
    private AutonomousSearchCheckpoint? _checkpoint;
    private CancellationTokenSource? _cancellation;
    private int _busy;
    public AutonomousSearchCheckpoint? Checkpoint => Volatile.Read(ref _checkpoint);
    public bool IsBusy => Volatile.Read(ref _busy) != 0;
    public string? RecordPath { get; private set; }
    public Task Pending { get; private set; } = Task.CompletedTask;

    public void Reset()
    {
        EnsureIdle();
        _checkpoint = null; RecordPath = null;
    }

    public async Task LoadAsync(string path, Action<AutonomousSearchPlan>? validate = null)
    {
        EnsureIdle();
        var loaded = await _store.LoadAsync(path);
        validate?.Invoke(loaded.Plan);
        _checkpoint = loaded;
        RecordPath = Path.GetFullPath(path);
    }

    public Task RunAsync(AutonomousSearchPlan plan, bool resume, Action<AutonomousSearchCheckpoint> publish,
        Action<AutonomousSearchPending, FixedTopologyStep>? progress = null)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) throw new InvalidOperationException("An autonomous desktop operation is already active.");
        _cancellation = new();
        Pending = ExecuteAsync();
        return Pending;

        async Task ExecuteAsync()
        {
            try
            {
                if (!resume)
                {
                    var id = "autonomous-" + Guid.NewGuid().ToString("N");
                    var created = AutonomousSearchService.Create(id, plan) with { Revision = 1 };
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, id + ".autonomous.json");
                    await _store.SaveAsync(created, path, 0, _cancellation.Token);
                    _checkpoint = created; RecordPath = path;
                    publish(created);
                }
                var source = Checkpoint ?? throw new InvalidOperationException("No autonomous checkpoint has been loaded.");
                if (source.PlanFingerprint != ContentFingerprint.Compute(plan)) throw new InvalidDataException("Cannot resume a changed autonomous task.");
                var revision = source.Revision;
                await Task.Run(() =>
                {
                    var endpoint = AutonomousSearchService.Run(source, Save, _cancellation.Token, progress);
                    _checkpoint = endpoint;
                    publish(endpoint);

                    void Save(AutonomousSearchCheckpoint next)
                    {
                        // Persistence finishes before the engine may dispatch its reserved phase.
                        _store.SaveAsync(next, RecordPath!, revision).AsTask().GetAwaiter().GetResult();
                        revision = next.Revision;
                        Volatile.Write(ref _checkpoint, next);
                        publish(next);
                    }
                });
            }
            finally
            {
                _cancellation.Dispose(); _cancellation = null;
                Volatile.Write(ref _busy, 0);
            }
        }
    }

    public void Cancel() => _cancellation?.Cancel();

    private void EnsureIdle()
    {
        if (IsBusy) throw new InvalidOperationException("Stop and save the active autonomous task before loading another.");
    }
}
