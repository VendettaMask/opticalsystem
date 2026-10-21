using System.Text.Json;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.ZemaxComparison;

// Offline stage controls, with externally captured input states. These values
// never enter the product trace or analysis.
internal static class Energy32SurfaceStageAudit
{
    public static void Run(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        const string root = "artifacts/validation/objectives32-continuation-20261021/native-active-parameters-v2/capture";
        var inputs = new List<Input>();
        foreach (var id in new[] { "apochromat", "apochromat2", "apochromat3", "apochromat4", "cooke-40-degree-field", "doublet", "doublet-using-mil-number-glasses" })
        {
            using var raw = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, id, "ray-audit.json")));
            using var indices = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, id, "refractive-indices.json")));
            var wave = raw.RootElement.GetProperty("wavelength").GetInt32();
            foreach (var surface in raw.RootElement.GetProperty("surfaces").EnumerateArray())
            {
                var s = surface.GetProperty("surface").GetInt32();
                var n1 = indices.RootElement[s - 1].GetProperty("indices")[wave - 1].GetDouble();
                var n2 = indices.RootElement[s].GetProperty("indices")[wave - 1].GetDouble();
                if (n1 == n2 || n1 <= 0 || n2 <= 0 || indices.RootElement[s].GetProperty("activeCurvature").GetDouble() == 0) continue;
                using var probe = JsonDocument.Parse(File.ReadAllText($"artifacts/validation/objectives32-continuation-20261021/native-probe-all-interfaces-v1/capture/{id}-before-{s}/probe-rays.json"));
                for (var p = 1; p < 5; p++)
                {
                    var ray = surface.GetProperty("rays")[p];
                    var previous = s == 1 ? raw.RootElement.GetProperty("launches")[p] : raw.RootElement.GetProperty("surfaces")[s - 2].GetProperty("rays")[p];
                    inputs.Add(new(id, s, p, indices.RootElement[s].GetProperty("activeCurvature").GetDouble(), n1, n2,
                        V(ray, "x", "y", "z"), V(ray, "nx", "ny", "nz"), V(previous, "l", "m", "n"), V(ray, "l", "m", "n"),
                        V(probe.RootElement.EnumerateArray().First(r => r.GetProperty("wave").GetDouble() == wave && (p switch
                        {
                            1 => r.GetProperty("x").GetDouble() == 0 && r.GetProperty("y").GetDouble() > 1,
                            2 => r.GetProperty("x").GetDouble() == 0 && r.GetProperty("y").GetDouble() < -1,
                            3 => r.GetProperty("y").GetDouble() == 0 && r.GetProperty("x").GetDouble() > 1,
                            _ => r.GetProperty("y").GetDouble() == 0 && r.GetProperty("x").GetDouble() < -1
                        })), "x", "y", "z")));
                }
            }
        }
        var geometryResults = new List<object>();
        foreach (var method in new[] { "radius", "scaled", "scaled-left", "scaled-root-radius", "scaled-rational", "scaled-direct" })
        {
            var rows = new List<object>();
            foreach (var i in inputs)
            {
                var p = i.Vertex; var d = i.Direction;
                var q = p.X * p.X + p.Y * p.Y;
                var bs = method == "scaled-left" ? d.Z - i.C * p.X * d.X - i.C * p.Y * d.Y : d.Z - i.C * (p.X * d.X + p.Y * d.Y);
                var br = d.Z / i.C - p.X * d.X - p.Y * d.Y;
                var squareRoot = Math.Sqrt(bs * bs - i.C * i.C * q);
                var distance = method switch
                {
                    "radius" => q / (br + Math.CopySign(Math.Sqrt(br * br - q), br)),
                    "scaled-root-radius" => q / (bs / i.C + Math.CopySign(squareRoot / Math.Abs(i.C), br)),
                    "scaled-rational" => (q / (bs + Math.CopySign(squareRoot, bs))) * i.C,
                    "scaled-direct" => (bs - Math.CopySign(squareRoot, bs)) / i.C,
                    _ => i.C * q / (bs + Math.CopySign(squareRoot, bs))
                };
                var point = p + d * distance;
                rows.Add(new { i.Lens, i.Surface, i.Pupil, error = (point - i.Hit).Length,
                    point = new { point.X, point.Y, point.Z }, expected = new { i.Hit.X, i.Hit.Y, i.Hit.Z } });
            }
            using var encoded = JsonDocument.Parse(JsonSerializer.Serialize(rows));
            geometryResults.Add(new { method, exact = encoded.RootElement.EnumerateArray().Count(r => r.GetProperty("error").GetDouble() == 0), rows });
        }
        JsonFiles.Write(Path.Combine(output, "intersections.json"), geometryResults);
        var results = new List<object>();
        foreach (var normalMethod in new[] { "reported", "implicit", "sdk", "radius", "sqrt-products", "sqrt-radius", "sqrt-fma", "implicit-normalized", "radius-normalized", "sqrt-products-normalized" })
        foreach (var dotMethod in new[] { "ordinary", "fma-z" })
        foreach (var radicand in new[] { "squared", "factored", "fma" })
        foreach (var normalize in new[] { false, true })
        foreach (var incidence in new[] { "dot", "vertex-radius", "vertex-scaled", "vertex-scaled-square", "vertex-cross", "vertex-cross-square", "vertex-center-cross", "vertex-center-cross-square", "vertex-expanded", "vertex-expanded-square" })
        {
            if (incidence != "dot" && normalMethod != "reported") continue;
            var rows = new List<object>();
            foreach (var i in inputs)
            {
                var cx = i.C * i.Hit.X; var cy = i.C * i.Hit.Y;
                var nz = normalMethod switch
                {
                    "sqrt-products" or "sqrt-products-normalized" => -Math.Sqrt(1 - (cx * cx + cy * cy)),
                    "sqrt-radius" => -Math.Sqrt(1 - i.C * i.C * (i.Hit.X * i.Hit.X + i.Hit.Y * i.Hit.Y)),
                    "sqrt-fma" => -Math.Sqrt(Math.FusedMultiplyAdd(-cy, cy, 1 - cx * cx)),
                    "sdk" => (i.Hit.Z - 1 / i.C) * i.C,
                    _ => i.C * i.Hit.Z - 1
                };
                var normal = normalMethod == "reported" ? i.Normal : normalMethod.StartsWith("radius", StringComparison.Ordinal)
                    ? new Vector3D(-i.Hit.X, -i.Hit.Y, 1 / i.C - i.Hit.Z) * -Math.Sign(i.C) : new Vector3D(cx, cy, nz);
                if (normalMethod.EndsWith("normalized", StringComparison.Ordinal)) normal /= normal.Length;
                var nerror = (normal - i.Normal).Length;
                var dot = dotMethod == "fma-z" ? Math.FusedMultiplyAdd(i.Direction.Z, normal.Z, i.Direction.X * normal.X + i.Direction.Y * normal.Y)
                    : i.Direction.X * normal.X + i.Direction.Y * normal.Y + i.Direction.Z * normal.Z;
                if (dot > 0) { dot = -dot; normal = -normal; }
                var cos = -dot; var eta = i.N1 / i.N2;
                var o = i.Vertex; var d = i.Direction;
                var q = o.X * o.X + o.Y * o.Y;
                var od = o.X * d.X + o.Y * d.Y;
                var b = d.Z / i.C - o.X * d.X - o.Y * d.Y;
                var bs = d.Z - i.C * od;
                var centerCross = new Vector3D(o.Y * d.Z + d.Y / i.C, -d.X / i.C - o.X * d.Z, o.X * d.Y - o.Y * d.X);
                var cosSquared = incidence switch
                {
                    "vertex-radius" => i.C * i.C * (b * b - q),
                    "vertex-scaled" or "vertex-scaled-square" => bs * bs - i.C * i.C * q,
                    "vertex-cross" or "vertex-cross-square" => 1 - i.C * i.C * (q - od * od),
                    "vertex-center-cross" or "vertex-center-cross-square" => 1 - i.C * i.C * (centerCross.X * centerCross.X + centerCross.Y * centerCross.Y + centerCross.Z * centerCross.Z),
                    "vertex-expanded" or "vertex-expanded-square" => d.Z * d.Z - 2 * d.Z * i.C * od + i.C * i.C * (od * od - q),
                    _ => cos * cos
                };
                if (incidence != "dot") cos = incidence == "vertex-radius" ? Math.Abs(i.C) * Math.Sqrt(b * b - q) : Math.Sqrt(cosSquared);
                var sineSquared = radicand == "factored" ? (1 - cos) * (1 + cos) : 1 - cos * cos;
                if (incidence.EndsWith("-square", StringComparison.Ordinal)) sineSquared = 1 - cosSquared;
                var square = radicand == "fma" ? Math.FusedMultiplyAdd(-eta * eta, sineSquared, 1) : 1 - eta * eta * sineSquared;
                var factor = eta * cos - Math.Sqrt(square);
                var actual = eta * i.Direction + factor * normal;
                if (normalize) actual /= actual.Length;
                rows.Add(new { i.Lens, i.Surface, i.Pupil, normalError = nerror, directionError = (actual - i.Expected).Length,
                    nx = normal.X, ny = normal.Y, nz = normal.Z, actual = new { actual.X, actual.Y, actual.Z }, expected = new { i.Expected.X, i.Expected.Y, i.Expected.Z } });
            }
            // Anonymous rows are deliberately kept as typed JSON for one receipt.
            using var encoded = JsonDocument.Parse(JsonSerializer.Serialize(rows));
            var rs = encoded.RootElement.EnumerateArray().ToArray();
            results.Add(new { normalMethod, dotMethod, radicand, normalize, incidence, count = inputs.Count,
                normalExact = rs.Count(r => r.GetProperty("normalError").GetDouble() == 0),
                directionExact = rs.Count(r => r.GetProperty("directionError").GetDouble() == 0),
                maximum = rs.Max(r => r.GetProperty("directionError").GetDouble()), rows });
        }
        JsonFiles.Write(Path.Combine(output, "results.json"), results);
    }
    private static Vector3D V(JsonElement e, string x, string y, string z) => new(e.GetProperty(x).GetDouble(), e.GetProperty(y).GetDouble(), e.GetProperty(z).GetDouble());
    private sealed record Input(string Lens, int Surface, int Pupil, double C, double N1, double N2, Vector3D Hit, Vector3D Normal, Vector3D Direction, Vector3D Expected, Vector3D Vertex);
}
