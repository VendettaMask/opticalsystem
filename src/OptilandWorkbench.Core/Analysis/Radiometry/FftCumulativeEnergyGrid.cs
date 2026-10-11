using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

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

    public FftCumulativeEnergyGrid(IReadOnlyList<EnergySample> samples,
        (double X, double Y) center, string type)
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
        _pitch = dx;
        var scale = samples.Max(s => s.Weight);
        _samples = samples.Where(s => s.Weight > 0).Select(s =>
        {
            var x = Math.Abs(s.X - center.X); var y = Math.Abs(s.Y - center.Y);
            var distance = type.StartsWith("X", StringComparison.OrdinalIgnoreCase) ? x
                : type.StartsWith("Y", StringComparison.OrdinalIgnoreCase) ? y
                : type.Contains("square", StringComparison.OrdinalIgnoreCase) ? Math.Max(x, y)
                : double.Hypot(x, y);
            return (distance, s.Weight / scale);
        }).ToArray();
        _total = _samples.Sum(s => s.Weight);
        _maximum = _samples.Select(s => s.Distance + _pitch / 2).DefaultIfEmpty(0).Max();
    }

    public double Fraction(double distance)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        if (distance <= 0 || _total <= 0) return 0;
        if (distance >= _maximum) return 1;
        return _samples.Sum(s => s.Weight
            * Math.Clamp((distance - s.Distance) / _pitch + .5, 0, 1)) / _total;
    }

    public double RadiusContaining(double target)
    {
        if (target <= 0 || _total <= 0) return 0;
        var low = 0d; var high = _maximum;
        for (var i = 0; i < 60; i++)
        {
            var middle = low + (high - low) / 2;
            if (Fraction(middle) < target) low = middle; else high = middle;
        }
        return high;
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
