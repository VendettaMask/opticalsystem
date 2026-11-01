using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Scattering;

public sealed record XyzCalibrationColor(double X, double Y, double Z);

/// <summary>A caller-declared cone of input colours. Its convex chromaticity hull uses XYZ
/// features only, never held-out radiometric labels. Black maps to black.</summary>
public sealed record XyzCalibrationGamut(string Source, string SourceSha256, IReadOnlyList<XyzCalibrationColor> Vertices)
{
    public const int MaximumVertices = 64;
    public static XyzCalibrationGamut Create(string source, string sourceSha256, IReadOnlyList<XyzCalibrationColor> colors)
    {
        if (colors is null || colors.Count is < 3 or > XyzRadiometricCalibrator.MaximumObservations
            || colors.Any(p => p is null || new[] { p.X, p.Y, p.Z }.Any(v => !double.IsFinite(v) || v < 0 || v > 1e12)))
            throw new InvalidDataException("XYZ 适用域需要有限非负的输入颜色，不能由辐射度验证标签推断。");
        var points = colors.Where(p => p.X + p.Y + p.Z > 0).Select(p => { var s = p.X + p.Y + p.Z; return new XyzCalibrationColor(p.X / s, p.Y / s, p.Z / s); })
            .OrderBy(p => p.X).ThenBy(p => p.Y).DistinctBy(p => (p.X, p.Y)).ToArray();
        var lower = new List<XyzCalibrationColor>(); var upper = new List<XyzCalibrationColor>();
        foreach (var p in points) { ComputationCancellation.ThrowIfCancellationRequested(); while (lower.Count > 1 && Turn(lower[^2], lower[^1], p) <= 0) lower.RemoveAt(lower.Count - 1); lower.Add(p); }
        foreach (var p in points.Reverse()) { while (upper.Count > 1 && Turn(upper[^2], upper[^1], p) <= 0) upper.RemoveAt(upper.Count - 1); upper.Add(p); }
        return new XyzCalibrationGamut(source, sourceSha256, lower.SkipLast(1).Concat(upper.SkipLast(1)).ToArray()).Freeze();
    }
    private static double Turn(XyzCalibrationColor a, XyzCalibrationColor b, XyzCalibrationColor c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    internal XyzCalibrationGamut Freeze()
    {
        if (string.IsNullOrWhiteSpace(Source) || Source.Length > 2048 || SourceSha256 is not { Length: 64 } || SourceSha256.Any(c => !Uri.IsHexDigit(c))
            || Vertices is null || Vertices.Count is < 3 or > MaximumVertices || Vertices.Any(p => p is null || new[] { p.X, p.Y, p.Z }.Any(v => !double.IsFinite(v) || v < 0 || v > 1)
                || Math.Abs(p.X + p.Y + p.Z - 1) > 2e-15)) throw new InvalidDataException("XYZ 颜色适用域无效或超出顶点预算。");
        var v = Vertices.ToArray();
        // Every other vertex must be strictly on the left: reject concavity, crossings,
        // collinear faces, duplicate vertices and an unbounded/degenerate domain.
        for (var i = 0; i < v.Length; i++) for (var j = 0; j < v.Length; j++)
            if (j != i && j != (i + 1) % v.Length && Turn(v[i], v[(i + 1) % v.Length], v[j]) <= 0)
                throw new InvalidDataException("XYZ 颜色适用域须为逆时针严格凸多边形。");
        return this with { Vertices = Array.AsReadOnly(v) };
    }
    public bool Contains(double x, double y, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z) || x < 0 || y < 0 || z < 0) return false;
        var sum = x + y + z; if (!double.IsFinite(sum)) return false; if (sum == 0) return true;
        var p = new XyzCalibrationColor(x / sum, y / sum, z / sum);
        for (var i = 0; i < Vertices.Count; i++)
        {
            var a = Vertices[i]; var b = Vertices[(i + 1) % Vertices.Count];
            var roundoff = 32 * 2.2204460492503131e-16 * (Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y));
            if (Turn(a, b, p) < -roundoff) return false;
        }
        return true;
    }
    internal double Minimum(XyzRadiometricBand b) => Vertices.Min(p => b.X * p.X + b.Y * p.Y + b.Z * p.Z);
}

internal static class XyzGamutLeastSquares
{
    // A homogeneous three-variable convex QP has its optimum in the interior, on
    // one face, on the intersection of two faces, or at zero. Enumerate those
    // subspaces and use the shared SVD, without normal equations or clipping.
    internal static double[] Solve(double[,] matrix, double[] target, double[] scales, XyzCalibrationGamut gamut)
    {
        static double Dot(double[] a, double[] b) => a.Zip(b).Sum(p => p.First * p.Second);
        static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
        static double[] Unit(double[] a) { var max = a.Max(Math.Abs); var scaled = a.Select(v => v / max).ToArray(); var s = Math.Sqrt(Dot(scaled, scaled)); return scaled.Select(v => v / s).ToArray(); }
        var normals = gamut.Vertices.Select(p =>
        {
            var log = new[] { p.X, p.Y, p.Z }.Select((v, j) => v > 0 ? Math.Log(v) - Math.Log(scales[j]) : double.NegativeInfinity).ToArray();
            var max = log.Max(); return Unit(log.Select(v => Math.Exp(v - max)).ToArray());
        }).ToArray();
        var best = new double[3]; var bestError = target.Sum(v => v * v); var rows = target.Length;
        void Candidate(double[][] basis)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var sub = new double[rows, basis.Length];
            for (var r = 0; r < rows; r++) for (var k = 0; k < basis.Length; k++) for (var j = 0; j < 3; j++) sub[r, k] += matrix[r, j] * basis[k][j];
            var solve = JacobiSvd.Solve(JacobiSvd.Factor(sub), target);
            var c = Enumerable.Range(0, 3).Select(j => basis.Select((b, k) => b[j] * solve[k]).Sum()).ToArray();
            var raw = c.Select((v, j) => v / scales[j]).ToArray(); var bound = 64 * 2.2204460492503131e-16 * raw.Sum(Math.Abs);
            var min = gamut.Vertices.Min(p => p.X * raw[0] + p.Y * raw[1] + p.Z * raw[2]);
            if (!double.IsFinite(min) || min < -bound) return;
            // Move a boundary solution into the feasible set by round-off only. This is
            // bounded in coefficient ULPs; no measured or evaluated BSDF is clipped.
            if (min <= bound)
            {
                var interior = Math.Max(0, -min) + bound;
                for (var j = 0; j < 3; j++) { raw[j] += interior; c[j] = raw[j] * scales[j]; }
            }
            if (raw.Any(v => !double.IsFinite(v)) || gamut.Vertices.Any(p => p.X * raw[0] + p.Y * raw[1] + p.Z * raw[2] < 0)) return;
            double error = 0; for (var r = 0; r < rows; r++) { var value = -target[r]; for (var j = 0; j < 3; j++) value += matrix[r, j] * c[j]; error += value * value; }
            if (error < bestError) { bestError = error; best = c; }
        }
        Candidate([[1, 0, 0], [0, 1, 0], [0, 0, 1]]);
        foreach (var n in normals) { var u = Unit(Cross(n, Math.Abs(n[0]) < .8 ? [1, 0, 0] : [0, 1, 0])); Candidate([u, Unit(Cross(n, u))]); }
        for (var i = 0; i < normals.Length; i++) for (var j = i + 1; j < normals.Length; j++)
        { var u = Cross(normals[i], normals[j]); if (Dot(u, u) > 1e-28) Candidate([Unit(u)]); }
        return best;
    }
}
