using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Application.Services;

internal sealed class WorkspaceCoordinator : IWorkspaceEventStream, IDisposable
{
    private readonly IOpticContext _context;
    private readonly Action<Optic> _automaticSemiDiameterUpdater;
    private WorkspaceChangeCategory _pendingCategory = WorkspaceChangeCategory.Prescription;
    private long _documentGeneration;
    private long _revision;
    private long _savedRevision;
    private int _mutationDepth;
    private bool _deferredEvent;
    private bool _deferredFileSwitch;
    private bool _disposed;

    public WorkspaceCoordinator(IOpticContext context)
        : this(context, AutomaticSemiDiameterSolver.Update)
    {
    }

    internal WorkspaceCoordinator(
        IOpticContext context,
        Action<Optic> automaticSemiDiameterUpdater)
    {
        _context = context;
        _automaticSemiDiameterUpdater = automaticSemiDiameterUpdater
            ?? throw new ArgumentNullException(nameof(automaticSemiDiameterUpdater));
        Runtime.OpticLoaded += OnOpticLoaded;
        Runtime.OpticChanged += OnOpticChanged;
        Runtime.StatusChanged += OnStatusChanged;
    }

    public event EventHandler<WorkspaceChangedEventArgs>? Changed;

    public event EventHandler? StatusChanged;

    public object Gate => _context.SyncRoot;

    public WorkbenchRuntime Runtime => _context.Runtime;

    public long Revision => Interlocked.Read(ref _revision);

    public long DocumentGeneration => Interlocked.Read(ref _documentGeneration);

    public string? CurrentPath { get; set; }

    public OpticalDocumentSnapshot GetDocumentSnapshot()
    {
        lock (Gate)
        {
            var optic = Runtime.CurrentOptic;
            var computable = OpticCapabilityPreflight.Inspect(optic).Count == 0;
            return new OpticalDocumentSnapshot(
                optic.Name,
                CurrentPath,
                Revision,
                Runtime.Status,
                Runtime.CanUndo,
                Runtime.CanRedo,
                computable ? optic.Paraxial.EstimateEffectiveFocalLength() : double.NaN,
                computable ? optic.Paraxial.EstimateFNumber() : double.NaN,
                optic.Aperture.Value,
                optic.SurfaceGroup.TotalTrack,
                optic.SurfaceGroup.Items.Count,
                optic.Fields.Count,
                optic.Wavelengths.Count,
                computable ? optic.Paraxial.EstimateEntrancePupilDiameter() : double.NaN,
                Revision != Interlocked.Read(ref _savedRevision));
        }
    }

    public CancellationTokenSource LinkDocumentToken(CancellationToken cancellationToken)
    {
        return _context.LinkDocumentToken(cancellationToken);
    }

    public void ReplaceDocument(WorkspaceChangeCategory category, Action action)
    {
        CancelDocumentTasks();
        Mutate(category, () =>
        {
            CurrentPath = null;
            action();
        });
    }

    public void CancelDocumentTasks()
    {
        _context.CancelDocumentTasks();
    }

    public void SetPendingCategory(WorkspaceChangeCategory category)
    {
        _pendingCategory = category;
    }

    public void MarkSavedRevision(long revision)
    {
        var currentRevision = Revision;
        if (revision < 0 || revision > currentRevision)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        var savedRevision = Interlocked.Read(ref _savedRevision);
        if (revision > savedRevision)
        {
            Interlocked.Exchange(ref _savedRevision, revision);
        }
    }

    public void Mutate(WorkspaceChangeCategory category, Action action)
    {
        lock (Gate)
        {
            _pendingCategory = category;
            _mutationDepth++;
            try
            {
                action();
            }
            finally
            {
                try
                {
                    if (_mutationDepth == 1 && UpdatesAutomaticSemiDiameters(category))
                    {
                        RefreshAutomaticSemiDiameters();
                    }
                }
                finally
                {
                    CompleteMutation();
                }
            }
        }
    }

    public T Mutate<T>(WorkspaceChangeCategory category, Func<T> action)
    {
        lock (Gate)
        {
            _pendingCategory = category;
            _mutationDepth++;
            try
            {
                return action();
            }
            finally
            {
                try
                {
                    if (_mutationDepth == 1 && UpdatesAutomaticSemiDiameters(category))
                    {
                        RefreshAutomaticSemiDiameters();
                    }
                }
                finally
                {
                    CompleteMutation();
                }
            }
        }
    }

