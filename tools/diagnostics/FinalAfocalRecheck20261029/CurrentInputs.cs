using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;

internal static class CurrentInputs
{
    internal static void Prepare(string repository, string root)
    {
        if (File.Exists(Path.Combine(root, "items.json"))) throw new IOException("Never overwrite a prepared run");
        Directory.CreateDirectory(root);
        var ledgerPath = Path.Combine(repository, "validation/rechecks/2026-10-29/final-core-170/afocal-comparison.json");
        var ledger = JsonFiles.Read<JsonElement>(ledgerPath);
        var originalPath = Path.Combine(repository, "validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip");
        if (Hash(originalPath) != "235f113b4633fce7341e837da03850ac3c8cff14a48c32f4b4731e7250d7f4b1") throw new InvalidDataException("Original reference changed");
        using var original = ZipFile.OpenRead(originalPath);
        var identities = ledger.GetProperty("inputIdentity").GetProperty("files").EnumerateArray().ToArray();
        var restored = new List<object>();
        string Restore(JsonElement item)
        {
            var member = item.GetProperty("archiveMember").GetString()!;
            var path = Path.GetFullPath(Path.Combine(root, item.GetProperty("path").GetString()!));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Input path escaped run");
            var entry = original.GetEntry(member) ?? throw new InvalidDataException("Missing frozen input: " + member);
            var expected = item.GetProperty("sha256").GetString();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var input = entry.Open()) using (var output = File.Create(path)) input.CopyTo(output);
            if (Hash(path) != expected) throw new InvalidDataException("Input identity changed: " + member);
            restored.Add(new { path = Path.GetRelativePath(root, path), archiveMember = member, sha256 = expected });
            return path;
        }
        foreach (var item in identities) Restore(item);
        string Materialized(JsonElement item) => Path.Combine(root, item.GetProperty("path").GetString()!);
        var policy = Materialized(identities.Single(i => i.GetProperty("archiveMember").GetString() == "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json"));
        File.Copy(policy, Path.Combine(root, "runner/comparison-settings.json"), true);
        var angular = ledger.GetProperty("cases").EnumerateArray().First(c => c.GetProperty("analysis").GetString() == "Spot Diagram")
            .GetProperty("effectiveTolerances").GetProperty("RmsSpotRadius").Deserialize<Tolerances>(JsonFiles.Options)!;
        var angularPath = Path.Combine(root, "inputs/angular-policy.json"); JsonFiles.Write(angularPath, angular);
        var archiveCache = new Dictionary<string, ZipArchive>();
        var rows = new List<Row>();
        try
        {
            foreach (var c in ledger.GetProperty("cases").EnumerateArray())
            {
                var id = c.GetProperty("id").GetString()!; var set = c.GetProperty("set").GetString()!;
                var request = c.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
                if (request.Fingerprint != c.GetProperty("requestFingerprint").GetString()) throw new InvalidDataException("Request changed: " + id);
                var snapshot = Materialized(identities.First(i => i.GetProperty("sha256").GetString() == c.GetProperty("snapshotSha256").GetString()));
                var native = Path.GetDirectoryName(Materialized(identities.First(i => i.GetProperty("archiveMember").GetString()!.EndsWith("/" + id + "/native/request.json")) ))!;
                var lens = c.GetProperty("lens").GetString()!;
                var source = Path.Combine(root, "inputs/sources", lens);
                Directory.CreateDirectory(Path.GetDirectoryName(source)!);
                File.Copy(Path.Combine(repository, "validation/zemax/2026-r1/afocal-repair-2026-10-24/captures/sources", lens), source, true);
                var sourceHash = Hash(source);
                if (sourceHash != c.GetProperty("sourceSha256").GetString()) throw new InvalidDataException("Source changed: " + id);
                string? baseline = null;
                var data = ledger.GetProperty("resultStorage").EnumerateArray().FirstOrDefault(s =>
                    s.GetProperty("dataKind").GetString() == "core-data" && s.GetProperty("path").GetString()!.Contains("/" + id + "/"));
                if (data.ValueKind != JsonValueKind.Undefined)
                {
                    var storage = data.GetProperty("storage");
                    var localArchive = storage.GetProperty("kind").GetString() == "this-archive";
                    var archive = localArchive ? "validation/rechecks/2026-10-29/final-core-170/evidence.zip"
                        : storage.GetProperty("archive").GetString()!;
                    if (!archiveCache.TryGetValue(archive, out var zip))
                    {
                        var path = Path.Combine(repository, archive);
                        var expectedArchive = localArchive ? ledger.GetProperty("evidenceArchiveSha256").GetString()
                            : storage.GetProperty("archiveSha256").GetString();
                        if (Hash(path) != expectedArchive) throw new InvalidDataException("Baseline archive changed");
                        archiveCache.Add(archive, zip = ZipFile.OpenRead(path));
                    }
                    baseline = Path.Combine(root, "baseline-data", set, id, "data.json"); Directory.CreateDirectory(Path.GetDirectoryName(baseline)!);
                    using (var input = zip.GetEntry(storage.GetProperty("member").GetString()!)!.Open()) using (var output = File.Create(baseline)) input.CopyTo(output);
                    if (Hash(baseline) != data.GetProperty("sha256").GetString()) throw new InvalidDataException("Baseline output changed");
                }
                rows.Add(new(set, id, lens, c.GetProperty("configuration").GetInt32(), c.GetProperty("analysis").GetString()!,
                    snapshot, native, source, sourceHash, baseline, c.GetProperty("conclusion").GetString()!, request.Fingerprint,
                    c.GetProperty("effectiveTolerances").Deserialize<Dictionary<string, Tolerances>>(JsonFiles.Options)!, request));
            }
        }
        finally { foreach (var archive in archiveCache.Values) archive.Dispose(); }
        if (rows.Count != 170 || rows.Select(r => r.Id).Distinct().Count() != 170) throw new InvalidDataException("Incomplete original scope");
        JsonFiles.Write(Path.Combine(root, "items.json"), rows);
        JsonFiles.Write(Path.Combine(root, "input-identity.json"), new
        {
            expectedCoreSha256 = Hash(typeof(Optic).Assembly.Location), policyPath = policy, angularPolicyPath = angularPath,
            ledgerPath, ledgerSha256 = Hash(ledgerPath), originalArchive = originalPath, originalArchiveSha256 = Hash(originalPath), files = restored,
            allOriginalRequestsAndGatesPreserved = true, backgroundOnly = true,
            binaries = Directory.GetFiles(Path.Combine(root, "runner"), "*.dll").Order().Select(p => new { file = Path.GetFileName(p), sha256 = Hash(p) }).ToArray()
        });
        Console.WriteLine($"Prepared {rows.Count} original requests; Core {Hash(typeof(Optic).Assembly.Location)}");
    }
    private static string Hash(string path) { using var s = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(s)); }
}
