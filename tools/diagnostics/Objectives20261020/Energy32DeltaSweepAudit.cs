using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison;

internal static class Energy32DeltaSweepAudit
{
    public static void Run(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        const string root = "artifacts/validation/objectives32-continuation-20261021";
        using var request = JsonDocument.Parse(File.ReadAllText(root + "/native-energy-delta-sweep-v2-request.json"));
        using var items = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives-20261020/final-v7-intersection-fallback/items.json"));
        var rows = new List<object>();
        foreach (var group in request.RootElement.GetProperty("jobs").EnumerateArray().GroupBy(j => j.GetProperty("originalId").GetString()!))
        {
            var lens = group.First().GetProperty("lens").GetString();
            var item = items.RootElement.EnumerateArray().Single(i => i.GetProperty("lens").GetString() == lens && i.GetProperty("run").GetProperty("key").GetString() == "Diffraction Encircled Energy");
            var holdout = group.Key.EndsWith("-minus", StringComparison.Ordinal) || group.Key.EndsWith("-plus", StringComparison.Ordinal);
            using var reference = JsonDocument.Parse(File.ReadAllText(holdout ? root + "/native-holdout/capture/" + group.Key + "/data.json"
                : Path.Combine(item.GetProperty("path").GetString()!, "native/data.json")));
            var variants = new List<object>();
            foreach (var job in group)
            {
                var path = root + "/native-energy-delta-sweep-v2/capture/" + job.GetProperty("id").GetString();
                using var data = JsonDocument.Parse(File.ReadAllText(path + "/data.json"));
                using var captured = JsonDocument.Parse(File.ReadAllText(path + "/captured-settings.json"));
                var errors = new List<double>();
                for (var series = 0; series < 2; series++)
                {
                    var source = reference.RootElement.GetProperty("dataSeries")[series].GetProperty("y");
                    var actual = data.RootElement.GetProperty("dataSeries")[series].GetProperty("y");
                    if (source.GetArrayLength() != 401 || actual.GetArrayLength() != 401) throw new InvalidDataException("Complete 401-point series required.");
                    errors.Add(source.EnumerateArray().Zip(actual.EnumerateArray(), (a, b) => Math.Abs(a[0].GetDouble() - b[0].GetDouble())).Max());
                }
                variants.Add(new { offset = job.GetProperty("offset").GetInt32(), delta = captured.RootElement.GetProperty("HuygensDelta").GetDouble(), ideal = errors[0], actual = errors[1] });
            }
            using var encoded = JsonDocument.Parse(JsonSerializer.Serialize(variants));
            var best = encoded.RootElement.EnumerateArray().OrderBy(r => r.GetProperty("ideal").GetDouble()).ThenBy(r => r.GetProperty("actual").GetDouble()).First();
            rows.Add(new { id = group.Key, best = best.Clone(), variants });
            Console.WriteLine($"{group.Key}: offset={best.GetProperty("offset")}, ideal={best.GetProperty("ideal")}, actual={best.GetProperty("actual")}, delta={best.GetProperty("delta")}");
        }
        JsonFiles.Write(Path.Combine(output, "rows.json"), rows);
    }
}
