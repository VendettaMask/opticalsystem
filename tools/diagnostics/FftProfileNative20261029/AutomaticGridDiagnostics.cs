using System.IO.Compression;
using System.Reflection;
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

internal static class AutomaticGridDiagnostics
{
    internal static int Run(string repository, string output)
    {
        if (Directory.Exists(output)) throw new IOException("Use a fresh diagnostic output");
        Directory.CreateDirectory(output);
        var manifestPath = Path.Combine(repository, "validation/zemax/2026-r1/afocal-psf-contract-2026-10-28/manifest.json");
        var manifest = JsonFiles.Read<JsonElement>(manifestPath);
        var archivePath = Path.Combine(repository, manifest.GetProperty("sourceArchive").GetString()!);
        if (Hash(archivePath) != manifest.GetProperty("sourceArchiveSha256").GetString()) throw new InvalidDataException("Frozen archive changed");
        using var zip = ZipFile.OpenRead(archivePath);
        byte[] Read(string path)
        {
            using var input = zip.GetEntry(path)!.Open();
            using var bytes = new MemoryStream(); input.CopyTo(bytes); return bytes.ToArray();
        }
        var entries = manifest.GetProperty("entries").EnumerateArray().ToDictionary(e => e.GetProperty("path").GetString()!, StringComparer.Ordinal);
        var ledger = JsonFiles.Read<JsonElement>(Path.Combine(repository, "validation/rechecks/2026-10-29/final-core-170/afocal-comparison.json"));
        var support = typeof(Optic).Assembly.GetType("OpticalSystemDesign.Core.Analysis.ImageSpaceAnalysisSupport")!;
        var rows = new List<object>();
        foreach (var item in manifest.GetProperty("cases").EnumerateArray())
        {
            var id = item.GetProperty("id").GetString()!;
            Console.WriteLine("Recalculating " + id);
            var request = item.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            var model = Regex.Match(id, @"(?:^|-)\d{4}-(?<model>.+?)-primary-").Groups["model"].Value;
            if (model.Length == 0) throw new InvalidDataException("Unknown original model identity");
            var snapshot = Read("artifacts/validation/afocal-recompare-20261026/baseline-v4/models/" + model + ".json");
            var optic = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(snapshot, JsonFiles.Options)!);
            var directory = Path.Combine(output, id); Directory.CreateDirectory(directory);
            foreach (var name in new[] { "data.json", "model.json", "captured-settings.json" })
            {
                var entry = entries[id + "/" + name]; var bytes = Read(entry.GetProperty("sourcePath").GetString()!);
                if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != entry.GetProperty("sha256").GetString()) throw new InvalidDataException("Native member changed");
                File.WriteAllBytes(Path.Combine(directory, name), bytes);
            }
            var key = request.CanonicalAnalysisKey;
            var data = new WorkbenchRuntime(optic).BuildAnalysisData(key, request.WorkbenchSettings);
            JsonFiles.Write(Path.Combine(directory, "workbench.json"), data);
            var entryType = AnalysisComparisonRegistry.Get(key);
            var actual = ResultNormalizer.Workbench(Path.Combine(directory, "workbench.json"), entryType, request).Grids.Single();
            var expected = ResultNormalizer.Zemax(Path.Combine(directory, "data.json"), entryType, request).Grids.Single();
            var xDifference = actual.X.Zip(expected.X, (a, b) => Math.Abs(a - b)).Max();
            var yDifference = actual.Y.Zip(expected.Y, (a, b) => Math.Abs(a - b)).Max();
            var xGateRatio = actual.X.Zip(expected.X, (a, b) => Math.Abs(a - b) / (1e-8 * Math.Max(1, Math.Abs(b)))).Max();
            var yGateRatio = actual.Y.Zip(expected.Y, (a, b) => Math.Abs(a - b) / (1e-8 * Math.Max(1, Math.Abs(b)))).Max();
            var corePitch = actual.X[1] - actual.X[0]; var nativePitch = expected.X[1] - expected.X[0];
            var diameter = (double)support.GetMethod("AfocalDiffractionPupilDiameterMillimeters", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [optic])!;
            var original = ledger.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("id").GetString() == id);
            var gate = original.GetProperty("effectiveTolerances").GetProperty("Irradiance").Deserialize<Tolerances>(JsonFiles.Options)!;
            ComparisonMetric? metric = null; int? violations = null; string? failure = null;
            try
            {
                var matched = ComparisonMetrics.Align(actual, expected, []);
                metric = ComparisonMetrics.Calculate("grid", "Dimensionless", matched, gate);
                violations = matched.Workbench.Zip(matched.Zemax, (a, b) => Math.Abs(a - b) > gate.Absolute + gate.Relative * Math.Abs(b)).Count(v => v);
            }
            catch (InvalidDataException ex) { failure = ex.Message; }
            rows.Add(new { id, key, request.Fingerprint, request.ImageDeltaMicrometers,
                snapshotSha256 = Convert.ToHexStringLower(SHA256.HashData(snapshot)),
                xCount = actual.X.Length, yCount = actual.Y.Length, nativeXCount = expected.X.Length, nativeYCount = expected.Y.Length,
                xDifference, yDifference, xGateRatio, yGateRatio, corePitch, nativePitch, coreDiameter = diameter,
                diagnosticDiameterFromPitchRatio = request.ImageDeltaMicrometers == 0 ? (double?)(diameter * corePitch / nativePitch) : null,
                inferredDiameterIsProductInput = false, failure, violations, metric });
            JsonFiles.Write(Path.Combine(output, "summary.json"), rows);
        }
        JsonFiles.Write(Path.Combine(output, "identity.json"), new { manifestSha256 = Hash(manifestPath), archiveSha256 = Hash(archivePath), coreSha256 = Hash(typeof(Optic).Assembly.Location), originalRequestsAndGatesChanged = false });
        Console.WriteLine("Completed " + rows.Count + " unchanged grid requests");
        return 0;
    }
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
