using OptilandWorkbench.ZemaxComparison.Normalization;

namespace OptilandWorkbench.ZemaxComparison.Metrics;

public sealed record MatchedValues(double[] X, double[]? Y, double[] Workbench, double[] Zemax, double Coverage);
public static class ComparisonMetrics
{
    public static MatchedValues Align(Series1DResult w, Series1DResult z, List<string> log)
    {
        w = PhysicalNormalization.Convert(w, z.XAxis, z.YAxis, log);
        var a = PhysicalNormalization.Sort(w.X, w.Y); var b = PhysicalNormalization.Sort(z.X, z.Y);
        var lo = Math.Max(a.X[0], b.X[0]); var hi = Math.Min(a.X[^1], b.X[^1]);
        if (hi <= lo) throw new InvalidDataException("No common physical coordinate interval");
        var x = a.X.Concat(b.X).Where(v => v >= lo && v <= hi).Distinct().Order().ToArray();
        log.Add($"{w.Id}: sorted coordinates; linear interpolation on union of {x.Length} physical knots in [{lo:R}, {hi:R}]; no extrapolation");
        return new(x, null, x.Select(v => PhysicalNormalization.Interpolate(a.X, a.Y, v)).ToArray(),
            x.Select(v => PhysicalNormalization.Interpolate(b.X, b.Y, v)).ToArray(),
            (hi - lo) / Math.Max(a.X[^1] - a.X[0], b.X[^1] - b.X[0]));
    }
    public static MatchedValues Align(Grid2DResult w, Grid2DResult z, List<string> log)
    {
        if (w.XAxis.Quantity != z.XAxis.Quantity || w.YAxis.Quantity != z.YAxis.Quantity || w.ValueAxis.Quantity != z.ValueAxis.Quantity)
            throw new InvalidDataException("Grid physical quantity mismatch");
        var sx = PhysicalNormalization.UnitScale(w.XAxis.Unit, z.XAxis.Unit);
        var sy = PhysicalNormalization.UnitScale(w.YAxis.Unit, z.YAxis.Unit);
        var sv = PhysicalNormalization.UnitScale(w.ValueAxis.Unit, z.ValueAxis.Unit);
        var wx = w.X.Select(v => v * sx).ToArray(); var wy = w.Y.Select(v => v * sy).ToArray();
        if (w.Z.Length != wy.Length || w.Z.Any(row => row.Length != wx.Length) || z.Z.Length != z.Y.Length || z.Z.Any(row => row.Length != z.X.Length))
            throw new InvalidDataException("Grid shape and axes disagree");
        if (wx.Length != z.X.Length || wy.Length != z.Y.Length
            || wx.Zip(z.X).Any(p => Math.Abs(p.First - p.Second) > 1e-8 * Math.Max(1, Math.Abs(p.Second)))
            || wy.Zip(z.Y).Any(p => Math.Abs(p.First - p.Second) > 1e-8 * Math.Max(1, Math.Abs(p.Second))))
            throw new InvalidDataException("Grid coordinates differ. No implicit grid resize, shift, flip or peak normalization is permitted.");
        var xs = new List<double>(); var ys = new List<double>(); var a = new List<double>(); var b = new List<double>();
        var union = 0;
        for (var row = 0; row < wy.Length; row++)
            for (var col = 0; col < wx.Length; col++)
            {
                var wa = w.Z[row][col]; var zb = z.Z[row][col];
                var validA = wa.HasValue && double.IsFinite(wa.Value); var validB = zb.HasValue && double.IsFinite(zb.Value);
                if (validA || validB) union++;
                if (!validA || !validB) continue;
                xs.Add(z.X[col]); ys.Add(z.Y[row]); a.Add(wa!.Value * sv); b.Add(zb!.Value);
            }
        if (a.Count == 0) throw new InvalidDataException("No valid grid overlap");
        log.Add($"{w.Id}: typed units ({sx:R},{sy:R},{sv:R}); identical physical axes; intersection of finite masks ({a.Count}/{union}); no orientation search");
        return new(xs.ToArray(), ys.ToArray(), a.ToArray(), b.ToArray(), (double)a.Count / union);
    }
    public static ComparisonMetric Calculate(string id, string unit, MatchedValues values, Tolerances t)
    {
        var a = values.Workbench; var b = values.Zemax;
        if (a.Length == 0 || a.Length != b.Length || a.Any(v => !double.IsFinite(v)) || b.Any(v => !double.IsFinite(v)))
            throw new InvalidDataException("Metric input is empty, mismatched or non-finite");
        var error = a.Zip(b, (w, z) => Math.Abs(w - z)).ToArray(); var sorted = error.Order().ToArray();
        var rmse = Math.Sqrt(error.Select(e => e * e).Average());
        var scale = Math.Max(b.Max(Math.Abs), t.Absolute);
        var nrmse = scale == 0 ? (rmse == 0 ? 0 : double.PositiveInfinity) : rmse / scale;
        var worst = Array.IndexOf(error, error.Max());
        var meanA = a.Average(); var meanB = b.Average();
        var ssA = a.Sum(x => (x - meanA) * (x - meanA)); var ssB = b.Sum(x => (x - meanB) * (x - meanB));
        double? correlation = ssA == 0 || ssB == 0 ? null : a.Zip(b, (x, y) => (x - meanA) * (y - meanB)).Sum() / Math.Sqrt(ssA * ssB);
        var pointwise = error.Select((e, i) => e <= t.Absolute + t.Relative * Math.Abs(b[i])).All(v => v);
        var conclusion = values.Coverage < t.MinimumCoverage ? Conclusion.Incomparable
            : pointwise || (a.Length > 1 && nrmse <= t.Nrmse) ? Conclusion.Pass
            : nrmse <= t.CloseNrmse ? Conclusion.Close : Conclusion.Difference;
        double Q(double p) { var pos = (sorted.Length - 1) * p; var i = (int)pos; return sorted[i] + (sorted[Math.Min(i + 1, sorted.Length - 1)] - sorted[i]) * (pos - i); }
        var extras = new Dictionary<string, double?>();
        if (a.Length == 1) { extras["workbench"] = a[0]; extras["zemax"] = b[0]; }
        return new(id, unit, a.Length, error.Max(), error.Average(), rmse, nrmse,
            error.Select((e, i) => b[i] == 0 ? e == 0 ? 0 : double.PositiveInfinity : e / Math.Abs(b[i])).Max(),
            Q(0.5), Q(0.9), Q(0.95), correlation, values.X[worst], values.Y?[worst], values.Coverage, conclusion, extras);
    }
    public static Dictionary<string, double?> GridStatistics(MatchedValues v, string prefix, bool wavefront)
    {
        var result = new Dictionary<string, double?>();
        foreach (var (tag, values) in new[] { ("workbench", v.Workbench), ("zemax", v.Zemax) })
        {
            var stem = prefix + "." + tag + ".";
            result[stem + "peak"] = values.Max(); result[stem + "pv"] = values.Max() - values.Min();
            result[stem + "rmsAboutZero"] = Math.Sqrt(values.Average(x => x * x));
            var mean = values.Average();
            result[stem + "rmsAfterPiston"] = Math.Sqrt(values.Average(x => Math.Pow(x - mean, 2)));
            if (wavefront) continue;
            var sum = values.Sum(); result[stem + "sampleSum"] = sum;
            var dx = v.X.Distinct().Order().Take(2).ToArray(); var dy = v.Y!.Distinct().Order().Take(2).ToArray();
            result[stem + "integratedEnergy"] = dx.Length == 2 && dy.Length == 2 ? sum * (dx[1] - dx[0]) * (dy[1] - dy[0]) : null;
            var cx = sum == 0 ? 0 : values.Zip(v.X, (a, x) => a * x).Sum() / sum;
            var cy = sum == 0 ? 0 : values.Zip(v.Y!, (a, y) => a * y).Sum() / sum;
            result[stem + "centroidX"] = sum == 0 ? null : cx; result[stem + "centroidY"] = sum == 0 ? null : cy;
            foreach (var axis in new[] { ("X", v.X), ("Y", v.Y!) })
            {
                var marginal = values.Zip(axis.Item2).GroupBy(p => p.Second).OrderBy(g => g.Key).ToArray();
                var xx = marginal.Select(g => g.Key).ToArray(); var yy = marginal.Select(g => g.Sum(p => p.First)).ToArray();
                var half = yy.Max() / 2;
                var crossings = Enumerable.Range(1, xx.Length - 1).Where(i => (yy[i - 1] - half) * (yy[i] - half) <= 0 && yy[i] != yy[i - 1])
                    .Select(i => xx[i - 1] + (xx[i] - xx[i - 1]) * (half - yy[i - 1]) / (yy[i] - yy[i - 1])).ToArray();
                result[stem + "marginalFwhm" + axis.Item1] = crossings.Length >= 2 ? crossings[^1] - crossings[0] : null;
            }
            var radial = values.Select((value, i) => (Radius: Math.Sqrt(Math.Pow(v.X[i] - cx, 2) + Math.Pow(v.Y![i] - cy, 2)), Value: value)).OrderBy(p => p.Radius).ToArray();
            foreach (var fraction in new[] { 0.5, 0.8, 0.9 })
            {
                double cumulative = 0; double? radius = null;
                if (sum > 0 && values.All(n => n >= 0)) foreach (var p in radial) { cumulative += p.Value; if (cumulative >= sum * fraction) { radius = p.Radius; break; } }
                result[stem + "encircledEnergyRadius" + (int)(100 * fraction)] = radius;
            }
        }
        return result;
    }
    public static Dictionary<string, double?> MtfStatistics(MatchedValues v)
    {
        var result = new Dictionary<string, double?>();
        foreach (var (tag, a) in new[] { ("workbench", v.Workbench), ("zemax", v.Zemax) })
        {
            result[tag + ".dc"] = v.X[0] == 0 ? a[0] : null;
            foreach (var f in new[] { 10d, 20d, 30d, 50d }) result[tag + ".frequency" + f] = f >= v.X[0] && f <= v.X[^1] ? PhysicalNormalization.Interpolate(v.X, a, f) : null;
            foreach (var fraction in new[] { 0.5, 0.1 })
            {
                double? crossing = null;
                for (var i = 1; i < a.Length; i++) if (a[i - 1] >= fraction && a[i] < fraction)
                { crossing = v.X[i - 1] + (v.X[i] - v.X[i - 1]) * (fraction - a[i - 1]) / (a[i] - a[i - 1]); break; }
                result[tag + ".firstCrossing" + (int)(fraction * 100)] = crossing;
            }
        }
        return result;
    }
}
