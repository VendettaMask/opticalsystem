using System.Text.Json;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.ZemaxComparison;

internal static class Energy32DispersionAudit
{
    public static void Run(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        const string root = "artifacts/validation/objectives32-continuation-20261021";
        using var request = JsonDocument.Parse(File.ReadAllText(root + "/native-bits-request.json"));
        var rows = new List<object>();
        foreach (var job in request.RootElement.GetProperty("jobs").EnumerateArray())
        {
            var id = job.GetProperty("id").GetString()!;
            var optic = OpticalFormatCatalog.Import(File.ReadAllText(job.GetProperty("input").GetString()!), ".zmx");
            using var native = JsonDocument.Parse(File.ReadAllText(root + "/native-active-parameters-v2/capture/" + id + "/refractive-indices.json"));
            foreach (var (s, index) in optic.SurfaceGroup.Items.Select((s, index) => (s, index)))
            {
                if (s.MaterialAfter is not CatalogGlassMaterial g || g.Formula != "zemax formula 2") continue;
                for (var w = 0; w < optic.Wavelengths.Count; w++)
                {
                    var lambda = optic.Wavelengths[w].Nanometers / 1000; var l2 = lambda * lambda;
                    var values = Enumerable.Range(0, 3).Select(k => g.Coefficients[2 * k] * l2 / (l2 - g.Coefficients[2 * k + 1])).ToArray();
                    var quotient = Enumerable.Range(0, 3).Select(k => g.Coefficients[2 * k] / (l2 - g.Coefficients[2 * k + 1])).ToArray();
                    var relative = Enumerable.Range(0, 3).Select(k => g.Coefficients[2 * k] / (1 - g.Coefficients[2 * k + 1] / l2)).ToArray();
                    var inverse = Enumerable.Range(0, 3).Select(k => g.Coefficients[2 * k] / (1 - g.Coefficients[2 * k + 1] * (1 / l2))).ToArray();
                    var expected = Math.Abs(native.RootElement[index].GetProperty("indices")[w].GetDouble());
                    foreach (var method in Enumerable.Range(0, 7))
                    {
                        var actual = method switch
                        {
                            0 => g.RefractiveIndex(optic.Wavelengths[w].Nanometers),
                            1 => Math.Sqrt(1 + (values[0] + values[1] + values[2])),
                            2 => Math.Sqrt(1 + l2 * (quotient[0] + quotient[1] + quotient[2])),
                            3 => Math.Sqrt(1 + relative[0] + relative[1] + relative[2]),
                            4 => Math.Sqrt(1 + (relative[0] + relative[1] + relative[2])),
                            5 => Math.Sqrt(1 + inverse[0] + inverse[1] + inverse[2]),
                            _ => Math.Sqrt(1 + (inverse[0] + inverse[1] + inverse[2]))
                        };
                        rows.Add(new { id, surface = index, g.Name, wavelength = w + 1, method, actual, expected, error = actual - expected });
                    }
                }
            }
        }
        JsonFiles.Write(Path.Combine(output, "rows.json"), rows);
        using var encoded = JsonDocument.Parse(JsonSerializer.Serialize(rows));
        var methods = encoded.RootElement.EnumerateArray().GroupBy(r => r.GetProperty("method").GetInt32()).Select(g => new
        {
            method = g.Key, count = g.Count(), exact = g.Count(v => v.GetProperty("error").GetDouble() == 0), maximum = g.Max(v => Math.Abs(v.GetProperty("error").GetDouble()))
        }).ToArray();
        JsonFiles.Write(Path.Combine(output, "methods.json"), methods);
        foreach (var m in methods) Console.WriteLine($"method={m.method}: {m.exact}/{m.count} exact, maximum={m.maximum:R}");
    }
}
