using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Scattering;

public sealed record ZemaxBsdfTis(BsdfSide Side, double SpecularThetaDegrees, double SampleRotationDegrees, double Fraction);
public sealed record ZemaxBsdfDefinition(bool Transmission, double IncidentIndex, double TransmittedIndex, IReadOnlyList<ZemaxBsdfTis> Integrals)
{
    internal ZemaxBsdfDefinition Freeze()
    {
        if (!double.IsFinite(IncidentIndex) || IncidentIndex <= 0 || !double.IsFinite(TransmittedIndex) || TransmittedIndex <= 0
            || Integrals is null || Integrals.Count is < 1 or > 6400 || Integrals.Any(x => x is null)) throw new InvalidDataException("Zemax BSDF 的介质或 TIS 数量无效。");
        return this with { Integrals = Array.AsReadOnly(Integrals.OrderBy(x => x.Side).ThenBy(x => x.SpecularThetaDegrees).ThenBy(x => x.SampleRotationDegrees).ToArray()) };
    }
}

/// <summary>Preserves the original specular-centred grid; no angular raster resampling or peak broadening.</summary>
internal sealed class ZemaxBsdfDistribution : IMeasuredBsdfDistribution
{
    private readonly ZemaxBsdfDefinition _definition;
    private readonly bool _fixed;
    private readonly double _wavelength;
    private readonly Dictionary<BsdfSide, SideGrid> _sides = new();
    public bool HasTransmission => _definition.Transmission && _sides.Values.Any(g => g.Slices.Any(s => s.Energy > 0));
    public bool HasBothSides => _sides.Count == 2;
    private sealed record SideGrid(double[] Cosines, double[] Rotations, Slice[] Slices);
    private readonly record struct Weighted(Slice Slice, double Weight);

