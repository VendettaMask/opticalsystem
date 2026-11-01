using System.Collections.Concurrent;
using System.Diagnostics;

namespace OpticalSystemDesign.InitialStructure.Engine;

public sealed record SearchPerformanceSample(string Name, long Calls, TimeSpan Elapsed);
public sealed record SearchLinearModel(double[,] Jacobian, double[] Residuals, double[,] Normals, double[] Upper, double Damping);

/// <summary>Opt-in inclusive timing only; never changes search decisions, budgets or result identities.</summary>
public sealed class SearchPerformanceMeasurement : IDisposable
{
    private static readonly AsyncLocal<SearchPerformanceMeasurement?> Current = new();
    private readonly SearchPerformanceMeasurement? _previous;
    private readonly Action<SearchLinearModel>? _observeModel;
    private readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.Ordinal);
    private int _disposed;
    private sealed class Counter { internal long Calls; internal long Ticks; }

    private SearchPerformanceMeasurement(Action<SearchLinearModel>? observeModel = null) { _previous = Current.Value; _observeModel = observeModel; Current.Value = this; }
    public static SearchPerformanceMeasurement Begin() => new();
    public static SearchPerformanceMeasurement Begin(Action<SearchLinearModel> observeModel) => new(observeModel);
    public IReadOnlyList<SearchPerformanceSample> Snapshot() => _counters.OrderBy(item => item.Key, StringComparer.Ordinal)
        .Select(item => new SearchPerformanceSample(item.Key, Interlocked.Read(ref item.Value.Calls),
            TimeSpan.FromSeconds((double)Interlocked.Read(ref item.Value.Ticks) / Stopwatch.Frequency))).ToArray();

    internal static Region Measure(string name) => Current.Value is { } frame ? new(frame, name) : default;
    internal static void ObserveModel(double[,] jacobian, double[] residuals, double[,] normals, double[] upper, double damping)
    {
        if (Current.Value?._observeModel is { } observer)
            observer(new((double[,])jacobian.Clone(), residuals.ToArray(), (double[,])normals.Clone(), upper.ToArray(), damping));
    }
    internal readonly struct Region : IDisposable
    {
        private readonly SearchPerformanceMeasurement? _owner;
        private readonly string? _name;
        private readonly long _start;
        internal Region(SearchPerformanceMeasurement owner, string name) { _owner = owner; _name = name; _start = Stopwatch.GetTimestamp(); }
        public void Dispose()
        {
            if (_owner is null) return;
            var counter = _owner._counters.GetOrAdd(_name!, static _ => new());
            Interlocked.Increment(ref counter.Calls); Interlocked.Add(ref counter.Ticks, Stopwatch.GetTimestamp() - _start);
        }
    }
    public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) Current.Value = _previous; }
}
