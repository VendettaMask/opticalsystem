using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Scattering;

public sealed class MeasuredBsdf(MeasuredBsdfData data) : IBsdf
{
    internal MeasuredBsdfData Data => data;
    public string Kind => data.ComputationProtocol;
    public BsdfEnergy Integrate(BsdfContext c) => data.Grid.Integrate(c);
    public double Evaluate(BsdfContext c, Vector3D direction) => data.Grid.Evaluate(c, direction);
    public double Pdf(BsdfContext c, Vector3D direction)
    {
        var energy = Integrate(c).Total; var cosine = BsdfContext.Dot(c.SurfaceNormal, BsdfContext.Unit(direction));
        return energy > 0 ? Evaluate(c, direction) * Math.Abs(cosine) / energy : Math.Max(0, cosine) / Math.PI;
    }
    public BsdfSample Sample(BsdfContext c, double u1, double u2) => data.Grid.Sample(this, c, u1, u2);
}

internal interface IMeasuredBsdfDistribution
{
    bool HasTransmission { get; }
    bool HasBothSides { get; }
    BsdfEnergy Integrate(BsdfContext context);
    double Evaluate(BsdfContext context, Vector3D direction);
    BsdfSample Sample(MeasuredBsdf model, BsdfContext context, double u1, double u2);
}

