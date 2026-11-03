using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;

if (args.Length != 3) throw new ArgumentException("psf|rays <repository> <fresh-output>");
var repo = Path.GetFullPath(args[1]); var root = Path.GetFullPath(args[2]);
if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException("Fresh output required");
Directory.CreateDirectory(root);
string Hash(string path) { using var s = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(s)); }
var coreHash = Hash(typeof(Optic).Assembly.Location);
if (args[0] == "toroid-native") { await ToroidNativeControls.Run(repo, root, coreHash); return; }
if (args[0] == "rays") { await FixedRayControls.Run(repo, root, coreHash); return; }
if (args[0] != "psf") throw new ArgumentException("Unknown mode");
var archivePath = Path.Combine(repo, "validation/rechecks/2026-11-01/example-incomparable-repair/accepted-evidence.zip");
if (Hash(archivePath) != "4c8bb49a9546f4bfcd076f0f46c7f2fc203b04450f047f1008a7d0c1f37408cd") throw new InvalidDataException("Native authority changed");
using var archive = ZipFile.OpenRead(archivePath);
var manifest = JsonFiles.Read<JsonElement>(Path.Combine(repo, "validation/rechecks/2026-11-01/example-incomparable-repair/manifest.json"))
    .GetProperty("entries").EnumerateArray().ToDictionary(e => e.GetProperty("path").GetString()!);
var verified = new List<object>();
string Restore(string member)
{
    var e = archive.GetEntry(member) ?? throw new InvalidDataException("Missing archived input: " + member);
    var target = Path.GetFullPath(Path.Combine(root, "inputs", member));
    if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Input escaped root");
    if (File.Exists(target)) return target;
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    using (var input = e.Open()) using (var output = File.Create(target)) input.CopyTo(output);
    var sha = Hash(target); var expected = manifest[member];
    if (sha != expected.GetProperty("sha256").GetString() || e.Length != expected.GetProperty("bytes").GetInt64()) throw new InvalidDataException("Archived input changed");
    verified.Add(new { member, sha256 = sha, bytes = e.Length }); return target;
}
var rows = JsonFiles.Read<ReplayRow[]>(Path.Combine(repo, "artifacts/validation/all-example-accuracy-20261031/rows.json"));
var results = new List<object>();
foreach (var index in new[] { 161, 191, 221, 243, 265, 287 })
{
    var row = rows.Single(r => r.Index == index);
    var source = Path.Combine(repo, "src/OpticalSystemDesign.App/Assets/SequentialExamples", row.Lens);
    var sourceHash = Hash(source); var optic = (await WorkbenchExecutor.Import(source)).Configurations[row.Configuration - 1];
    var snapshot = Path.Combine(root, "models", index + ".optic.json"); JsonFiles.Write(snapshot, optic.ToSnapshot());
    var requests = new Dictionary<int, CanonicalAnalysisRequest>();
    var folders = new Dictionary<int, string>();
    var native = new Dictionary<int, string>();
    foreach (var n in new[] { 256, 512 })
    {
        var nativeMember = manifest.Keys.Single(k => k.EndsWith($"/psf-{index}-{n}/data.json", StringComparison.Ordinal));
        native[n] = Restore(nativeMember);
        var captured = Restore(nativeMember[..^"data.json".Length] + "captured-settings.json");
        var capturedRequest = JsonFiles.Read<JsonElement>(captured).GetProperty("request");
        var expectedSource = capturedRequest.GetProperty("inputSha256").GetString();
        if (sourceHash != expectedSource) throw new InvalidDataException("Original/native source identity mismatch");
        var originalJobPath = Restore($"workbench/core-{index}-{n}.request.json");
        var originalJob = JsonFiles.Read<WorkbenchJob>(originalJobPath);
        if (originalJob.Request.Surface >= 0) throw new InvalidDataException("Explicit surface requires a mapping");
        var request = originalJob.Request with { SurfaceCount = optic.SurfaceGroup.Items.Count };
        var folder = Path.Combine(root, "workbench", $"{index}-{n}"); folders[n] = folder; requests[n] = request;
        JsonFiles.Write(Path.Combine(folder, "request.json"), new WorkbenchJob(snapshot, folder, request));
        WorkbenchExecutor.Execute(new(snapshot, folder, request));
    }
    var entry = AnalysisComparisonRegistry.Get(row.Key);
    var z256 = ResultNormalizer.Zemax(native[256], entry, requests[256]);
    var z512 = ResultNormalizer.Zemax(native[512], entry, requests[512]);
    var w256 = ResultNormalizer.Workbench(Path.Combine(folders[256], "data.json"), entry, requests[256]);
    var w512 = ResultNormalizer.Workbench(Path.Combine(folders[512], "data.json"), entry, requests[512]);
    foreach (var comparison in new[] { ("core256-native512", w256, z512), ("core512-native512", w512, z512),
        ("core256-core512", w256, w512), ("native256-native512", z256, z512) })
    {
        var output = Path.Combine(root, "comparisons", index + "-" + comparison.Item1);
        var run = new AnalysisRun { Key = row.Key, Request = requests[512], Directory = output };
        ComparisonRunner.Compare(run, entry, row.Settings, comparison.Item2, comparison.Item3, output);
        JsonFiles.Write(Path.Combine(output, "comparison.json"), run);
        results.Add(new { index, row.Lens, kind = comparison.Item1, run.Conclusion, run.Reason, run.Metrics,
            coreSha256 = coreHash, sourceSha256 = sourceHash, corePupilSampling = new[] { 256, 512 }, nativePupilSampling = new[] { 256, 512 },
            reference = archivePath, originalInvalidRequestPromoted = false });
        Console.WriteLine($"{index} {comparison.Item1}: {run.Conclusion}");
    }
    JsonFiles.Write(Path.Combine(root, "results.json"), results);
}
if (Hash(typeof(Optic).Assembly.Location) != coreHash) throw new InvalidDataException("Core changed during execution");
JsonFiles.Write(Path.Combine(root, "identity.json"), new { coreSha256 = coreHash, referenceArchive = archivePath,
    verifiedInputs = verified, checks = results.Count, newNativeCapture = false, completedUtc = DateTimeOffset.UtcNow });

internal sealed record ReplayRow(int Index, string Lens, int Configuration, string Key, AnalysisConfiguration Settings);
