using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Workbench;

if (args.Length != 3) throw new ArgumentException("rays|compare <repository> <study>");
var repository = Path.GetFullPath(args[1]);
var study = Path.GetFullPath(args[2]);
Directory.CreateDirectory(study);
var baseline = Path.Combine(repository, "artifacts/validation/model-capabilities-stage5-20261101");
var rows = JsonFiles.Read<JsonElement[]>(Path.Combine(baseline, "rows.json"));
var records = new List<object>();
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
var originalCore = JsonFiles.Read<JsonElement>(Path.Combine(baseline, "identity.json")).GetProperty("coreSha256").GetString();
if (args[0] == "grid-controls") { await GridControls.Run(study); return; }
if (args[0] == "grid-ray-controls") { await GridControls.Run(study, includeWavefront: false); return; }
if (args[0] == "grid-entrance-index") { await GridControls.EntranceIndex(study); return; }
if (args[0] == "grid-fft-projection") { await FftProjectionStudy.Run(repository, study); return; }
if (args[0] == "grid-energy-quadrature") { await EnergyQuadratureStudy.Run(repository, study); return; }
if (args[0] == "grid-fft-reference") { await FftReferenceStudy.Run(repository, study); return; }
if (args[0] == "grid-transverse-chief") { await TransverseControls.Run(study); return; }
if (args[0] == "grid-small-native-controls") { await SmallNativeControlLane.Run(repository, study); return; }
if (args[0] == "grid-index-profiles") { await GridIndexProfileStudy.Run(study); return; }
if (args[0] == "grid-window-candidates") { await GridWindowCandidates.Run(study); return; }
if (args[0] == "grid-rule-rays") { await GridRuleRayControls.Run(study); return; }
if (args[0] == "grid-tensor-windows") { await GridTensorWindows.Run(study); return; }
if (args[0] == "projected-grid") { await GridControls.ProjectedGrid(study); return; }
if (args[0] == "directional-grid") { await GridControls.ProjectedGrid(study, true); return; }
if (args[0] == "native-sphere") { NativeSphereRules.Run(study); return; }
if (args[0] == "pupil-pencil") { await GridControls.Pencil(study); return; }
if (args[0] == "grid-paraxial")
{
    var control = JsonFiles.Read<JsonElement>(Path.Combine(study, "controls.request.json"));
    var optic = await new OpticalSystemDesign.Core.FileIO.ZemaxZmxImporter().ImportFileAsync(control.GetProperty("input").GetString()!);
    var sphere = OpticalSystemDesign.Core.Analysis.WavefrontEngine.CreateChiefRayReferenceSphere(optic, (0, 0), optic.Wavelengths[1], false);
    var geometric = OpticalSystemDesign.Core.Analysis.ExitPupilGeometryEngine.Compute(optic, (0, 0), optic.Wavelengths[1], new[] { (0d, 0d), (.01, 0d), (0d, .01) });
    var result = new { exitPupil = optic.Paraxial.EstimateExitPupilLocation(.55), entranceDiameter = optic.Paraxial.EstimateEntrancePupilDiameter(),
        exitDiameter = optic.Paraxial.EstimateExitPupilDiameter(.55), effectiveFocalLength = optic.Paraxial.EstimateEffectiveFocalLength(), sphere, geometric };
    JsonFiles.Write(Path.Combine(study, "canonical-momentum-paraxial.json"), result);
    Console.WriteLine(JsonSerializer.Serialize(result)); return;
}
if (args[0] == "execute")
{
    foreach (var path in JsonFiles.Read<string[]>(Path.Combine(study, "workbench-jobs.json")))
    {
        var job = JsonFiles.Read<WorkbenchJob>(path);
        if (File.Exists(Path.Combine(job.OutputDirectory, "data.json"))) throw new IOException("Completed Core result must not be overwritten");
        Directory.CreateDirectory(job.OutputDirectory);
        WorkbenchExecutor.Execute(job);
        Console.WriteLine(job.OutputDirectory);
    }
    return;
}
if (args[0] == "core-convergence")
{
    foreach (var pair in JsonFiles.Read<CoreConvergenceJob[]>(Path.Combine(study, "core-convergence-jobs.json")))
    {
        var row = rows.Single(r => r.GetProperty("index").GetInt32() == pair.Index);
        var key = row.GetProperty("key").GetString()!; var entry = AnalysisComparisonRegistry.Get(key);
        var a = JsonFiles.Read<WorkbenchJob>(pair.RequestA); var b = JsonFiles.Read<WorkbenchJob>(pair.RequestB);
        var pa = JsonFiles.Read<JsonElement>(Path.Combine(a.OutputDirectory, "provenance.json"));
        var pb = JsonFiles.Read<JsonElement>(Path.Combine(b.OutputDirectory, "provenance.json"));
        if (pa.GetProperty("codeFingerprint").GetString()!.Split(':')[^1] != pb.GetProperty("codeFingerprint").GetString()!.Split(':')[^1])
            throw new InvalidDataException("Core sampling convergence requires the same Core binary");
        var output = Path.Combine(study, "core-convergence", pair.Id); Directory.CreateDirectory(output);
        var run = new AnalysisRun { Key = key, Directory = output, Support = entry.Support, Request = b.Request };
        ComparisonRunner.Compare(run, entry, row.GetProperty("settings").Deserialize<AnalysisConfiguration>(JsonFiles.Options)!,
            ResultNormalizer.Workbench(Path.Combine(a.OutputDirectory, "data.json"), entry, a.Request),
            ResultNormalizer.Workbench(Path.Combine(b.OutputDirectory, "data.json"), entry, b.Request), output);
        JsonFiles.Write(Path.Combine(output, "comparison.json"), run);
        records.Add(new { pair.Id, pair.Index, run.Conclusion, run.Reason, run.Metrics, requestA = a.Request, requestB = b.Request,
            provenanceA = pa, provenanceB = pb });
        Console.WriteLine($"{pair.Id}: {run.Conclusion}");
    }
    JsonFiles.Write(Path.Combine(study, "core-convergence-results.json"), records); return;
}
if (args[0] == "convergence")
{
    foreach (var pair in JsonFiles.Read<ConvergenceJob[]>(Path.Combine(study, "convergence-jobs.json")))
    {
        var row = rows.Single(r => r.GetProperty("index").GetInt32() == pair.Index);
        var key = row.GetProperty("key").GetString()!;
        var entry = AnalysisComparisonRegistry.Get(key);
        var settings = row.GetProperty("settings").Deserialize<AnalysisConfiguration>(JsonFiles.Options)!;
        CanonicalAnalysisRequest Request(string folder) => JsonFiles.Read<JsonElement>(Path.Combine(folder, "captured-settings.json"))
            .GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        var output = Path.Combine(study, "convergence", pair.Id); Directory.CreateDirectory(output);
        var run = new AnalysisRun { Key = key, Directory = output, Support = entry.Support, Request = Request(pair.NativeB) };
        try
        {
            var a = ResultNormalizer.Zemax(Path.Combine(pair.NativeA, "data.json"), entry, Request(pair.NativeA));
            var b = ResultNormalizer.Zemax(Path.Combine(pair.NativeB, "data.json"), entry, Request(pair.NativeB));
            ComparisonRunner.Compare(run, entry, settings, a, b, output);
        }
        catch (Exception e) { run.Conclusion = Conclusion.Incomparable; run.Reason = e.Message; }
        JsonFiles.Write(Path.Combine(output, "comparison.json"), run);
        records.Add(new { pair.Id, pair.Index, key, run.Conclusion, run.Reason, run.Metrics, pair.NativeA, pair.NativeB,
            requestA = Request(pair.NativeA), requestB = Request(pair.NativeB), settings });
        Console.WriteLine($"{pair.Id}: {run.Conclusion}; {run.Reason}");
    }
    JsonFiles.Write(Path.Combine(study, "convergence-results.json"), records);
    return;
}
var jobs = args[0] == "rays"
    ? JsonFiles.Read<JsonElement[]>(Path.Combine(baseline, "results.json"))
        .Where(r => r.GetProperty("conclusion").GetString() == "Incomparable" && r.GetProperty("key").GetString() == "Single Ray Trace")
        .Select(r => new ComparisonJob("ray-" + r.GetProperty("index").GetInt32(), r.GetProperty("index").GetInt32(), null)).ToArray()
    : JsonFiles.Read<ComparisonJob[]>(Path.Combine(study, "comparison-jobs.json"));
