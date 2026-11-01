using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;

internal static class ConvergedAssessment
{
    internal static void Run(string repository, string prior, string root)
    {
        var policy = Path.Combine(repository, "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json");
        var gate = ComparisonConfiguration.Load(policy).Analyses["Fourier MTF vs Field"].Quantities["Modulation"];
        string Hash(string p) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)));
        Series1DResult Native(string path, int index, int direction)
        {
            var s = JsonFiles.Read<JsonElement>(path).GetProperty("dataSeries")[index];
            return new("curve", "Zemax", s.GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray(),
                s.GetProperty("y").EnumerateArray().Select(v => v[direction].GetDouble()).ToArray(),
                new("NormalizedField", "Dimensionless"), new("Modulation", "Dimensionless"));
        }
        Series1DResult Core(string path, int direction)
        {
            var points = JsonFiles.Read<JsonElement>(path).GetProperty("seriesList")[direction].GetProperty("points").EnumerateArray().ToArray();
            return new("curve", "Core", points.Select(p => p.GetProperty("x").GetDouble()).ToArray(),
                points.Select(p => p.GetProperty("y").GetDouble()).ToArray(), new("NormalizedField", "Dimensionless"), new("Modulation", "Dimensionless"));
        }
        var comparisons = new List<object>();
        void Compare(string kind, int config, int direction, Series1DResult a, Series1DResult b, string sourceA, string sourceB)
        {
            var aligned = ComparisonMetrics.Align(a, b, []);
            var metric = ComparisonMetrics.Calculate("curve", "Dimensionless", aligned, gate);
            var violations = aligned.Workbench.Zip(aligned.Zemax, (w, z) => Math.Abs(w - z) > gate.Absolute + gate.Relative * Math.Abs(z)).Count(v => v);
            comparisons.Add(new { kind, config, direction = direction == 0 ? "T" : "S", metric, pointwiseViolations = violations,
                sourceA, sourceASha256 = Hash(sourceA), sourceB, sourceBSha256 = Hash(sourceB) });
            Console.WriteLine($"{kind}/C{config}/{direction}: {metric.Conclusion}; NRMSE={metric.Nrmse:R}");
        }
        for (var config = 1; config <= 2; config++)
        {
            var low = Path.Combine(prior, $"native/m{config + 2}-fourier/data.json");
            var medium = Path.Combine(root, $"single-frequency-native/c{config}-n128/data.json");
            var high = Path.Combine(root, $"single-frequency-native/c{config}-n256/data.json");
            // Validate native physical settings independently of the chosen Core density.
            foreach (var sampling in new[] { 128, 256 })
            {
                var directory = Path.Combine(root, $"single-frequency-native/c{config}-n{sampling}");
                var model = JsonFiles.Read<JsonElement>(Path.Combine(directory, "model.json"));
                if (!model.GetProperty("aperture").GetProperty("AFocalImageSpace").GetBoolean()
                    || model.GetProperty("units").GetProperty("AfocalModeUnits").GetString() != "Milliradians")
                    throw new InvalidDataException("Native afocal mode/unit readback changed");
                var capture = JsonFiles.Read<JsonElement>(Path.Combine(directory, "captured-settings.json"));
                var settings = capture.GetProperty("properties");
                if (settings.GetProperty("SampleSize").GetString() != $"S_{sampling}x{sampling}"
                    || settings.GetProperty("Freq_1").GetDouble() != 3.2 || settings.GetProperty("FieldDensity").GetInt32() != 10
                    || settings.GetProperty("ScanType").GetString() != "Plus_Y" || !settings.GetProperty("RemoveVignetting").GetBoolean()
                    || settings.GetProperty("UsePolarization").GetBoolean() || capture.GetProperty("selectors").GetProperty("Wavelength").GetInt32() != 0
                    || capture.GetProperty("request").GetProperty("configuration").GetInt32() != config)
                    throw new InvalidDataException("Native high-frequency scan contract changed");
            }
            foreach (var direction in new[] { 0, 1 })
            {
                var c32 = Path.Combine(root, $"core-curve-c{config}-n32.json");
                var c128 = Path.Combine(root, $"core-curve-c{config}-n128.json");
                Compare("Original32", config, direction, Core(c32, direction), Native(low, 5, direction), c32, low);
                Compare("NativeConvergence128To256", config, direction, Native(medium, 0, direction), Native(high, 0, direction), medium, high);
                Compare("CoreConvergence32To128", config, direction, Core(c32, direction), Core(c128, direction), c32, c128);
                Compare("ConvergedReference", config, direction, Core(c32, direction), Native(high, 0, direction), c32, high);
            }
        }
        JsonFiles.Write(Path.Combine(root, "converged-assessment.json"), new
        {
            referenceBasis = "ConvergenceCheckedNativeResult", userDirection = "以收敛结果为精度基准",
            physicalFrequencyCyclesPerMilliradian = 3.2, coreSampling = 32, nativeReferenceSampling = 256,
            convergenceSampling = new[] { 128, 256 }, gate, policySha256 = Hash(policy), comparisons,
            original32ClassificationsPreserved = true, allSixFrequenciesCertified = false,
            productOpticalComputationChanged = false
        });
    }
}
