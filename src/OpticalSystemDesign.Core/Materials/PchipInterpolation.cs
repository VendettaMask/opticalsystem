namespace OpticalSystemDesign.Core.Materials;

/// <summary>Shape-preserving cubic interpolation with explicit endpoint holding.
/// Fritsch-Butland weighted harmonic interior slopes; one-sided endpoint slopes.</summary>
internal sealed class PchipInterpolation
{
    private readonly double[] _x, _y, _slopes, _quadratic, _cubic;
    public PchipInterpolation(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        if (x.Count == 0 || x.Count != y.Count || x.Any(v => !double.IsFinite(v)) || y.Any(v => !double.IsFinite(v))
            || x.Zip(x.Skip(1)).Any(pair => pair.First >= pair.Second))
            throw new ArgumentException("PCHIP requires finite, strictly increasing sample coordinates and matching values.");
        _x = x.ToArray(); _y = y.ToArray(); _slopes = new double[x.Count];
        _quadratic = new double[x.Count - 1]; _cubic = new double[x.Count - 1];
        if (x.Count == 1) return;
        var h = new double[x.Count - 1]; var delta = new double[x.Count - 1];
        for (var i = 0; i < h.Length; i++) { h[i] = x[i + 1] - x[i]; delta[i] = (y[i + 1] - y[i]) / h[i]; }
        if (x.Count == 2) _slopes[0] = _slopes[1] = delta[0];
        else
        {
            _slopes[0] = Endpoint(h[0], h[1], delta[0], delta[1]);
            _slopes[^1] = Endpoint(h[^1], h[^2], delta[^1], delta[^2]);
            for (var i = 1; i < x.Count - 1; i++)
            {
                if (!SameSign(delta[i - 1], delta[i])) continue;
                var w1 = 2 * h[i] + h[i - 1]; var w2 = h[i] + 2 * h[i - 1];
                _slopes[i] = (w1 + w2) / (w1 / delta[i - 1] + w2 / delta[i]);
            }
        }
        for (var i = 0; i < h.Length; i++)
        {
            _quadratic[i] = (3 * delta[i] - 2 * _slopes[i] - _slopes[i + 1]) / h[i];
            _cubic[i] = (_slopes[i] + _slopes[i + 1] - 2 * delta[i]) / (h[i] * h[i]);
        }
    }
    public double Evaluate(double x)
    {
        if (!double.IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
        if (x <= _x[0]) return _y[0];
        if (x >= _x[^1]) return _y[^1];
        var found = Array.BinarySearch(_x, x);
        if (found >= 0) return _y[found];
        var i = ~found - 1; var dx = x - _x[i];
        return _y[i] + dx * (_slopes[i] + dx * (_quadratic[i] + dx * _cubic[i]));
    }
    private static bool SameSign(double a, double b) => a > 0 && b > 0 || a < 0 && b < 0;
    private static double Endpoint(double h0, double h1, double d0, double d1)
    {
        var slope = ((2 * h0 + h1) * d0 - h0 * d1) / (h0 + h1);
        if (!SameSign(slope, d0)) return 0;
        return !SameSign(d0, d1) && Math.Abs(slope) > 3 * Math.Abs(d0) ? 3 * d0 : slope;
    }
}
