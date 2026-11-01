using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;

internal static class RemainingFrequencies
{
    private sealed record Case(string Id, int Configuration, double Frequency, string Source, string SourceSha256,
        string Snapshot, string SnapshotSha256);
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static string CoreHash => Hash(typeof(Optic).Assembly.Location);
    internal static void Prepare(string repository, string prior, string root)
    {
        if (Directory.Exists(root)) throw new IOException("Fresh remaining-frequency root required");
        Directory.CreateDirectory(root);
        var original = JsonFiles.Read<JsonElement>(Path.Combine(prior, "cases.json"));
        var cases = new List<Case>();
        var frequencies = new[] { .1, .2, .4, .8, 1.6 };
        for (var config = 1; config <= 2; config++)
        {
            var basis = original.EnumerateArray().Single(r => r.GetProperty("id").GetString() == $"m{config + 2}-fourier");
            foreach (var frequency in frequencies)
            {
                var id = $"c{config}-f{frequency.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', 'p')}";
                var row = new Case(id, config, frequency, basis.GetProperty("source").GetString()!, basis.GetProperty("sourceSha256").GetString()!,
                    basis.GetProperty("snapshot").GetString()!, basis.GetProperty("snapshotSha256").GetString()!);
                if (Hash(row.Source) != row.SourceSha256 || Hash(row.Snapshot) != row.SnapshotSha256) throw new InvalidDataException("Original input changed");
                cases.Add(row);
            }
        }
        foreach (var sampling in new[] { 128, 256 })
        {
            var folder = Path.Combine(root, "native" + sampling); Directory.CreateDirectory(folder);
            var jobs = new List<object>();
            foreach (var row in cases)
            {
                var request = JsonFiles.Read<Dictionary<string, JsonElement>>(Path.Combine(prior, $"native/m{row.Configuration + 2}-fourier.request.json"));
                JsonElement E(object value) => JsonSerializer.SerializeToElement(value, JsonFiles.Options);
                var settings = request["zemaxSettings"].Deserialize<Dictionary<string, JsonElement>>(JsonFiles.Options)!;
                settings["SampleSize"] = E($"S_{sampling}x{sampling}");
                for (var index = 1; index <= 6; index++) settings["Freq_" + index] = E(index == 1 ? row.Frequency : 0d);
                request["zemaxSettings"] = E(settings); request["pupilSampling"] = E(sampling); request["imageSampling"] = E(sampling);
                request["studyPhysicalFrequencyCyclesPerMilliradian"] = E(row.Frequency);
                request["studyRole"] = E("NativeReferenceConvergence");
                JsonFiles.Write(Path.Combine(folder, row.Id + ".request.json"), request);
                jobs.Add(new { id = row.Id, request = row.Id + ".request.json", inputSha256 = row.SourceSha256 });
            }
            JsonFiles.Write(Path.Combine(folder, "jobs.json"), jobs);
        }
        JsonFiles.Write(Path.Combine(root, "cases.json"), cases);
        JsonFiles.Write(Path.Combine(root, "preparation.json"), new
        {
            coreSha256 = CoreHash, policySha256 = Hash(Path.Combine(repository, "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json")),
            frequenciesCyclesPerMilliradian = frequencies, nativeSampling = new[] { 128, 256 },
            coreSampling = new[] { 32, 64 }, backgroundOnly = true, original32ResultsPreserved = true
        });
        Console.WriteLine("Prepared ten cases / twenty single-frequency native captures");
    }
    internal static void Compute(string root, int sampling)
    {
        var cases = JsonFiles.Read<Case[]>(Path.Combine(root, "cases.json"));
        var folder = Path.Combine(root, "core-" + CoreHash[..12], "n" + sampling);
        if (Directory.Exists(folder)) throw new IOException("Fresh Core cohort/sampling required");
        Directory.CreateDirectory(folder); var status = new List<object>();
        foreach (var group in cases.GroupBy(c => c.Configuration))
        {
            var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(group.First().Snapshot));
            foreach (var row in group)
            {
                var timer = Stopwatch.StartNew();
                using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                try
                {
                    if (Hash(row.Source) != row.SourceSha256 || Hash(row.Snapshot) != row.SnapshotSha256) throw new InvalidDataException("Input changed");
                    var data = new MtfVsFieldAnalysis(optic, MtfComputationMethod.Fourier, spatialFrequency: row.Frequency,
                        fieldPointCount: 10, spatialFrequencies: [row.Frequency], wavelengthNumber: 0, removeVignettingFactors: true,
                        settings: new(PupilSampling: sampling, ImageSize: sampling, ZemaxCompatible: true), zemaxCompatibleOutput: true)
                        .GenerateData(deadline.Token);
                    JsonFiles.Write(Path.Combine(folder, row.Id + ".json"), data);
                    status.Add(new { row.Id, data.Outcome, seconds = timer.Elapsed.TotalSeconds, error = (string?)null });
                    Console.WriteLine($"N{sampling}/{row.Id}: {data.Outcome}; {timer.Elapsed.TotalSeconds:F1}s");
                }
                catch (Exception error)
                {
                    status.Add(new { row.Id, Outcome = AnalysisOutcome.Unavailable, seconds = timer.Elapsed.TotalSeconds, error = error.ToString() });
                    Console.WriteLine($"N{sampling}/{row.Id}: {error.Message}");
                }
                JsonFiles.Write(Path.Combine(folder, "status.json"), status);
            }
        }
        JsonFiles.Write(Path.Combine(folder, "identity.json"), new { coreSha256 = CoreHash, sampling, nativeReferenceBasis = "ConvergedNativeResult" });
    }
    internal static void Assess(string repository, string root)
    {
        var policy = Path.Combine(repository, "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json");
        var prep = JsonFiles.Read<JsonElement>(Path.Combine(root, "preparation.json"));
        if (Hash(policy) != prep.GetProperty("policySha256").GetString()) throw new InvalidDataException("Policy changed");
        var gate = ComparisonConfiguration.Load(policy).Analyses["Fourier MTF vs Field"].Quantities["Modulation"];
        var cohortPath = Path.Combine(root, "core-cohort.json");
        var cohort = File.Exists(cohortPath) ? JsonFiles.Read<JsonElement>(cohortPath).GetProperty("coreSha256").GetString()! : prep.GetProperty("coreSha256").GetString()!;
        var cases = JsonFiles.Read<Case[]>(Path.Combine(root, "cases.json")); var rows = new List<object>();
        Series1DResult Native(Case c, int sampling, int direction)
        {
            var folder = Path.Combine(root, "derived-curves", $"c{c.Configuration}-n{sampling}");
            var model = JsonFiles.Read<JsonElement>(Path.Combine(folder, "model.json"));
            var request = JsonFiles.Read<JsonElement>(Path.Combine(folder, "request.json"));
            if (!model.GetProperty("aperture").GetProperty("AFocalImageSpace").GetBoolean()
                || model.GetProperty("units").GetProperty("AfocalModeUnits").GetString() != "Milliradians"
                || request.GetProperty("configuration").GetInt32() != c.Configuration
                || request.GetProperty("inputSha256").GetString() != c.SourceSha256)
                throw new InvalidDataException("Native physical/settings readback changed: " + c.Id);
            double[] frequencies = [.1, .2, .4, .8, 1.6, 3.2];
            var curve = JsonFiles.Read<JsonElement>(Path.Combine(folder, "curve.json"));
            if (curve.GetProperty("provenance").GetString() != "NativeOperandsWithValidatedDisplayInterpolation"
                || curve.GetProperty("sampling").GetInt32() != sampling || curve.GetProperty("config").GetInt32() != c.Configuration
                || curve.GetProperty("rawNodesSha256").GetString() != Hash(Path.Combine(folder, "fourier-operands.json"))
                || curve.GetProperty("splineProofSha256").GetString() != Hash(Path.Combine(root, "native-display-proof.json")))
                throw new InvalidDataException("Normalized native reference provenance changed");
            var all = curve.GetProperty("dataSeries");
            if (all.GetArrayLength() != 6) throw new InvalidDataException("Expected six native frequencies");
            var series = all[Array.IndexOf(frequencies, c.Frequency)];
            return new("curve", "Native", series.GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray(),
                series.GetProperty("y").EnumerateArray().Select(v => v[direction].GetDouble()).ToArray(),
                new("NormalizedField", "Dimensionless"), new("Modulation", "Dimensionless"));
        }
        Series1DResult Core(Case c, int sampling, int direction)
        {
            var file = Path.Combine(root, "core-" + cohort[..12], "n" + sampling, c.Id + ".json");
            if (JsonFiles.Read<JsonElement>(Path.Combine(Path.GetDirectoryName(file)!, "identity.json")).GetProperty("coreSha256").GetString() != cohort)
                throw new InvalidDataException("Core cohort identity differs");
            var data = JsonFiles.Read<JsonElement>(file);
            if (data.GetProperty("outcome").GetString() != "Success") throw new InvalidDataException("Core unavailable");
            var points = data.GetProperty("seriesList")[direction].GetProperty("points").EnumerateArray().ToArray();
            return new("curve", "Core", points.Select(p => p.GetProperty("x").GetDouble()).ToArray(),
                points.Select(p => p.GetProperty("y").GetDouble()).ToArray(), new("NormalizedField", "Dimensionless"), new("Modulation", "Dimensionless"));
        }
        foreach (var c in cases) foreach (var direction in new[] { 0, 1 })
        {
            try
            {
                var low = Native(c, 128, direction); var high = Native(c, 256, direction);
                var current = Core(c, 32, direction); var refined = Core(c, 64, direction);
                ComparisonMetric Metric(string id, Series1DResult a, Series1DResult b) => ComparisonMetrics.Calculate(id, "Dimensionless", ComparisonMetrics.Align(a, b, []), gate);
                var nativeConvergence = Metric("Native128To256", low, high);
                var coreConvergence = Metric("Core32To64", current, refined);
                var result = Metric("Core32VsNative256", current, high);
                var aligned = ComparisonMetrics.Align(current, high, []);
                var violations = aligned.Workbench.Zip(aligned.Zemax, (w, z) => Math.Abs(w - z) > gate.Absolute + gate.Relative * Math.Abs(z)).Count(v => v);
                var conclusion = nativeConvergence.Conclusion == Conclusion.Pass && coreConvergence.Conclusion == Conclusion.Pass
                    ? result.Conclusion.ToString() : "ConvergenceNotEstablished";
                rows.Add(new { c.Id, c.Configuration, c.Frequency, direction = direction == 0 ? "T" : "S", conclusion,
                    nativeConvergence, coreConvergence, result, pointwiseViolations = violations, error = (string?)null });
                Console.WriteLine($"{c.Id}/{direction}: {conclusion}; native={nativeConvergence.Nrmse:R}; core={coreConvergence.Nrmse:R}; result={result.Nrmse:R}");
            }
            catch (Exception e) { rows.Add(new { c.Id, c.Configuration, c.Frequency, direction = direction == 0 ? "T" : "S", conclusion = "Incomplete", error = e.ToString() }); Console.WriteLine(c.Id + ": " + e.Message); }
        }
        JsonFiles.Write(Path.Combine(root, "assessment-" + cohort[..12] + ".json"), new
        {
            referenceBasis = "ConvergenceCheckedNativeResult", policySha256 = Hash(policy), gate, coreSha256 = cohort,
            referenceMethod = "NativeOperandsWithValidatedDisplayInterpolation",
            frequenciesCyclesPerMilliradian = new[] { .1, .2, .4, .8, 1.6 }, coreSampling = new[] { 32, 64 }, nativeSampling = new[] { 128, 256 }, rows
        });
    }
}
