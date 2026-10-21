using System.Numerics;
using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison;

// Offline grouping search for the single Fourier monomial. It can falsify an
// arithmetic-stage hypothesis; no expression or native value enters Core.
internal static class Energy32ExpressionAudit
{
    private sealed record Probe(string Id, double K, double Wave, double F, double Q, double Delta, double Radius, double Expected);
    private sealed record Term(string Expression, double[] Values, int Operations);
    public static void Run(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        const string root = "artifacts/validation/objectives32-continuation-20261021";
        using var inputs = JsonDocument.Parse(File.ReadAllText(root + "/fourier-stage-v8-physical-factor/inputs.json"));
        using var explicitProbes = JsonDocument.Parse(File.ReadAllText(root + "/probe-explicit-delta-v2-summary.json"));
        using var raw = JsonDocument.Parse(File.ReadAllText(root + "/discriminant-coordinate-v1/inputs.json"));
        var probes = new List<Probe>();
        foreach (var i in inputs.RootElement.EnumerateArray())
        {
            var id = i.GetProperty("id").GetString()!; var wave = i.GetProperty("wave").GetDouble();
            var f = i.GetProperty("f").GetDouble(); var radius = i.GetProperty("radius").GetDouble();
            var original = raw.RootElement.EnumerateArray().FirstOrDefault(v => Path.GetFileNameWithoutExtension(v.GetProperty("lens").GetString()!).Replace(' ', '-').ToLowerInvariant() == id);
            var n = original.ValueKind == JsonValueKind.Undefined ? 1 : original.GetProperty("native")[1].GetProperty("z").GetDouble();
            var q = 1 - n * n;
            foreach (var (p, k) in i.GetProperty("observed").EnumerateArray().Select((p, k) => (p, k)))
                probes.Add(new(id + "/auto", 1 + 2 * k, wave, f, q, wave * f / Math.Sqrt(32), radius, p.GetDouble()));
            foreach (var p in explicitProbes.RootElement.EnumerateArray().Where(p => p.GetProperty("id").GetString()!.StartsWith(id + "-delta", StringComparison.Ordinal)
                && p.GetProperty("delta").GetDouble() < 4))
                foreach (var (v, k) in p.GetProperty("axis").EnumerateArray().Select(v => v.GetDouble()).Where(v => v > .01 && v < radius).Take(3).Select((v, k) => (v, k)))
                    probes.Add(new(id + "/delta" + p.GetProperty("delta"), 1 + 2 * k, wave, f, q, p.GetProperty("delta").GetDouble(), radius, v));
        }
        JsonFiles.Write(Path.Combine(output, "probes.json"), probes);
        var summaries = new List<object>();
        foreach (var physical in new[] { false })
        foreach (var axis in new[] { "default" })
        foreach (var units in Enumerable.Range(0, 5))
        {
            var names = axis == "default" ? new[] { "k", "defaultPitch", "M", "delta" }
                : new[] { "k", "defaultPitch", "M", "delta", "axisF", "scalarF" };
            int[] signs = axis == "default" ? [1, 1, -1, -1] : [1, 1, -1, -1, 1, -1];
            var useNa = axis.StartsWith("na", StringComparison.Ordinal);
            if (useNa) { signs[2] = -1; names[2] = "NA"; }
            var leaves = Enumerable.Range(0, names.Length).Select(j => probes.Select(p => j switch
            {
                0 => p.K,
                1 => units switch { 1 => (p.Wave * .001) * p.F * 30 / 64, 2 => (p.Wave * p.F * 30 / 64) * .001,
                    3 => (p.Wave * p.F * 30 / 64) / 1000, 4 => (p.Wave / 1000) * p.F * 30 / 64, _ => p.Wave * p.F * 30 / 64 },
                2 => 15,
                3 => units == 0 ? p.Delta : units is 1 or 2 ? p.Delta * .001 : p.Delta / 1000,
                _ => p.F
            }).ToArray()).ToArray();
            var table = new Dictionary<(int, int), Term[]>();
            var all = (1 << names.Length) - 1;
            for (var size = 1; size <= names.Length; size++)
            foreach (var mask in Enumerable.Range(1, all).Where(m => BitOperations.PopCount((uint)m) == size))
            {
                var direct = new[] { new Dictionary<string, Term>(), new Dictionary<string, Term>() };
                for (var orientation = 0; orientation < 2; orientation++)
                {
                    if (size == 1)
                    {
                        var j = BitOperations.TrailingZeroCount((uint)mask);
                        var reciprocal = (signs[j] < 0) != (orientation != 0);
                        Add(direct[orientation], new(reciprocal ? "(1/" + names[j] + ")" : names[j],
                            leaves[j].Select(v => reciprocal ? 1 / v : v).ToArray(), reciprocal ? 1 : 0));
                    }
                    for (var a = (mask - 1) & mask; a > 0; a = (a - 1) & mask)
                    {
                        var b = mask ^ a;
                        if (b == 0) continue;
                        foreach (var x in table[(a, orientation)])
                        {
                            foreach (var y in table[(b, orientation)])
                                Add(direct[orientation], new("(" + x.Expression + "*" + y.Expression + ")", x.Values.Zip(y.Values, (u, v) => u * v).ToArray(), x.Operations + y.Operations + 1));
                            foreach (var y in table[(b, 1 - orientation)])
                                Add(direct[orientation], new("(" + x.Expression + "/" + y.Expression + ")", x.Values.Zip(y.Values, (u, v) => u / v).ToArray(), x.Operations + y.Operations + 1));
                        }
                    }
                }
                for (var orientation = 0; orientation < 2; orientation++)
                {
                    var result = new Dictionary<string, Term>(direct[orientation]);
                    foreach (var term in direct[1 - orientation].Values)
                        Add(result, new("(1/" + term.Expression + ")", term.Values.Select(v => 1 / v).ToArray(), term.Operations + 1));
                    table.Add((mask, orientation), result.Values.ToArray());
                }
            }
            var ranked = table[(all, 0)].Select(t => new { t.Expression, t.Operations,
                exact = t.Values.Where((v, j) => (physical ? v : v * probes[j].Radius) == probes[j].Expected).Count(),
                maximum = t.Values.Select((v, j) => Math.Abs((physical ? v : v * probes[j].Radius) - probes[j].Expected)).Max(),
                rows = t.Values.Select((v, j) => new { probes[j].Id, probes[j].K, expected = probes[j].Expected, actual = physical ? v : v * probes[j].Radius })
            }).OrderByDescending(t => t.exact).ThenBy(t => t.Operations).Take(10).ToArray();
            summaries.Add(new { physical, units, axis, candidates = table[(all, 0)].Length, rows = ranked });
            Console.WriteLine($"physical={physical}, units={units}, axis={axis}: {ranked[0].exact}/{probes.Count} exact; {table[(all, 0)].Length} distinct groupings; {ranked[0].Expression}");
        }
        JsonFiles.Write(Path.Combine(output, "methods.json"), summaries);
    }
    private static void Add(Dictionary<string, Term> terms, Term term)
    {
        var key = string.Join(',', term.Values.Select(BitConverter.DoubleToInt64Bits));
        if (!terms.TryGetValue(key, out var existing) || existing.Operations > term.Operations) terms[key] = term;
    }
}
