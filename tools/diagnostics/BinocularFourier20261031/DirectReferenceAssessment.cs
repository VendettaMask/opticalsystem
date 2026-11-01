using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Diagnostics;
using OpticalSystemDesign.ZemaxComparison.Metrics;

internal static class DirectReferenceAssessment
{
    internal static void Run(string repository, string root)
    {
        var previous = Path.Combine(repository, "artifacts/validation/binocular-fourier-remaining-20261031");
        var policy = Path.Combine(repository, "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json");
        var prep = JsonFiles.Read<JsonElement>(Path.Combine(previous, "preparation.json"));
        if (FftFieldCaptureAudit.Hash(policy) != prep.GetProperty("policySha256").GetString()) throw new InvalidDataException("Original policy changed");
        var gate = ComparisonConfiguration.Load(policy).Analyses["Fourier MTF vs Field"].Quantities["Modulation"];
        double[] frequencies = [.1, .2, .4, .8, 1.6, 3.2];
        var cohorts = new List<(string Role, string Root, string Hash)>();
        var priorCore = JsonFiles.Read<JsonElement>(Path.Combine(previous, "core-cohort.json")).GetProperty("coreSha256").GetString()!;
        cohorts.Add(("PreviousConclusion", previous, priorCore));
        var currentCohort = Path.Combine(root, "core-cohort.json");
        if (File.Exists(currentCohort)) cohorts.Add(("CurrentRegression", root,
            JsonFiles.Read<JsonElement>(currentCohort).GetProperty("coreSha256").GetString()!));
        var rows = new List<object>(); var normalization = new List<object>(); var captures = new List<object>();
        for (var config = 1; config <= 2; config++)
        {
            var native = new Dictionary<int, FftFieldCapture>();
            foreach (var n in new[] { 128, 256 })
            {
                var id = $"c{config}-n{n}"; var jobs = Path.Combine(root, "direct-default");
                var inspection = FftFieldCaptureAudit.Inspect(jobs, id);
                if (inspection.Status != CaptureStatus.Captured) throw new InvalidDataException(inspection.ToString());
                var folder = Path.Combine(jobs, id);
                var request = JsonFiles.Read<JsonElement>(Path.Combine(folder, "captured-settings.json")).GetProperty("request");
                native[n] = FftFieldCaptureAudit.Read(folder, config, n, frequencies, request.GetProperty("inputSha256").GetString()!);
                captures.Add(new { config, sampling = n, inspection, dataSha256 = native[n].DataSha256 });
                var derivedPath = Path.Combine(previous, $"derived-curves/c{config}-n{n}/curve.json");
                var derived = JsonFiles.Read<JsonElement>(derivedPath).GetProperty("dataSeries");
                for (var f = 0; f < 6; f++) for (var d = 0; d < 2; d++)
                {
                    var direct = native[n].Curves[2 * f + d];
                    var old = direct with { X = derived[f].GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray(),
                        Y = derived[f].GetProperty("y").EnumerateArray().Select(v => v[d].GetDouble()).ToArray() };
                    var aligned = ComparisonMetrics.Align(old, direct, []);
                    var metric = ComparisonMetrics.Calculate("PreviousDerivedVsDirect", "Dimensionless", aligned, gate);
                    normalization.Add(new { config, sampling = n, frequency = frequencies[f], direction = d == 0 ? "T" : "S", metric,
                        derivedSha256 = FftFieldCaptureAudit.Hash(derivedPath), directSha256 = native[n].DataSha256 });
                }
            }
            foreach (var cohort in cohorts)
            for (var f = 0; f < (cohort.Role == "PreviousConclusion" ? 5 : 6); f++) for (var d = 0; d < 2; d++)
            {
                Series1DResult Core(int n)
                {
                    var id = $"c{config}-f{frequencies[f].ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', 'p')}";
                    var folder = Path.Combine(cohort.Root, "core-" + cohort.Hash[..12], "n" + n);
                    if (JsonFiles.Read<JsonElement>(Path.Combine(folder, "identity.json")).GetProperty("coreSha256").GetString() != cohort.Hash)
                        throw new InvalidDataException("Core cohort identity changed");
                    var data = JsonFiles.Read<JsonElement>(Path.Combine(folder, id + ".json"));
                    if (data.GetProperty("outcome").GetString() != "Success") throw new InvalidDataException("Core result unavailable");
                    var points = data.GetProperty("seriesList")[d].GetProperty("points").EnumerateArray().ToArray();
                    return native[256].Curves[2 * f + d] with { X = points.Select(p => p.GetProperty("x").GetDouble()).ToArray(),
                        Y = points.Select(p => p.GetProperty("y").GetDouble()).ToArray() };
                }
                var actual = Core(32); var reference = native[256].Curves[2 * f + d];
                ComparisonMetric Metric(string id, Series1DResult a, Series1DResult b) => ComparisonMetrics.Calculate(id, "Dimensionless", ComparisonMetrics.Align(a, b, []), gate);
                var nativeConvergence = Metric("Native128To256", native[128].Curves[2 * f + d], reference);
                var coreConvergence = Metric("Core32To64", actual, Core(64));
                var comparison = Metric("Core32VsDirectNative256", actual, reference);
                var aligned = ComparisonMetrics.Align(actual, reference, []);
                var violations = aligned.Workbench.Zip(aligned.Zemax, (a, b) => Math.Abs(a - b) > gate.Absolute + gate.Relative * Math.Abs(b)).Count(v => v);
                var conclusion = nativeConvergence.Conclusion == Conclusion.Pass && coreConvergence.Conclusion == Conclusion.Pass
                    ? comparison.Conclusion.ToString() : "ConvergenceNotEstablished";
                rows.Add(new { cohort.Role, coreSha256 = cohort.Hash, config, frequency = frequencies[f], direction = d == 0 ? "T" : "S",
                    conclusion, nativeConvergence, coreConvergence, comparison, pointwiseViolations = violations });
                Console.WriteLine($"{cohort.Role}/C{config}/F{frequencies[f]}/{d}: {conclusion}; {comparison.Nrmse:R}");
            }
        }
        var late = FftFieldCaptureAudit.Inspect(Path.Combine(previous, "live-buffer-control"), "full");
        var failed = FftFieldCaptureAudit.Inspect(Path.Combine(previous, "full-frequency-bounded"), "c1-n128");
        JsonFiles.Write(Path.Combine(root, "direct-assessment.json"), new { referenceBasis = "ConvergenceCheckedDirectNativeBuffers",
            policySha256 = FftFieldCaptureAudit.Hash(policy), gate, captures, lateCompletionCorrection = late, genuineTimeout = failed,
            normalization, rows, productOpticalMathChanged = false });
    }
}
