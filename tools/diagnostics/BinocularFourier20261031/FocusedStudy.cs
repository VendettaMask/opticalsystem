using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;

internal static class FocusedStudy
{
    private static Optic Model(string prior, int config)
    {
        using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(prior, "cases.json")));
        var row = cases.RootElement.EnumerateArray().Single(r => r.GetProperty("id").GetString() == $"m{config + 2}-fourier");
        return Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(row.GetProperty("snapshot").GetString()!));
    }
    internal static void Endpoints(string prior, string root)
    {
        var rows = new List<object>();
        foreach (var config in new[] { 1, 2 }) foreach (var sampling in new[] { 32, 64, 128, 256, 512 })
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var timer = Stopwatch.StartNew();
            try
            {
                var optic = Model(prior, config);
                var data = new MtfThroughFocusAnalysis(optic, MtfComputationMethod.Fourier,
                    spatialFrequency: 3.2, focusPlaneCount: 1, wavelengthNumber: 0, fieldNumber: 3,
                    settings: new(PupilSampling: sampling, ImageSize: sampling, ZemaxCompatible: true))
                    .GenerateData(deadline.Token);
                rows.Add(new { config, sampling, T = data.PlotSeries[0].Points[0].Y, S = data.PlotSeries[1].Points[0].Y,
                    coreSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Optic).Assembly.Location))),
                    seconds = timer.Elapsed.TotalSeconds, error = (string?)null });
                JsonFiles.Write(Path.Combine(root, "core-endpoints.json"), rows);
                Console.WriteLine($"C{config}/N{sampling}: {data.PlotSeries[1].Points[0].Y:R}; {timer.Elapsed.TotalSeconds:F1}s");
            }
            catch (Exception e)
            {
                rows.Add(new { config, sampling, error = e.ToString(), seconds = timer.Elapsed.TotalSeconds });
                JsonFiles.Write(Path.Combine(root, "core-endpoints.json"), rows);
                Console.WriteLine($"C{config}/N{sampling}: {e.Message}");
            }
        }
    }
    internal static void Curves(string repository, string prior, string root)
    {
        var gate = ComparisonConfiguration.Load(Path.Combine(repository, "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json"))
            .Analyses["Fourier MTF vs Field"].Quantities["Modulation"];
        var rows = new List<object>();
        foreach (var config in new[] { 1, 2 }) foreach (var sampling in new[] { 32, 128 })
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var timer = Stopwatch.StartNew();
            var optic = Model(prior, config);
            var data = new MtfVsFieldAnalysis(optic, MtfComputationMethod.Fourier, spatialFrequency: 3.2,
                fieldPointCount: 10, spatialFrequencies: [3.2], wavelengthNumber: 0,
                settings: new(PupilSampling: sampling, ImageSize: sampling, ZemaxCompatible: true),
                zemaxCompatibleOutput: true).GenerateData(deadline.Token);
            JsonFiles.Write(Path.Combine(root, $"core-curve-c{config}-n{sampling}.json"), data);
            var nativeFile = sampling == 32 ? Path.Combine(prior, $"native/m{config + 2}-fourier/data.json")
                : config == 1 ? Path.Combine(root, "native/scan128-all/data.json")
                : Path.Combine(root, "endpoint-native/c2-curve128/data.json");
            if (!File.Exists(nativeFile)) { Console.WriteLine("Native curve not available: " + nativeFile); continue; }
            var z = JsonFiles.Read<JsonElement>(nativeFile).GetProperty("dataSeries")[5];
            var x = z.GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var y = z.GetProperty("y").EnumerateArray().ToArray();
            for (var direction = 0; direction < 2; direction++)
            {
                var s = data.PlotSeries[direction];
                var actual = new Series1DResult("curve", "Core", s.Points.Select(p => p.X).ToArray(), s.Points.Select(p => p.Y).ToArray(),
                    new("NormalizedField", "Dimensionless"), new("Modulation", "Dimensionless"));
                var expected = actual with { DisplayName = "Zemax", X = x, Y = y.Select(v => v[direction].GetDouble()).ToArray() };
                var matched = ComparisonMetrics.Align(actual, expected, []);
                var metric = ComparisonMetrics.Calculate("curve", "Dimensionless", matched, gate);
                rows.Add(new { config, sampling, direction = direction == 0 ? "T" : "S", metric,
                    nativeSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(nativeFile))),
                    seconds = timer.Elapsed.TotalSeconds });
                Console.WriteLine($"C{config}/N{sampling}/{direction}: {metric.Conclusion}; max={metric.MaxAbsolute:R}");
            }
            JsonFiles.Write(Path.Combine(root, "curve-comparisons.json"), rows);
        }
    }
}
