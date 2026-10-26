namespace OpticalSystemDesign.Core.Services;

public static class ComputationCancellation
{
    private static readonly AsyncLocal<CancellationToken> CurrentToken = new();
    private static readonly AsyncLocal<Action?> CheckpointObserver = new();

    internal static IDisposable ObserveCheckpoints(Action observer)
    {
        var previous = CheckpointObserver.Value;
        CheckpointObserver.Value = observer;
        return new ObserverScope(previous);
    }

    public static CancellationToken Current => CurrentToken.Value;

    public static IDisposable Push(CancellationToken cancellationToken)
    {
        var previous = CurrentToken.Value;
        CurrentToken.Value = cancellationToken;
        return new Scope(previous);
    }

    public static void ThrowIfCancellationRequested()
    {
        CheckpointObserver.Value?.Invoke();
        CurrentToken.Value.ThrowIfCancellationRequested();
    }

    private sealed class ObserverScope(Action? previous) : IDisposable
    {
        public void Dispose() => CheckpointObserver.Value = previous;
    }

    private sealed class Scope(CancellationToken previous) : IDisposable
    {
        public void Dispose()
        {
            CurrentToken.Value = previous;
        }
    }
}
