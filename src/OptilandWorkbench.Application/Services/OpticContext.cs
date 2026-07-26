using OptilandWorkbench.Application.Legacy;
using OptilandWorkbench.Core;

namespace OptilandWorkbench.Application.Services;

internal interface IOpticContext : IDisposable
{
    object SyncRoot { get; }

    OpticalWorkspaceModel Connector { get; }

    CancellationTokenSource LinkDocumentToken(CancellationToken cancellationToken);

    void CancelDocumentTasks();
}

internal sealed class OpticContext : IOpticContext
{
    private CancellationTokenSource _documentLifetime = new();
    private bool _disposed;

    public OpticContext(Optic optic)
    {
        Connector = new OpticalWorkspaceModel(optic);
    }

    public object SyncRoot { get; } = new();

    public OpticalWorkspaceModel Connector { get; }

    public CancellationTokenSource LinkDocumentToken(CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _documentLifetime.Token);
        }
    }

    public void CancelDocumentTasks()
    {
        CancellationTokenSource previous;
        lock (SyncRoot)
        {
            if (_disposed)
            {
                return;
            }

            previous = _documentLifetime;
            _documentLifetime = new CancellationTokenSource();
        }

        previous.Cancel();
        previous.Dispose();
    }

    public void Dispose()
    {
        CancellationTokenSource lifetime;
        lock (SyncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            lifetime = _documentLifetime;
        }

        lifetime.Cancel();
        lifetime.Dispose();
    }
}
