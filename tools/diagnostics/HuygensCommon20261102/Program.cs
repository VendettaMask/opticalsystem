using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.ZemaxComparison.Zemax;

var mode = args[0]; var repo = Path.GetFullPath(args[1]); var output = Path.GetFullPath(args[2]);
Directory.CreateDirectory(output);
string Hash(string p) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)));
var coreHash = Hash(typeof(DiffractionEngine).Assembly.Location);
var source = Path.Combine(repo, "src/OpticalSystemDesign.App/Assets/SequentialExamples/Optical testing/Twyman Green Interferometer.zmx");
var imported = await WorkbenchExecutor.Import(source);
if (mode == "illumination") { await IlluminationControl.Run(repo, output, source, imported.Configurations[0], coreHash); return; }
if (mode == "probe")
{
    var results = new List<object>();
    foreach (var sampling in new[] { 256, 512, 1024 })
    {
        var optic = imported.Configurations[0]; var timer = Stopwatch.StartNew();
        var psf = MtfMethodEvaluator.ComputeHuygensPolychromaticPsf(optic, (0, 0), [optic.Wavelengths[0]],
            new(PupilSampling: sampling, ImageSize: 32, PixelPitchMillimeters: .00025, UseZemaxHuygensSemantics: true));
        var mtf = MtfMethodEvaluator.SampleHuygensMtf(DiffractionEngine.ComputePsfMtf(psf, true), 64, 50, true);
        var item = new { sampling, seconds = timer.Elapsed.TotalSeconds, mtf = new { mtf.Tangential, mtf.Sagittal }, checks = psf.HuygensFourierChecks,
            values = Enumerable.Range(0, 32).Select(y => Enumerable.Range(0, 32).Select(x => psf.Values[y, x]).ToArray()).ToArray(), coreHash };
        results.Add(item); JsonFiles.Write(Path.Combine(output, "results.json"), results);
        Console.WriteLine($"Pupil {sampling}: {timer.Elapsed.TotalSeconds:F2}s, MTF {mtf}, {psf.HuygensFourierChecks?.SingleOrDefault()}");
    }
    return;
}
if (mode is "replay" or "psf")
{
    var rows = JsonFiles.Read<Row[]>(Path.Combine(repo, "artifacts/validation/all-example-accuracy-20261031/rows.json"));
    using var lease = new FileStream(Path.Combine(repo, "artifacts/validation/.background-small-native-controls.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    var native = await ZemaxExecutor.Build(ZemaxExecutor.Locate(null), output, CancellationToken.None);
    using var archive = new ArchivedEvidence(repo);
    var records = new List<object>();
    foreach (var index in mode == "replay" ? new[] { 2165, 2187 } : new[] { 2158, 2160, 2162 })
    {
        var row = rows.Single(r => r.Index == index); var optic = imported.Configurations[row.Configuration - 1];
        var snapshot = Path.Combine(output, index.ToString(), "source.optic.json"); JsonFiles.Write(snapshot, optic.ToSnapshot());
        CanonicalAnalysisRequest? previousRequest = null; string? previousCore = null; string? previousNative = null;
        foreach (var sampling in mode == "replay" ? new[] { 128, 256, 512, 1024 } : new[] { 512, 1024 })
        {
            var request = row.Run.Request!;
            var wb = new Dictionary<string, string>(request.WorkbenchSettings);
            foreach (var key in new[] { "PupilSampling", "Sampling", "NumRays" }) if (wb.ContainsKey(key)) wb[key] = sampling.ToString();
            request = request with { PupilSampling = sampling, SurfaceCount = optic.SurfaceGroup.Items.Count, WorkbenchSettings = wb,
                ZemaxSettings = new Dictionary<string, object>(request.ZemaxSettings) { ["PupilSampleSize"] = $"S_{sampling}x{sampling}" } };
            var folder = Path.Combine(output, index.ToString(), sampling.ToString()); var timer = Stopwatch.StartNew();
            var job = new WorkbenchJob(snapshot, Path.Combine(folder, "core"), request); JsonFiles.Write(Path.Combine(folder, "request.json"), job);
            WorkbenchExecutor.Execute(job); Console.WriteLine($"Core {index}/{sampling}: {timer.Elapsed.TotalSeconds:F1}s");
            string nativeFolder;
            if (sampling <= 256) nativeFolder = archive.Materialize($"convergence-virtual/{index}/{sampling}/native");
            else
            {
                var saved = Path.Combine(repo, "validation/rechecks/2026-11-02/huygens-common/convergence-first", index.ToString(), sampling.ToString(), "native");
                if (mode == "replay" && File.Exists(Path.Combine(saved, "data.json"))) nativeFolder = saved;
                else
                {
                var processes = Process.GetProcessesByName("OpticStudio"); var count = processes.Length; foreach (var p in processes) p.Dispose();
                if (count > 1) throw new InvalidOperationException("Independent native slot unavailable.");
                nativeFolder = Path.Combine(folder, "native");
                var entry = AnalysisComparisonRegistry.Get(row.Key);
                var result = await native.Capture(source, nativeFolder, "2026 R1", entry.ZemaxSettingsMapper, entry.ZemaxAnalysisType, request, row.Configuration, 120, false, CancellationToken.None);
                JsonFiles.Write(Path.Combine(folder, "execution.json"), new { result, coreHash, inputSha256 = Hash(source), screenshots = false });
                if (result.ExitCode != 0) throw new InvalidOperationException("Native capture failed; failure retained.");
                }
            }
            Compare("same-sampling", Path.Combine(folder, "core"), false, request, nativeFolder, true, request);
            if (previousRequest is not null)
            {
                Compare("core-convergence", previousCore!, false, previousRequest, Path.Combine(folder, "core"), false, request);
                Compare("native-convergence", previousNative!, true, previousRequest, nativeFolder, true, request);
            }
            previousRequest = request; previousCore = Path.Combine(folder, "core"); previousNative = nativeFolder;
            void Compare(string kind, string a, bool na, CanonicalAnalysisRequest ra, string b, bool nb, CanonicalAnalysisRequest rb)
            {
                var entry = AnalysisComparisonRegistry.Get(row.Key);
                var x = na ? ResultNormalizer.Zemax(Path.Combine(a, "data.json"), entry, ra) : ResultNormalizer.Workbench(Path.Combine(a, "data.json"), entry, ra);
                var y = nb ? ResultNormalizer.Zemax(Path.Combine(b, "data.json"), entry, rb) : ResultNormalizer.Workbench(Path.Combine(b, "data.json"), entry, rb);
                var comparisonFolder = Path.Combine(output, "comparisons", $"{index}-{sampling}-{kind}");
                var run = new AnalysisRun { Key = row.Key, Directory = comparisonFolder, Request = rb, Tolerances = row.Run.Tolerances, Support = row.Run.Support };
                ComparisonRunner.Compare(run, entry, row.Settings, x, y, comparisonFolder);
                JsonFiles.Write(Path.Combine(comparisonFolder, "comparison.json"), run);
                records.Add(new { index, sampling, kind, run.Conclusion, run.Metrics, coreHash, a, b });
                JsonFiles.Write(Path.Combine(output, "results.json"), records); Console.WriteLine($"{index}/{sampling} {kind}: {run.Conclusion}");
            }
        }
    }
    return;
}
throw new ArgumentException("Unknown diagnostic mode");

internal sealed record Row(int Index, string Key, string Lens, int Configuration, AnalysisConfiguration Settings, AnalysisRun Run);
internal sealed class ArchivedEvidence : IDisposable
{
    private readonly ZipArchive zip; private readonly JsonElement[] entries; private readonly string cache;
    internal ArchivedEvidence(string repo)
    {
        var root = Path.Combine(repo, "validation/rechecks/2026-11-01/testing-three");
        zip = ZipFile.OpenRead(Path.Combine(root, "evidence.zip")); entries = JsonFiles.Read<JsonElement[]>(Path.Combine(root, "archive-index.json"));
        cache = Path.Combine(repo, "artifacts/validation/huygens-common-reference-cache");
    }
    internal string Materialize(string prefix)
    {
        var folder = Path.Combine(cache, prefix);
        foreach (var item in entries.Where(e => e.GetProperty("logicalPath").GetString()!.StartsWith(prefix + "/")
            && Path.GetExtension(e.GetProperty("logicalPath").GetString()) is ".json" or ".txt"))
        {
            var name = item.GetProperty("logicalPath").GetString()!; var hash = item.GetProperty("sha256").GetString()!;
            var path = Path.Combine(cache, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path)) { using var input = zip.GetEntry("payloads/" + hash)!.Open(); using var target = File.Create(path); input.CopyTo(target); }
            if (Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) != hash) throw new InvalidDataException("Archived reference changed.");
        }
        if (!File.Exists(Path.Combine(folder, "data.json"))) throw new FileNotFoundException(prefix);
        return folder;
    }
    public void Dispose() => zip.Dispose();
}
