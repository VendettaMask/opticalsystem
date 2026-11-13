namespace OpticalSystemDesign.Core.Analysis.Imaging;

public static partial class MonochromeSlantedEdge
{
    /// <summary>Exact convolution of a sampled 2-D intensity PSF with an analytic half-plane,
    /// integrated over each full square pixel. Sorted projected shifts and prefix moments
    /// evaluate the same quadratic square-coverage function without a large 2-D object FFT.</summary>
    private sealed class ProjectedEdgeKernel
    {
        private readonly double[] _q, _w, _m1, _m2;
        private readonly double _a, _b;

        internal ProjectedEdgeKernel(PsfResult psf, int subgrid, double nx, double ny, double mass)
        {
            _a = Math.Max(Math.Abs(nx), Math.Abs(ny)) / 2; _b = Math.Min(Math.Abs(nx), Math.Abs(ny)) / 2;
            var center = psf.GridSize / 2;
            var sorted = new List<(double Q, double W)>();
            for (var y = 0; y < psf.GridSize; y++) for (var x = 0; x < psf.GridSize; x++)
                if (psf.Values[y, x] > 0) sorted.Add(((nx * (x - center) + ny * (y - center)) / subgrid, psf.Values[y, x] / mass));
            sorted.Sort((a, b) => a.Q.CompareTo(b.Q));
            _q = new double[sorted.Count]; _w = new double[sorted.Count + 1];
            _m1 = new double[_w.Length]; _m2 = new double[_w.Length];
            double c0 = 0, c1 = 0, c2 = 0;
            for (var i = 0; i < sorted.Count; i++)
            {
                var (q, w) = sorted[i]; _q[i] = q;
                _w[i + 1] = Add(_w[i], w, ref c0);
                _m1[i + 1] = Add(_m1[i], q * w, ref c1);
                _m2[i + 1] = Add(_m2[i], q * q * w, ref c2);
            }
        }

        internal double At(double d)
        {
            var a = _a + _b; var b = _a - _b;
            var i0 = Upper(d - a); var i1 = Upper(d - b); var i2 = Upper(d + b); var i3 = Upper(d + a);
            var full = _w[i0];
            if (_b < 1e-14) return full + (.5 + d / (2 * _a)) * (_w[i3] - _w[i0]) - (_m1[i3] - _m1[i0]) / (2 * _a);
            double Square(int lo, int hi, double origin) => (_m2[hi] - _m2[lo])
                - 2 * origin * (_m1[hi] - _m1[lo]) + origin * origin * (_w[hi] - _w[lo]);
            return full + (_w[i1] - _w[i0]) - Square(i0, i1, d - a) / (8 * _a * _b)
                + (.5 + d / (2 * _a)) * (_w[i2] - _w[i1]) - (_m1[i2] - _m1[i1]) / (2 * _a)
                + Square(i2, i3, d + a) / (8 * _a * _b);
        }

        private int Upper(double value)
        {
            int lo = 0, hi = _q.Length;
            while (lo < hi) { var mid = lo + (hi - lo) / 2; if (_q[mid] <= value) lo = mid + 1; else hi = mid; }
            return lo;
        }
        private static double Add(double sum, double value, ref double compensation)
        {
            var corrected = value - compensation; var next = sum + corrected;
            compensation = (next - sum) - corrected; return next;
        }
    }
}
