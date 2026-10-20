using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Capabilities;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Diagnostics;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.ZemaxComparison.Zemax;

if (args.Length is < 3 or > 5) throw new ArgumentException("prepare|workbench|compare <repository> <fresh-run-directory> [source-directory] [zos-reference]");
var mode = args[0];
var repo = Path.GetFullPath(args[1]);
var root = Path.GetFullPath(args[2]);
Directory.SetCurrentDirectory(repo);
string Full(string path) => Path.Combine(root, path);
string Hash(string path) => JsonFiles.Hash(File.ReadAllBytes(path));
var primaryKeys = new[] { "First Order", "Single Ray Trace", "Spot Diagram", "Ray Fan", "Optical Path Difference", "Wavefront", "PSF", "Huygens PSF", "MTF", "Huygens MTF", "FFT PSF Cross Section", "Huygens PSF Cross Section", "Encircled Energy", "Diffraction Encircled Energy", "Huygens Through Focus MTF", "Relative Illumination", "RMS vs Field", "Field Curvature and Distortion" };
var edgeKeys = new[] { "Single Ray Trace", "Spot Diagram", "Ray Fan", "Optical Path Difference" };
var api = ZemaxExecutor.Locate(null);
if (mode == "prepare")
{
    if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException("Use a fresh run directory");
    Directory.CreateDirectory(root);
    using var runLock = new FileStream(Full(".prepare.lock"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
    var policyPath = Path.Combine(repo, "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json");
    var policy = ComparisonConfiguration.Load(policyPath);
    policy.Validate();
    File.Copy(policyPath, Full("comparison-settings.json"));
    var sourceRoot = args.Length >= 4 ? Path.GetFullPath(args[3])
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Zemax", "Samples", "Sequential");
    var zosReference = args.Length == 5 && args[4] == "zos-reference";
    var sourceFiles = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    var inventory = sourceFiles.Select(path => new
    {
        id = Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'),
        path,
        bytes = new FileInfo(path).Length,
        sha256 = Hash(path),
        extension = Path.GetExtension(path).ToLowerInvariant()
    }).ToArray();
    JsonFiles.Write(Full("inventory.json"), new
    {
        sourceRoot,
        files = inventory,
        scope = "User explicitly requested every lens in Sequential on 2026-10-19; paired ZMX/ZOS inventoried as one logical lens; no independent residual repair added."
    });
    foreach (var file in inventory.Where(file => file.extension is ".zmx" or ".zos" or ".cfg" or ".xml"))
    {
        var copyPath = Full("sources/" + file.id);
        Directory.CreateDirectory(Path.GetDirectoryName(copyPath)!);
        File.Copy(file.path, copyPath);
    }
    var sources = sourceFiles.Where(path => Path.GetExtension(path).Equals(".zmx", StringComparison.OrdinalIgnoreCase))
        .Select(path => new Source(Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'), path, Hash(path),
            File.ReadLines(path).FirstOrDefault(line => line.StartsWith("UNIT ", StringComparison.Ordinal))?.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1] ?? "UNKNOWN")).ToList();
    var items = new List<Item>();
    var nativeJobs = new List<NativeJob>();
    var lensModels = new List<object>();
    var sourceRecords = new List<object>();
    var index = 0;
    var sourceIndex = 0;
    foreach (var source in sources)
    {
        var actual = Hash(source.Path);
        if (!actual.Equals(source.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Source changed: " + source.Id);
        sourceRecords.Add(new { source, actualSha256 = actual, excluded = false, reason = "Included in the user's full-directory comparison" });
        var slug = (++sourceIndex).ToString("D3", CultureInfo.InvariantCulture) + "-" + JsonFiles.Slug(Path.GetFileNameWithoutExtension(source.Id));
        var copy = Full("sources/" + source.Id);
        var nativeInput = zosReference ? Path.ChangeExtension(copy, ".zos") : copy;
        AddNative($"probe:{slug}:c1", Full($"native-models/{slug}-c1"),
            new { zosApiPath = api, input = copy, zemaxVersion = "2026 R1", adapter = "probe", configuration = 1 });
        var zosPath = Path.ChangeExtension(copy, ".zos");
        if (File.Exists(zosPath))
            AddNative($"format-probe:{slug}:c1", Full($"native-zos-models/{slug}-c1"),
                new { zosApiPath = api, input = zosPath, zemaxVersion = "2026 R1", adapter = "probe", configuration = 1 });
        Console.WriteLine("IMPORT " + source.Id);
        try
        {
            var imported = await WorkbenchExecutor.Import(copy);
            for (var c = 1; c <= imported.Configurations.Count; c++)
            {
                var optic = imported.Configurations[c - 1];
                var snap = optic.ToSnapshot();
                var snapshot = Full($"models/{slug}-c{c}.json");
                JsonFiles.Write(snapshot, snap);
                var primary = Math.Max(1, snap.Wavelengths.FindIndex(w => w.IsPrimary) + 1);
                var waves = Enumerable.Range(1, optic.Wavelengths.Count).Where(w => optic.Wavelengths[w - 1].Weight > 0).ToArray();
                var preflight = OpticCapabilityPreflight.Inspect(optic);
                lensModels.Add(new
                {
                    source.Id,
                    configuration = c,
                    source.Unit,
                    optic.ImageSpaceAfocal,
                    optic.FieldDefinition,
                    optic.RayAimingEnabled,
                    surfaceCount = optic.SurfaceGroup.Items.Count,
                    fields = optic.Fields,
                    wavelengths = optic.Wavelengths,
                    primary,
                    snapshot,
                    snapshotSha256 = Hash(snapshot),
                    preflight
                });
                var probe = Full($"native-models/{slug}-c{c}");
                if (c != 1) AddNative($"probe:{slug}:c{c}", probe, new { zosApiPath = api, input = copy, zemaxVersion = "2026 R1", adapter = "probe", configuration = c });
                foreach (var key in primaryKeys) Analysis(key, "primary", 1, primary);
                foreach (var w in waves)
                    foreach (var key in edgeKeys) Analysis(key, "edge", optic.Fields.Count, w);
                foreach (var w in waves)
                {
                    var id = $"{slug}-c{c}-w{w}";
                    var directory = Full($"rays/{id}");
                    try
                    {
                        var max = optic.Fields.Max(f => Math.Sqrt(f.X * f.X + f.Y * f.Y));
                        var fields = optic.Fields.Select(f => max == 0 ? new[] { 0d, 0d } : new[] { f.X / max, f.Y / max }).DistinctBy(f => (f[0], f[1])).ToArray();
                        var job = new RayAuditJob
                        {
                            Source = copy,
                            SourceSha256 = actual,
                            Output = directory,
                            Configuration = c,
                            Wavelength = w,
                            Fields = fields,
                            RayDensity = 6,
                            RemoveVignettingFactors = false,
                            ZosApiPath = api
                        };
                        var pupils = job.CreateSamples();
                        job.ValidateSurfaceBudget(optic.SurfaceGroup.Items.Count);
                        var inputs = fields.SelectMany((f, fi) => pupils.Select((p, pi) => new { fieldIndex = fi, pupilIndex = pi, hx = f[0], hy = f[1], px = p.X, py = p.Y, weight = p.Weight })).ToArray();
                        JsonFiles.Write(Path.Combine(directory, "job.json"), job);
                        JsonFiles.Write(Path.Combine(directory, "inputs.json"), inputs);
                        var traced = fields.Select((f, fi) =>
                        {
                            var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(f[0], f[1], optic.Wavelengths[w - 1].Micrometers, pupils, aimAtStop: optic.RayAimingEnabled);
                            var histories = optic.SequentialRayTracer.Trace(bundle).RayHistories;
                            return new
                            {
                                fieldIndex = fi,
                                rays = histories.Select((h, pi) => new
                                {
                                    pupilIndex = pi,
                                    samples = h.Select(s => new
                                    {
                                        s.SurfaceNumber,
                                        s.Vignetted,
                                        s.Intensity,
                                        s.InteractionKind,
                                        hit = s.RefractiveIndexBefore.HasValue,
                                        position = optic.SurfaceGroup.Items[s.SurfaceNumber].CoordinateSystem.ToLocalPoint(s.Position),
                                        direction = optic.SurfaceGroup.Items[s.SurfaceNumber].CoordinateSystem.ToLocalDirection(s.Direction)
                                    }).ToArray()
                                }).ToArray()
                            };
                        }).ToArray();
                        JsonFiles.Write(Path.Combine(directory, "core.json"), traced);
                        AddNative("rays:" + id, Path.Combine(directory, "native"), new
                        {
                            zosApiPath = api,
                            input = nativeInput,
                            zemaxVersion = "2026 R1",
                            adapter = "ray-audit",
                            configuration = c,
                            wavelength = w,
                            removeVignettingFactors = false,
                            rayAuditInputs = inputs,
                            surfaceRecordLimit = SequentialTraceLimits.MaximumRetainedSamples
                        });
                    }
                    catch (Exception e) { JsonFiles.Write(Path.Combine(directory, "error.json"), new { source.Id, configuration = c, wavelength = w, error = e.ToString() }); }
                }

                void Analysis(string key, string scope, int field, int wavelength)
                {
                    var entry = AnalysisComparisonRegistry.Get(key);
                    var a = policy.Analyses[key] with { Field = field, Wavelength = wavelength };
                    var id = (++index).ToString("D4", CultureInfo.InvariantCulture) + "-" + slug + $"-c{c}-{scope}-f{field}-w{wavelength}-" + JsonFiles.Slug(key);
                    var directory = Full("analyses/" + id);
                    var run = new AnalysisRun { Key = key, Directory = id, Support = entry.Support, Tolerances = a.Quantities };
                    var item = new Item(source.Id, source.Unit, c, scope, directory, snapshot, null, a, run);
                    items.Add(item);
                    if (AfocalAnalysisContracts.BlockReason(source.Unit, optic.ImageSpaceAfocal, key, normalizeNativeLength: true) is { } blockReason)
                    {
                        run.Conclusion = Conclusion.Incomparable;
                        run.Reason = blockReason;
                        return;
                    }
                    var runtime = new OpticalSystemDesign.Application.Runtime.WorkbenchRuntime(optic);
                    var request = new CanonicalAnalysisRequest
                    {
                        CanonicalAnalysisKey = key,
                        Configuration = c,
                        Field = field,
                        Wavelength = wavelength,
                        PupilSampling = a.PupilSampling,
                        ImageSampling = a.ImageSampling,
                        GridSize = a.ImageSampling,
                        RayCount = a.RayCount,
                        MaximumFrequency = a.MaximumFrequency,
                        ImageDeltaMicrometers = a.ImageDeltaMicrometers,
                        Apodization = JsonSerializer.Serialize(snap.Apodization, JsonFiles.Options),
                        UseRayAiming = optic.RayAimingEnabled,
                        WavelengthCount = optic.Wavelengths.Count,
                        FieldDefinition = optic.FieldDefinition.ToString(),
                        SurfaceCount = optic.SurfaceGroup.Items.Count,
                        FieldCount = optic.Fields.Count,
                        DefinedFields = optic.Fields.Select(f => new[] { f.X, f.Y }).ToArray(),
                        PrimaryWavelengthMicrometers = optic.Wavelengths[primary - 1].Micrometers,
                        MaximumFieldRadius = optic.Fields.Max(f => Math.Sqrt(f.X * f.X + f.Y * f.Y)),
                        WorkbenchSettings = runtime.MergeAnalysisSettings(key, a.WorkbenchSettings)
                    };
                    request = NativeLengthUnitContract.Configure(request, source.Unit);
                    request = AfocalAnalysisContracts.Configure(request, optic.ImageSpaceAfocal, File.ReadAllText(copy));
                    request = ExtendedAnalysisContracts.Configure(entry, request, primary);
                    request = ExtendedAnalysisContracts.ConfigureRmsFieldSampling(request, a);
                    request = request with { WorkbenchSettings = AnalysisComparisonRegistry.MapWorkbench(entry, request, a.VignettedPupil) };
                    run.Request = request;
                    var wbJob = Path.Combine(directory, "workbench/request.json");
                    JsonFiles.Write(wbJob, new WorkbenchJob(snapshot, Path.GetDirectoryName(wbJob)!, request));
                    items[^1] = item with { WorkbenchJob = wbJob };
                    var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(request, JsonFiles.Options), JsonFiles.Options)!;
                    payload["zosApiPath"] = api; payload["input"] = nativeInput; payload["zemaxVersion"] = "2026 R1";
                    payload["adapter"] = entry.ZemaxSettingsMapper; payload["analysisType"] = entry.ZemaxAnalysisType; payload["configuration"] = c; payload["captureScreenshots"] = false;
                    AfocalAnalysisContracts.MapNativePayload(payload, request);
                    AddNative(id, Path.Combine(directory, "native"), payload);
                }
            }
        }
        catch (Exception e)
        {
            lensModels.Add(new { source.Id, error = e.ToString(), phase = "ImportOrPreparation" });
            foreach (var incomplete in items.Where(item => item.Lens == source.Id && item.WorkbenchJob is null && item.Run.Conclusion == Conclusion.Skipped))
            {
                incomplete.Run.Conclusion = Conclusion.Error;
                incomplete.Run.Reason = "Core import/preparation failed before request materialization: " + e.Message;
            }
        }
    }
    JsonFiles.Write(Full("sources.json"), sourceRecords);
    JsonFiles.Write(Full("models.json"), lensModels);
    JsonFiles.Write(Full("items.json"), items);
    JsonFiles.Write(Full("native-jobs.json"), nativeJobs);
    JsonFiles.Write(Full("identity.json"), new
    {
        preparedUtc = DateTimeOffset.UtcNow,
        policySha256 = Hash(policyPath),
        inventorySha256 = Hash(Full("inventory.json")), nativeReferenceFormat = zosReference ? "zos" : "zmx",
        nativeJobs = nativeJobs.Count,
        analysisRequests = items.Count,
        assemblies = new[] { typeof(Optic).Assembly.Location, typeof(WorkbenchExecutor).Assembly.Location, typeof(Item).Assembly.Location }
            .Select(p => new { path = p, sha256 = Hash(p) }),
        rayPolicy = new
        {
            coordinateAbsoluteMillimeters = 1e-6,
            coordinateRelative = 1e-6,
            directionAbsolute = 1e-8,
            directionRelative = 1e-6,
            acceptanceAndFirstClipMustMatch = true,
            source = "Existing Single Ray Trace pointwise tolerances; fixed before native capture; GQ6 explicit rays, no analysis convergence claim"
        }
    });
    Console.WriteLine($"Prepared {items.Count} analyses and {nativeJobs.Count} native jobs");
    void AddNative(string id, string output, object request)
    {
        var path = Path.Combine(output, "request.json");
        JsonFiles.Write(path, request);
        nativeJobs.Add(new(id, path, output));
    }
}
else if (mode == "workbench")
{
    var items = JsonFiles.Read<List<Item>>(Full("items.json"));
    var results = new System.Collections.Concurrent.ConcurrentBag<object>();
    await Parallel.ForEachAsync(items.Where(i => i.WorkbenchJob is not null), new ParallelOptions { MaxDegreeOfParallelism = 2 }, async (item, ct) =>
    {
        var output = Path.Combine(item.Path, "workbench");
        var result = await ProcessIsolation.Run("dotnet", [typeof(WorkbenchExecutor).Assembly.Location, "--workbench-worker", item.WorkbenchJob!], repo, 900, ct);
        File.WriteAllText(Path.Combine(output, "process.log"), result.StandardOutput + result.StandardError);
        JsonFiles.Write(Path.Combine(output, "worker-result.json"), result);
        results.Add(new { item.Run.Directory, result.ExitCode, result.TimedOut });
        Console.WriteLine($"WB {item.Run.Directory} exit={result.ExitCode}");
    });
    JsonFiles.Write(Full("workbench-results.json"), results);
}
else if (mode == "compare")
{
    var items = JsonFiles.Read<List<Item>>(Full("items.json"));
    foreach (var item in items)
    {
        var run = item.Run;
        if (item.WorkbenchJob is null) continue;
        var native = Path.Combine(item.Path, "native");
        var wb = Path.Combine(item.Path, "workbench");
        try
        {
            if (!File.Exists(Path.Combine(native, "batch-result.json"))) throw new IOException("Native request has no completion record");
            var nr = JsonFiles.Read<JsonElement>(Path.Combine(native, "batch-result.json"));
            run.ZemaxStatus = nr.GetProperty("exitCode").GetInt32() == 0 ? CaptureStatus.Captured : CaptureStatus.Failed;
            var wr = JsonFiles.Read<JsonElement>(Path.Combine(wb, "worker-result.json"));
            run.WorkbenchStatus = wr.GetProperty("exitCode").GetInt32() == 0 ? CaptureStatus.Captured : CaptureStatus.Failed;
            if (run.ZemaxStatus != CaptureStatus.Captured || run.WorkbenchStatus != CaptureStatus.Captured)
            {
                run.Conclusion = Conclusion.Error;
                run.Reason = "Native or Workbench capture failed; raw error/process logs retained";
                if (File.Exists(Path.Combine(native, "error.json")))
                {
                    var error = JsonFiles.Read<JsonElement>(Path.Combine(native, "error.json"));
                    run.Reason += ": " + error.GetRawText();
                    if (error.TryGetProperty("errorCode", out var code) && code.GetString() is "UnsupportedFieldMapping" or "AnalysisUnavailableForProgramMode" or "FeatureNotSupported" or "NotYetImplemented" or "NativeInvalidResult")
                        run.Conclusion = Conclusion.Incomparable;
                }
                continue;
            }
            var entry = AnalysisComparisonRegistry.Get(run.Key);
            var z = ResultNormalizer.Zemax(Path.Combine(native, "data.json"), entry, run.Request!);
            JsonFiles.Write(Path.Combine(item.Path, "normalized-native.json"), z);
            var w = ResultNormalizer.Workbench(Path.Combine(wb, "data.json"), entry, run.Request!);
            JsonFiles.Write(Path.Combine(item.Path, "normalized-workbench.json"), w);
            run.Normalizations.AddRange(w.Transformations.Concat(z.Transformations));
            ComparisonRunner.Compare(run, entry, item.Settings, w, z, item.Path);
        }
        catch (InvalidDataException e) { run.Conclusion = Conclusion.Incomparable; run.Reason = e.Message; }
        catch (Exception e) { run.Conclusion = Conclusion.Error; run.Reason = e.ToString(); }
        finally { JsonFiles.Write(Path.Combine(item.Path, "comparison.json"), run); }
    }
    JsonFiles.Write(Full("analysis-results.json"), items);
    JsonFiles.Write(Full("analysis-summary.json"), new
    {
        completedUtc = DateTimeOffset.UtcNow,
        total = items.Count,
        counts = items.GroupBy(i => i.Run.Conclusion.ToString()).ToDictionary(g => g.Key, g => g.Count()),
        lenses = items.GroupBy(i => i.Lens).Select(g => new { lens = g.Key, total = g.Count(), counts = g.GroupBy(i => i.Run.Conclusion.ToString()).ToDictionary(v => v.Key, v => v.Count()) })
    });
    Console.WriteLine(File.ReadAllText(Full("analysis-summary.json")));
}
else if (mode == "isolated-controls")
{
    var items = JsonFiles.Read<List<Item>>(Full("items.json"));
    var selected = items.Where(i => i.Scope == "primary" && i.Run.Key == "Spot Diagram" &&
        (i.Lens is "Objectives/Apochromat.zmx" or "Objectives/Double Gauss 28 degree field.zmx" || i.Lens == "Zoom systems/Zoom lens.zmx" && i.Configuration is 1 or 3)).Reverse().ToArray();
    var output = Full("isolated-controls");
    if (Directory.Exists(output)) throw new IOException("Use a new run for isolated controls");
    var executor = await ZemaxExecutor.Build(api, output, CancellationToken.None);
    var controls = new List<object>();
    foreach (var item in selected)
    {
        var original = Path.Combine(item.Path, "native");
        var payload = JsonFiles.Read<JsonElement>(Path.Combine(original, "request.json"));
        var directory = Path.Combine(output, item.Run.Directory);
        var entry = AnalysisComparisonRegistry.Get(item.Run.Key);
        var process = await executor.Capture(payload.GetProperty("input").GetString()!, directory, "2026 R1", entry.ZemaxSettingsMapper,
            entry.ZemaxAnalysisType, item.Run.Request, item.Configuration, 180, false, CancellationToken.None);
        var oldData = ResultNormalizer.Zemax(Path.Combine(original, "data.json"), entry, item.Run.Request!);
        var fresh = ResultNormalizer.Zemax(Path.Combine(directory, "data.json"), entry, item.Run.Request!);
        var oldHash = JsonFiles.CanonicalHash(JsonSerializer.SerializeToElement(oldData, JsonFiles.Options));
        var newHash = JsonFiles.CanonicalHash(JsonSerializer.SerializeToElement(fresh, JsonFiles.Options));
        controls.Add(new
        {
            item.Lens,
            item.Configuration,
            item.Run.Key,
            process.ExitCode,
            normalizedPersistentSha256 = oldHash,
            normalizedIsolatedSha256 = newHash,
            exactMatch = oldHash == newHash,
            directory
        });
        Console.WriteLine($"Isolated control {item.Lens} c{item.Configuration}: equal={oldHash == newHash}");
    }
    JsonFiles.Write(Full("isolated-controls.json"), controls);
}
else if (mode == "classify-preparation-errors")
{
    var errors = JsonFiles.Read<JsonElement>(Full("models.json")).EnumerateArray()
        .Where(model => model.TryGetProperty("error", out _)).GroupBy(model => model.GetProperty("id").GetString()!)
        .ToDictionary(group => group.Key, group => group.Last().GetProperty("error").GetString()!.Split('\n')[0].Trim());
    foreach (var name in new[] { "items.json", "analysis-results.json" })
    {
        if (!File.Exists(Full(name))) continue;
        var entries = JsonFiles.Read<List<Item>>(Full(name));
        var changed = entries.Where(item => item.WorkbenchJob is null && item.Run.Conclusion == Conclusion.Skipped && errors.ContainsKey(item.Lens)).ToArray();
        if (changed.Length == 0) continue;
        File.Copy(Full(name), Full(Path.GetFileNameWithoutExtension(name) + "-before-preparation-error-classification.json"));
        foreach (var item in changed)
        {
            item.Run.Conclusion = Conclusion.Error;
            item.Run.Reason = "Core import/preparation failed before request materialization: " + errors[item.Lens];
        }
        JsonFiles.Write(Full(name), entries);
        Console.WriteLine($"Classified {changed.Length} unmaterialized preparation failures in {name}; numerical requests and tolerance policy unchanged.");
        if (name == "analysis-results.json")
        {
            File.Copy(Full("analysis-summary.json"), Full("analysis-summary-before-preparation-error-classification.json"));
            JsonFiles.Write(Full("analysis-summary.json"), new
            {
                completedUtc = DateTimeOffset.UtcNow, total = entries.Count,
                counts = entries.GroupBy(item => item.Run.Conclusion.ToString()).ToDictionary(group => group.Key, group => group.Count()),
                lenses = entries.GroupBy(item => item.Lens).Select(group => new { lens = group.Key, total = group.Count(), counts = group.GroupBy(item => item.Run.Conclusion.ToString()).ToDictionary(value => value.Key, value => value.Count()) })
            });
        }
    }
}
else throw new ArgumentException("Unknown mode");

internal sealed record Source(string Id, string Path, string Sha256, string Unit);
internal sealed record NativeJob(string Id, string Request, string Output);
internal sealed record Item(string Lens, string Unit, int Configuration, string Scope, string Path, string Snapshot,
    string? WorkbenchJob, AnalysisConfiguration Settings, AnalysisRun Run);
