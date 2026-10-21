using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison;

internal static class Energy32FrequencyBoundaryAudit
{
    public static void Run(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        const string root = "artifacts/validation/objectives32-continuation-20261021";
        using var inputs = JsonDocument.Parse(File.ReadAllText(root + "/fourier-stage-v8-physical-factor/inputs.json"));
        using var original = JsonDocument.Parse(File.ReadAllText(root + "/discriminant-coordinate-v1/inputs.json"));
        using var holdouts = JsonDocument.Parse(File.ReadAllText(root + "/native-holdout/capture/cones.json"));
        var qs = new Dictionary<string, double>();
        foreach (var i in inputs.RootElement.EnumerateArray())
        {
            var id = i.GetProperty("id").GetString()!;
            var o = original.RootElement.EnumerateArray().FirstOrDefault(o => Path.GetFileNameWithoutExtension(o.GetProperty("lens").GetString()!).Replace(' ', '-').ToLowerInvariant() == id);
            var n = o.ValueKind != JsonValueKind.Undefined ? o.GetProperty("native")[1].GetProperty("z").GetDouble()
                : holdouts.RootElement.EnumerateArray().Single(c => c.GetProperty("id").GetString() == id + "-cone").GetProperty("waves").EnumerateArray()
                    .OrderBy(w => Math.Abs(w.GetProperty("micrometers").GetDouble() - i.GetProperty("wave").GetDouble())).First().GetProperty("rays")[1].GetProperty("n").GetDouble();
            qs.Add(id, 1 - n * n);
        }
        var results = new List<object>();
        foreach (var units in Enumerable.Range(0, 3))
        foreach (var mask in Enumerable.Range(0, 14))
        foreach (var coord in Enumerable.Range(0, 5))
        {
            var matches = 0; var exactPoints = 0; var rows = new List<object>();
            foreach (var i in inputs.RootElement.EnumerateArray())
            {
                var id = i.GetProperty("id").GetString()!; var q = qs[id]; var na = Math.Sqrt(q);
                var w = i.GetProperty("wave").GetDouble(); var f = i.GetProperty("f").GetDouble();
                var delta = w * f / Math.Sqrt(32); var r = i.GetProperty("radius").GetDouble();
                if (units == 1) { w *= .001; delta *= .001; }
                if (units == 2) { w /= 1000; delta /= 1000; }
                double Frequency(double k) => coord switch
                {
                    1 => k / (32 * delta), 2 => k / 32 / delta, 3 => k / (delta * 32),
                    4 => (k + 16) / (32 * delta) - 16 / (32 * delta), _ => k * (1 / (32 * delta))
                };
                var nu = Frequency(4); var s = nu * nu + nu * nu;
                var c = 1 / (w * f); var ca = 2 * na / w;
                var angular = nu * w; var angularSquared = angular * angular + angular * angular;
                var inside = mask switch
                {
                    0 => s <= c * c, 1 => s <= ca * ca,
                    2 => s <= 4 * q / (w * w),
                    3 => s * (w * w) <= 4 * q,
                    4 => angularSquared <= 4 * q,
                    5 => s / (4 * q) <= 1 / (w * w),
                    6 => s * (w * w) / (4 * q) <= 1,
                    7 => Math.Sqrt(s) <= ca,
                    8 => Math.Sqrt(s) <= c,
                    9 => s <= 4 * (q / (w * w)),
                    10 => s * w * w <= 4 * q,
                    11 => angularSquared <= 4 * na * na,
                    12 => Math.Sqrt(angularSquared) <= 2 * na,
                    _ => Math.Sqrt(s / (4 * q)) <= 1 / w
                };
                var actual = inside ? 101 : 97; var expected = i.GetProperty("expected").GetInt32();
                if (actual == expected) matches++;
                var pointRows = i.GetProperty("observed").EnumerateArray().Select((p, k) => new { k = 1 + 2 * k,
                    observed = p.GetDouble(), predicted = Frequency(1 + 2 * k) / c * r }).ToArray();
                exactPoints += pointRows.Count(p => p.observed == p.predicted);
                rows.Add(new { id, actual, expected, q, c, ca, s, pointRows });
            }
            results.Add(new { units, mask, coord, matches, exactPoints, rows });
        }
        JsonFiles.Write(Path.Combine(output, "methods.json"), results);
        using var encoded = JsonDocument.Parse(JsonSerializer.Serialize(results));
        foreach (var v in encoded.RootElement.EnumerateArray().OrderByDescending(v => v.GetProperty("matches").GetInt32()).ThenByDescending(v => v.GetProperty("exactPoints").GetInt32()).Take(12))
            Console.WriteLine($"support={v.GetProperty("matches")}/18; points={v.GetProperty("exactPoints")}/12: units={v.GetProperty("units")}, mask={v.GetProperty("mask")}, coord={v.GetProperty("coord")}");
    }
}
