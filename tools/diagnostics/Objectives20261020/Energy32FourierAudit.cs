using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison;

// Offline Fourier-coordinate stage controls. Native values only identify the
// arithmetic stage; they are never a product input or a per-lens correction.
internal static class Energy32FourierAudit
{
    private sealed record Input(string Id, double Wave, double F, double Na, double Radius, int Expected, double[] Observed);
    public static void Run(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        const string root = "artifacts/validation/objectives32-continuation-20261021";
        using var original = JsonDocument.Parse(File.ReadAllText(root + "/discriminant-coordinate-v1/inputs.json"));
        using var probes = JsonDocument.Parse(File.ReadAllText(root + "/probe-v4-summary.json"));
        using var holdouts = JsonDocument.Parse(File.ReadAllText(root + "/native-holdout/capture/cones.json"));
        using var measured = JsonDocument.Parse(File.ReadAllText(root + "/width-holdout-v1/rows.json"));
        var inputs = new List<Input>();
        foreach (var item in original.RootElement.EnumerateArray())
        {
            var id = Path.GetFileNameWithoutExtension(item.GetProperty("lens").GetString()!).Replace(' ', '-').ToLowerInvariant();
            var f = item.GetProperty("nativeF").GetDouble();
            var n = item.GetProperty("native")[1].GetProperty("z").GetDouble();
            var obs = probes.RootElement.EnumerateArray().FirstOrDefault(p => p.GetProperty("id").GetString() == id);
            var radius = item.GetProperty("pupilRadius").GetDouble();
            inputs.Add(new(id, item.GetProperty("wavelength").GetDouble(), f, Math.Sqrt(1 - n * n), radius,
                item.GetProperty("expected").GetInt32(), obs.ValueKind == JsonValueKind.Undefined ? [] : obs.GetProperty("gridAxisX").EnumerateArray().Select(v => v.GetDouble()).Where(v => v < radius).Take(3).ToArray()));
        }
        foreach (var cone in holdouts.RootElement.EnumerateArray())
        {
            var id = cone.GetProperty("id").GetString()![..^5];
            var baseId = Path.GetFileNameWithoutExtension(cone.GetProperty("lens").GetString()!).Replace(' ', '-').ToLowerInvariant();
            var originalInput = inputs.Single(i => i.Id == baseId);
            var row = measured.RootElement.EnumerateArray().Single(r => r.GetProperty("id").GetString() == id
                && r.GetProperty("method").GetString() == "measured-101-control");
            var primary = cone.GetProperty("waves").EnumerateArray().OrderBy(w => Math.Abs(w.GetProperty("micrometers").GetDouble() - originalInput.Wave)).First();
            var f = primary.GetProperty("primaryWorkingFNumber").GetDouble();
            var n = primary.GetProperty("rays")[1].GetProperty("n").GetDouble();
            inputs.Add(new(id, primary.GetProperty("micrometers").GetDouble(), f, Math.Sqrt(1 - n * n),
                originalInput.Radius * cone.GetProperty("apertureScale").GetDouble(),
                row.GetProperty("max").GetDouble() < 1e-5 ? 101 : 97, []));
        }
        var results = new List<object>();
        foreach (var unit in new[] { 1d, .001 })
        foreach (var pitchUnit in new[] { 1d, .001 })
        foreach (var wavelengthConversion in new[] { "multiply", "divide" })
        foreach (var pitchWavelengthConversion in new[] { "multiply", "divide" })
        foreach (var conversion in new[] { "multiply", "divide" })
        foreach (var pitchMethod in Enumerable.Range(0, 10))
        foreach (var roundtrip in Enumerable.Range(0, 5))
        foreach (var coordinateMethod in Enumerable.Range(0, 57))
        foreach (var mask in Enumerable.Range(0, 3))
        {
            var supportExact = 0; var pointExact = 0; var maxPointError = 0d;
            var rows = new List<object>();
            foreach (var i in inputs)
            {
                var l = wavelengthConversion == "multiply" ? i.Wave * unit : i.Wave / (1 / unit);
                var f = i.F; var na = i.Na; var r = i.Radius;
                var pl = pitchWavelengthConversion == "multiply" ? i.Wave * pitchUnit : i.Wave / (1 / pitchUnit);
                var p = pitchMethod switch
                {
                    0 => pl * f / Math.Sqrt(32), 1 => pl / Math.Sqrt(32) * f,
                    2 => Math.Sqrt(32) * pl * f / 32, 3 => pl * f * Math.Sqrt(32) / 32,
                    4 => Math.Sqrt(pl * pl * f * f / 32), 5 => Math.Sqrt((pl * f) * (pl * f) / 32),
                    6 => Math.Sqrt(1d / 32) / (2 * na / pl), 7 => 1 / (Math.Sqrt(32) * (2 * na / pl)),
                    8 => 1 / (32 * ((1 / (pl * f)) / Math.Sqrt(32))), _ => Math.Sqrt(1d / 32) / (1 / (pl * f))
                };
                p = conversion == "multiply" ? p * (unit / pitchUnit) : p / (pitchUnit / unit);
                p = roundtrip switch
                {
                    1 => p * 1000 * .001, 2 => p * 1000 / 1000,
                    3 => p * .001 * 1000, 4 => p / 1000 * 1000, _ => p
                };
                double Physical(double k) => coordinateMethod switch
                {
                    0 => k * (1 / (32 * p / l / f)) * r,
                    1 => (k / (32 * p)) * l * f * r,
                    2 => (k * l / (32 * p)) * f * r,
                    3 => ((k * r * l) / (32 * p)) * f,
                    4 => (k * (l * f) / (32 * p)) * r,
                    5 => (k / (32 * p)) * (l * f * r),
                    6 => (k / (32 * p)) * (l * (f * r)),
                    7 => k / (32 * p) * ((l * r) / (2 * na)),
                    8 => (k * r * l) / ((32 * p) * (2 * na)),
                    9 => (k * l / (32 * p)) * (r / (2 * na)),
                    10 => (k * r) / (32 * p / l / f),
                    11 => (((k + 16) / (32 * p)) - 1 / (2 * p)) * l * f * r,
                    12 => ((k + 16) * (1 / (32 * p)) - 16 * (1 / (32 * p))) * l * f * r,
                    13 => ((k + 16) * (l * f / (32 * p)) - 16 * (l * f / (32 * p))) * r,
                    14 => ((k + 16) * (r * l * f / (32 * p)) - 16 * (r * l * f / (32 * p))),
                    15 => ((k + 16) / 32 / p - .5 / p) * l * f * r,
                    16 => k * ((l * f / 32) / p) * r,
                    17 => k / (p / (l * f / 32)) * r,
                    18 => k * (l / (32 * p) * f) * r,
                    19 => k * r * (l / (32 * p) * f),
                    20 => k / (32 * p / l / f / r),
                    21 => k / ((32 * p / (l * f)) / r),
                    22 => (k * l) / ((32 * p) * (2 * na)) * r,
                    23 => (k / (32 * p)) / (2 * na / l) * r,
                    24 => (k / 32 / p) / (2 * na / l) * r,
                    25 => (k * r / (32 * p)) / (2 * na / l),
                    26 => (k * r / 32 / p) / (2 * na / l),
                    27 => ((k * r * l) / 32 / p) * f,
                    28 => ((k * r * l) / p / 32) * f,
                    29 => k * ((r * l / 32 / p) * f),
                    30 => k * ((l * f * 30 / 64 / p) / 15) * r,
                    31 => (l * f * 30 / 64 / p) * (k / 15) * r,
                    32 => (l * 30 / 64 / p * f) * (k / 15) * r,
                    33 => k / (15 / (l * f * 30 / 64 / p)) * r,
                    34 => ((l * f * 30 / 64) / p / f) * f * (k / 15) * r,
                    35 => (k * (l * f * 30 / 64 / p)) / 15 * r,
                    36 => (2 * k * (l * f * 30 / 64 / p)) / 30 * r,
                    37 => (k * (2 * (l * f * 30 / 64 / p))) / 30 * r,
                    38 => k * l * f * 30 / 64 / p / 15 * r,
                    39 => k * l * f * 30 / 64 / p / 15 * r,
                    40 => (k * r * (l * f * 30 / 64 / p)) / 15,
                    41 => (k * r / 15) * (l * f * 30 / 64 / p),
                    42 => (k * (l * f * 30 / 64 / p) * r) / 15,
                    43 => k * r * l * f / (32 * p),
                    44 => k * (r * l * f) / (32 * p),
                    45 => k * (l * f * r) / (32 * p),
                    46 => (k * r) * (l * f) / (32 * p),
                    47 => l * f * k * r / (32 * p),
                    48 => k * l * f / (32 * p) * r,
                    49 => k * l * f * r / 32 / p,
                    50 => k * (r * l) / 32 / p * f,
                    51 => k * (l * f * r / (32 * p)),
                    52 => k * (r * l * f / (32 * p)),
                    53 => k * (r * l / (32 * p)) * f,
                    54 => k * r / 32 * l * f / p,
                    55 => k * (r / 32 * l * f / p),
                    _ => k * (l * f / (32 * p)) * r
                };
                var x = Physical(4); var normalized = x / r;
                var inside = mask switch { 0 => normalized * normalized * 2 <= 1, 1 => x * x * 2 <= r * r, _ => Math.Sqrt(x * x * 2) <= r };
                var actual = inside ? 101 : 97;
                if (actual == i.Expected) supportExact++;
                foreach (var (point, j) in i.Observed.Select((v, j) => (v, j)))
                {
                    var predicted = Physical(1 + 2 * j); var delta = Math.Abs(predicted - point);
                    if (delta == 0) pointExact++;
                    maxPointError = Math.Max(maxPointError, delta);
                }
                rows.Add(new { i.Id, i.Expected, actual, x, normalized });
            }
            results.Add(new { unit, pitchUnit, wavelengthConversion, pitchWavelengthConversion, conversion, pitchMethod, roundtrip, coordinateMethod, mask, supportExact, pointExact, maxPointError, rows = supportExact >= 17 || pointExact >= 11 ? rows : [] });
        }
        JsonFiles.Write(Path.Combine(output, "inputs.json"), inputs);
        JsonFiles.Write(Path.Combine(output, "methods.json"), results);
        using var encoded = JsonDocument.Parse(JsonSerializer.Serialize(results));
        foreach (var r in encoded.RootElement.EnumerateArray().OrderByDescending(r => r.GetProperty("pointExact").GetInt32()).ThenByDescending(r => r.GetProperty("supportExact").GetInt32()).Take(12))
            Console.WriteLine($"points={r.GetProperty("pointExact")}/12; support={r.GetProperty("supportExact")}/18: unit={r.GetProperty("unit")}, pitchUnit={r.GetProperty("pitchUnit")}, wavelength={r.GetProperty("wavelengthConversion")}, convert={r.GetProperty("conversion")}, pitch={r.GetProperty("pitchMethod")}, roundtrip={r.GetProperty("roundtrip")}, coord={r.GetProperty("coordinateMethod")}, mask={r.GetProperty("mask")}");
    }
}
