namespace OptilandWorkbench.Core.Raytrace;

/// <summary>
/// Optional execution-scope accounting. Counts rays submitted to real sequential
/// computation, including aiming/diagnostic passes and failed rays, excluding cache
/// hits. This is not a count of surface intersections or a hard computation budget.
/// </summary>
public sealed class SequentialTraceMeasurement : IDisposable
{
    private static readonly AsyncLocal<SequentialTraceMeasurement?> Current = new();
    private readonly SequentialTraceMeasurement? _parent;
    private long _rays;
    private bool _disposed;

    private SequentialTraceMeasurement()
    {
        _parent = Current.Value;
        Current.Value = this;
    }

    public static SequentialTraceMeasurement Begin() => new();
    public long RayCount => Interlocked.Read(ref _rays);

    internal static void Record(int count)
    {
        for (var scope = Current.Value; scope is not null; scope = scope._parent)
            Interlocked.Add(ref scope._rays, count);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (!ReferenceEquals(Current.Value, this))
            throw new InvalidOperationException("Trace measurement scopes must be disposed in nesting order.");
        Current.Value = _parent;
        _disposed = true;
    }
}
