using System.Globalization;
using System.Text.Json;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.ZemaxComparison;

// Independent causal control: test whether a spherical intersection's
// discriminant, rather than a rounded normal dot product, supplies incidence.
// All native states are diagnostic inputs and never enter product output.
internal static class Energy32IncidentCosineAudit
{
    public static void Run(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        const string root = "artifacts/validation/objectives32-repair-20261021/marginals-v1";
        using var items = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives-20261020/final-v7-intersection-fallback/items.json"));
        string[] selected = ["Apochromat", "Apochromat2", "Apochromat3", "Apochromat4", "Cooke 40 degree field", "Doublet", "Doublet using MIL number glasses"];
        var inputs = new List<Input>();
        foreach (var name in selected)
        {
            var lens = "Objectives/" + name + ".zmx";
            var id = name.Replace(' ', '-').ToLowerInvariant();
            using var raw = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "native/capture", id, "ray-audit.json")));
            using var indices = JsonDocument.Parse(File.ReadAllText(Path.Combine("artifacts/validation/objectives32-continuation-20261021/native-active-parameters-v2/capture", id, "refractive-indices.json")));
            var item = items.RootElement.EnumerateArray().First(i => i.GetProperty("lens").GetString() == lens && i.GetProperty("run").GetProperty("key").GetString() == "Diffraction Encircled Energy");
            using var model = JsonDocument.Parse(File.ReadAllText(Path.Combine(item.GetProperty("path").GetString()!, "native/model.json")));
            var source = File.ReadAllLines(Path.Combine(root, id, "source.zmx"));
            var scale = source.Single(l => l.StartsWith("UNIT ", StringComparison.Ordinal)).Split(' ', StringSplitOptions.RemoveEmptyEntries)[1] == "CM" ? 10 : 1d;
            var cs = new Dictionary<int, double>(); var number = 0;
            foreach (var line in source)
            {
                var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 2) continue;
                if (p[0] == "SURF") number = int.Parse(p[1], CultureInfo.InvariantCulture);
                if (p[0] == "CURV") cs[number] = double.Parse(p[1], CultureInfo.InvariantCulture) / scale;
            }
            var wave = raw.RootElement.GetProperty("wavelength").GetInt32();
            var surfaces = raw.RootElement.GetProperty("surfaces");
            foreach (var surface in surfaces.EnumerateArray())
            {
                var s = surface.GetProperty("surface").GetInt32();
                var n1 = indices.RootElement[s - 1].GetProperty("indices")[wave - 1].GetDouble();
                var n2 = indices.RootElement[s].GetProperty("indices")[wave - 1].GetDouble();
                if (n1 == n2 || n1 <= 0 || n2 <= 0) continue;
                var nativeSurface = model.RootElement.GetProperty("surfaces")[s];
                var cRadius = nativeSurface.GetProperty("Radius").ValueKind == JsonValueKind.Number ? 1 / nativeSurface.GetProperty("Radius").GetDouble() : 0;
                var thickness = s == 1 ? 0 : model.RootElement.GetProperty("surfaces")[s - 1].GetProperty("Thickness").GetDouble();
                for (var p = 1; p < 5; p++)
                {
                    var outgoing = surface.GetProperty("rays")[p];
                    var incoming = s == 1 ? raw.RootElement.GetProperty("launches")[p] : surfaces[s - 2].GetProperty("rays")[p];
                    var origin = V(incoming, "x", "y", "z") - new Vector3D(0, 0, thickness);
                    var normal = V(outgoing, "nx", "ny", "nz");
                    var hit = V(outgoing, "x", "y", "z");
                    var cNormal = Math.Abs(hit.Y) > Math.Abs(hit.X) ? -normal.Y / hit.Y / Math.Sign(normal.Z) : -normal.X / hit.X / Math.Sign(normal.Z);
                    inputs.Add(new(id, s, p, n1, n2, cs[s], cRadius, cNormal,
                        indices.RootElement[s].GetProperty("activeCurvature").GetDouble(), hit,
                        origin, V(incoming, "l", "m", "n"), normal, V(outgoing, "l", "m", "n")));
                }
            }
        }
        var results = new List<object>();
        foreach (var curvature in new[] { "source", "radius", "normal" })
        foreach (var incidence in new[] { "dot", "unit-discriminant", "norm-discriminant", "cross-distance", "dot-normal", "dot-direction", "dot-both", "dot-squared-norm", "cos-from-cross", "cos-from-unit-cross" })
        foreach (var correction in new[] { "difference", "scaled-cos", "scaled-delta", "scaled-each", "scaled-ratio-root", "inverse-delta", "rational-ratio", "rational-factored-ratio", "rational-scaled", "rational-scaled-factored", "rational-inverse" })
        foreach (var directionScaling in new[] { "ratio", "numerator", "inverse", "divide-first" })
        foreach (var normalMode in new[] { "exported", "implicit", "sqrt-products", "sqrt-radius", "sdk", "normalized-gradient" })
        {
            if (normalMode != "exported" && (curvature != "source" || incidence != "dot" || directionScaling != "ratio" || correction != "difference")) continue;
            var rows = new List<object>(); var exact = 0; var exactN = 0; var maximum = 0d;
            foreach (var i in inputs)
            {
                var c = curvature == "source" ? i.CSource : curvature == "radius" ? i.CRadius : i.CNormal;
                var d = i.Direction; var n = i.Normal; var o = i.Origin;
                if (normalMode != "exported")
                {
                    var cx = i.CActive * i.Hit.X; var cy = i.CActive * i.Hit.Y;
                    var nz = normalMode switch
                    {
                        "sqrt-products" => -Math.Sqrt(1 - (cx * cx + cy * cy)),
                        "sqrt-radius" => -Math.Sqrt(1 - i.CActive * i.CActive * (i.Hit.X * i.Hit.X + i.Hit.Y * i.Hit.Y)),
                        "sdk" when i.CActive != 0 => (i.Hit.Z - 1 / i.CActive) * i.CActive,
                        _ => i.CActive * i.Hit.Z - 1
                    };
                    n = new Vector3D(cx, cy, nz);
                    if (normalMode == "normalized-gradient") n /= n.Length;
                }
                var dot = d.X * n.X + d.Y * n.Y + d.Z * n.Z;
                if (dot > 0) n = -n;
                var b = d.Z - c * (o.X * d.X + o.Y * d.Y + o.Z * d.Z);
                var q = c * (o.X * o.X + o.Y * o.Y + o.Z * o.Z) - 2 * o.Z;
                var a = incidence == "unit-discriminant" ? c : c * (d.X * d.X + d.Y * d.Y + d.Z * d.Z);
                var cross = new Vector3D(d.Y * n.Z - d.Z * n.Y, d.Z * n.X - d.X * n.Z, d.X * n.Y - d.Y * n.X);
                var cos = incidence switch
                {
                    "dot" => Math.Abs(dot),
                    "dot-normal" => Math.Abs(dot) / n.Length,
                    "dot-direction" => Math.Abs(dot) / d.Length,
                    "dot-both" => Math.Abs(dot) / (n.Length * d.Length),
                    "dot-squared-norm" => Math.Sqrt(dot * dot / ((n.X * n.X + n.Y * n.Y + n.Z * n.Z) * (d.X * d.X + d.Y * d.Y + d.Z * d.Z))),
                    "cos-from-cross" => Math.Sqrt(1 - (cross.X * cross.X + cross.Y * cross.Y + cross.Z * cross.Z)),
                    "cos-from-unit-cross" => Math.Sqrt(1 - (cross.X * cross.X + cross.Y * cross.Y + cross.Z * cross.Z) / ((n.X * n.X + n.Y * n.Y + n.Z * n.Z) * (d.X * d.X + d.Y * d.Y + d.Z * d.Z))),
                    _ => Math.Sqrt(b * b - a * q)
                };
                if (incidence == "cross-distance" && c != 0)
                {
                    var center = o - new Vector3D(0, 0, 1 / c);
                    var centerCross = new Vector3D(center.Y * d.Z - center.Z * d.Y, center.Z * d.X - center.X * d.Z, center.X * d.Y - center.Y * d.X);
                    cos = Math.Sqrt(1 - c * c * (centerCross.X * centerCross.X + centerCross.Y * centerCross.Y + centerCross.Z * centerCross.Z));
                }
                var eta = i.N1 / i.N2;
                var cosT = Math.Sqrt(1 - eta * eta * (1 - cos * cos));
                var scaledT = Math.Sqrt(i.N2 * i.N2 - i.N1 * i.N1 * (1 - cos * cos));
                var reverse = i.N2 / i.N1;
                var factor = correction switch
                {
                    "scaled-cos" => (i.N1 * cos) / i.N2 - cosT,
                    "scaled-delta" => (i.N1 * cos - scaledT) / i.N2,
                    "scaled-each" => (i.N1 * cos) / i.N2 - scaledT / i.N2,
                    "scaled-ratio-root" => (i.N1 * cos - i.N2 * cosT) / i.N2,
                    "inverse-delta" => (cos - Math.Sqrt(reverse * reverse - (1 - cos * cos))) / reverse,
                    "rational-ratio" => (eta * eta - 1) / (eta * cos + cosT),
                    "rational-factored-ratio" => ((eta - 1) * (eta + 1)) / (eta * cos + cosT),
                    "rational-scaled" => (i.N1 * i.N1 - i.N2 * i.N2) / (i.N2 * (i.N1 * cos + scaledT)),
                    "rational-scaled-factored" => ((i.N1 - i.N2) * (i.N1 + i.N2)) / (i.N2 * (i.N1 * cos + scaledT)),
                    "rational-inverse" => ((1 - reverse * reverse) / (cos + Math.Sqrt(reverse * reverse - (1 - cos * cos)))) / reverse,
                    _ => eta * cos - cosT
                };
                double Scale(double value) => directionScaling switch
                {
                    "numerator" => (i.N1 * value) / i.N2,
                    "inverse" => value / reverse,
                    "divide-first" => value / i.N2 * i.N1,
                    _ => eta * value
                };
                var actual = new Vector3D(Scale(d.X) + factor * n.X, Scale(d.Y) + factor * n.Y, Scale(d.Z) + factor * n.Z);
                var delta = Math.Max(Math.Abs(actual.X - i.Expected.X), Math.Max(Math.Abs(actual.Y - i.Expected.Y), Math.Abs(actual.Z - i.Expected.Z)));
                if (delta == 0) exact++;
                if (actual.Z == i.Expected.Z) exactN++;
                maximum = Math.Max(maximum, delta);
                rows.Add(new { i.Lens, i.Surface, i.Pupil, cos, delta, dz = actual.Z - i.Expected.Z });
            }
            results.Add(new { curvature, incidence, correction, directionScaling, normalMode, count = inputs.Count, exact, exactN, maximum, rows });
            if (normalMode != "exported")
                Console.WriteLine($"{normalMode}: exact={exact}/{inputs.Count}, N exact={exactN}, maximum={maximum:R}");
        }
        JsonFiles.Write(Path.Combine(output, "methods.json"), results);
    }
    private static Vector3D V(JsonElement e, string x, string y, string z) => new(e.GetProperty(x).GetDouble(), e.GetProperty(y).GetDouble(), e.GetProperty(z).GetDouble());
    private sealed record Input(string Lens, int Surface, int Pupil, double N1, double N2, double CSource, double CRadius, double CNormal,
        double CActive, Vector3D Hit, Vector3D Origin, Vector3D Direction, Vector3D Normal, Vector3D Expected);
}
