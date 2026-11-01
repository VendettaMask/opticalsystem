using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Geometries;

/// <summary>Exact ruled closure between two oriented circular edges. No tracing tessellation.</summary>
internal sealed class RuledCircularRim(Vector3D centerA, Vector3D uA, Vector3D vA, Vector3D centerB, Vector3D uB, Vector3D vB)
{
    public (double Distance, Vector3D Point, Vector3D Normal)? Intersect(Vector3D origin, Vector3D direction, double maximumDistance = double.PositiveInfinity)
    {
        (double Distance, Vector3D Point, Vector3D Normal)? nearest = null;
        // Two bounded tan-half-angle charts cover the complete circle, including pi.
        foreach (var sign in new[] { 1d, -1d })
        {
            var w = centerA - origin; var dc = centerB - centerA;
            Vector3D[] a = [w + uA * sign, vA * (2 * sign), w - uA * sign];
            Vector3D[] b = [dc + (uB - uA) * sign, (vB - vA) * (2 * sign), dc - (uB - uA) * sign];
            var polynomial = new double[5];
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) polynomial[i + j] += Dot(direction, Cross(a[i], b[j]));
            foreach (var q in Roots(polynomial))
            {
                var cosine = sign * (1 - q * q) / (1 + q * q); var sine = sign * 2 * q / (1 + q * q);
                var start = centerA + uA * cosine + vA * sine;
                var end = centerB + uB * cosine + vB * sine; var generator = end - start;
                var cross = Cross(direction, generator); var denominator = Dot(cross, cross);
                if (denominator <= 1e-28 * Math.Max(1, Dot(generator, generator))) continue;
                var offset = start - origin;
                var distance = Dot(Cross(offset, generator), cross) / denominator;
                var fraction = Dot(Cross(offset, direction), cross) / denominator;
                if (distance <= 0 || distance >= maximumDistance || fraction < -1e-12 || fraction > 1 + 1e-12) continue;
                var point = origin + direction * distance;
                if ((point - (start + generator * fraction)).Length > 2e-10 * Math.Max(1, Math.Max(uA.Length, uB.Length))) continue;
                var tangent = (-uA * sine + vA * cosine) * (1 - fraction) + (-uB * sine + vB * cosine) * fraction;
                var normal = Cross(tangent, generator); var length = normal.Length;
                if (!double.IsFinite(length) || length <= 1e-15) throw new InvalidDataException("Ruled rim has a singular normal.");
                nearest = (distance, start+generator*fraction, normal / length); maximumDistance = distance;
            }
        }
        return nearest;
    }

    internal static IReadOnlyList<double> Roots(double[] polynomial)
    {
        var scale = polynomial.Max(Math.Abs); if (scale == 0) return [];
        if (!double.IsFinite(scale)) throw new InvalidDataException("Nonfinite ruled-rim polynomial.");
        var p = polynomial.Select(c => c / scale).ToArray(); var degree = p.Length - 1;
        while (degree > 0 && p[degree] == 0) degree--;
        if (degree == 0) return [];
        if (degree == 1) { var root = -p[0] / p[1]; return root >= -1 && root <= 1 ? [root] : []; }
        var derivative = Enumerable.Range(1, degree).Select(i => i * p[i]).ToArray();
        var knots = new[] { -1d }.Concat(Roots(derivative)).Append(1d).Order().ToArray(); var roots = new List<double>();
        double Value(double x) { double result = p[degree]; for (int i = degree - 1; i >= 0; i--) result = result * x + p[i]; return result; }
        foreach (var knot in knots) if (Math.Abs(Value(knot)) <= 2e-13) roots.Add(knot);
        for (int i = 0; i < knots.Length - 1; i++)
        {
            var lo = knots[i]; var hi = knots[i + 1]; var fl = Value(lo); var fh = Value(hi);
            if (fl == 0 || fh == 0 || Math.Sign(fl) == Math.Sign(fh)) continue;
            for (int iteration = 0; iteration < 64; iteration++)
            {
                var middle = lo + (hi - lo) / 2; var fm = Value(middle);
                if (fm == 0) { lo = hi = middle; break; }
                if (Math.Sign(fm) == Math.Sign(fl)) { lo = middle; fl = fm; } else hi = middle;
            }
            roots.Add(lo + (hi - lo) / 2);
        }
        return roots.Order().DistinctBy(x => Math.Round(x, 13)).ToArray();
    }
    private static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static Vector3D Cross(Vector3D a, Vector3D b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}