internal sealed class MeasuredBsdfGrid : IMeasuredBsdfDistribution
{
    private readonly double[] _wavelengths, _incidences, _azimuths;
    private readonly bool _fixed;
    private readonly bool _projected;
    private readonly Dictionary<(BsdfSide S, int W, int I, int P), Slice> _slices = new();
    public bool HasTransmission { get; }
    public bool HasBothSides { get; }
    internal static double Cos(double degrees) => degrees == 90 ? 0 : degrees == 0 ? 1 : Math.Cos(degrees * Math.PI / 180);
    internal static bool Close(double a, double b) => Math.Abs(a - b) <= 32 * 2.2204460492503131e-16 * Math.Max(1e-300, Math.Max(a, b));
    internal MeasuredBsdfGrid(IReadOnlyList<MeasuredBsdfPoint> points, BsdfCoordinates coordinates, bool variableOutputGrids = false, bool projected = false)
    {
        _fixed = coordinates == BsdfCoordinates.FixedMaterialAxes;
        _projected = projected;
        foreach (var p in points)
            if (!Enum.IsDefined(p.Side) || !Finite(p.WavelengthNanometers, 200, 20000) || !Finite(p.IncidentThetaDegrees, 0, 90)
                || !Finite(p.OutgoingThetaDegrees, 0, 90) || !Finite(p.OutgoingPhiDegrees, 0, 360) || !Finite(p.IncidentPhiDegrees, 0, 360)
                || !_fixed && p.IncidentPhiDegrees != 0 || !double.IsFinite(p.BrdfPerSteradian) || p.BrdfPerSteradian < 0
                || !double.IsFinite(p.BtdfPerSteradian) || p.BtdfPerSteradian < 0) throw new InvalidDataException("BSDF 角度、波长或 sr⁻¹ 无效。");
        _wavelengths = points.Select(p => p.WavelengthNanometers).Distinct().Order().ToArray();
        _incidences = points.Select(p => Cos(p.IncidentThetaDegrees)).Distinct().Order().ToArray();
        _azimuths = points.Select(p => p.IncidentPhiDegrees * Math.PI / 180).Distinct().Order().ToArray();
        if (_wavelengths.Length > 32 || _incidences.Length > 64 || _azimuths.Length > 65
            || _fixed && (_azimuths.Length < 2 || _azimuths[0] != 0 || _azimuths[^1] != 2 * Math.PI))
            throw new InvalidDataException("BSDF 入射轴超限；固定轴数据须完整覆盖 0–360° 入射方位。");
        var sides = points.Select(p => p.Side).Distinct().ToArray(); HasBothSides = sides.Length == 2;
        foreach (var g in points.GroupBy(p => (p.Side, p.WavelengthNanometers, I: Cos(p.IncidentThetaDegrees), P: p.IncidentPhiDegrees * Math.PI / 180)))
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var key = (g.Key.Side, Array.BinarySearch(_wavelengths, g.Key.WavelengthNanometers), Array.BinarySearch(_incidences, g.Key.I), Array.BinarySearch(_azimuths, g.Key.P));
            var slice = new Slice(g.ToArray(), !_fixed && g.Key.I == 1, projected);
            if (!double.IsFinite(slice.R + slice.T) || slice.R + slice.T > 1 + 32 * 2.2204460492503131e-16)
                throw new InvalidDataException($"BSDF 半球积分 R+T={slice.R + slice.T:G17} 超过 1；不自动归一化测量数据。");
            _slices.Add(key, slice);
        }
        if ((long)sides.Length * _wavelengths.Length * _incidences.Length * _azimuths.Length != _slices.Count)
            throw new InvalidDataException("BSDF 入射/波长轴缺少切片。");
        if (!_fixed && !variableOutputGrids)
        {
            var count = (long)sides.Length * _wavelengths.Length * _incidences.Length * points.Select(p => p.OutgoingThetaDegrees).Distinct().Count() * points.Select(p => p.OutgoingPhiDegrees).Distinct().Count();
            if (count != points.Count) throw new InvalidDataException("各向同性 BSDF 需要完整公共出射网格。");
        }
        else foreach (var side in sides) for (var w = 0; w < _wavelengths.Length; w++) for (var i = 0; i < _incidences.Length; i++)
        {
            Same(_slices[(side, w, i, 0)], _slices[(side, w, i, _azimuths.Length - 1)], "入射方位接缝");
            if (_incidences[i] == 1) for (var p = 1; p < _azimuths.Length; p++) Same(_slices[(side, w, i, 0)], _slices[(side, w, i, p)], "法向入射分布");
        }
        HasTransmission = _slices.Values.Any(s => s.T > 0);
    }
    private static bool Finite(double x, double low, double high) => double.IsFinite(x) && x >= low && x <= high;
    private static void Same(Slice a, Slice b, string label)
    {
        foreach (var mu in a.Mu.Concat(b.Mu).Distinct()) foreach (var phi in a.Phi.Concat(b.Phi).Distinct())
            if (!Close(a.Eval(false, mu, phi), b.Eval(false, mu, phi)) || !Close(a.Eval(true, mu, phi), b.Eval(true, mu, phi))) throw new InvalidDataException("BSDF " + label + "不一致。");
    }
    private readonly record struct Weighted(Slice Slice, double Weight);
    private Weighted[] Inputs(BsdfContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var w = Bracket(_wavelengths, c.WavelengthNanometers); var i = Bracket(_incidences, Math.Clamp(BsdfContext.Dot(c.IncomingDirection, c.SurfaceNormal), 0, 1));
        var p = Bracket(_azimuths, _fixed ? Azimuth(c.IncomingDirection, Frame(c)) : 0); var result = new List<Weighted>(8);
        foreach (var x in Terms(w)) foreach (var y in Terms(i)) foreach (var z in Terms(p))
        {
            if (!_slices.TryGetValue((c.Side, x.Index, y.Index, z.Index), out var s)) throw new InvalidDataException("BSDF 未提供当前入射侧的测量数据。");
            result.Add(new(s, x.Weight * y.Weight * z.Weight));
        }
        return result.ToArray();
    }
    private static IEnumerable<(int Index, double Weight)> Terms((int Low, int High, double T) v)
    {
        if (v.Low == v.High) { yield return (v.Low, 1); yield break; }
        if (v.T < 1) yield return (v.Low, 1 - v.T); if (v.T > 0) yield return (v.High, v.T);
    }
    internal static (int Low, int High, double T) Bracket(double[] a, double x)
    {
        var tolerance = 32 * 2.2204460492503131e-16 * Math.Max(1, Math.Max(Math.Abs(a[0]), Math.Abs(a[^1])));
        if (x < a[0] - tolerance || x > a[^1] + tolerance) throw new InvalidDataException("BSDF 波长或角度超出测量范围；不外推。");
        x = Math.Clamp(x, a[0], a[^1]); var index = Array.BinarySearch(a, x); if (index >= 0) return (index, index, 0);
        index = ~index; return (index - 1, index, (x - a[index - 1]) / (a[index] - a[index - 1]));
    }
    public BsdfEnergy Integrate(BsdfContext c) { var input = Inputs(c); return new(input.Sum(s => s.Weight * s.Slice.R), input.Sum(s => s.Weight * s.Slice.T)); }
    private (Vector3D X, Vector3D Y) Frame(BsdfContext c)
    {
        var n = c.SurfaceNormal; var x = _fixed ? c.MaterialTangent ?? throw new ArgumentException("固定材料轴 BSDF 需要显式材料切向基底。") : c.IncomingDirection - n * BsdfContext.Dot(c.IncomingDirection, n);
        if (x.Length < 1e-12) x = BsdfContext.Cross(Math.Abs(n.Z) < .9 ? new(0, 0, 1) : new Vector3D(0, 1, 0), n);
        x = BsdfContext.Unit(x); return (x, BsdfContext.Cross(n, x));
    }
    private static double Azimuth(Vector3D v, (Vector3D X, Vector3D Y) frame)
    {
        var x = BsdfContext.Dot(v, frame.X); var y = BsdfContext.Dot(v, frame.Y); if (x * x + y * y < 1e-28) return 0;
        var phi = Math.Atan2(y, x); return phi < 0 ? phi + 2 * Math.PI : phi;
    }
    public double Evaluate(BsdfContext c, Vector3D direction)
    {
        var input = Inputs(c); var unit = BsdfContext.Unit(direction); var mu = BsdfContext.Dot(c.SurfaceNormal, unit); var phi = Azimuth(unit, Frame(c));
        var value = input.Sum(s => s.Weight * s.Slice.Eval(mu < 0, Math.Min(1, Math.Abs(mu)), phi));
        return _projected ? Math.Abs(mu) > 0 ? value / Math.Abs(mu) : 0 : value;
    }
    private static int Select(double[] masses, ref double u)
    {
        var total = masses.Sum(); double cumulative = 0; var last = Array.FindLastIndex(masses, v => v > 0);
        for (var i = 0; i < masses.Length; i++) { var before = cumulative; cumulative += masses[i]; if (masses[i] > 0 && (u * total < cumulative || i == last)) { u = Open((u * total - before) / masses[i]); return i; } }
        throw new InvalidOperationException("BSDF 采样质量为零。");
    }
    private static double Open(double u) => Math.Clamp(u, 5.551115123125783e-17, Math.BitDecrement(1d));
    public BsdfSample Sample(MeasuredBsdf model, BsdfContext c, double u1, double u2)
    {
        if (!double.IsFinite(u1) || !double.IsFinite(u2) || u1 is < 0 or >= 1 || u2 is < 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(u1));
        var input = Inputs(c); var energy = input.Sum(s => s.Weight * (s.Slice.R + s.Slice.T)); if (energy == 0) return new LambertianReflectionBsdf(0).Sample(c, u1, u2);
        u1 = Open(u1); u2 = Open(u2); var s = input[Select(input.Select(v => v.Weight * (v.Slice.R + v.Slice.T)).ToArray(), ref u1)].Slice;
        var transmission = Select([s.R, s.T], ref u1) == 1; var (mu, phi) = s.Sample(transmission, u1, u2); var frame = Frame(c); var radius = Math.Sqrt(Math.Max(0, 1 - mu * mu));
        var direction = BsdfContext.Unit(frame.X * (radius * Math.Cos(phi)) + frame.Y * (radius * Math.Sin(phi)) + c.SurfaceNormal * (transmission ? -mu : mu));
        return new(direction, model.Evaluate(c, direction), model.Pdf(c, direction), transmission ? BsdfLobe.DiffuseTransmission : BsdfLobe.DiffuseReflection);
    }
    internal sealed class Slice
    {
        public double[] Mu { get; }
        public double[] Phi { get; }
        private readonly double[] _r, _t, _rcdf, _tcdf;
        private readonly bool _projected;
        public double R => _rcdf[^1]; public double T => _tcdf[^1];
        public Slice(MeasuredBsdfPoint[] points, bool normalIsotropic, bool projected)
        {
            _projected = projected;
            Mu = points.Select(p => Cos(p.OutgoingThetaDegrees)).Distinct().Order().ToArray(); Phi = points.Select(p => p.OutgoingPhiDegrees * Math.PI / 180).Distinct().Order().ToArray();
            if (Mu.Length is < 2 or > 129 || Phi.Length is < 2 or > 257 || Mu[0] != 0 || Mu[^1] != 1 || Phi[0] != 0 || Phi[^1] != 2 * Math.PI || Mu.Length * Phi.Length != points.Length) throw new InvalidDataException("BSDF 出射切片需要完整有界 0–90°/0–360° 网格。");
            _r = new double[points.Length]; _t = new double[points.Length]; var seen = new HashSet<int>();
            foreach (var p in points) { var at = Array.BinarySearch(Mu, Cos(p.OutgoingThetaDegrees)) * Phi.Length + Array.BinarySearch(Phi, p.OutgoingPhiDegrees * Math.PI / 180); if (!seen.Add(at)) throw new InvalidDataException("BSDF 存在重复节点。"); _r[at] = p.BrdfPerSteradian; _t[at] = p.BtdfPerSteradian; }
            for (var m = 0; m < Mu.Length; m++)
            {
                var start = m * Phi.Length; if (!Close(_r[start], _r[start + Phi.Length - 1]) || !Close(_t[start], _t[start + Phi.Length - 1])) throw new InvalidDataException("BSDF 出射方位接缝不连续。");
                if (m == Mu.Length - 1 || normalIsotropic) for (var a = 1; a < Phi.Length; a++) if (!Close(_r[start], _r[start + a]) || !Close(_t[start], _t[start + a])) throw new InvalidDataException("BSDF 出射极点或各向同性法向入射不唯一。");
            }
            _rcdf = Build(_r); _tcdf = Build(_t);
        }
        private double[] Masses(double[] v, int m, int a)
        {
            var d = Mu[m + 1] - Mu[m]; var phi = (Phi[a + 1] - Phi[a]) / 2; var lo = d * (Mu[m] / 2 + d / 6) * phi; var hi = d * (Mu[m] / 2 + d / 3) * phi; var at = m * Phi.Length + a;
            if (_projected) lo = hi = d * phi / 2;
            return [v[at] * lo, v[at + 1] * lo, v[at + Phi.Length] * hi, v[at + Phi.Length + 1] * hi];
        }
        private double[] Build(double[] v)
        {
            var cdf = new double[(Mu.Length - 1) * (Phi.Length - 1)]; double sum = 0, correction = 0; int cell = 0;
            for (var m = 0; m < Mu.Length - 1; m++) for (var a = 0; a < Phi.Length - 1; a++) { var mass = Masses(v, m, a).Sum(); var adjusted = mass - correction; var next = sum + adjusted; correction = (next - sum) - adjusted; cdf[cell++] = sum = next; }
            return cdf;
        }
        public double Eval(bool transmission, double mu, double phi)
        {
            var m = Bracket(Mu, mu); var a = Bracket(Phi, phi); var v = transmission ? _t : _r;
            var lo = v[m.Low * Phi.Length + a.Low] * (1 - a.T) + v[m.Low * Phi.Length + a.High] * a.T; var hi = v[m.High * Phi.Length + a.Low] * (1 - a.T) + v[m.High * Phi.Length + a.High] * a.T;
            return lo * (1 - m.T) + hi * m.T;
        }
        public (double Mu, double Phi) Sample(bool transmission, double u1, double u2)
        {
            var cdf = transmission ? _tcdf : _rcdf; var target = Math.Min(u1 * cdf[^1], Math.BitDecrement(cdf[^1])); int low = 0, high = cdf.Length - 1;
            while (low < high) { var mid = low + (high - low) / 2; if (cdf[mid] > target) high = mid; else low = mid + 1; }
            var before = low == 0 ? 0 : cdf[low - 1]; u1 = Open((target - before) / (cdf[low] - before)); var m = low / (Phi.Length - 1); var a = low % (Phi.Length - 1);
            var node = Select(Masses(transmission ? _t : _r, m, a), ref u1); bool upperMu = node >= 2, upperPhi = node % 2 == 1; var mu0 = Mu[m]; var d = Mu[m + 1] - mu0; var normalization = upperMu ? mu0 / 2 + d / 3 : mu0 / 2 + d / 6;
            if (_projected) { var s = upperMu ? Math.Sqrt(u1) : 1 - Math.Sqrt(1 - u1); var p = upperPhi ? Math.Sqrt(u2) : 1 - Math.Sqrt(1 - u2); return (mu0 + d * s, Phi[a] + (Phi[a + 1] - Phi[a]) * p); }
            double left = 0, right = 1;
            for (var iteration = 0; iteration < 54; iteration++) { var s = (left + right) / 2; var integral = upperMu ? mu0 * s * s / 2 + d * s * s * s / 3 : mu0 * (s - s * s / 2) + d * (s * s / 2 - s * s * s / 3); if (integral < u1 * normalization) left = s; else right = s; }
            var mu = Math.Clamp(mu0 + d * ((left + right) / 2), 0, 1); var t = upperPhi ? Math.Sqrt(u2) : 1 - Math.Sqrt(1 - u2); return (mu, Phi[a] + (Phi[a + 1] - Phi[a]) * t);
        }
    }
}
