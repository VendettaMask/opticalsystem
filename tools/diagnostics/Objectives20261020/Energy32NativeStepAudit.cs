using System.Text.Json;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.ZemaxComparison;

// Independent interface arithmetic using native input states to separate
// refraction rounding from accumulated position, normal and material errors.
internal static class Energy32NativeStepAudit
{
    public static void Run(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        const string root = "artifacts/validation/objectives32-repair-20261021/marginals-v1/native/capture";
        var inputs = new List<Input>();
        foreach (var directory in Directory.GetDirectories(root).Where(d => !Path.GetFileName(d).StartsWith("default-fft-", StringComparison.Ordinal)))
        {
            using var raw = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "ray-audit.json")));
            using var indices = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "refractive-indices.json")));
            var wave = raw.RootElement.GetProperty("wavelength").GetInt32();
            var surfaces = raw.RootElement.GetProperty("surfaces");
            foreach (var surface in surfaces.EnumerateArray())
            {
                var number = surface.GetProperty("surface").GetInt32();
                var n1 = indices.RootElement[number - 1].GetProperty("indices")[wave - 1].GetDouble();
                var n2 = indices.RootElement[number].GetProperty("indices")[wave - 1].GetDouble();
                if (Math.Abs(n1 - n2) < 1e-12) continue;
                for (var i = 1; i < 5; i++)
                {
                    var outgoing = surface.GetProperty("rays")[i];
                    var incoming = number == 1 ? raw.RootElement.GetProperty("launches")[i] : surfaces[number - 2].GetProperty("rays")[i];
                    inputs.Add(new(Path.GetFileName(directory), number, i, n1, n2, V(incoming, "l", "m", "n"), V(outgoing, "nx", "ny", "nz"), V(outgoing, "l", "m", "n")));
                }
            }
        }
        var results = new List<object>();
        foreach (var normalizeDirection in new[] { false })
        foreach (var normalizeNormal in new[] { false })
        foreach (var etaMode in new[] { "division", "inverse-product", "inverse-reverse" })
        foreach (var dotMode in new[] { "ordinary", "fma", "fma-z", "fma-z-xy" })
        foreach (var radicand in new[] { "ratio", "ratio-expanded", "ratio-fma", "ratio-full-fma", "scaled", "scaled-expanded", "scaled-fma", "scaled-full-fma", "scaled-factored", "inverse", "inverse-factored", "ratio-cross", "scaled-cross", "ratio-projection", "ratio-norm", "ratio-sine-product", "ratio-sine-angle", "ratio-cross-product", "scaled-sine-product", "ratio-squares-expanded", "ratio-squares-positive", "ratio-nested", "ratio-postmultiply", "ratio-indexsquared" })
        foreach (var correction in new[] { "ordinary", "fma" })
        foreach (var outgoing in new[] { "ordinary", "fma-direction", "fma-normal", "tangent", "expanded-products" })
        {
            var rows = new List<object>(); var exact = 0; var exactN = 0; var squared = 0d; var max = 0d;
            foreach (var input in inputs)
            {
                var d = input.Direction; var n = input.Normal;
                if (normalizeDirection) d /= d.Length;
                if (normalizeNormal) n /= n.Length;
                var dot = dotMode switch
                {
                    "fma" => Math.FusedMultiplyAdd(d.X, n.X, Math.FusedMultiplyAdd(d.Y, n.Y, d.Z * n.Z)),
                    "fma-z" => Math.FusedMultiplyAdd(d.Z, n.Z, d.X * n.X + d.Y * n.Y),
                    "fma-z-xy" => Math.FusedMultiplyAdd(d.Z, n.Z, Math.FusedMultiplyAdd(d.X, n.X, d.Y * n.Y)),
                    _ => d.X * n.X + d.Y * n.Y + d.Z * n.Z
                };
                if (dot > 0) { dot = -dot; n = -n; }
                var cos = -dot;
                var eta = etaMode switch { "inverse-product" => input.N1 * (1 / input.N2), "inverse-reverse" => 1 / (input.N2 / input.N1), _ => input.N1 / input.N2 };
                var scaled = radicand.StartsWith("scaled", StringComparison.Ordinal);
                var inverse = radicand.StartsWith("inverse", StringComparison.Ordinal);
                var reverse = input.N2 / input.N1;
                var squaredIndex = scaled ? input.N1 * input.N1 : eta * eta;
                var baseValue = scaled ? input.N2 * input.N2 : 1;
                var cosTerm = scaled ? input.N1 * cos : eta * cos;
                if (inverse) cosTerm = cos;
                var cross = new Vector3D(d.Y * n.Z - d.Z * n.Y, d.Z * n.X - d.X * n.Z, d.X * n.Y - d.Y * n.X);
                var projection = d + cos * n;
                var normSquared = d.X * d.X + d.Y * d.Y + d.Z * d.Z;
                var square = radicand switch
                {
                    "ratio-squares-expanded" => 1 - eta * eta + eta * eta * (cos * cos),
                    "ratio-squares-positive" => 1 + eta * eta * (cos * cos) - eta * eta,
                    "ratio-nested" => 1 - eta * (eta * (1 - cos * cos)),
                    "ratio-postmultiply" => 1 - (eta * (1 - cos * cos)) * eta,
                    "ratio-indexsquared" => 1 - (input.N1 * input.N1 / (input.N2 * input.N2)) * (1 - cos * cos),
                    "ratio-sine-product" or "ratio-sine-angle" => 1 - Math.Pow(eta * Math.Sqrt(1 - cos * cos), 2),
                    "scaled-sine-product" => input.N2 * input.N2 - Math.Pow(input.N1 * Math.Sqrt(1 - cos * cos), 2),
                    "ratio-cross-product" => 1 - Math.Pow(eta * cross.Length, 2),
                    "inverse" => reverse * reverse - (1 - cos * cos),
                    "inverse-factored" => (reverse - 1) * (reverse + 1) + cos * cos,
                    "ratio-cross" or "scaled-cross" => baseValue - squaredIndex * (cross.X * cross.X + cross.Y * cross.Y + cross.Z * cross.Z),
                    "ratio-projection" => 1 - eta * eta * (projection.X * projection.X + projection.Y * projection.Y + projection.Z * projection.Z),
                    "ratio-norm" => normSquared - eta * eta * (normSquared - cos * cos),
                    "ratio-expanded" or "scaled-expanded" => baseValue - squaredIndex + cosTerm * cosTerm,
                    "ratio-fma" or "scaled-fma" => Math.FusedMultiplyAdd(-squaredIndex, 1 - cos * cos, baseValue),
                    "ratio-full-fma" or "scaled-full-fma" => Math.FusedMultiplyAdd(-squaredIndex, Math.FusedMultiplyAdd(-cos, cos, 1), baseValue),
                    "scaled-factored" => (input.N2 - input.N1) * (input.N2 + input.N1) + cosTerm * cosTerm,
                    _ => baseValue - squaredIndex * (1 - cos * cos)
                };
                var cosTransmitted = radicand == "ratio-sine-angle" ? Math.Cos(Math.Asin(eta * Math.Sqrt(1 - cos * cos))) : Math.Sqrt(square);
                var factor = correction == "ordinary" ? cosTerm - cosTransmitted
                    : Math.FusedMultiplyAdd(inverse ? 1 : scaled ? input.N1 : eta, cos, -cosTransmitted);
                double Component(double value, double normal) => (outgoing switch
                {
                    "tangent" => (inverse ? 1 : scaled ? input.N1 : eta) * (value + cos * normal) - cosTransmitted * normal,
                    "expanded-products" => (inverse ? 1 : scaled ? input.N1 : eta) * value + cosTerm * normal - cosTransmitted * normal,
                    "fma-direction" => Math.FusedMultiplyAdd(inverse ? 1 : scaled ? input.N1 : eta, value, factor * normal),
                    "fma-normal" => Math.FusedMultiplyAdd(factor, normal, (inverse ? 1 : scaled ? input.N1 : eta) * value),
                    _ => (inverse ? 1 : scaled ? input.N1 : eta) * value + factor * normal
                }) / (inverse ? reverse : scaled ? input.N2 : 1);
                var actual = new Vector3D(Component(d.X, n.X), Component(d.Y, n.Y), Component(d.Z, n.Z));
                var error = Math.Max(Math.Abs(actual.X - input.Expected.X), Math.Max(Math.Abs(actual.Y - input.Expected.Y), Math.Abs(actual.Z - input.Expected.Z)));
                if (error == 0) exact++;
                if (actual.Z == input.Expected.Z) exactN++;
                squared += Math.Pow(actual.X - input.Expected.X, 2) + Math.Pow(actual.Y - input.Expected.Y, 2) + Math.Pow(actual.Z - input.Expected.Z, 2);
                max = Math.Max(max, error);
                rows.Add(new { input.Lens, input.Surface, input.Pupil, actual = new { actual.X, actual.Y, actual.Z }, error });
            }
            results.Add(new { method = string.Join("/", "direction=" + normalizeDirection, "normal=" + normalizeNormal, etaMode, dotMode, radicand, correction, outgoing), count = inputs.Count, exact, exactN, squared, maximum = max, rows = exactN >= 128 ? rows.ToArray() : [] });
        }
        JsonFiles.Write(Path.Combine(output, "inputs.json"), inputs.Select(i => new { i.Lens, i.Surface, i.Pupil, i.N1, i.N2,
            direction = new { i.Direction.X, i.Direction.Y, i.Direction.Z }, normal = new { i.Normal.X, i.Normal.Y, i.Normal.Z }, expected = new { i.Expected.X, i.Expected.Y, i.Expected.Z } }));
        JsonFiles.Write(Path.Combine(output, "methods.json"), results);
    }
    private static Vector3D V(JsonElement e, string x, string y, string z) => new(e.GetProperty(x).GetDouble(), e.GetProperty(y).GetDouble(), e.GetProperty(z).GetDouble());
    private sealed record Input(string Lens, int Surface, int Pupil, double N1, double N2, Vector3D Direction, Vector3D Normal, Vector3D Expected);
}
