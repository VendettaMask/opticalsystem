using System.Buffers;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Raytrace;

public enum TraceRetention
{
    FinalOnly,
    SelectedSurfaces,
    FullHistory
}

public sealed record TraceRequest
{
    public TraceRetention Retention { get; init; } = TraceRetention.FinalOnly;

    public IReadOnlyCollection<int> SurfaceIndices { get; init; } = Array.Empty<int>();

    public bool RecordSurfaceData { get; init; }

    public bool NormalizeOpticalPathDifference { get; init; }

    public int ParallelThreshold { get; init; } = 64;

    public int MaxDegreeOfParallelism { get; init; } = -1;
    public bool UseBatchedBackend { get; init; } = true;


    public static TraceRequest FinalOnly(
        bool normalizeOpticalPathDifference = true,
        int maxDegreeOfParallelism = -1) => new()
        {
            Retention = TraceRetention.FinalOnly,
            NormalizeOpticalPathDifference = normalizeOpticalPathDifference,
            MaxDegreeOfParallelism = maxDegreeOfParallelism
        };

    public static TraceRequest Selected(
        IEnumerable<int> surfaceIndices,
        bool normalizeOpticalPathDifference = false,
        int maxDegreeOfParallelism = -1) => new()
        {
            Retention = TraceRetention.SelectedSurfaces,
            SurfaceIndices = surfaceIndices?.Distinct().Order().ToArray()
            ?? throw new ArgumentNullException(nameof(surfaceIndices)),
            NormalizeOpticalPathDifference = normalizeOpticalPathDifference,
            MaxDegreeOfParallelism = maxDegreeOfParallelism
        };

    public static TraceRequest FullHistory(
        bool recordSurfaceData = false,
        int maxDegreeOfParallelism = -1) => new()
        {
            Retention = TraceRetention.FullHistory,
            RecordSurfaceData = recordSurfaceData,
            NormalizeOpticalPathDifference = true,
            MaxDegreeOfParallelism = maxDegreeOfParallelism
        };

    internal int[] ResolveSurfaceIndices(int surfaceCount)
    {
        if (surfaceCount == 0)
        {
            return Array.Empty<int>();
        }

        return Retention switch
        {
            TraceRetention.FinalOnly => new[] { surfaceCount - 1 },
            TraceRetention.FullHistory => Enumerable.Range(0, surfaceCount).ToArray(),
            TraceRetention.SelectedSurfaces => SurfaceIndices
                .Distinct()
                .Order()
                .Select(index => index < 0 ? surfaceCount + index : index)
                .Select(index => index >= 0 && index < surfaceCount
                    ? index
                    : throw new ArgumentOutOfRangeException(
                        nameof(SurfaceIndices),
                        index,
                        "A requested surface index is outside the optical system."))
                .Distinct()
                .Order()
                .ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(Retention))
        };
    }
}

public sealed class RequestedTrace : IDisposable
{
    private RayTraceSampleValue[]? _samples;
    private bool[]? _hasSamples;
    private readonly Dictionary<int, int> _surfaceSlots;

    internal RequestedTrace(
        int rayCount,
        int[] retainedSurfaceIndices,
        RayTraceSampleValue[] samples,
        bool[] hasSamples)
    {
        RayCount = rayCount;
        RetainedSurfaceIndices = retainedSurfaceIndices;
        _samples = samples;
        _hasSamples = hasSamples;
        _surfaceSlots = retainedSurfaceIndices
            .Select((surfaceIndex, slot) => (surfaceIndex, slot))
            .ToDictionary(item => item.surfaceIndex, item => item.slot);
    }

    public int RayCount { get; }

    public IReadOnlyList<int> RetainedSurfaceIndices { get; }

    public IReadOnlyList<RayTraceSampleValue?> GetSurfaceSamples(int surfaceIndex)
    {
        ThrowIfDisposed();
        if (!_surfaceSlots.TryGetValue(surfaceIndex, out var slot))
        {
            throw new ArgumentException(
                $"Surface {surfaceIndex} was not retained by this trace request.",
                nameof(surfaceIndex));
        }

        return new SurfaceSampleView(this, slot);
    }

    public IReadOnlyList<RayTraceSampleValue?> GetRaySamples(int rayIndex)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(rayIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(rayIndex, RayCount);
        return new RaySampleView(this, rayIndex);
    }

    public bool TryGetSample(int rayIndex, int surfaceIndex, out RayTraceSampleValue sample)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(rayIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(rayIndex, RayCount);
        if (!_surfaceSlots.TryGetValue(surfaceIndex, out var slot))
        {
            sample = default;
            return false;
        }

        return TryGetSampleBySlot(rayIndex, slot, out sample);
    }

    public void Dispose()
    {
        var samples = Interlocked.Exchange(ref _samples, null);
        var hasSamples = Interlocked.Exchange(ref _hasSamples, null);
        if (samples is not null)
        {
            ArrayPool<RayTraceSampleValue>.Shared.Return(samples, clearArray: true);
        }

        if (hasSamples is not null)
        {
            ArrayPool<bool>.Shared.Return(hasSamples, clearArray: true);
        }
    }

    internal bool TryGetSampleBySlot(int rayIndex, int slot, out RayTraceSampleValue sample)
    {
        var samples = _samples;
        var hasSamples = _hasSamples;
        if (samples is null || hasSamples is null)
        {
            throw new ObjectDisposedException(nameof(RequestedTrace));
        }

        var offset = (slot * RayCount) + rayIndex;
        sample = samples[offset];
        return hasSamples[offset];
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_samples is null, this);
    }

    private sealed class RaySampleView : IReadOnlyList<RayTraceSampleValue?>
    {
        private readonly RequestedTrace _owner;
        private readonly int _rayIndex;

        public RaySampleView(RequestedTrace owner, int rayIndex)
        {
            _owner = owner;
            _rayIndex = rayIndex;
        }

        public int Count => _owner.RetainedSurfaceIndices.Count;

        public RayTraceSampleValue? this[int index]
        {
            get
            {
                ArgumentOutOfRangeException.ThrowIfNegative(index);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
                return _owner.TryGetSampleBySlot(_rayIndex, index, out var sample)
                    ? sample
                    : null;
            }
        }

        public IEnumerator<RayTraceSampleValue?> GetEnumerator()
        {
            for (var index = 0; index < Count; index++)
            {
                yield return this[index];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class SurfaceSampleView : IReadOnlyList<RayTraceSampleValue?>
    {
        private readonly RequestedTrace _owner;
        private readonly int _slot;

        public SurfaceSampleView(RequestedTrace owner, int slot)
        {
            _owner = owner;
            _slot = slot;
        }

        public int Count => _owner.RayCount;

        public RayTraceSampleValue? this[int index]
        {
            get
            {
                ArgumentOutOfRangeException.ThrowIfNegative(index);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
                return _owner.TryGetSampleBySlot(index, _slot, out var sample) ? sample : null;
            }
        }

        public IEnumerator<RayTraceSampleValue?> GetEnumerator()
        {
            for (var index = 0; index < Count; index++)
            {
                yield return this[index];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