foreach (var job in jobs)
{
    var row = rows.Single(r => r.GetProperty("index").GetInt32() == job.Index);
    var oldRun = row.GetProperty("run").Deserialize<AnalysisRun>(JsonFiles.Options)!;
    var settings = row.GetProperty("settings").Deserialize<AnalysisConfiguration>(JsonFiles.Options)!;
    var request = oldRun.Request!;
    var native = job.Native ?? Path.Combine(row.GetProperty("legacyPath").GetString()!, "native");
    var workbench = job.Workbench ?? Path.Combine(baseline, "analyses", job.Index.ToString("D4"), "data.json");
    var workbenchRequest = job.WorkbenchRequest is null ? request : JsonFiles.Read<WorkbenchJob>(job.WorkbenchRequest).Request;
    var nativeRequestPath = Path.Combine(native, "captured-settings.json");
    var nativeRequest = File.Exists(nativeRequestPath)
        ? JsonFiles.Read<JsonElement>(nativeRequestPath).GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!
        : request;
    var output = Path.Combine(study, "comparison", job.Id);
    Directory.CreateDirectory(output);
    var run = new AnalysisRun { Key = oldRun.Key, Directory = output, Support = oldRun.Support, Request = request,
        WorkbenchStatus = CaptureStatus.Captured, ZemaxStatus = CaptureStatus.Captured };
    try
    {
        var entry = AnalysisComparisonRegistry.Get(oldRun.Key);
        var z = ResultNormalizer.Zemax(Path.Combine(native, "data.json"), entry, nativeRequest);
        var w = ResultNormalizer.Workbench(workbench, entry, workbenchRequest);
        ComparisonRunner.Compare(run, entry, settings, w, z, output);
    }
    catch (InvalidDataException e) { run.Conclusion = Conclusion.Incomparable; run.Reason = e.Message; }
    catch (Exception e) { run.Conclusion = Conclusion.Error; run.Reason = e.ToString(); }
    JsonFiles.Write(Path.Combine(output, "comparison.json"), run);
    var provenancePath = Path.Combine(Path.GetDirectoryName(workbench)!, "provenance.json");
    JsonElement? provenance = File.Exists(provenancePath) ? JsonFiles.Read<JsonElement>(provenancePath) : null;
    var capturedCore = provenance?.GetProperty("codeFingerprint").GetString()?.Split(':')[^1];
    records.Add(new { job.Id, job.Index, lens = row.GetProperty("lens").GetString(), run.Key, run.Conclusion, run.Reason, run.Metrics,
        native, nativeSha256 = File.Exists(Path.Combine(native, "data.json")) ? Hash(Path.Combine(native, "data.json")) : null,
        workbench, workbenchSha256 = Hash(workbench), workbenchCoreSha256 = capturedCore ?? (job.Workbench is null ? originalCore : null),
        workbenchProvenance = provenance,
        normalizerSha256 = Hash(typeof(ResultNormalizer).Assembly.Location), originalFingerprint = request.Fingerprint,
        nativeFingerprint = nativeRequest.Fingerprint, originalSettings = settings });
    Console.WriteLine($"{job.Id}: {run.Conclusion}; {run.Reason}");
}
JsonFiles.Write(Path.Combine(study, args[0] + "-results.json"), records);

internal sealed record ComparisonJob(string Id, int Index, string? Native, string? Workbench = null, string? WorkbenchRequest = null);
internal sealed record ConvergenceJob(string Id, int Index, string NativeA, string NativeB);
internal sealed record CoreConvergenceJob(string Id, int Index, string RequestA, string RequestB);
