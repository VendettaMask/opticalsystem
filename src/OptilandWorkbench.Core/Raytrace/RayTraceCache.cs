using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Raytrace;

public sealed record RayTraceCacheStatistics(
    long Hits,
    long Misses,
    int EntryCount,
    long SampleCount);

public sealed class RayTraceCache
{
    private readonly object _gate = new();
    private readonly Dictionary<RayTraceCacheKey, RayTraceCacheEntry> _entries = new();
    private readonly Queue<RayTraceCacheKey> _insertionOrder = new();
    private readonly int _maximumEntries;
    private readonly long _maximumSamples;
    private long _currentRevision = long.MinValue;
    private long _sampleCount;
    private long _hits;
    private long _misses;

    public RayTraceCache(int maximumEntries = 256, long maximumSamples = 500_000)
    {
        _maximumEntries = Math.Max(1, maximumEntries);
        _maximumSamples = Math.Max(1, maximumSamples);
    }

    public RayTraceCacheStatistics Statistics
    {
        get
        {
            lock (_gate)
            {
                return new RayTraceCacheStatistics(
                    _hits,
                    _misses,
                    _entries.Count,
                    _sampleCount);
            }
        }
    }

    public void SetCurrentRevision(long opticRevision)
    {
        lock (_gate)
        {
            if (_currentRevision == opticRevision)
            {
                return;
            }

            _currentRevision = opticRevision;
            _entries.Clear();
            _insertionOrder.Clear();
            _sampleCount = 0;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _insertionOrder.Clear();
            _sampleCount = 0;
        }
    }

    internal bool TryCopyTo(
        RayTraceCacheKey key,
        RayTraceSampleValue[] samples,
        bool[] hasSamples)
    {
        lock (_gate)
        {
            if (key.OpticRevision != _currentRevision
                || !_entries.TryGetValue(key, out var entry))
            {
                _misses++;
                return false;
            }

            Array.Copy(entry.Samples, samples, entry.Samples.Length);
            Array.Copy(entry.HasSamples, hasSamples, entry.HasSamples.Length);
            _hits++;
            return true;
        }
    }

    internal void Store(
        RayTraceCacheKey key,
        RayTraceSampleValue[] samples,
        bool[] hasSamples,
        int sampleCount)
    {
        if (sampleCount <= 0 || sampleCount > _maximumSamples)
        {
            return;
        }

        var cachedSamples = new RayTraceSampleValue[sampleCount];
        var cachedPresence = new bool[sampleCount];
        Array.Copy(samples, cachedSamples, sampleCount);
        Array.Copy(hasSamples, cachedPresence, sampleCount);

        lock (_gate)
        {
            if (key.OpticRevision != _currentRevision || _entries.ContainsKey(key))
            {
                return;
            }

            while (_entries.Count >= _maximumEntries
                   || (_sampleCount + sampleCount > _maximumSamples && _entries.Count > 0))
            {
                EvictOldest();
            }

            _entries.Add(key, new RayTraceCacheEntry(cachedSamples, cachedPresence));
            _insertionOrder.Enqueue(key);
            _sampleCount += sampleCount;
        }
    }

    private void EvictOldest()
    {
        while (_insertionOrder.Count > 0)
        {
            var key = _insertionOrder.Dequeue();
            if (!_entries.Remove(key, out var entry))
            {
                continue;
            }

            _sampleCount -= entry.Samples.Length;
            return;
        }
    }
}

internal sealed class RayTraceCacheKey : IEquatable<RayTraceCacheKey>
{
    private readonly RaySignature[] _rays;
    private readonly int[] _surfaceIndices;
    private readonly int _hashCode;

    private RayTraceCacheKey(
        long opticRevision,
        string backendName,
        TraceRequest request,
        IReadOnlyList<int> surfaceIndices,
        IReadOnlyList<RealRay> rays)
    {
        OpticRevision = opticRevision;
        BackendName = backendName;
        NormalizeOpticalPathDifference = request.NormalizeOpticalPathDifference;
        UseBatchedBackend = request.UseBatchedBackend;
        _surfaceIndices = surfaceIndices.ToArray();
        _rays = rays.Select(RaySignature.FromRay).ToArray();

        var hash = new HashCode();
        hash.Add(OpticRevision);
        hash.Add(BackendName, StringComparer.Ordinal);
        hash.Add(NormalizeOpticalPathDifference);
        hash.Add(UseBatchedBackend);
        foreach (var surfaceIndex in _surfaceIndices)
        {
            hash.Add(surfaceIndex);
        }

        foreach (var ray in _rays)
        {
            hash.Add(ray);
        }

        _hashCode = hash.ToHashCode();
    }

    public long OpticRevision { get; }

    private string BackendName { get; }

    private bool NormalizeOpticalPathDifference { get; }

    private bool UseBatchedBackend { get; }

    public static RayTraceCacheKey Create(
        long opticRevision,
        string backendName,
        TraceRequest request,
        IReadOnlyList<int> surfaceIndices,
        IReadOnlyList<RealRay> rays) =>
        new(opticRevision, backendName, request, surfaceIndices, rays);

    public bool Equals(RayTraceCacheKey? other)
    {
        return other is not null
            && OpticRevision == other.OpticRevision
            && string.Equals(BackendName, other.BackendName, StringComparison.Ordinal)
            && NormalizeOpticalPathDifference == other.NormalizeOpticalPathDifference
            && UseBatchedBackend == other.UseBatchedBackend
            && _surfaceIndices.AsSpan().SequenceEqual(other._surfaceIndices)
            && _rays.AsSpan().SequenceEqual(other._rays);
    }

    public override bool Equals(object? obj) => Equals(obj as RayTraceCacheKey);

    public override int GetHashCode() => _hashCode;

    private readonly record struct RaySignature(
        Vector3D Origin,
        Vector3D Direction,
        double WavelengthNanometers,
        double Intensity,
        double OpticalPathDifference,
        Matrix3x3? PolarizationMatrix,
        bool IsNormalized)
    {
        public static RaySignature FromRay(RealRay ray) => new(
            ray.Origin,
            ray.Direction,
            ray.WavelengthNanometers,
            ray.Intensity,
            ray.OpticalPathDifference,
            ray.PolarizationMatrix,
            ray.IsNormalized);
    }
}

internal sealed record RayTraceCacheEntry(
    RayTraceSampleValue[] Samples,
    bool[] HasSamples);