    public void MutateTransactional(
        WorkspaceChangeCategory category,
        Action action,
        CancellationToken automaticSemiDiameterCancellationToken = default)
    {
        lock (Gate)
        {
            var previousCategory = _pendingCategory;
            var previousDeferredEvent = _deferredEvent;
            var previousDeferredFileSwitch = _deferredFileSwitch;
            _pendingCategory = category;
            _mutationDepth++;
            try
            {
                Runtime.ExecuteTransactionalEdit(() =>
                {
                    action();
                    if (_mutationDepth == 1 && UpdatesAutomaticSemiDiameters(category))
                    {
                        RefreshAutomaticSemiDiameters(automaticSemiDiameterCancellationToken);
                        automaticSemiDiameterCancellationToken.ThrowIfCancellationRequested();
                    }
                });
            }
            catch
            {
                _deferredEvent = previousDeferredEvent;
                _deferredFileSwitch = previousDeferredFileSwitch;
                throw;
            }
            finally
            {
                CompleteMutation();
                if (_mutationDepth > 0)
                {
                    _pendingCategory = previousCategory;
                }
            }
        }
    }

    public T MutateTransactional<T>(
        WorkspaceChangeCategory category,
        Func<T> action,
        CancellationToken automaticSemiDiameterCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        T result = default!;
        MutateTransactional(
            category,
            () =>
            {
                result = action();
            },
            automaticSemiDiameterCancellationToken);
        return result;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Runtime.OpticLoaded -= OnOpticLoaded;
        Runtime.OpticChanged -= OnOpticChanged;
        Runtime.StatusChanged -= OnStatusChanged;
        _context.Dispose();
    }

    private void OnOpticLoaded(object? sender, EventArgs args)
    {
        var documentReplaced = _pendingCategory == WorkspaceChangeCategory.Document;
        if (documentReplaced)
        {
            Interlocked.Increment(ref _documentGeneration);
        }

        if (_mutationDepth > 0)
        {
            _deferredEvent = true;
            _deferredFileSwitch = true;
            return;
        }

        RefreshAutomaticSemiDiameters();
        Publish(
            _pendingCategory,
            fileSwitched: true,
            markCurrentRevisionSaved: documentReplaced && CurrentPath is not null);
    }

    private void OnOpticChanged(object? sender, EventArgs args)
    {
        if (_mutationDepth > 0)
        {
            _deferredEvent = true;
            return;
        }

        Publish(_pendingCategory, fileSwitched: false);
    }

    private void OnStatusChanged(object? sender, EventArgs args)
    {
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Publish(
        WorkspaceChangeCategory category,
        bool fileSwitched,
        bool markCurrentRevisionSaved = false)
    {
        var revision = Interlocked.Increment(ref _revision);
        if (markCurrentRevisionSaved)
        {
            Interlocked.Exchange(ref _savedRevision, revision);
        }

        using var cancellationScope = ComputationCancellation.Push(CancellationToken.None);
        Changed?.Invoke(this, new WorkspaceChangedEventArgs(
            revision,
            category,
            Runtime.Status,
            fileSwitched));
    }

    public void RefreshAutomaticSemiDiameters(CancellationToken cancellationToken = default)
    {
        using var cancellationScope = ComputationCancellation.Push(cancellationToken);
        _automaticSemiDiameterUpdater(Runtime.CurrentOptic);
    }

    private static bool UpdatesAutomaticSemiDiameters(WorkspaceChangeCategory category) => category is
        WorkspaceChangeCategory.Document
        or WorkspaceChangeCategory.Prescription
        or WorkspaceChangeCategory.Surface
        or WorkspaceChangeCategory.Field
        or WorkspaceChangeCategory.Wavelength
        or WorkspaceChangeCategory.SystemSettings
        or WorkspaceChangeCategory.Configuration
        or WorkspaceChangeCategory.Optimization;

    private void CompleteMutation()
    {
        _mutationDepth--;
        if (_mutationDepth != 0 || !_deferredEvent)
        {
            return;
        }

        var fileSwitched = _deferredFileSwitch;
        _deferredEvent = false;
        _deferredFileSwitch = false;
        Publish(
            _pendingCategory,
            fileSwitched,
            markCurrentRevisionSaved: _pendingCategory == WorkspaceChangeCategory.Document
                && CurrentPath is not null);
    }
}
