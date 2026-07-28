namespace OptilandWorkbench.Core.Services;

public static class ComputationParallelism
{
    private static readonly AsyncLocal<int> SuppressionDepth = new();

    public static IDisposable SuppressNestedParallelism()
    {
        SuppressionDepth.Value++;
        return new SuppressionScope();
    }

    internal static int ResolveMaxDegreeOfParallelism(int requested) =>
        SuppressionDepth.Value > 0 ? 1 : requested;

    private sealed class SuppressionScope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            SuppressionDepth.Value = Math.Max(0, SuppressionDepth.Value - 1);
        }
    }
}
