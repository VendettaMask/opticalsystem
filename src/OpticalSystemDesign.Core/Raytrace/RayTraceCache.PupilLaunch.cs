using OpticalSystemDesign.Core.Rays;

namespace OpticalSystemDesign.Core.Raytrace;

public sealed record PupilLaunchCacheStatistics(long Hits, long Misses, int EntryCount, long RayCount);

public sealed partial class RayTraceCache
{
    // Launches and retained trace samples each have the declared entry/sample
    // capacity. Neither cache grows with revisions; both clear together.
    private readonly Dictionary<PupilLaunchCacheKey, RealRay[]> _pupilLaunches = new();
    private readonly Queue<PupilLaunchCacheKey> _pupilLaunchOrder = new();
    private long _pupilLaunchRayCount, _pupilLaunchHits, _pupilLaunchMisses;

    public PupilLaunchCacheStatistics PupilLaunchStatistics
    {
        get { lock (_gate) return new(_pupilLaunchHits, _pupilLaunchMisses, _pupilLaunches.Count, _pupilLaunchRayCount); }
    }

    private void ClearPupilLaunches()
    {
        _pupilLaunches.Clear();
        _pupilLaunchOrder.Clear();
        _pupilLaunchRayCount = 0;
    }

    internal bool TryGetPupilLaunch(PupilLaunchCacheKey key, out RealRayBundle? bundle)
    {
        lock (_gate)
        {
            if (key.Revision == _currentRevision && _pupilLaunches.TryGetValue(key, out var rays))
            {
                // Copy the container, preserving the already-normalized bit
                // patterns. Never expose the cache array or normalize twice.
                bundle = RealRayBundle.CopyNormalized(rays);
                _pupilLaunchHits++;
                return true;
            }
            _pupilLaunchMisses++;
            bundle = null;
            return false;
        }
    }

    internal void StorePupilLaunch(PupilLaunchCacheKey key, IReadOnlyList<RealRay> rays)
    {
        if (!CanCache(rays.Count)) return;
        lock (_gate)
        {
            if (key.Revision != _currentRevision || _pupilLaunches.ContainsKey(key)) return;
            while (_pupilLaunches.Count >= _maximumEntries || _pupilLaunchRayCount + rays.Count > _maximumSamples)
            {
                var oldest = _pupilLaunchOrder.Dequeue();
                if (_pupilLaunches.Remove(oldest, out var removed)) _pupilLaunchRayCount -= removed.Length;
            }
            _pupilLaunches.Add(key, rays.ToArray());
            _pupilLaunchOrder.Enqueue(key);
            _pupilLaunchRayCount += rays.Count;
        }
    }
}

internal sealed class PupilLaunchCacheKey : IEquatable<PupilLaunchCacheKey>
{
    private readonly (double X, double Y, double Weight)[] _pupils;
    private readonly string _opticIdentity, _backend;
    private readonly double _hx, _hy, _wavelength;
    private readonly bool _vignetting;
    private readonly (double X, double Y)? _imageLaunch;
    private readonly int _hash;
    internal long Revision { get; }

    internal PupilLaunchCacheKey(long revision, string opticIdentity, string backend, double hx, double hy,
        double wavelength, bool vignetting, (double X, double Y)? imageLaunch, IReadOnlyList<PupilSample> pupils)
    {
        Revision = revision; _opticIdentity = opticIdentity; _backend = backend;
        _hx = hx; _hy = hy; _wavelength = wavelength; _vignetting = vignetting; _imageLaunch = imageLaunch;
        _pupils = new (double, double, double)[pupils.Count];
        var hash = new HashCode();
        hash.Add(revision); hash.Add(opticIdentity, StringComparer.Ordinal); hash.Add(backend, StringComparer.Ordinal);
        hash.Add(hx); hash.Add(hy); hash.Add(wavelength); hash.Add(vignetting); hash.Add(imageLaunch);
        for (var i = 0; i < pupils.Count; i++)
        {
            _pupils[i] = (pupils[i].X, pupils[i].Y, pupils[i].Weight);
            hash.Add(_pupils[i]);
        }
        _hash = hash.ToHashCode();
    }

    public bool Equals(PupilLaunchCacheKey? other) => other is not null && Revision == other.Revision
        && _opticIdentity == other._opticIdentity && _backend == other._backend && _hx.Equals(other._hx)
        && _hy.Equals(other._hy) && _wavelength.Equals(other._wavelength) && _vignetting == other._vignetting
        && _imageLaunch == other._imageLaunch && _pupils.AsSpan().SequenceEqual(other._pupils);
    public override bool Equals(object? obj) => Equals(obj as PupilLaunchCacheKey);
    public override int GetHashCode() => _hash;
}
