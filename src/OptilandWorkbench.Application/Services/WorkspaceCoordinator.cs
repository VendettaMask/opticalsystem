using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Application.Services;

internal sealed class WorkspaceCoordinator : IWorkspaceEventStream, IDisposable
{
    private readonly IOpticContext _context;
    private WorkspaceChangeCategory _pendingCategory = WorkspaceChangeCategory.Prescription;
    private long _documentGeneration;
    private long _revision;
    private int _mutationDepth;
    private bool _deferredEvent;
    private bool _deferredFileSwitch;
    private bool _disposed;

    public WorkspaceCoordinator(IOpticContext context)
    {
        _context = context;
        Runtime.OpticLoaded += OnOpticLoaded;
        Runtime.OpticChanged += OnOpticChanged;
    }

    public event EventHandler<WorkspaceChangedEventArgs>? Changed;

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
            return new OpticalDocumentSnapshot(
                optic.Name,
                CurrentPath,
                Revision,
                Runtime.Status,
                Runtime.CanUndo,
                Runtime.CanRedo,
                optic.Paraxial.EstimateEffectiveFocalLength(),
                optic.Paraxial.EstimateFNumber(),
                optic.Aperture.Value,
                optic.SurfaceGroup.TotalTrack,
                optic.SurfaceGroup.Items.Count,
                optic.Fields.Count,
                optic.Wavelengths.Count,
                optic.Paraxial.EstimateEntrancePupilDiameter());
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
                if (_mutationDepth == 1 && UpdatesAutomaticSemiDiameters(category))
                {
                    RefreshAutomaticSemiDiameters();
                }

                CompleteMutation();
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
                if (_mutationDepth == 1 && UpdatesAutomaticSemiDiameters(category))
                {
                    RefreshAutomaticSemiDiameters();
                }

                CompleteMutation();
            }
        }
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
        _context.Dispose();
    }

    private void OnOpticLoaded(object? sender, EventArgs args)
    {
        Interlocked.Increment(ref _documentGeneration);
        if (_mutationDepth > 0)
        {
            _deferredEvent = true;
            _deferredFileSwitch = true;
            return;
        }

        RefreshAutomaticSemiDiameters();
        Publish(_pendingCategory, fileSwitched: true);
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

    private void Publish(WorkspaceChangeCategory category, bool fileSwitched)
    {
        var revision = Interlocked.Increment(ref _revision);
        using var cancellationScope = ComputationCancellation.Push(CancellationToken.None);
        Changed?.Invoke(this, new WorkspaceChangedEventArgs(
            revision,
            category,
            Runtime.Status,
            fileSwitched));
    }

    public void RefreshAutomaticSemiDiameters()
    {
        using var cancellationScope = ComputationCancellation.Push(CancellationToken.None);
        AutomaticSemiDiameterSolver.Update(Runtime.CurrentOptic);
    }

    private static bool UpdatesAutomaticSemiDiameters(WorkspaceChangeCategory category) => category is
        WorkspaceChangeCategory.Document
        or WorkspaceChangeCategory.Prescription
        or WorkspaceChangeCategory.Surface
        or WorkspaceChangeCategory.Field
        or WorkspaceChangeCategory.Wavelength
        or WorkspaceChangeCategory.SystemSettings
        or WorkspaceChangeCategory.Configuration;

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
        Publish(_pendingCategory, fileSwitched);
    }
}
