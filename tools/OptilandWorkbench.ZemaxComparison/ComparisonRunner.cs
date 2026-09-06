using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.ZemaxComparison.Configuration;
using OptilandWorkbench.ZemaxComparison.Metrics;
using OptilandWorkbench.ZemaxComparison.Normalization;
using OptilandWorkbench.ZemaxComparison.Reporting;
using OptilandWorkbench.ZemaxComparison.Workbench;
using OptilandWorkbench.ZemaxComparison.Zemax;

namespace OptilandWorkbench.ZemaxComparison;

public sealed class ComparisonRunner
{
    public static string PrepareOutput(string? supplied, string input, string sourceHash, string configHash, string version, bool overwrite)
    {
        var name = Path.GetFileNameWithoutExtension(input) + "-" + JsonFiles.Slug(version) + "-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ", CultureInfo.InvariantCulture) + "-" + sourceHash[..12];
        var path = Path.GetFullPath(supplied ?? Path.Combine("artifacts", "zemax-comparisons", name));
        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
        {
            if (!overwrite) throw new IOException("Output is not empty; use a new directory: " + path);
            var manifestPath = Path.Combine(path, "manifest.json");
            if (!File.Exists(manifestPath)) throw new IOException("--overwrite requires a previous tool manifest");
            using var prior = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (prior.RootElement.GetProperty("sourceSha256").GetString() != sourceHash || prior.RootElement.GetProperty("configurationSha256").GetString() != configHash)
                throw new IOException("A changed source or tolerance configuration must use a new output directory");
        }
        Directory.CreateDirectory(path); return path;
    }
    public static int ExitCode(IEnumerable<AnalysisRun> runs, string failOn, bool cancelled, bool timedOut, bool setupError, bool internalError)
    {
        if (cancelled || timedOut) return 4;
        if (setupError) return 2;
        if (internalError) return 3;
        if (failOn != "none" && runs.Any(r => r.Conclusion == Conclusion.Error)) return 2;
        if (failOn == "difference" && runs.Any(r => r.Conclusion is Conclusion.Difference or Conclusion.Close)) return 1;
        return 0;
    }
    public static void ArchivePreviousRun(string output)
    {
        // Called only after manifest/hash validation and acquisition of the exclusive run lock.
        // Keep the previous evidence together so a subset rerun cannot display stale plots or raw data.
        if (!File.Exists(Path.Combine(output, "manifest.json"))) return;
        var root = Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var archive = Path.Combine(root, "previous-run-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(archive);
        foreach (var name in new[] { "logs", "raw", "normalized", "comparisons", "errors", "input", "host",
            "manifest.json", "run-summary.json", "analysis-matrix.csv", "analysis-registry.json", "comparison-settings.json", "COMPARISON_REPORT.md" })
        {
            var source = Path.GetFullPath(Path.Combine(root, name));
            var target = Path.GetFullPath(Path.Combine(archive, name));
            if (!source.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Archive paths must remain inside the validated output directory");
            if (Directory.Exists(source)) Directory.Move(source, target);
            else if (File.Exists(source)) File.Move(source, target);
        }
    }
    public async Task<int> Run(ComparisonOptions options, CancellationToken ct)
    {
        foreach (var key in options.Analyses) AnalysisComparisonRegistry.Get(key);
        var config = ComparisonConfiguration.Load(options.Config); config.Validate();
        var version = options.ZemaxVersion ?? config.ZemaxVersion;
        var input = Path.GetFullPath(options.Input);
        if (!Path.GetExtension(input).Equals(".zmx", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("--input must be a ZMX file");
        var bytes = await File.ReadAllBytesAsync(input, ct); var sourceHash = JsonFiles.Hash(bytes);
        var configHash = JsonFiles.Hash(File.ReadAllBytes(options.Config)); var stat = new FileInfo(input);
        var output = PrepareOutput(options.Output, input, sourceHash, configHash, version, options.Overwrite);
        if (input.StartsWith(output + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("Output must not contain the source input file");
        using var outputLock = new FileStream(Path.Combine(output, ".run.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (options.Overwrite) ArchivePreviousRun(output);
        Console.WriteLine("Output: " + output);
        foreach (var d in new[] { "logs", "raw/zemax", "raw/workbench", "normalized/zemax", "normalized/workbench", "comparisons", "errors", "input" }) Directory.CreateDirectory(Path.Combine(output, d));
        File.WriteAllBytes(Path.Combine(output, "comparison-settings.json"), File.ReadAllBytes(options.Config));
        JsonFiles.Write(Path.Combine(output, "analysis-registry.json"), AnalysisComparisonRegistry.Entries);
        var snapshotInput = Path.Combine(output, "input", Path.GetFileName(input)); File.WriteAllBytes(snapshotInput, bytes);
        var runs = new List<AnalysisRun>();
        var manifest = new Dictionary<string, object?>
        {
            ["inputPath"] = input,
            ["fileName"] = stat.Name,
            ["fileLength"] = bytes.Length,
            ["sourceSha256"] = sourceHash,
            ["inputLastWriteUtc"] = stat.LastWriteTimeUtc,
            ["startedUtc"] = DateTimeOffset.UtcNow,
            ["configurationSha256"] = configHash,
            ["configurationVersion"] = config.Version,
            ["dotnetVersion"] = RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = RuntimeInformation.OSDescription,
            ["toolVersion"] = "1.0.0",
            ["expectedZemaxVersion"] = version,
            ["toolAssemblySha256"] = JsonFiles.Hash(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location)),
            ["settingsOrigin"] = "CapturedSettings",
            ["originalFileUnchanged"] = null
        };
        var git = await ProcessIsolation.Run("git", ["rev-parse", "HEAD"], Directory.GetCurrentDirectory(), 10, ct);
        manifest["gitSha"] = git.ExitCode == 0 ? git.StandardOutput.Trim() : "unavailable";
        var dirty = await ProcessIsolation.Run("git", ["status", "--porcelain"], Directory.GetCurrentDirectory(), 10, ct);
        manifest["gitWorkingTreeDirty"] = dirty.ExitCode == 0 ? dirty.StandardOutput.Length > 0 : null;
        bool setupError = false, internalError = false, timedOut = false; string? fatal = null;
        try
        {
            var imported = await WorkbenchExecutor.Import(snapshotInput);
            var configs = options.Configuration == "all" ? Enumerable.Range(1, imported.Configurations.Count).ToArray() : [int.Parse(options.Configuration, CultureInfo.InvariantCulture)];
            if (configs.Any(n => n > imported.Configurations.Count)) throw new ArgumentException("Configuration index exceeds imported configuration count");
            var text = File.ReadAllText(snapshotInput);
            var headers = text.Split('\n').Select(l => l.Trim()).Where(l => new[] { "VERS ", "MODE ", "UNIT ", "GCAT ", "RAIM ", "APOD ", "FTYP ", "AFCL " }.Any(prefix => l.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
            manifest["zmxHeaderRecords"] = headers;
            manifest["zmxFileVersion"] = headers.FirstOrDefault(h => h.StartsWith("VERS ", StringComparison.Ordinal));
            manifest["configurationCount"] = imported.Configurations.Count;
            manifest["workbenchParsing"] = imported.Configurations.Select((o, i) => new
            {
                Configuration = i + 1,
                SurfaceCount = o.SurfaceGroup.Items.Count,
                o.Fields,
                o.Wavelengths,
                o.FieldDefinition,
                o.Aperture,
                o.ImageSpaceAfocal,
                o.RayAimingEnabled,
                GlassCatalogs = o.ToSnapshot().GlassCatalogs,
                Apodization = o.ToSnapshot().Apodization,
                PreflightIssues = OpticCapabilityPreflight.Inspect(o),
                CoordinateBreaks = o.SurfaceGroup.Items.Where(s => s.Geometry.GetType().Name.Contains("Coordinate", StringComparison.Ordinal)).Select(s => s.Number).ToArray(),
                ParserWarnings = "Importer exposes capability issues; unsupported preserved records are available in input snapshot. No additional warning stream is exposed."
            }).ToArray();
            var selected = AnalysisComparisonRegistry.Entries.Where(e => options.Analyses.Count == 0 || options.Analyses.Contains(e.CanonicalAnalysisKey, StringComparer.Ordinal)).ToArray();
            foreach (var c in configs) foreach (var e in AnalysisComparisonRegistry.Entries)
                runs.Add(new()
                {
                    Key = e.CanonicalAnalysisKey,
                    Directory = JsonFiles.Slug(e.CanonicalAnalysisKey) + "-c" + c,
                    Support = e.Support,
                    Reason = selected.Contains(e) ? string.IsNullOrEmpty(e.Reason) ? "Pending execution" : e.Reason : "Not selected by --analysis"
                });
            ZemaxExecutor? zemax = null;
            try
            {
                var api = ZemaxExecutor.Locate(options.ZosApiPath); manifest["zosApiPath"] = api;
                zemax = await ZemaxExecutor.Build(api, output, ct);
                var probeDir = Path.Combine(output, "raw", "zemax", "probe");
                var probe = await zemax.Capture(snapshotInput, probeDir, version, "probe", null, null, configs[0], options.Timeout, false, ct);
                timedOut |= probe.TimedOut;
                if (File.Exists(Path.Combine(probeDir, "environment.json")))
                {
                    var environment = JsonFiles.Read<JsonElement>(Path.Combine(probeDir, "environment.json")); manifest["zemaxEnvironment"] = environment;
                    foreach (var native in AnalysisComparisonRegistry.NativeOnly(environment.GetProperty("analysisIds").EnumerateArray().Select(v => v.GetString()!)))
                        runs.Add(new()
                        {
                            Key = native.CanonicalAnalysisKey,
                            Directory = JsonFiles.Slug(native.CanonicalAnalysisKey.Replace(':', '-')),
                            Support = native.Support,
                            Reason = native.Reason
                        });
                }
                if (File.Exists(Path.Combine(probeDir, "model.json"))) manifest["zemaxParsing"] = JsonFiles.Read<JsonElement>(Path.Combine(probeDir, "model.json"));
                if (probe.ExitCode != 0) throw new InvalidOperationException("ZOS-API probe failed; see raw/zemax/probe: " + probe.StandardError);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            { setupError = true; fatal = e.Message; zemax = null; Log(output, "zemax", e.ToString()); }
            Flush();
            foreach (var c in configs)
            {
                var optic = imported.Configurations[c - 1]; var snapshot = optic.ToSnapshot();
                var opticPath = Path.Combine(output, "input", "configuration-" + c + ".json"); JsonFiles.Write(opticPath, snapshot);
                var runtime = new WorkbenchRuntime(optic);
                foreach (var entry in selected)
                {
                    var run = runs.Single(r => r.Key == entry.CanonicalAnalysisKey && r.Directory.EndsWith("-c" + c, StringComparison.Ordinal));
                    if (ct.IsCancellationRequested) { run.Reason = "Cancelled before execution"; continue; }
                    if (entry.Mode == "NonSequential") { run.Support = SupportStatus.NotApplicableToModel; run.Reason = "Sequential ZMX snapshot has no non-sequential detector/ray database"; continue; }
                    var a = config.Analyses.GetValueOrDefault(entry.CanonicalAnalysisKey) ?? new AnalysisConfiguration();
                    run.Tolerances = a.Quantities;
                    if (a.Field > optic.Fields.Count || a.Wavelength > optic.Wavelengths.Count) { run.Conclusion = Conclusion.Error; run.Reason = "Explicit field/wavelength index exceeds model"; continue; }
                    var settings = runtime.MergeAnalysisSettings(entry.CanonicalAnalysisKey, a.WorkbenchSettings);
                    var request = new CanonicalAnalysisRequest
                    {
                        CanonicalAnalysisKey = entry.CanonicalAnalysisKey,
                        Configuration = c,
                        Field = a.Field,
                        Wavelength = entry.ZemaxSettingsMapper == "first-order" ? Math.Max(1, snapshot.Wavelengths.FindIndex(w => w.IsPrimary) + 1) : a.Wavelength,
                        PupilSampling = a.PupilSampling,
                        ImageSampling = a.ImageSampling,
                        GridSize = a.ImageSampling,
                        RayCount = a.RayCount,
                        MaximumFrequency = a.MaximumFrequency,
                        ImageDeltaMicrometers = a.ImageDeltaMicrometers,
                        Apodization = JsonSerializer.Serialize(snapshot.Apodization, JsonFiles.Options),
                        UseRayAiming = optic.RayAimingEnabled,
                        WavelengthCount = optic.Wavelengths.Count,
                        FieldDefinition = optic.FieldDefinition.ToString(),
                        SurfaceCount = optic.SurfaceGroup.Items.Count,
                        FieldCount = optic.Fields.Count,
                        DefinedFields = optic.Fields.Select(f => new[] { f.X, f.Y }).ToArray(),
                        PrimaryWavelengthMicrometers = (optic.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? optic.Wavelengths[0]).Micrometers,
                        MaximumFieldRadius = optic.Fields.Max(f => Math.Sqrt(f.X * f.X + f.Y * f.Y)),
                        WorkbenchSettings = settings
                    };
                    run.Request = request;
                    var clock = Stopwatch.StartNew(); Console.WriteLine($"c{c} {entry.CanonicalAnalysisKey}");
                    try
                    {
                        request = ExtendedAnalysisContracts.Configure(entry, request, Math.Max(1, snapshot.Wavelengths.FindIndex(w => w.IsPrimary) + 1));
                        if (request.SourceImagePath is { } sourceImage)
                        {
                            var sourceBytes = File.ReadAllBytes(sourceImage);
                            if (JsonFiles.Hash(sourceBytes) != request.SourceImageSha256) throw new InvalidDataException("Source image changed before freezing");
                            var frozen = Path.Combine(output, "input", "source-" + request.SourceImageSha256 + Path.GetExtension(sourceImage));
                            File.WriteAllBytes(frozen, sourceBytes);
                            var nativeSettings = new Dictionary<string, object>(request.ZemaxSettings);
                            foreach (var key in nativeSettings.Keys.ToArray())
                                if (Equals(nativeSettings[key], sourceImage)) nativeSettings[key] = frozen;
                            request = request with { SourceImagePath = frozen, ZemaxSettings = nativeSettings };
                        }
                        request = request with { WorkbenchSettings = AnalysisComparisonRegistry.MapWorkbench(entry, request) };
                        run.Request = request;
                        var wbDir = Path.Combine(output, "raw", "workbench", run.Directory); Directory.CreateDirectory(wbDir);
                        var jobPath = Path.Combine(wbDir, "request.json"); JsonFiles.Write(jobPath, new WorkbenchJob(opticPath, wbDir, request));
                        var wb = await ProcessIsolation.Run("dotnet", [Assembly.GetExecutingAssembly().Location, "--workbench-worker", jobPath], output, options.Timeout, ct);
                        Log(output, "workbench", run.Key + ": " + wb.StandardOutput + wb.StandardError);
                        run.WorkbenchStatus = Status(wb); timedOut |= wb.TimedOut;
                        if (wb.ExitCode == 0)
                        {
                            JsonFiles.Write(Path.Combine(output, "normalized", "workbench", run.Directory + ".json"),
                                ResultNormalizer.CaptureWorkbench(Path.Combine(wbDir, "data.json")));
                            var data = JsonFiles.Read<OptilandWorkbench.Core.Analysis.AnalysisData>(Path.Combine(wbDir, "data.json"));
                            if (data.Outcome != OptilandWorkbench.Core.Analysis.AnalysisOutcome.Success)
                            {
                                run.WorkbenchStatus = CaptureStatus.Skipped; run.Support = SupportStatus.NotApplicableToModel;
                                run.Conclusion = Conclusion.Skipped; run.Reason = data.Outcome + ": " + data.OutcomeReason;
                                continue;
                            }
                        }
                        if (wb.ExitCode != 0) { run.Conclusion = Conclusion.Error; run.Reason = wb.Cancelled ? "Cancelled" : wb.TimedOut ? "Workbench timeout" : wb.StandardError; }
                        if (wb.Cancelled) continue;
                        if (entry.ZemaxSettingsMapper == "unimplemented") { if (wb.ExitCode == 0) run.Conclusion = Conclusion.Incomparable; continue; }
                        if (zemax is null) { run.ZemaxStatus = CaptureStatus.Failed; run.Conclusion = Conclusion.Error; run.Reason = "ZOS-API setup failed; see probe log"; continue; }
                        if (optic.ImageSpaceAfocal || !headers.Any(h => h.StartsWith("UNIT MM", StringComparison.Ordinal)))
                        { run.Conclusion = Conclusion.Incomparable; run.Reason = "Live adapter currently requires focal image space and MM lens units; no angular/linear unit substitution"; continue; }
                        var zDir = Path.Combine(output, "raw", "zemax", run.Directory);
                        var z = await zemax.Capture(snapshotInput, zDir, version, entry.ZemaxSettingsMapper, entry.ZemaxAnalysisType, request, c, options.Timeout, options.CaptureScreenshots, ct);
                        run.ZemaxStatus = Status(z); timedOut |= z.TimedOut; Log(output, "zemax", run.Key + ": " + z.StandardOutput + z.StandardError);
                        if (z.ExitCode != 0)
                        {
                            run.Conclusion = Conclusion.Error; run.Reason = z.Cancelled ? "Cancelled" : z.TimedOut ? "Zemax timeout" : z.StandardError;
                            var errorPath = Path.Combine(zDir, "error.json");
                            if (File.Exists(errorPath))
                            {
                                var error = JsonFiles.Read<JsonElement>(errorPath);
                                var code = error.GetProperty("errorCode").GetString();
                                if (code is "LicenseUnavailable" or "NoSolverLicenseAvailable") { run.Support = SupportStatus.LicenseUnavailable; run.ZemaxStatus = CaptureStatus.LicenseUnavailable; }
                                else if (code is "AnalysisUnavailableForProgramMode" or "SequentialOnly" or "NonSequentialOnly") { run.Support = SupportStatus.NotApplicableToModel; run.Conclusion = Conclusion.Skipped; }
                                else if (code is "NotYetImplemented" or "FeatureNotSupported") { run.Support = SupportStatus.UnsupportedByZosApi; run.Conclusion = Conclusion.Skipped; }
                                else if (code == "UnsupportedFieldMapping") { run.Support = SupportStatus.NotApplicableToModel; run.Conclusion = Conclusion.Incomparable; }
                                else run.Support = SupportStatus.Failed;
                                run.Reason = code + ": " + error.GetProperty("error").GetString();
                            }
                            continue;
                        }
                        if (File.Exists(Path.Combine(zDir, "capture.json"))) run.ScreenshotStatus = JsonFiles.Read<JsonElement>(Path.Combine(zDir, "capture.json")).GetProperty("screenshotStatus").GetString()!;
                        if (options.CaptureScreenshots)
                            run.ScreenshotStatus = await ZemaxScreenshotExecutor.Capture((string)manifest["zosApiPath"]!, zDir, entry.ScreenshotCode, options.Timeout, ct);
                        if (entry.ZemaxSettingsMapper is "spot-layout" or "capability-audit")
                        {
                            using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(zDir, "data.json")));
                            var counts = NativeResultChannels.Count(native.RootElement);
                            var textSaved = JsonFiles.Read<JsonElement>(Path.Combine(zDir, "capture.json")).GetProperty("textSaved").GetBoolean();
                            JsonFiles.Write(Path.Combine(zDir, "result-channel-audit.json"), new
                            {
                                counts,
                                textSaved,
                                bitmapSaved = File.Exists(Path.Combine(zDir, "native-image.bmp")),
                                inspectionOnly = entry.ZemaxSettingsMapper == "capability-audit"
                            });
                            run.Conclusion = wb.ExitCode == 0 ? Conclusion.Incomparable : Conclusion.Error;
                            if (entry.ZemaxSettingsMapper == "capability-audit") { run.Support = entry.Support; run.Reason = entry.Reason; continue; }
                            var empty = counts.Values.All(n => n == 0) && !textSaved;
                            run.Support = empty ? SupportStatus.UnsupportedByZosApi : SupportStatus.AdapterNotImplemented;
                            run.Reason = empty
                                ? "The captured 26.1 native layout exposes no IAR numeric, scatter, RGB, ray or spot-metric channels. No point-cloud or numerical-equivalence claim; raw channel audit retained."
                                : "Native layout now exposes a result channel; interpretation must be audited before numerical comparison. Raw channel audit retained.";
                            continue;
                        }
                        // Persist each side independently, including when the other normalizer rejects an output contract.
                        var zResult = ResultNormalizer.Zemax(Path.Combine(zDir, "data.json"), entry, request);
                        JsonFiles.Write(Path.Combine(output, "normalized", "zemax", run.Directory + ".json"), zResult);
                        if (wb.ExitCode != 0) continue;
                        var wResult = ResultNormalizer.Workbench(Path.Combine(wbDir, "data.json"), entry, request);
                        JsonFiles.Write(Path.Combine(output, "normalized", "workbench", run.Directory + ".json"), wResult);
                        run.Normalizations.AddRange(wResult.Transformations.Concat(zResult.Transformations));
                        Compare(run, entry, a, wResult, zResult, output);
                    }
                    catch (InvalidDataException e) { run.Conclusion = run.WorkbenchStatus == CaptureStatus.Failed ? Conclusion.Error : Conclusion.Incomparable; run.Reason = e.Message; }
                    catch (Exception e) when (e is not OperationCanceledException) { run.Conclusion = Conclusion.Error; run.Reason = e.ToString(); }
                    finally { run.ElapsedMilliseconds = clock.ElapsedMilliseconds; Flush(); }
                }
            }
        }
        catch (OperationCanceledException) { fatal = "Cancelled"; }
        catch (Exception e) when (e is ArgumentException or IOException or InvalidOperationException) { setupError = true; fatal = e.ToString(); }
        catch (Exception e) { internalError = true; fatal = e.ToString(); }
        finally
        {
            manifest["completedUtc"] = DateTimeOffset.UtcNow;
            var unchanged = File.Exists(input) && JsonFiles.Hash(File.ReadAllBytes(input)) == sourceHash && File.GetLastWriteTimeUtc(input) == stat.LastWriteTimeUtc;
            manifest["snapshotInputUnchanged"] = File.Exists(snapshotInput) && JsonFiles.Hash(File.ReadAllBytes(snapshotInput)) == sourceHash;
            manifest["originalFileUnchanged"] = unchanged;
            if (!unchanged) { internalError = true; fatal = "Original input changed during run; report cannot certify input integrity"; }
            manifest["fatalError"] = fatal; Flush();
        }
        Console.WriteLine(Path.Combine(output, "COMPARISON_REPORT.md"));
        return ExitCode(runs, options.FailOn, ct.IsCancellationRequested, timedOut, setupError, internalError);

        void Flush()
        {
            foreach (var r in runs) JsonFiles.Write(Path.Combine(output, "comparisons", r.Directory, "comparison.json"), r);
            ReportWriter.Write(output, manifest, runs, options.ReportLanguage, ExitCode(runs, options.FailOn, ct.IsCancellationRequested, timedOut, setupError, internalError), fatal);
        }
    }
    private static CaptureStatus Status(ProcessResult r) => r.Cancelled ? CaptureStatus.Cancelled : r.TimedOut ? CaptureStatus.TimedOut : r.ExitCode == 0 ? CaptureStatus.Captured : CaptureStatus.Failed;
    private static void Log(string root, string stream, string text)
    {
        File.AppendAllText(Path.Combine(root, "logs", stream + ".log"), DateTimeOffset.UtcNow.ToString("O") + " " + text + "\n");
        File.AppendAllText(Path.Combine(root, "logs", "run.log"), DateTimeOffset.UtcNow.ToString("O") + " " + stream + " " + text + "\n");
    }
    private static void Compare(AnalysisRun run, AnalysisComparisonEntry entry, AnalysisConfiguration config, NumericResult w, NumericResult z, string output)
    {
        var directory = Path.Combine(output, "comparisons", run.Directory); Directory.CreateDirectory(directory);
        Tolerances T(string key) => config.Quantities.GetValueOrDefault(key) ?? throw new InvalidDataException("No per-quantity tolerance: " + key);
        foreach (var scalar in z.Scalars)
        {
            var actual = w.Scalars.Single(s => s.Id == scalar.Id);
            var values = new MatchedValues([0], null, [actual.Value * PhysicalNormalization.UnitScale(actual.Unit, scalar.Unit)], [scalar.Value], 1);
            run.Metrics.Add(ComparisonMetrics.Calculate(scalar.Id, scalar.Unit, values, T(scalar.Id)));
            ReportWriter.Values(Path.Combine(directory, JsonFiles.Slug(scalar.Id) + "-values.csv"), values);
        }
        foreach (var curve in z.Series)
        {
            var actual = w.Series.Single(s => s.Id == curve.Id);
            var values = ComparisonMetrics.Align(actual, curve, run.Normalizations);
            var metric = ComparisonMetrics.Calculate(curve.Id, curve.YAxis.Unit, values, T(curve.YAxis.Quantity));
            if (curve.YAxis.Quantity == "Modulation" && curve.XAxis.Quantity == "SpatialFrequency") metric = metric with { Extra = ComparisonMetrics.MtfStatistics(values) };
            run.Metrics.Add(metric); ReportWriter.Values(Path.Combine(directory, JsonFiles.Slug(curve.Id) + "-values.csv"), values);
            PlotWriter.Curves(directory, JsonFiles.Slug(curve.Id), values, curve.XAxis.Quantity + " / " + curve.XAxis.Unit, curve.YAxis.Quantity + " / " + curve.YAxis.Unit);
        }
        foreach (var grid in z.Grids)
        {
            var actual = w.Grids.Single(s => s.Id == grid.Id);
            var values = ComparisonMetrics.Align(actual, grid, run.Normalizations);
            var metric = ComparisonMetrics.Calculate(grid.Id, grid.ValueAxis.Unit, values, T(grid.ValueAxis.Quantity));
            if (grid.ValueAxis.Quantity is "Irradiance" or "WavefrontError")
                metric = metric with { Extra = ComparisonMetrics.GridStatistics(values, grid.Id, grid.ValueAxis.Quantity == "WavefrontError") };
            run.Metrics.Add(metric); ReportWriter.Values(Path.Combine(directory, JsonFiles.Slug(grid.Id) + "-values.csv"), values); PlotWriter.Grid(directory, JsonFiles.Slug(grid.Id), values);
        }
        if (run.Metrics.Count == 0) { run.Conclusion = Conclusion.Incomparable; run.Reason = "No shared numerical output"; return; }
        run.Conclusion = run.Metrics.Any(m => m.Conclusion == Conclusion.Incomparable) ? Conclusion.Incomparable
            : run.Metrics.Any(m => m.Conclusion == Conclusion.Difference) ? Conclusion.Difference
            : run.Metrics.Any(m => m.Conclusion == Conclusion.Close) ? Conclusion.Close : Conclusion.Pass;
        run.Reason = entry.Reason;
    }
}
