using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.App.Laboratories.InitialStructure;

/// <summary>In-process desktop lifetime. Loading is read-only; only RunAsync dispatches optical work.</summary>
internal sealed class AutonomousDesktopSession(string directory)
{
    private readonly AutonomousSearchCheckpointStore _store = new();
    private readonly SeedPreparationCheckpointStore _preparationStore = new();
    private AutonomousSearchCheckpoint? _checkpoint;
    private SeedPreparationCheckpoint? _preparation;
    private CancellationTokenSource? _cancellation;
    private int _busy;
    public AutonomousSearchCheckpoint? Checkpoint => Volatile.Read(ref _checkpoint);
    public bool IsBusy => Volatile.Read(ref _busy) != 0;
    public string? RecordPath { get; private set; }
    public ParaxialSeedSearchResult? PreparationResult { get; private set; }
    public string? PreparationReportPath { get; private set; }
    public SeedPreparationCheckpoint? PreparationCheckpoint => Volatile.Read(ref _preparation);
    public string? PreparationCheckpointPath { get; private set; }
    public string? PreparationPlanPath { get; private set; }
    public bool CanResumePreparation => _checkpoint is null && _preparation is { } preparation
        && SeedPreparationSearch.CanExecute(preparation)
        && (preparation.Stage != "Finished" || preparation.ContinueIntoMain && preparation.Result?.Seeds.Length > 0);
    public Task Pending { get; private set; } = Task.CompletedTask;

    public void Reset()
    {
        EnsureIdle();
        _checkpoint = null; RecordPath = null; PreparationResult = null; PreparationReportPath = null;
        _preparation = null; PreparationCheckpointPath = null; PreparationPlanPath = null;
    }

    public async Task LoadAsync(string path, Action<AutonomousSearchPlan>? validate = null)
    {
        EnsureIdle();
        if (path.EndsWith(".preparation.json", StringComparison.OrdinalIgnoreCase))
        {
            var preparation = await _preparationStore.LoadAsync(path);
            validate?.Invoke(preparation.Plan);
            _checkpoint = null; RecordPath = null; _preparation = preparation;
            PreparationCheckpointPath = Path.GetFullPath(path);
            PreparationPlanPath = null;
            PreparationResult = preparation.Result ?? SeedPreparationSearch.Result(preparation);
            PreparationReportPath = null;
            return;
        }
        var loaded = await _store.LoadAsync(path);
        validate?.Invoke(loaded.Plan);
        _checkpoint = loaded;
        PreparationResult = null; PreparationReportPath = null;
        RecordPath = Path.GetFullPath(path);
        _preparation = null; PreparationCheckpointPath = null; PreparationPlanPath = null;
    }

    public Task RunAsync(AutonomousSearchPlan plan, bool resume, Action<AutonomousSearchCheckpoint> publish,
        Action<AutonomousSearchPending, FixedTopologyStep>? progress = null,
        Action<AutonomousSearchPending, ParaxialRecoveryStep>? recoveryProgress = null,
        Action<int, int>? preparationProgress = null, bool prepareOnly = false)
    {
        if (AutonomousSearchService.IsHistoricalReadOnlyPlan(plan))
            throw new NotSupportedException("Historical transmission prototype plans are read-only; create a fresh versioned plan.");
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) throw new InvalidOperationException("An autonomous desktop operation is already active.");
        _cancellation = new();
        Pending = ExecuteAsync();
        return Pending;

        async Task ExecuteAsync()
        {
            try
            {
                if (!resume || CanResumePreparation)
                {
                    var id = resume && _preparation is not null ? _preparation.RunId : "autonomous-" + Guid.NewGuid().ToString("N");
                    PreparationResult = null; PreparationReportPath = null;
                    if (plan.Options.SeedPreparation is { } preparation && (prepareOnly || resume || preparation.ColdStart is not null || preparation.Spc is not null))
                    {
                        // Validate before any new run file. Cold failure never falls back to an old seed/checkpoint.
                        _ = AutonomousSearchService.Create("automatic-cold-preflight", plan);
                        _checkpoint = null; RecordPath = null; PreparationResult = null; PreparationReportPath = null;
                        var reports = Path.Combine(directory, "preparations");
                        Directory.CreateDirectory(reports);
                        if (!resume)
                        {
                            var initial = SeedPreparationSearch.Create(id, plan,preparation.ColdStart is not null || preparation.Spc is not null) with { Revision = 1 };
                            PreparationCheckpointPath = Path.Combine(reports, id + ".preparation.json");
                            await _preparationStore.SaveAsync(initial, PreparationCheckpointPath, 0);
                            _preparation = initial;
                        }
                        var preparationSource = _preparation ?? throw new InvalidOperationException("No preparation checkpoint has been loaded.");
                        if (preparationSource.PlanFingerprint != ContentFingerprint.Compute(plan)) throw new InvalidDataException("Cannot resume a changed preparation task.");
                        var preparationRevision = preparationSource.Revision;
                        var prepared = await Task.Run(() => SeedPreparationSearch.Run(preparationSource, SavePreparation, _cancellation.Token, preparationProgress));
                        Volatile.Write(ref _preparation, prepared);
                        var result = prepared.Result ?? SeedPreparationSearch.Result(prepared);
                        PreparationResult = result;
                        PreparationReportPath = Path.Combine(reports, id + "-r" + prepared.Revision + ".seed-search.json");
                        // Cancellation stops computation; persistence retains the attempted fresh run and its actual costs.
                        await ParaxialSeedSearchFiles.SaveAsync(result, PreparationReportPath);
                        if(prepareOnly || !prepared.ContinueIntoMain)
                        {
                            PreparationPlanPath = Path.Combine(reports,id+".topology-plan.json");
                            await AutonomousDesignFiles.SavePlanAsync(result.Seeds.IsEmpty?plan:plan with{Seeds=result.Seeds},PreparationPlanPath);
                            return;
                        }
                        if (result.Seeds.IsEmpty || _cancellation.IsCancellationRequested) return;
                        plan = plan with { Seeds = result.Seeds };
                        await AutonomousDesignFiles.SavePlanAsync(plan, Path.Combine(reports, id + ".topology-plan.json"));
                        void SavePreparation(SeedPreparationCheckpoint next)
                        {
                            _preparationStore.SaveAsync(next, PreparationCheckpointPath!, preparationRevision).AsTask().GetAwaiter().GetResult();
                            preparationRevision = next.Revision; Volatile.Write(ref _preparation, next);
                        }
                    }
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, id + ".autonomous.json");
                    AutonomousSearchCheckpoint created;
                    if (File.Exists(path))
                    {
                        created = await _store.LoadAsync(path);
                        if (created.RunId != id || created.PlanFingerprint != ContentFingerprint.Compute(plan))
                            throw new InvalidDataException("Existing main run does not match this preparation handoff.");
                    }
                    else
                    {
                        created = AutonomousSearchService.Create(id, plan) with { Revision = 1 };
                        await _store.SaveAsync(created, path, 0, _cancellation.Token);
                    }
                    _checkpoint = created; RecordPath = path;
                    publish(created);
                }
                var source = Checkpoint ?? throw new InvalidOperationException("No autonomous checkpoint has been loaded.");
                if (source.PlanFingerprint != ContentFingerprint.Compute(plan)) throw new InvalidDataException("Cannot resume a changed autonomous task.");
                var revision = source.Revision;
                await Task.Run(() =>
                {
                    var endpoint = AutonomousSearchService.Run(source, Save, _cancellation.Token, progress, recoveryProgress);
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
