using System.Text.Json;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.ZemaxComparison;

internal static class Energy32ConicAudit
{
    public static void Run(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        const string root = "artifacts/validation/objectives32-continuation-20261021";
        const string id = "gaussian-quadrature-over-annular-pupils";
        using var raw = JsonDocument.Parse(File.ReadAllText(root + "/native-active-parameters-v2/capture/" + id + "/ray-audit.json"));
        using var indices = JsonDocument.Parse(File.ReadAllText(root + "/native-active-parameters-v2/capture/" + id + "/refractive-indices.json"));
        using var items = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives-20261020/final-v7-intersection-fallback/items.json"));
        var item = items.RootElement.EnumerateArray().First(i => i.GetProperty("lens").GetString() == "Objectives/Gaussian Quadrature over Annular Pupils.zmx");
        using var model = JsonDocument.Parse(File.ReadAllText(Path.Combine(item.GetProperty("path").GetString()!, "native/model.json")));
        var optic = OpticalFormatCatalog.Import(File.ReadAllText("src/OpticalSystemDesign.App/Assets/SequentialExamples/Objectives/Gaussian Quadrature over Annular Pupils.zmx"), ".zmx");
        var wave = raw.RootElement.GetProperty("wavelength").GetInt32();
        var methods = new List<object>();
        foreach (var normalMode in Enumerable.Range(0, 4))
        foreach (var cosMode in Enumerable.Range(0, 4))
        foreach (var normalize in new[] { false, true })
        {
            var rows = new List<object>();
            foreach (var surface in new[] { 2, 3 })
            for (var pupil = 1; pupil < 5; pupil++)
            {
                var after = raw.RootElement.GetProperty("surfaces")[surface - 1].GetProperty("rays")[pupil];
                var before = raw.RootElement.GetProperty("surfaces")[surface - 2].GetProperty("rays")[pupil];
                var beforeSign = Math.Sign(indices.RootElement[surface - 1].GetProperty("indices")[wave - 1].GetDouble());
                var afterSign = Math.Sign(indices.RootElement[surface].GetProperty("indices")[wave - 1].GetDouble());
                var d = V(before, "l", "m", "n") * beforeSign;
                var o = V(before, "x", "y", "z") - new Vector3D(0, 0, model.RootElement.GetProperty("surfaces")[surface - 1].GetProperty("Thickness").GetDouble());
                var c = indices.RootElement[surface].GetProperty("activeCurvature").GetDouble(); var k = optic.SurfaceGroup.Items[surface].Conic;
                var p = o + d * (-o.Z / d.Z); p = p with { Z = 0 };
                var b = d.Z - c * (p.X * d.X + p.Y * d.Y); var a = 1 + k * d.Z * d.Z;
                var disc = b * b - a * c * c * (p.X * p.X + p.Y * p.Y);
                var t = c * (p.X * p.X + p.Y * p.Y) / (b + Math.CopySign(Math.Sqrt(disc), b));
                var hit = p + d * t; var nativeHit = V(after, "x", "y", "z");
                var zc = hit.Z * c; var g2 = zc * k * (zc * (k + 1) - 2) + 1; var rootG = Math.Sqrt(g2);
                var gradient = new Vector3D(c * hit.X, c * hit.Y, (1 + k) * c * hit.Z - 1);
                var casp = c / rootG;
                var n = normalMode switch
                {
                    1 => new Vector3D(hit.X * casp, hit.Y * casp, (zc * (1 + k) - 1) / rootG),
                    2 => new Vector3D(hit.X * casp, hit.Y * casp, (hit.Z - (1 / c - hit.Z * k)) * casp),
                    3 => gradient / gradient.Length,
                    _ => gradient / rootG
                };
                var dot = d.X * n.X + d.Y * n.Y + d.Z * n.Z;
                if (dot > 0) n = -n;
                var cos = cosMode switch { 1 => Math.Sqrt(disc) / rootG, 2 => Math.Sqrt(disc / g2), 3 => Math.Abs(dot), _ => Math.Sqrt(disc) / Math.Sqrt(gradient.X * gradient.X + gradient.Y * gradient.Y + gradient.Z * gradient.Z) };
                var reflected = d + 2 * cos * n;
                if (normalize) reflected /= reflected.Length;
                var expected = V(after, "l", "m", "n") * afterSign;
                rows.Add(new { surface, pupil, pointError = (hit - nativeHit).Length, directionError = (reflected - expected).Length,
                    point = new { hit.X, hit.Y, hit.Z }, actual = new { reflected.X, reflected.Y, reflected.Z }, expected = new { expected.X, expected.Y, expected.Z } });
            }
            using var encoded = JsonDocument.Parse(JsonSerializer.Serialize(rows));
            methods.Add(new { normalMode, cosMode, normalize, pointExact = encoded.RootElement.EnumerateArray().Count(r => r.GetProperty("pointError").GetDouble() == 0),
                directionExact = encoded.RootElement.EnumerateArray().Count(r => r.GetProperty("directionError").GetDouble() == 0), maximum = encoded.RootElement.EnumerateArray().Max(r => r.GetProperty("directionError").GetDouble()), rows });
        }
        JsonFiles.Write(Path.Combine(output, "methods.json"), methods);
        using var result = JsonDocument.Parse(JsonSerializer.Serialize(methods));
        foreach (var m in result.RootElement.EnumerateArray().OrderByDescending(m => m.GetProperty("directionExact").GetInt32()).Take(8))
            Console.WriteLine($"normal={m.GetProperty("normalMode")}, cos={m.GetProperty("cosMode")}, normalize={m.GetProperty("normalize")}: points={m.GetProperty("pointExact")}/8, directions={m.GetProperty("directionExact")}/8, maximum={m.GetProperty("maximum")}");
    }
    private static Vector3D V(JsonElement e, string x, string y, string z) => new(e.GetProperty(x).GetDouble(), e.GetProperty(y).GetDouble(), e.GetProperty(z).GetDouble());
}
