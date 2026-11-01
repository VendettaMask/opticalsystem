namespace OpticalSystemDesign.Core.Scattering;

/// <summary>Nonnegative theta/phi bilinear density; integrates and samples the same retained table.</summary>
internal sealed class SpeosThetaSlice
{
    private readonly double[] _theta, _phi, _r, _t, _rcdf, _tcdf;
    private readonly bool _projected;
    internal double R => _rcdf[^1]; internal double T => _tcdf[^1];
    internal SpeosThetaSlice(MeasuredBsdfPoint[] points, bool projected, bool normalIsotropic = false)
    {
        _projected = projected;
        _theta = points.Select(p => p.OutgoingThetaDegrees * Math.PI / 180).Distinct().Order().ToArray();
        _phi = points.Select(p => p.OutgoingPhiDegrees * Math.PI / 180).Distinct().Order().ToArray();
        if (_theta.Length is < 2 or > 1025 || _phi.Length is < 2 or > 257 || _theta[0] != 0 || _theta[^1] != Math.PI / 2 || _phi[0] != 0 || _phi[^1] != 2 * Math.PI || _theta.Length * _phi.Length != points.Length) throw new InvalidDataException("Speos theta table must cover the full hemisphere.");
        _r = new double[points.Length]; _t = new double[points.Length]; var seen = new HashSet<int>();
        foreach (var p in points) { var at = Array.BinarySearch(_theta, p.OutgoingThetaDegrees * Math.PI / 180) * _phi.Length + Array.BinarySearch(_phi, p.OutgoingPhiDegrees * Math.PI / 180); if (!seen.Add(at) || !double.IsFinite(p.BrdfPerSteradian) || !double.IsFinite(p.BtdfPerSteradian) || p.BrdfPerSteradian < 0 || p.BtdfPerSteradian < 0) throw new InvalidDataException("Invalid Speos theta node."); _r[at] = p.BrdfPerSteradian; _t[at] = p.BtdfPerSteradian; }
        for (int i = 0; i < _theta.Length; i++) { var k = i * _phi.Length; if (!MeasuredBsdfGrid.Close(_r[k], _r[k + _phi.Length - 1]) || !MeasuredBsdfGrid.Close(_t[k], _t[k + _phi.Length - 1])) throw new InvalidDataException("Speos theta seam differs."); if (i == 0 || normalIsotropic) for (int j = 1; j < _phi.Length; j++) if (!MeasuredBsdfGrid.Close(_r[k], _r[k + j]) || !MeasuredBsdfGrid.Close(_t[k], _t[k + j])) throw new InvalidDataException("Speos outgoing pole or isotropic normal-incidence distribution differs."); }
        _rcdf = Build(_r); _tcdf = Build(_t);
    }
    // Integral of linear basis times sin(theta), or sin(theta)*cos(theta).
    internal static double BasisIntegral(double a, double width, double x, bool upper, bool projected)
    {
        if (x <= 0) return 0;
        if (x < .001)
        {
            ReadOnlySpan<double> nodes = [-.8611363115940526, -.3399810435848563, .3399810435848563, .8611363115940526];
            ReadOnlySpan<double> weights = [.34785484513745385, .6521451548625461, .6521451548625461, .34785484513745385];
            double sum = 0; for (int i = 0; i < 4; i++) { var s = x * (nodes[i] + 1) / 2; var b = upper ? s / width : 1 - s / width; sum += weights[i] * b * Math.Sin(a + s) * (projected ? 1 : Math.Cos(a + s)); }
            return sum * x / 2;
        }
        var end = a + x; var j0 = projected ? 2 * Math.Sin(a + x / 2) * Math.Sin(x / 2) : Math.Sin(2 * a + x) * Math.Sin(x) / 2;
        var j1 = projected ? Math.Sin(end) - Math.Sin(a) - x * Math.Cos(end) : (Math.Sin(2 * end) - Math.Sin(2 * a)) / 8 - x * Math.Cos(2 * end) / 4;
        return Math.Max(0, upper ? j1 / width : j0 - j1 / width);
    }
    private double[] Masses(double[] values, int i, int j) { var a = _theta[i]; var d = _theta[i + 1] - a; var lo = BasisIntegral(a, d, d, false, _projected); var hi = BasisIntegral(a, d, d, true, _projected); var phi = (_phi[j + 1] - _phi[j]) / 2; var k = i * _phi.Length + j; return [values[k] * lo * phi, values[k + 1] * lo * phi, values[k + _phi.Length] * hi * phi, values[k + _phi.Length + 1] * hi * phi]; }
    private double[] Build(double[] v) { var cdf = new double[(_theta.Length - 1) * (_phi.Length - 1)]; double sum = 0, correction = 0; int k = 0; for (int i = 0; i < _theta.Length - 1; i++) for (int j = 0; j < _phi.Length - 1; j++) { var adjusted = Masses(v, i, j).Sum() - correction; var next = sum + adjusted; correction = (next - sum) - adjusted; cdf[k++] = sum = next; } return cdf; }
    internal double Eval(bool t, double mu, double phi) { var theta = Math.Acos(Math.Clamp(mu, 0, 1)); var b = MeasuredBsdfGrid.Bracket(_theta, theta); var c = MeasuredBsdfGrid.Bracket(_phi, phi); var v = t ? _t : _r; double At(int i) => v[i * _phi.Length + c.Low] * (1 - c.T) + v[i * _phi.Length + c.High] * c.T; return At(b.Low) * (1 - b.T) + At(b.High) * b.T; }
    internal (double Mu, double Phi) Sample(bool t, double u, double v) { var cdf = t ? _tcdf : _rcdf; var target = Math.Min(u * cdf[^1], Math.BitDecrement(cdf[^1])); int low = 0, high = cdf.Length - 1; while (low < high) { var mid = (low + high) / 2; if (cdf[mid] > target) high = mid; else low = mid + 1; } var before = low == 0 ? 0 : cdf[low - 1]; u = (target - before) / (cdf[low] - before); var i = low / (_phi.Length - 1); var j = low % (_phi.Length - 1); var masses = Masses(t ? _t : _r, i, j); var total = masses.Sum(); var choice = Array.FindLastIndex(masses, m => m > 0); var selection = u * total; for (int k = 0; k < masses.Length; k++) { if (masses[k] > 0 && selection < masses[k]) { choice = k; break; } selection -= masses[k]; } u = Math.Clamp(selection / masses[choice], 0, Math.BitDecrement(1d)); var a = _theta[i]; var d = _theta[i + 1] - a; var upper = choice >= 2; var norm = BasisIntegral(a, d, d, upper, _projected); double left = 0, right = d; for (int n = 0; n < 54; n++) { var x = (left + right) / 2; if (BasisIntegral(a, d, x, upper, _projected) < u * norm) left = x; else right = x; } var p = choice % 2 == 1 ? Math.Sqrt(v) : 1 - Math.Sqrt(1 - v); return (Math.Cos(a + (left + right) / 2), _phi[j] + (_phi[j + 1] - _phi[j]) * p); }
}
