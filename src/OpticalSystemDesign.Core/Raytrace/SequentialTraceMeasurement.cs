namespace OpticalSystemDesign.Core.Raytrace;

/// <summary>
/// Optional execution-scope accounting. Counts rays submitted to real sequential
/// computation, including aiming/diagnostic passes and failed rays, excluding cache
/// hits. An optional CPU-only quota reserves submissions before computation. This
/// is not a count of surface intersections or GPU derivative work.
/// </summary>
public sealed class SequentialTraceMeasurement : IDisposable
{
    private static readonly AsyncLocal<SequentialTraceMeasurement?> Current = new();
    private readonly SequentialTraceMeasurement? _parent;
    private long _rays;
    private readonly long? _maximumRays;
    private bool _limitReached;
    private static readonly object ReservationGate = new();
    private bool _disposed;

    private SequentialTraceMeasurement(long? maximumRays = null)
    {
        if (maximumRays < 0) throw new ArgumentOutOfRangeException(nameof(maximumRays));
        if (maximumRays is not null && SequentialGpuSession.Current is { Enabled: true })
            throw new NotSupportedException("CPU trace quotas cannot enclose an enabled GPU session.");
        _maximumRays = maximumRays;
        _parent = Current.Value;
        Current.Value = this;
    }

    public static SequentialTraceMeasurement Begin() => new();
    public static SequentialTraceMeasurement BeginCpuBudget(long maximumRays) => new(maximumRays);
    public long RayCount => Interlocked.Read(ref _rays);
    public bool LimitReached { get { lock (ReservationGate) return _limitReached; } }

    internal static bool HasCpuBudget
    {
        get
        {
            for (var scope = Current.Value; scope is not null; scope = scope._parent)
                if (scope._maximumRays is not null) return true;
            return false;
        }
    }
    public static bool CpuBudgetReached
    {
        get
        {
            lock (ReservationGate)
                for (var scope = Current.Value; scope is not null; scope = scope._parent)
                    if (scope._limitReached) return true;
            return false;
        }
    }

    internal static void Record(long count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var first = Current.Value;
        if (first is null) return;
        if (!HasCpuBudget)
        {
            for (var scope = first; scope is not null; scope = scope._parent)
                Interlocked.Add(ref scope._rays, count);
            return;
        }
        lock (ReservationGate)
        {
            var exceeded = false;
            for (var scope = Current.Value; scope is not null; scope = scope._parent)
                if (scope._maximumRays is { } limit && count > limit - scope._rays)
                { scope._limitReached = true; exceeded = true; }
            if (exceeded) throw new SequentialTraceBudgetExceededException();
            for (var scope = Current.Value; scope is not null; scope = scope._parent)
                Interlocked.Add(ref scope._rays, count);
        }
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

public sealed class SequentialTraceBudgetExceededException() : InvalidOperationException("The CPU sequential trace quota cannot reserve this submission.")
{
    public static bool IsBudgetException(Exception error) => error is SequentialTraceBudgetExceededException
        || error is AggregateException aggregate && aggregate.Flatten().InnerExceptions.All(item => item is SequentialTraceBudgetExceededException);
}
