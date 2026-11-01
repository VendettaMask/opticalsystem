using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

internal interface IEnergyImageGrid
{
    double Fraction(double distance);
    double RadiusContaining(double target);
}

/// <summary>
/// FFT enclosed-energy output accumulates linear distance bins. This output
/// convention is distinct from the physical rectangular-pixel area integrator
/// used by diffraction-energy operands.
/// </summary>
internal sealed class FftCumulativeEnergyGrid : IEnergyImageGrid
{
    private readonly (double Distance, double Weight)[] _samples;
    private readonly double _pitch;
    private readonly double _total;
    private readonly double _maximum;
    private readonly bool _windowed;
    private readonly double[] _prefixWeight, _prefixDistanceWeight;

    public FftCumulativeEnergyGrid(IReadOnlyList<EnergySample> samples,
        (double X, double Y) center, string type, double? fullImagePower = null, double? coveredDistance = null)
    {
        if (samples.Count == 0 || samples.Any(s => !double.IsFinite(s.X)
            || !double.IsFinite(s.Y) || !double.IsFinite(s.Weight) || s.Weight < 0)
            || !double.IsFinite(center.X) || !double.IsFinite(center.Y))
            throw new InvalidOperationException("FFT energy requires finite coordinates and nonnegative power.");
        var xs = samples.Select(s => s.X).Distinct().Order().ToArray();
        var ys = samples.Select(s => s.Y).Distinct().Order().ToArray();
        var dx = Pitch(xs); var dy = Pitch(ys);
        if ((long)xs.Length * ys.Length != samples.Count
            || samples.Select(s => (s.X, s.Y)).Distinct().Count() != samples.Count
            || Math.Abs(dx / dy - 1) > 1e-8)
            throw new InvalidOperationException("FFT energy requires a complete uniform square-pitch image grid.");
        if (coveredDistance is { } radius && (type.StartsWith("X", StringComparison.OrdinalIgnoreCase)
            || type.StartsWith("Y", StringComparison.OrdinalIgnoreCase)
            || xs[0] > center.X - radius - dx / 2 || xs[^1] < center.X + radius + dx / 2
            || ys[0] > center.Y - radius - dy / 2 || ys[^1] < center.Y + radius + dy / 2))
            throw new ArgumentException("The readout must contain the complete requested radial or square distance bins.");
        _pitch = dx;
        if (fullImagePower.HasValue != coveredDistance.HasValue
            || fullImagePower is { } full && (!double.IsFinite(full) || full <= 0)
            || coveredDistance is { } covered && (!double.IsFinite(covered) || covered <= 0))
            throw new ArgumentException("A bounded energy window requires positive full-image power and covered distance.");
        var scale = Math.Max(samples.Max(s => s.Weight), double.Epsilon);
        _samples = samples.Where(s => s.Weight > 0).Select(s =>
        {
            var x = Math.Abs(s.X - center.X); var y = Math.Abs(s.Y - center.Y);
            var distance = type.StartsWith("X", StringComparison.OrdinalIgnoreCase) ? x
                : type.StartsWith("Y", StringComparison.OrdinalIgnoreCase) ? y
                : type.Contains("square", StringComparison.OrdinalIgnoreCase) ? Math.Max(x, y)
                : double.Hypot(x, y);
            return (Distance: distance, Weight: s.Weight / scale);
        }).OrderBy(s => s.Distance).ToArray();
        _windowed = fullImagePower.HasValue;
        _total = fullImagePower.HasValue ? fullImagePower.Value / scale : _samples.Sum(s => s.Weight);
        if (_windowed && _samples.Sum(s => s.Weight) > _total * (1 + 1e-10))
            throw new ArgumentException("Window power exceeds the full-image Parseval power.");
        _maximum = coveredDistance ?? _samples.Select(s => s.Distance + _pitch / 2).DefaultIfEmpty(0).Max();
        _prefixWeight = new double[_samples.Length + 1]; _prefixDistanceWeight = new double[_samples.Length + 1];
        for (var i = 0; i < _samples.Length; i++)
        {
            _prefixWeight[i + 1] = _prefixWeight[i] + _samples[i].Weight;
            _prefixDistanceWeight[i + 1] = _prefixDistanceWeight[i] + _samples[i].Distance * _samples[i].Weight;
        }
    }

    public double Fraction(double distance)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        if (distance <= 0 || _total <= 0) return 0;
        if (_windowed && distance > _maximum * (1 + 1e-12))
            throw new ArgumentOutOfRangeException(nameof(distance), "Requested radius exceeds the complete physical readout window.");
        if (!_windowed && distance >= _maximum) return 1;
        var lower = UpperBound(distance - _pitch / 2); var upper = UpperBound(distance + _pitch / 2);
        return Math.Clamp((_prefixWeight[lower] + ((distance + _pitch / 2)
            * (_prefixWeight[upper] - _prefixWeight[lower])
            - (_prefixDistanceWeight[upper] - _prefixDistanceWeight[lower])) / _pitch) / _total, 0, 1);
    }

    public double RadiusContaining(double target)
    {
        if (target <= 0 || _total <= 0) return 0;
        if (_windowed && Fraction(_maximum) < target)
            throw new InvalidOperationException("The requested energy quantile lies outside the evaluated window.");
        var low = 0d; var high = _maximum;
        for (var i = 0; i < 60; i++)
        {
            var middle = low + (high - low) / 2;
            if (Fraction(middle) < target) low = middle; else high = middle;
        }
        return high;
    }

    private int UpperBound(double distance)
    {
        var low = 0; var high = _samples.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_samples[middle].Distance <= distance) low = middle + 1; else high = middle;
        }
        return low;
    }

    private static double Pitch(double[] coordinates)
    {
        if (coordinates.Length < 2) throw new InvalidOperationException("FFT energy grid requires at least two nodes per axis.");
        var pitch = coordinates[1] - coordinates[0];
        if (!double.IsFinite(pitch) || pitch <= 0
            || coordinates.Skip(1).Select((x, i) => x - coordinates[i]).Any(d => Math.Abs(d / pitch - 1) > 1e-8))
            throw new InvalidOperationException("FFT energy grid pitch must be positive and uniform.");
        return pitch;
    }
}
