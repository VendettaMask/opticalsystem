using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison;

internal static class TaggedFacetRuleScoring
{
    internal static int Run(string input, string output)
    {
        if (File.Exists(output)) throw new IOException("Output exists");
        var data = JsonFiles.Read<JsonElement>(input);
        var rows = new List<object>();
        foreach (var formula in new[] { "distance", "planeDistance", "rawDistance", "rawPlaneDistance", "moller", "fmaDistance", "fmaPlaneDistance", "yzFirstDistance" })
        foreach (var edge in new[] { 0d, 1e-8, 1e-6 })
        foreach (var tie in new[] { "first", "last", "minX", "maxX", "centroidX", "bvh" })
        {
            var errors = new int[2]; var major = new int[2]; var missed = new int[2];
            foreach (var config in data.EnumerateArray())
            {
                var index = config.GetProperty("configuration").GetInt32() - 1;
                foreach (var probe in config.GetProperty("probes").EnumerateArray())
                foreach (var hit in probe.GetProperty("hits").EnumerateArray())
                {
                    var all = hit.GetProperty("candidates").EnumerateArray().ToArray();
                    var valid = all.Where(c => c.GetProperty("minEdgeDistance").GetDouble() >= -edge
                        && (tie != "bvh" || c.GetProperty("bvhRank").GetInt32() >= 0)).ToArray();
                    if (valid.Length == 0) { missed[index]++; errors[index]++; continue; }
                    var minimum = valid.Min(c => c.GetProperty(formula).GetDouble());
                    var allowance = tie is "first" or "last" or "bvh" ? 0 : 8 * Math.Abs(Math.BitIncrement(minimum) - minimum);
                    var near = valid.Where(c => c.GetProperty(formula).GetDouble() <= minimum + allowance).ToArray();
                    var selected = tie switch
                    {
                        "last" => near[^1],
                        "minX" => near.OrderBy(c => c.GetProperty("minX").GetDouble()).ThenBy(c => c.GetProperty("f").GetInt32()).First(),
                        "maxX" => near.OrderBy(c => c.GetProperty("maxX").GetDouble()).ThenBy(c => c.GetProperty("f").GetInt32()).First(),
                        "centroidX" => near.OrderBy(c => c.GetProperty("centroid").GetProperty("x").GetDouble()).ThenBy(c => c.GetProperty("f").GetInt32()).First(),
                        "bvh" => near.MinBy(c => c.GetProperty("bvhRank").GetInt32()),
                        _ => near[0]
                    };
                    var target = all.Single(c => c.GetProperty("f").GetInt32() == hit.GetProperty("chosen").GetInt32());
                    if (selected.GetProperty("f").GetInt32() == hit.GetProperty("chosen").GetInt32()) continue;
                    errors[index]++;
                    var sum = new[] { "x", "y", "z" }.Sum(k => Math.Pow(selected.GetProperty("n").GetProperty(k).GetDouble() - target.GetProperty("n").GetProperty(k).GetDouble(), 2));
                    if (Math.Sqrt(sum) > 1e-12) major[index]++;
                }
            }
            rows.Add(new { formula, edge, tie, c1 = errors[0], c2 = errors[1], total = errors.Sum(), majorC1 = major[0], majorC2 = major[1], missed });
        }
        JsonFiles.Write(output, new { meaning = "Offline hypotheses only; no fitted winner or optical acceptance", hypotheses = rows });
        Console.WriteLine("Scored " + rows.Count + " offline hypotheses; no product changes.");
        return 0;
    }
}
