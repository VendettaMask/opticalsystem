using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison;

internal static class Energy32GridStageAudit
{
    public static void Run(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        const string root = "artifacts/validation/objectives32-continuation-20261021";
        using var input = JsonDocument.Parse(File.ReadAllText(root + "/fourier-stage-v8-physical-factor/inputs.json"));
        using var defaults = JsonDocument.Parse(File.ReadAllText(root + "/native-fft-auto-comparison.json"));
        using var raw = JsonDocument.Parse(File.ReadAllText(root + "/discriminant-coordinate-v1/inputs.json"));
        using var holdouts = JsonDocument.Parse(File.ReadAllText(root + "/native-holdout/capture/cones.json"));
        var qValues = new Dictionary<string, double>();
        foreach (var i in input.RootElement.EnumerateArray())
        {
            var id = i.GetProperty("id").GetString()!;
            var original = raw.RootElement.EnumerateArray().FirstOrDefault(v => Path.GetFileNameWithoutExtension(v.GetProperty("lens").GetString()!).Replace(' ', '-').ToLowerInvariant() == id);
            var n = original.ValueKind != JsonValueKind.Undefined ? original.GetProperty("native")[1].GetProperty("z").GetDouble()
                : holdouts.RootElement.EnumerateArray().Single(c => c.GetProperty("id").GetString() == id + "-cone").GetProperty("waves").EnumerateArray()
                    .OrderBy(w => Math.Abs(w.GetProperty("micrometers").GetDouble() - i.GetProperty("wave").GetDouble())).First().GetProperty("rays")[1].GetProperty("n").GetDouble();
            qValues.Add(id, 1 - n * n);
        }
        var results = new List<object>();
        foreach (var units in Enumerable.Range(0, 7))
        foreach (var naSource in new[] { "direction", "f-number" })
        foreach (var axisSource in Enumerable.Range(0, 4))
        foreach (var method in Enumerable.Range(0, 55))
        foreach (var mask in Enumerable.Range(0, 4))
        {
            var exact = 0; var points = 0; var rows = new List<object>();
            foreach (var i in input.RootElement.EnumerateArray())
            {
                var id = i.GetProperty("id").GetString();
                var l = i.GetProperty("wave").GetDouble(); var f = i.GetProperty("f").GetDouble(); var r = i.GetProperty("radius").GetDouble();
                var na = naSource == "direction" ? i.GetProperty("na").GetDouble() : .5 / f;
                var fa = axisSource switch
                {
                    1 => Math.Sqrt(.25 / qValues[id!]),
                    2 => 1 / Math.Sqrt(1 / (f * f)),
                    3 => .5 / Math.Sqrt(na * na),
                    _ => f
                };
                var delta = l * f / Math.Sqrt(32);
                var nativeDx = defaults.RootElement.EnumerateArray().Single(d => d.GetProperty("id").GetString() == id).GetProperty("dx").GetDouble();
                var dx = units switch
                {
                    1 => nativeDx * .001, 2 => nativeDx / 1000,
                    3 => (l * .001) * f * 30 / 64,
                    4 => (l / 1000) * f * 30 / 64,
                    5 => l * f * .001 * 30 / 64,
                    6 => l * f * 30 / 64 / 1000,
                    _ => nativeDx
                };
                if (units != 0) { delta = units is 2 or 4 or 6 ? delta / 1000 : delta * .001; l = units is 2 or 4 or 6 ? l / 1000 : l * .001; }
                var e = dx / delta;
                double Coordinate(double k) => method switch
                {
                    0 => e * (k / 15), 1 => k * (e / 15), 2 => k * e / 15,
                    3 => ((k + 15) / 15 - 1) * e,
                    4 => ((k + 16) / 15 - 16d / 15) * e,
                    5 => ((k + 16) * 2 / 30 - 32d / 30) * e,
                    6 => Math.FusedMultiplyAdd(k + 15, 1d / 15, -1) * e,
                    7 => Math.FusedMultiplyAdd(k + 16, 1d / 15, -16d / 15) * e,
                    8 => (k + 16) * (e / 15) - 16 * (e / 15),
                    9 => Math.FusedMultiplyAdd(k + 16, e / 15, -16 * (e / 15)),
                    10 => k * dx / delta / 15,
                    11 => k * dx / 15 / delta,
                    12 => k / 15 * dx / delta,
                    13 => k * dx / (delta * 15),
                    14 => k / (15 * (delta / dx)),
                    15 => k * (dx / 15 / delta),
                    16 => k * (dx / (15 * delta)),
                    17 => k * (1 / (15 * delta / dx)),
                    18 => k * l * f / (32 * delta),
                    19 => k * l * f / 32 / delta,
                    20 => k / (32 * delta / l / f),
                    21 => ((k + 15) * dx - 15 * dx) / delta / 15,
                    22 => ((k + 16) * dx - 16 * dx) / delta / 15,
                    23 => Math.FusedMultiplyAdd(k + 15, dx, -15 * dx) / delta / 15,
                    24 => Math.FusedMultiplyAdd(k + 16, dx, -16 * dx) / delta / 15,
                    25 => (k * l / (32 * delta)) / (2 * na),
                    26 => k * (l / (32 * delta)) / (2 * na),
                    27 => k * (l / (32 * delta) / (2 * na)),
                    28 => (k / 32 * (l / delta)) / (2 * na),
                    29 => k * l / (32 * delta) / na / 2,
                    30 => k / (32 * delta) * l / na / 2,
                    31 => k * l / 32 / delta / na / 2,
                    32 => k * (l / (32 * delta * na)) / 2,
                    33 => k * l / (32 * delta * na) / 2,
                    34 => k / 32 / delta * (l / na) / 2,
                    35 => k / 32 * (l / delta / na) / 2,
                    36 => (k * l / delta) / (32 * na * 2),
                    37 => k * (1 / (32 * delta * (1 / (l * f)))),
                    38 => k / (32 * delta * (1 / (l * f))),
                    39 => k * (1 / (32 * delta * (2 * na / l))),
                    40 => k / (32 * delta * (2 * na / l)),
                    41 => k * (1 / (delta * (32 / (l * f)))),
                    42 => k * (1 / (delta * (64 * (na / l)))),
                    43 => k * (1 / (32 * (delta * (1 / (l * f))))),
                    44 => k / (delta * (32 * (1 / (l * f)))),
                    45 => (k * e / f) * fa / 15,
                    46 => (e / f) * fa * (k / 15),
                    47 => (e / fa) * f * (k / 15),
                    48 => ((k / 15 * e) / f) * fa,
                    49 => (dx / f / delta) * fa * (k / 15),
                    50 => (dx / fa / delta) * f * (k / 15),
                    51 => k / (32 * delta / l / fa),
                    52 => k / (32 * delta * (1 / (l * fa))),
                    53 => (l * fa / (32 * delta)) * k,
                    _ => (k / (32 * delta)) / (1 / (l * fa))
                };
                var x = Coordinate(4); var p = x * r;
                var inside = mask switch { 0 => 2 * x * x <= 1, 1 => Math.Sqrt(2 * x * x) <= 1, 2 => 2 * p * p <= r * r, _ => Math.Sqrt(2 * p * p) <= r };
                var actual = inside ? 101 : 97; var expected = i.GetProperty("expected").GetInt32();
                if (actual == expected) exact++;
                var observed = i.GetProperty("observed").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                var pointRows = observed.Select((v, k) => new { k = 1 + 2 * k, observed = v, predicted = Coordinate(1 + 2 * k) * r }).ToArray();
                points += pointRows.Count(p => p.observed == p.predicted);
                rows.Add(new { id, actual, expected, x, e, pointRows });
            }
            results.Add(new { units, naSource, axisSource, method, mask, exact, points, rows });
        }
        JsonFiles.Write(Path.Combine(output, "methods.json"), results);
        using var encoded = JsonDocument.Parse(JsonSerializer.Serialize(results));
        foreach (var v in encoded.RootElement.EnumerateArray().OrderByDescending(v => v.GetProperty("points").GetInt32()).ThenByDescending(v => v.GetProperty("exact").GetInt32()).Take(10))
            Console.WriteLine($"points={v.GetProperty("points")}/12; support={v.GetProperty("exact")}/18: units={v.GetProperty("units")}, NA={v.GetProperty("naSource")}, axis={v.GetProperty("axisSource")}, method={v.GetProperty("method")}, mask={v.GetProperty("mask")}");
    }
}
