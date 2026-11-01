using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Scattering;

/// <summary>The retained log1p-bilinear function supplies evaluation, quadrature and both CDFs.
/// No ordinate, total power or sampled weight is normalized to pass a passive-energy test.</summary>
internal sealed class LogBsdfSlice
{
    private readonly double[] _mu, _phi, _r, _t, _rcdf, _tcdf;
    private readonly Cell?[] _reflection, _transmission;
    private readonly MeasuredBsdfLogInterpolation _model;
    public double R => _rcdf[^1]; public double T => _tcdf[^1];
    internal LogBsdfSlice(double[] mu, double[] phi, double[] reflection, double[] transmission, MeasuredBsdfLogInterpolation model)
    {
        _mu = mu; _phi = phi; _model = model;
        _r = reflection.Select(model.Encode).ToArray(); _t = transmission.Select(model.Encode).ToArray();
        var budget = 4 * MeasuredBsdfData.MaximumPoints;
        (_reflection, _rcdf) = Build(_r, ref budget); (_transmission, _tcdf) = Build(_t, ref budget);
    }
    private (Cell?[], double[]) Build(double[] values, ref int budget)
    {
        var cells = new Cell?[(_mu.Length - 1) * (_phi.Length - 1)]; var cdf = new double[cells.Length];
        double sum = 0, correction = 0; int index = 0;
        for (int i = 0; i < _mu.Length - 1; i++) for (int j = 0; j < _phi.Length - 1; j++)
        {
            ComputationCancellation.ThrowIfCancellationRequested(); var k = i * _phi.Length + j;
            var g = new[] { values[k], values[k + 1], values[k + _phi.Length], values[k + _phi.Length + 1] };
            if (g.Any(v => v > 0)) cells[index] = new(_mu[i], _mu[i + 1], _phi[j], _phi[j + 1], g, _model.ScalePerSteradian, ref budget);
            var mass = cells[index]?.Mass ?? 0; var adjusted = mass - correction; var next = sum + adjusted; correction = (next - sum) - adjusted;
            cdf[index++] = sum = next;
        }
        if (!double.IsFinite(sum) || sum < 0) throw new InvalidDataException("BSDF 对数插值积分无效。");
        return (cells, cdf);
    }
    internal double Eval(bool transmission, double mu, double phi)
    {
        const double tolerance = 32 * 2.2204460492503131e-16;
        if (mu < _mu[0] - tolerance || mu > _mu[^1] + tolerance) return 0;
        var m = MeasuredBsdfGrid.Bracket(_mu, mu); var p = MeasuredBsdfGrid.Bracket(_phi, phi); var v = transmission ? _t : _r;
        double At(int i) => v[i * _phi.Length + p.Low] * (1 - p.T) + v[i * _phi.Length + p.High] * p.T;
        return _model.ScalePerSteradian * MeasuredBsdfLogInterpolation.Expm1(At(m.Low) * (1 - m.T) + At(m.High) * m.T);
    }
    private static int Select(double[] cdf, double target)
    {
        int low = 0, high = cdf.Length - 1;
        while (low < high) { var mid = (low + high) / 2; if (cdf[mid] > target) high = mid; else low = mid + 1; } return low;
    }
    internal (double Mu, double Phi) Sample(bool transmission, double u, double v)
    {
        var cdf = transmission ? _tcdf : _rcdf; var cells = transmission ? _transmission : _reflection;
        var target = Math.Min(u * cdf[^1], Math.BitDecrement(cdf[^1])); var index = Select(cdf, target);
        var before = index == 0 ? 0 : cdf[index - 1];
        return cells[index]!.Sample(Math.Clamp((target - before) / (cdf[index] - before), 5.551115123125783e-17, Math.BitDecrement(1d)), v);
    }
    private sealed class Cell
    {
        private readonly double _mu0, _width, _phi0, _phiWidth, _scale;
        private readonly double[] _g, _cdf;
        private readonly Leaf[] _leaves;
        private readonly record struct Leaf(double Low, double High, double Mass);
        public double Mass => _cdf[^1];
        // Positive Gauss-Legendre half rules, including their symmetric partners.
        private static readonly (double X, double W)[] Rule8 = [(.1834346424956498,.362683783378362),(.525532409916329,.3137066458778873),(.7966664774136267,.2223810344533745),(.9602898564975363,.1012285362903763)];
        private static readonly (double X, double W)[] Rule16 = [(.09501250983763744,.1894506104550685),(.2816035507792589,.1826034150449236),(.4580167776572274,.1691565193950025),(.6178762444026438,.1495959888165767),(.755404408355003,.1246289712555339),(.8656312023878318,.09515851168249278),(.9445750230732326,.06225352393864789),(.9894009349916499,.02715245941175409)];
        public Cell(double low, double high, double phiLow, double phiHigh, double[] g, double scale, ref int budget)
        {
            _mu0 = low; _width = high - low; _phi0 = phiLow; _phiWidth = phiHigh - phiLow; _g = g; _scale = scale;
            var leaves = new List<Leaf>(); Split(0, 1, 0, leaves, ref budget); _leaves = leaves.ToArray(); _cdf = new double[leaves.Count];
            double sum = 0, correction = 0;
            for (int i = 0; i < leaves.Count; i++) { var adjusted = leaves[i].Mass - correction; var next = sum + adjusted; correction = (next - sum) - adjusted; _cdf[i] = sum = next; }
        }
        private double PhiIntegral(double u, double v)
        {
            var a = _g[0] * (1 - u) + _g[2] * u;
            var b = (_g[1] - _g[0]) * (1 - u) + (_g[3] - _g[2]) * u;
            return _scale * _phiWidth * v * (MeasuredBsdfLogInterpolation.Expm1(a) + Math.Exp(a) * MeasuredBsdfLogInterpolation.ExprelMinusOne(b * v));
        }
        private double Density(double u) => (_mu0 + _width * u) * _width * PhiIntegral(u, 1);
        private double Quad(double low, double high, (double X, double W)[] rule)
        {
            double sum = 0; var mid = (low + high) / 2; var half = (high - low) / 2;
            foreach (var node in rule) sum += node.W * (Density(mid - half * node.X) + Density(mid + half * node.X));
            return sum * half;
        }
        private void Split(double low, double high, int depth, List<Leaf> leaves, ref int budget)
        {
            ComputationCancellation.ThrowIfCancellationRequested(); var fine = Quad(low, high, Rule16); var coarse = Quad(low, high, Rule8);
            if (!double.IsFinite(fine) || fine < 0) throw new InvalidDataException("BSDF 对数单元积分溢出或负值。");
            if (Math.Abs(fine - coarse) <= 1e-18 * (high - low) + 1e-12 * fine)
            {
                if (--budget < 0) throw new InvalidDataException("BSDF 对数积分超过有界细分预算。");
                leaves.Add(new(low, high, fine)); return;
            }
            if (depth >= 12) throw new InvalidDataException("BSDF 对数单元积分未收敛；不使用未验证的功率。");
            var mid = (low + high) / 2; Split(low, mid, depth + 1, leaves, ref budget); Split(mid, high, depth + 1, leaves, ref budget);
        }
        public (double Mu, double Phi) Sample(double u, double v)
        {
            var target = Math.Min(u * Mass, Math.BitDecrement(Mass)); var index = Select(_cdf, target); var leaf = _leaves[index];
            target -= index == 0 ? 0 : _cdf[index - 1]; double left = leaf.Low, right = leaf.High;
            var at = left + (right - left) * (target / leaf.Mass);
            for (int i = 0; i < 48; i++)
            {
                var residual = Quad(leaf.Low, at, Rule16) - target;
                if (Math.Abs(residual) <= 2e-15 * leaf.Mass || right - left < 2e-15) break;
                if (residual < 0) left = at; else right = at;
                var next = at - residual / Density(at); at = double.IsFinite(next) && next > left && next < right ? next : (left + right) / 2;
            }
            var totalPhi = PhiIntegral(at, 1); left = 0; right = 1;
            for (int i = 0; i < 48; i++) { var mid = (left + right) / 2; if (PhiIntegral(at, mid) < v * totalPhi) left = mid; else right = mid; }
            var mu = Math.Clamp(_mu0 + _width * at, _mu0, _mu0 + _width);
            return (mu, _phi0 + _phiWidth * ((left + right) / 2));
        }
    }
}
