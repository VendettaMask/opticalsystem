using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;

internal static class CentroidEnergyDiagnostics
{
    internal static int Run(string repository, string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required");
        Directory.CreateDirectory(output);
        var archivePath = Path.Combine(repository, "validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip");
        if (Hash(archivePath) != "235f113b4633fce7341e837da03850ac3c8cff14a48c32f4b4731e7250d7f4b1") throw new InvalidDataException("Frozen source changed");
        using var zip = ZipFile.OpenRead(archivePath);
        byte[] Read(string name)
        {
            using var input = zip.GetEntry(name)!.Open(); using var stream = new MemoryStream(); input.CopyTo(stream); return stream.ToArray();
        }
        var ledgerPath = Path.Combine(repository, "validation/rechecks/2026-10-29/final-core-170/afocal-comparison.json");
        var ledger = JsonFiles.Read<JsonElement>(ledgerPath);
        var entry = AnalysisComparisonRegistry.Get("Encircled Energy");
        var originalSettings = ComparisonConfiguration.Load(Path.Combine(repository, "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json")).Analyses["Encircled Energy"];
        var rows = new List<object>();
        foreach (var item in ledger.GetProperty("cases").EnumerateArray().Where(c => c.GetProperty("request").GetProperty("canonicalAnalysisKey").GetString() == "Encircled Energy"))
        {
            var id = item.GetProperty("id").GetString()!;
            var request = item.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            var model = Regex.Match(id, @"^\d{4}-(?<model>.+?)-primary-").Groups["model"].Value;
            var snapshot = Read("artifacts/validation/afocal-recompare-20261026/baseline-v4/models/" + model + ".json");
            var optic = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(snapshot, JsonFiles.Options)!);
            var directory = Path.Combine(output, id); Directory.CreateDirectory(directory);
            var nativePrefix = "artifacts/validation/afocal-recompare-20261026/expanded/" + id + "/native/";
            foreach (var name in new[] { "data.json", "data.txt", "model.json", "request.json", "captured-settings.json" })
                File.WriteAllBytes(Path.Combine(directory, name), Read(nativePrefix + name));
            var data = new WorkbenchRuntime(optic).BuildAnalysisData("Encircled Energy", request.WorkbenchSettings);
            var workbench = Path.Combine(directory, "workbench.json"); JsonFiles.Write(workbench, data);
            var actual = ResultNormalizer.Workbench(workbench, entry, request);
            var expected = ResultNormalizer.Zemax(Path.Combine(directory, "data.json"), entry, request);
            var gates = item.GetProperty("effectiveTolerances").Deserialize<Dictionary<string, Tolerances>>(JsonFiles.Options)!;
            var settings = originalSettings with { Quantities = gates, AfocalQuantities = gates };
            var run = new AnalysisRun { Key = "Encircled Energy", Directory = id, Request = request };
            ComparisonRunner.Compare(run, entry, settings, actual, expected, directory);
            var curves = new List<object>();
            foreach (var curve in expected.Series)
            {
                var matched = ComparisonMetrics.Align(actual.Series.Single(s => s.Id == curve.Id), curve, []);
                var gate = gates[curve.YAxis.Quantity];
                var violations = matched.Workbench.Zip(matched.Zemax, (a, b) => Math.Abs(a - b) > gate.Absolute + gate.Relative * Math.Abs(b)).Count(v => v);
                curves.Add(new { curve.Id, curve.YAxis.Unit, gate, count = matched.Workbench.Length, violations,
                    metric = ComparisonMetrics.Calculate(curve.Id, curve.YAxis.Unit, matched, gate) });
            }
            rows.Add(new { id, request.Fingerprint, originalConclusion = item.GetProperty("conclusion").GetString(), currentConclusion = run.Conclusion,
                snapshotSha256 = Convert.ToHexStringLower(SHA256.HashData(snapshot)), nativeDataSha256 = Hash(Path.Combine(directory, "data.json")), curves });
            JsonFiles.Write(Path.Combine(output, "summary.json"), rows);
            Console.WriteLine(id + ": " + run.Conclusion);
        }
        JsonFiles.Write(Path.Combine(output, "identity.json"), new { coreSha256 = Hash(typeof(Optic).Assembly.Location), archiveSha256 = Hash(archivePath), ledgerSha256 = Hash(ledgerPath), originalInputsAndGatesChanged = false });
        return 0;
    }
    private static string Hash(string p) { using var s = File.OpenRead(p); return Convert.ToHexStringLower(SHA256.HashData(s)); }
}
