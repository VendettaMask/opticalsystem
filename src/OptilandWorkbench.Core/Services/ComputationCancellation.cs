namespace OptilandWorkbench.Core.Services;

public static class ComputationCancellation
{
    private static readonly AsyncLocal<CancellationToken> CurrentToken = new();

    public static CancellationToken Current => CurrentToken.Value;

    public static IDisposable Push(CancellationToken cancellationToken)
    {
        var previous = CurrentToken.Value;
        CurrentToken.Value = cancellationToken;
        return new Scope(previous);
    }

    public static void ThrowIfCancellationRequested()
    {
        CurrentToken.Value.ThrowIfCancellationRequested();
    }

    private sealed class Scope(CancellationToken previous) : IDisposable
    {
        public void Dispose()
        {
            CurrentToken.Value = previous;
        }
    }
}