    public ZemaxBsdfDistribution(IReadOnlyList<MeasuredBsdfPoint> points, BsdfCoordinates coordinates, ZemaxBsdfDefinition definition)
    {
        _definition = definition; _fixed = coordinates == BsdfCoordinates.FixedMaterialAxes;
        var waves = points.Select(p => p.WavelengthNanometers).Distinct().ToArray();
        if (waves.Length != 1 || !double.IsFinite(waves[0]) || waves[0] is < 200 or > 20000) throw new InvalidDataException("Zemax 单色 BSDF 需要一个明确的辐射度测量波长。");
        _wavelength = waves[0];
        if (points.Any(p => !Enum.IsDefined(p.Side) || !Finite(p.IncidentThetaDegrees, 0, 90) || !Finite(p.IncidentPhiDegrees, 0, 360)
            || !Finite(p.OutgoingThetaDegrees, 0, 180) || !Finite(p.OutgoingPhiDegrees, 0, 360)
            || !Finite(p.BrdfPerSteradian, 0, double.MaxValue) || !Finite(p.BtdfPerSteradian, 0, double.MaxValue)
            || (definition.Transmission ? p.BrdfPerSteradian : p.BtdfPerSteradian) != 0)) throw new InvalidDataException("Zemax BSDF 的侧、角度或分布无效。");
        var energies = definition.Integrals.ToDictionary(x => (x.Side, x.SpecularThetaDegrees, x.SampleRotationDegrees));
        foreach (var side in points.GroupBy(p => p.Side))
        {
            var ratio = definition.Transmission ? (side.Key == BsdfSide.Front ? definition.TransmittedIndex / definition.IncidentIndex : definition.IncidentIndex / definition.TransmittedIndex) : 1;
            var slices = new List<Slice>();
            foreach (var g in side.GroupBy(p => (p.IncidentThetaDegrees, p.IncidentPhiDegrees)))
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                if (!energies.Remove((side.Key, g.Key.IncidentThetaDegrees, g.Key.IncidentPhiDegrees), out var tis)
                    || !Finite(tis.Fraction, 0, 1)) throw new InvalidDataException("Zemax BSDF 缺少唯一有效 TIS。");
                var sin = Math.Sin(g.Key.IncidentThetaDegrees * Math.PI / 180) * ratio;
                if (sin > 1 + 1e-14) throw new InvalidDataException("BTDF 文件的折射后角度与声明介质不相容。");
                var cosine = Math.Sqrt(Math.Max(0, 1 - sin * sin));
                slices.Add(new(g.ToArray(), tis.Fraction, cosine, definition.Transmission));
            }
            var cosines = slices.Select(s => s.IncidentCosine).Distinct().Order().ToArray(); var rotations = slices.Select(s => s.Rotation).Distinct().Order().ToArray();
            if (cosines.Length > 100 || rotations.Length > 50 || slices.Count != cosines.Length * rotations.Length
                || _fixed && (rotations.Length < 2 || rotations[0] != 0 || rotations[^1] != 360)
                || !_fixed && (rotations.Length != 1 || rotations[0] != 0)) throw new InvalidDataException("Zemax BSDF 入射/旋转网格不完整。");
            var ordered = cosines.SelectMany(c => rotations.Select(a => slices.Single(s => s.IncidentCosine == c && s.Rotation == a))).ToArray();
            if (_fixed) foreach (var cosine in cosines)
            {
                var first = slices.Single(s => s.IncidentCosine == cosine && s.Rotation == 0); var last = slices.Single(s => s.IncidentCosine == cosine && s.Rotation == 360);
                if (!first.Same(last)) throw new InvalidDataException("Zemax 0/360° 材料旋转接缝不一致。");
            }
            var normal = slices.Where(s => s.IncidentCosine == 1).ToArray();
            if (normal.Length > 0)
            {
                var mus = normal.SelectMany(s => s.Mus).Where(mu => mu > 0).Distinct().ToArray();
                var boundaries = normal.SelectMany(s => s.Phis.Select(p => Wrap((90 - s.Rotation) + (definition.Transmission ? -p : p) * 180 / Math.PI) * Math.PI / 180)).Append(0).Append(2 * Math.PI).Distinct().Order().ToArray();
                // Unmeasured azimuth gaps are zero, including between supplied endpoints. Probe each interval too.
                var azimuths = boundaries.Concat(boundaries.Zip(boundaries.Skip(1), (a, b) => (a + b) / 2)).ToArray();
                foreach (var mu in mus) foreach (var phi in azimuths)
                {
                    var expected = normal[0].NormalValue(mu, phi, definition.Transmission);
                    if (_fixed ? normal.Skip(1).Any(s => !MeasuredBsdfGrid.Close(expected, s.NormalValue(mu, phi, definition.Transmission)))
                        : !MeasuredBsdfGrid.Close(expected, normal[0].NormalValue(mu, azimuths[0], definition.Transmission)))
                        throw new InvalidDataException("Zemax 法向入射分布不唯一。");
                }
            }
            _sides.Add(side.Key, new(cosines, rotations, ordered));
        }
        if (energies.Count != 0) throw new InvalidDataException("Zemax BSDF 含未引用的 TIS。");
    }
    private static bool Finite(double x, double min, double max) => double.IsFinite(x) && x >= min && x <= max;
    private static double Dot(Vector3D a, Vector3D b) => BsdfContext.Dot(a, b);
    private static double Wrap(double degrees) { var x = degrees % 360; return x < 0 ? x + 360 : x; }
    private Weighted[] Inputs(BsdfContext c)
    {
        if (!_sides.TryGetValue(c.Side, out var g)) throw new InvalidDataException("Zemax BSDF 缺少当前入射侧。");
        if (Math.Abs(c.WavelengthNanometers - _wavelength) > 1e-12 * _wavelength) throw new InvalidDataException("Zemax 单色测量不能外推到其他波长。");
        if (_definition.Transmission)
        {
            var ni = c.Side == BsdfSide.Front ? _definition.IncidentIndex : _definition.TransmittedIndex;
            var nt = c.Side == BsdfSide.Front ? _definition.TransmittedIndex : _definition.IncidentIndex;
            if (Math.Abs(c.IncidentRefractiveIndex / c.TransmittedRefractiveIndex - ni / nt) > 1e-10 * ni / nt)
                throw new InvalidDataException("Zemax BTDF 的实际介质折射率比与测量声明不匹配。");
        }
        var incidence = MeasuredBsdfGrid.Bracket(g.Cosines, Math.Clamp(Dot(c.IncomingDirection, c.SurfaceNormal), 0, 1));
        var rotation = 0d;
        if (_fixed)
        {
            var x = c.MaterialTangent ?? throw new ArgumentException("固定轴 Zemax BSDF 需要材料轴。"); var y = BsdfContext.Cross(c.SurfaceNormal, x);
            var px = Dot(c.IncomingDirection, x); var py = Dot(c.IncomingDirection, y);
            rotation = px * px + py * py < 1e-28 ? 0 : Wrap(90 - Math.Atan2(py, px) * 180 / Math.PI);
        }
        var a = MeasuredBsdfGrid.Bracket(g.Rotations, rotation); var list = new List<Weighted>(4);
        IEnumerable<(int Index, double Weight)> Terms((int Low, int High, double T) v)
        { if (v.Low == v.High) { yield return (v.Low, 1); yield break; } if (v.T < 1) yield return (v.Low, 1 - v.T); if (v.T > 0) yield return (v.High, v.T); }
        foreach (var i in Terms(incidence)) foreach (var p in Terms(a)) list.Add(new(g.Slices[i.Index * g.Rotations.Length + p.Index], i.Weight * p.Weight));
        return list.ToArray();
    }
    public BsdfEnergy Integrate(BsdfContext c)
    { var e = Inputs(c).Sum(s => s.Weight * s.Slice.Energy); return _definition.Transmission ? new(0, e) : new(e, 0); }
    private (Vector3D S, Vector3D E, Vector3D F) Frame(BsdfContext c, Slice slice)
    {
        var n = c.SurfaceNormal; Vector3D u;
        if (_fixed)
        {
            var x = c.MaterialTangent!.Value; var y = BsdfContext.Cross(n, x); var angle = (90 - slice.Rotation) * Math.PI / 180;
            u = x * Math.Cos(angle) + y * Math.Sin(angle);
        }
        else
        {
            u = c.IncomingDirection - n * Dot(c.IncomingDirection, n);
            if (u.Length < 1e-12) u = BsdfContext.Cross(Math.Abs(n.Z) < .9 ? new(0, 0, 1) : new Vector3D(0, 1, 0), n);
            u = BsdfContext.Unit(u);
        }
        var sign = _definition.Transmission ? -1 : 1; var s = u * -slice.SinTheta + n * (sign * slice.CosTheta);
        var e = u * slice.CosTheta + n * (sign * slice.SinTheta); return (s, e, BsdfContext.Cross(s, e));
    }
    public double Evaluate(BsdfContext c, Vector3D direction)
    {
        var input = Inputs(c); var d = BsdfContext.Unit(direction); var muNormal = Dot(d, c.SurfaceNormal);
        if (_definition.Transmission ? muNormal >= 0 : muNormal <= 0) return 0;
        double sum = 0;
        foreach (var w in input)
        {
            var frame = Frame(c, w.Slice); var mu = Math.Clamp(Dot(d, frame.S), -1, 1); var phi = Math.Atan2(Dot(d, frame.F), Dot(d, frame.E)); if (phi < 0) phi += 2 * Math.PI;
            sum += w.Weight * w.Slice.Value(mu, phi);
        }
        return sum;
    }
    public BsdfSample Sample(MeasuredBsdf model, BsdfContext c, double u1, double u2)
    {
        if (!Finite(u1, 0, Math.BitDecrement(1d)) || !Finite(u2, 0, Math.BitDecrement(1d))) throw new ArgumentOutOfRangeException(nameof(u1));
        var input = Inputs(c); var total = input.Sum(w => w.Weight * w.Slice.Energy); if (total == 0) return new LambertianReflectionBsdf(0).Sample(c, u1, u2);
        u1 = Math.Clamp(u1, 5.551115123125783e-17, Math.BitDecrement(1d)); var target = u1 * total; var selected = input.Last(w => w.Weight * w.Slice.Energy > 0);
        double before = 0;
        foreach (var w in input) { var mass = w.Weight * w.Slice.Energy; if (mass > 0 && target < before + mass) { selected = w; break; } before += mass; }
        var (mu, phi) = selected.Slice.Sample(Math.Clamp((target - before) / (selected.Weight * selected.Slice.Energy), 0, Math.BitDecrement(1d)), u2);
        var f = Frame(c, selected.Slice); var r = Math.Sqrt(Math.Max(0, 1 - mu * mu)); var d = BsdfContext.Unit(f.S * mu + f.E * (r * Math.Cos(phi)) + f.F * (r * Math.Sin(phi)));
        return new(d, model.Evaluate(c, d), model.Pdf(c, d), _definition.Transmission ? BsdfLobe.DiffuseTransmission : BsdfLobe.DiffuseReflection);
    }

    private sealed class Slice
    {
        public double Energy { get; } public double IncidentCosine { get; } public double Rotation { get; }
        public double CosTheta { get; } public double SinTheta { get; }
        public double[] Mus => _mu; public double[] Phis => _phi;
        private readonly double[] _mu, _phi, _values; private readonly Cell[] _cells; private readonly double[] _cdf; private readonly double _integral;
        public Slice(MeasuredBsdfPoint[] nodes, double energy, double incidentCosine, bool transmission)
        {
            Energy = energy; IncidentCosine = incidentCosine; Rotation = nodes[0].IncidentPhiDegrees;
            CosTheta = MeasuredBsdfGrid.Cos(nodes[0].IncidentThetaDegrees); SinTheta = Math.Sqrt(Math.Max(0, 1 - CosTheta * CosTheta));
            _mu = nodes.Select(p => MeasuredBsdfGrid.Cos(p.OutgoingThetaDegrees)).Distinct().Order().ToArray(); _phi = nodes.Select(p => p.OutgoingPhiDegrees * Math.PI / 180).Distinct().Order().ToArray();
            if (_mu.Length is < 2 or > 1000 || _phi.Length is < 2 or > 1000 || nodes.Length != _mu.Length * _phi.Length) throw new InvalidDataException("Zemax 出射网格不完整或超限。");
            _values = new double[nodes.Length]; var seen = new HashSet<int>();
            foreach (var p in nodes) { var k = Array.BinarySearch(_mu, MeasuredBsdfGrid.Cos(p.OutgoingThetaDegrees)) * _phi.Length + Array.BinarySearch(_phi, p.OutgoingPhiDegrees * Math.PI / 180); if (!seen.Add(k)) throw new InvalidDataException("Zemax 重复出射节点。"); _values[k] = transmission ? p.BtdfPerSteradian : p.BrdfPerSteradian; }
            for (var i = 0; i < _mu.Length; i++)
            {
                if (_phi[0] == 0 && _phi[^1] == 2 * Math.PI && !MeasuredBsdfGrid.Close(_values[i * _phi.Length], _values[(i + 1) * _phi.Length - 1])) throw new InvalidDataException("Zemax 出射方位接缝不一致。");
                if (Math.Abs(_mu[i]) == 1) for (var p = 1; p < _phi.Length; p++) if (!MeasuredBsdfGrid.Close(_values[i * _phi.Length], _values[i * _phi.Length + p])) throw new InvalidDataException("Zemax 镜面中心极点不唯一。");
            }
            var cells = new List<Cell>(); var cdf = new List<double>(); double sum = 0;
            for (var m = 0; m < _mu.Length - 1; m++) for (var p = 0; p < _phi.Length - 1; p++)
            {
                ComputationCancellation.ThrowIfCancellationRequested(); var at = m * _phi.Length + p;
                var cell = new Cell(_mu[m], _mu[m + 1], _phi[p], _phi[p + 1], _values[at], _values[at + 1], _values[at + _phi.Length], _values[at + _phi.Length + 1], CosTheta, SinTheta);
                if (cell.Mass > 0) { sum += cell.Mass; cells.Add(cell); cdf.Add(sum); }
            }
            _cells = cells.ToArray(); _cdf = cdf.ToArray(); _integral = sum;
            if (!double.IsFinite(sum) || sum <= 0 && energy > 0) throw new InvalidDataException("Zemax 非零 TIS 没有有效出射半球分布。");
        }
        public bool Same(Slice b) => MeasuredBsdfGrid.Close(Energy, b.Energy) && _mu.SequenceEqual(b._mu) && _phi.SequenceEqual(b._phi) && _values.Zip(b._values).All(v => MeasuredBsdfGrid.Close(v.First, v.Second));
        public double NormalValue(double mu, double phi, bool transmission)
        { var rawPhi = transmission ? (90 - Rotation) * Math.PI / 180 - phi : phi - (90 - Rotation) * Math.PI / 180; rawPhi %= 2 * Math.PI; if (rawPhi < 0) rawPhi += 2 * Math.PI; return Value(mu, rawPhi); }
        public double Value(double mu, double phi)
        {
            if (Energy == 0 || mu < _mu[0] - 8e-15 || mu > _mu[^1] + 8e-15 || phi < _phi[0] || phi > _phi[^1]) return 0;
            mu = Math.Clamp(mu, _mu[0], _mu[^1]);
            var m = MeasuredBsdfGrid.Bracket(_mu, mu); var p = MeasuredBsdfGrid.Bracket(_phi, phi);
            double At(int i, int j) => _values[i * _phi.Length + j];
            var v = (At(m.Low, p.Low) * (1 - p.T) + At(m.Low, p.High) * p.T) * (1 - m.T)
                + (At(m.High, p.Low) * (1 - p.T) + At(m.High, p.High) * p.T) * m.T;
            return v * Energy / _integral;
        }
        public (double Mu, double Phi) Sample(double u, double v)
        {
            var target = Math.Clamp(u, 5.551115123125783e-17, Math.BitDecrement(1d)) * _integral;
            var i = Array.BinarySearch(_cdf, target); if (i < 0) i = ~i; i = Math.Min(i, _cdf.Length - 1);
            return _cells[i].Sample(target - (i == 0 ? 0 : _cdf[i - 1]), v);
        }
    }

    // The physical cosine is A*mu+B*sqrt(1-mu²)*cos(phi). Integrate its positive region analytically in phi.
    private sealed class Cell
    {
        private readonly double _lo, _hi, _p0, _p1, _v00, _v01, _v10, _v11, _a, _b, _scale;
        private readonly double[] _cuts;
        public double Mass { get; }
        public Cell(double lo, double hi, double p0, double p1, double v00, double v01, double v10, double v11, double a, double b)
        {
            (_lo, _hi, _p0, _p1, _v00, _v01, _v10, _v11, _a, _b) = (lo, hi, p0, p1, v00, v01, v10, v11, a, b);
            _scale = Math.Max(Math.Max(v00, v01), Math.Max(v10, v11));
            var cuts = new List<double> { lo, hi };
            foreach (var p in new[] { p0, p1, 0, Math.PI, 2 * Math.PI }.Where(p => p >= p0 && p <= p1))
            { var c = Math.Cos(p); var den = Math.Sqrt(a * a + b * b * c * c); if (den > 0) { var root = -b * c / den; if (root > lo && root < hi) cuts.Add(root); } }
            _cuts = cuts.Distinct().Order().ToArray(); Mass = Integral(lo, hi);
        }
        private double PhiMass(double mu, double upper)
        {
            var t = (mu - _lo) / (_hi - _lo); var left = _v00 * (1 - t) + _v10 * t; var right = _v01 * (1 - t) + _v11 * t;
            var slope = (right - left) / (_p1 - _p0); var c = _a * mu; var d = _b * Math.Sqrt(Math.Max(0, 1 - mu * mu));
            if (d == 0 && c <= 0 || upper <= _p0) return 0;
            var intervals = new List<(double L, double H)>();
            if (d == 0 || c >= d) intervals.Add((_p0, upper));
            else if (c > -d)
            {
                var angle = Math.Acos(Math.Clamp(-c / d, -1, 1));
                foreach (var (l, h) in new[] { (0d, angle), (2 * Math.PI - angle, 2 * Math.PI) }) if (Math.Min(upper, h) > Math.Max(_p0, l)) intervals.Add((Math.Max(_p0, l), Math.Min(upper, h)));
            }
            double sum = 0;
            foreach (var (l, h) in intervals)
            {
                var width = h - l; var fl = left + slope * (l - _p0);
                sum += c * (fl * width + slope * width * width / 2)
                    + d * (fl * (Math.Sin(h) - Math.Sin(l)) + slope * (width * Math.Sin(h) + Math.Cos(h) - Math.Cos(l)));
            }
            return Math.Max(0, sum);
        }
        private double Integral(double low, double high)
        {
            if (_scale == 0 || high <= low) return 0; double sum = 0;
            var cuts = _cuts.Where(x => x > low && x < high).Prepend(low).Append(high).ToArray();
            for (var k = 0; k < cuts.Length - 1; k++)
            {
                // Integrate in radial angle: the sin(theta) Jacobian removes the square-root endpoint singularity in mu.
                var l = Math.Acos(cuts[k + 1]); var h = Math.Acos(cuts[k]); var m = (l + h) / 2;
                var fl = ThetaMass(l); var fm = ThetaMass(m); var fh = ThetaMass(h); var whole = (h - l) * (fl + 4 * fm + fh) / 6;
                sum += Refine(l, h, fl, fm, fh, whole, Math.Max(1e-300, _scale * (_p1 - _p0) * (cuts[k + 1] - cuts[k]) * 2e-11), 18);
            }
            return Math.Max(0, sum);
        }
        private double ThetaMass(double theta) => PhiMass(Math.Cos(theta), _p1) * Math.Sin(theta);
        private double Refine(double l, double h, double fl, double fm, double fh, double whole, double tolerance, int depth)
        {
            var m = (l + h) / 2; var f1 = ThetaMass((l + m) / 2); var f2 = ThetaMass((m + h) / 2);
            var a = (m - l) * (fl + 4 * f1 + fm) / 6; var b = (h - m) * (fm + 4 * f2 + fh) / 6; var delta = a + b - whole;
            if (Math.Abs(delta) <= 15 * tolerance) return a + b + delta / 15;
            if (depth == 0) throw new InvalidDataException("Zemax 投影积分未收敛，不能生成不可靠 PDF。");
            return Refine(l, m, fl, f1, fm, a, tolerance / 2, depth - 1) + Refine(m, h, fm, f2, fh, b, tolerance / 2, depth - 1);
        }
        public (double Mu, double Phi) Sample(double mass, double v)
        {
            var low = _lo; var high = _hi; var mu = low + (high - low) * Math.Clamp(mass / Mass, 1e-12, 1 - 1e-12);
            for (var k = 0; k < 48; k++)
            {
                var error = Integral(_lo, mu) - mass; if (Math.Abs(error) <= 2e-12 * Mass) break;
                if (error > 0) high = mu; else low = mu; var derivative = PhiMass(mu, _p1);
                var next = derivative > 0 ? mu - error / derivative : double.NaN;
                mu = next > low && next < high ? next : (low + high) / 2;
            }
            var pLow = _p0; var pHigh = _p1; var target = Math.Clamp(v, 5.551115123125783e-17, Math.BitDecrement(1d)) * PhiMass(mu, _p1);
            for (var k = 0; k < 52; k++) { var p = (pLow + pHigh) / 2; if (PhiMass(mu, p) < target) pLow = p; else pHigh = p; }
            return (mu, (pLow + pHigh) / 2);
        }
    }
}
