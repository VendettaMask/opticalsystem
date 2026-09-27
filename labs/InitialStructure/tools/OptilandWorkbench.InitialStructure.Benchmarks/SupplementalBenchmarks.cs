using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Persistence;

internal static class SupplementalBenchmarks
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    public static Task<int> RunLegacy(string[] args) => Run(args, legacy: true);
    public static Task<int> RunHoldout(string[] args) => Run(args, legacy: false);

    private static async Task<int> Run(string[] args, bool legacy)
    {
        if (args.Length != 2) throw new ArgumentException("Usage: --legacy <flat-start-v1 directory> <new output> OR --holdout <holdout directory> <new output>");
        var input = Path.GetFullPath(args[0]);
        var output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Use a new directory; completed evidence is never overwritten.");
        var protocolPath = Path.Combine(input, "protocol.json");
        using var protocol = JsonDocument.Parse(await File.ReadAllTextAsync(protocolPath));
        var seeds = protocol.RootElement.GetProperty("RandomSeeds").EnumerateArray().Select(value => value.GetInt64()).ToArray();
        var paths = Directory.GetFiles(Path.Combine(input, "specs"), "*.json").Order(StringComparer.Ordinal).ToArray();
        if (paths.Length != protocol.RootElement.GetProperty("SpecificationCount").GetInt32())
            throw new InvalidDataException("Specification count does not match the protocol.");
        if (legacy)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("OptilandWorkbench.InitialStructure.Benchmarks.FrozenInputs.json")!;
            var expected = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
            foreach (var item in expected)
                if (HashText(File.ReadAllText(Path.Combine(input, item.Key)).Replace("\r\n", "\n", StringComparison.Ordinal)) != item.Value)
                    throw new InvalidDataException("Frozen input changed: " + item.Key);
            if (paths.Length != 12) throw new InvalidDataException("Unexpected files in the frozen specification set.");
        }
        else if (protocol.RootElement.GetProperty("Profile").GetString() != "independent-holdout-v1")
            throw new InvalidDataException("Holdout inputs must declare their separate protocol.");
        Directory.CreateDirectory(output);
        var inputs = paths.Prepend(protocolPath).ToDictionary(path => Path.GetRelativePath(input, path).Replace('\\', '/'), HashFile);
        var binaries = new[] { typeof(Optic).Assembly, typeof(FlatStartSearchService).Assembly, typeof(CandidateSnapshot).Assembly, Assembly.GetExecutingAssembly() }
            .ToDictionary(assembly => assembly.GetName().Name!, assembly => HashFile(assembly.Location));
        var results = new List<object>();
        var successfulRuns = 0;
        var created = DateTimeOffset.UtcNow;
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        await Save();
        foreach (var path in paths)
        {
            var template = JsonSerializer.Deserialize<InitialStructureSpecification>(await File.ReadAllTextAsync(path), Json)!;
            if (template.FlatStart is null || template.Budget.MaximumEvaluations != 10_000
                || template.Budget.TimeLimit != TimeSpan.FromSeconds(600) || template.Budget.MaximumParallelism != 1)
                throw new InvalidDataException("Supplemental runs preserve the same evaluation/time/parallelism ceilings.");
            foreach (var seed in seeds)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var spec = template with { Budget = template.Budget with { RandomSeed = seed } };
                var directory = Path.Combine(output, Path.GetFileNameWithoutExtension(path), seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Directory.CreateDirectory(directory);
                var clock = Stopwatch.StartNew();
                using var measurement = SequentialTraceMeasurement.Begin();
                IReadOnlyList<CandidateSnapshot> candidates;
                IReadOnlyList<FamilyTrial> trials = [];
                string accounting;
                string state;
                if (legacy)
                {
                    // The historical API rejects FlatStart. Keep its unchanged objective,
                    // and independently enforce ALL frozen target constraints afterwards.
                    var manifest = await new InitialStructureSearchService().RunAsync(spec with { FlatStart = null }, cancellationToken: cancellation.Token);
                    await File.WriteAllTextAsync(Path.Combine(directory, "legacy-manifest.json"), JsonSerializer.Serialize(manifest, Json));
                    candidates = manifest.Candidates;
                    state = manifest.State.ToString();
                    accounting = manifest.Diagnostics.FirstOrDefault(item => item.Code == "run.evaluation-count")?.Message ?? "Unavailable";
                }
                else
                {
                    var search = await new FlatStartSearchService().RunAsync(spec, cancellationToken: cancellation.Token);
                    FlatStartCheckpointValidation.Validate(search.Checkpoint);
                    await new FlatStartSearchCheckpointStore().SaveAsync(search.Checkpoint, Path.Combine(directory, "checkpoint.json"));
                    trials = search.Checkpoint.Trials;
                    candidates = trials.Where(trial => trial.Candidate is not null).Select(trial => trial.Candidate!).ToArray();
                    state = search.Checkpoint.State.ToString();
                    accounting = $"Consumed {search.Checkpoint.ChargedEvaluations} of {spec.Budget.MaximumEvaluations} evaluations.";
                }
                var seconds = clock.Elapsed.TotalSeconds;
                var rays = measurement.RayCount;
                var errors = new List<string>();
                var verified = new List<(CandidateSnapshot Candidate, DesignEvaluation Evaluation)>();
                foreach (var candidate in candidates.DistinctBy(candidate => candidate.OpticFingerprint))
                {
                    try
                    {
                        var evaluation = Validate(spec, candidate);
                        if (!legacy && candidate.Status == CandidateStatus.LabAccepted && !evaluation.MeetsTargets)
                            errors.Add(candidate.CandidateId + ": independent full-target validation failed");
                        verified.Add((candidate, evaluation));
                    }
                    catch (Exception exception) when (exception is InvalidOperationException or ArithmeticException or ArgumentException)
                    { errors.Add(candidate.CandidateId + ": " + exception.Message); }
                }
                var selected = verified.OrderBy(item => item.Evaluation.MeetsTargets ? 0 : item.Evaluation.IsFeasible ? 1 : 2)
                    .ThenBy(item => item.Evaluation.Merit).FirstOrDefault();
                bool? reloadIdentical = null;
                if (selected.Candidate is not null)
                {
                    var export = await new CandidateExportService().ExportStarOptAsync(selected.Candidate, Path.Combine(directory, "selected.staropt"));
                    var document = await StarOptProjectStore.LoadAsync(export);
                    var snapshot = document.Configurations[document.ActiveConfigurationIndex].ToSnapshot();
                    var reloaded = selected.Candidate with { Optic = snapshot };
                    reloadIdentical = ContentFingerprint.Compute(snapshot) == selected.Candidate.OpticFingerprint
                        && ContentFingerprint.Compute(Validate(spec, reloaded) with { Residuals = [] })
                            == ContentFingerprint.Compute(selected.Evaluation with { Residuals = [] });
                    if (reloadIdentical != true) errors.Add("STAROPT reload differs.");
                }
                var success = verified.Any(item => item.Evaluation.MeetsTargets) && errors.Count == 0
                    && trials.All(trial => trial.State != FamilyTrialState.Failed);
                if (success) successfulRuns++;
                results.Add(new
                {
                    Specification = Path.GetFileName(path),
                    Seed = seed,
                    Success = success,
                    State = state,
                    SearchSeconds = seconds,
                    MeasuredSearchRays = rays,
                    ReportedEvaluationAccounting = accounting,
                    VerificationRays = measurement.RayCount - rays,
                    VerificationSeconds = clock.Elapsed.TotalSeconds - seconds,
                    AcceptedCandidates = verified.Count(item => item.Evaluation.MeetsTargets),
                    ReloadIdentical = reloadIdentical,
                    SelectedCandidateId = selected.Candidate?.CandidateId,
                    SelectedValidation = selected.Evaluation,
                    VerificationErrors = errors,
                    FailedTrials = trials.Count(trial => trial.State == FamilyTrialState.Failed)
                });
                await Save();
                Console.WriteLine($"{Path.GetFileName(path)} / {seed}: {(success ? "PASS" : "FAIL")} {rays} measured rays, {seconds:F2}s");
            }
        }
        return results.Count == paths.Length * seeds.Length ? 0 : 2;

        async Task Save()
        {
            var report = new
            {
                SchemaVersion = 1,
                Profile = legacy ? "legacy-hybrid-same-target-comparison" : "independent-holdout-v1",
                Algorithm = new AlgorithmIdentity(legacy ? "flat-to-usable-hybrid" : "strict-flat-family-search", legacy ? "3" : FlatStartAlgorithm.Version, "Managed CPU", true),
                CreatedUtc = created,
                UpdatedUtc = DateTimeOffset.UtcNow,
                Machine = new { OS = RuntimeInformation.OSDescription, Runtime = RuntimeInformation.FrameworkDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount },
                InputSha256 = inputs,
                BinarySha256 = binaries,
                ExpectedRuns = paths.Length * seeds.Length,
                CompletedRuns = results.Count,
                SuccessfulRuns = successfulRuns,
                FullProtocolExecuted = results.Count == paths.Length * seeds.Length,
                SearchAcceptanceGatePassed = (bool?)null,
                P5ReleaseAccepted = false,
                Scope = legacy
                    ? "Unchanged legacy objective with FlatStart removed only at its API boundary; every returned prescription is independently evaluated against all original full-target constraints. This does not certify legacy support for fixed back focus or edge-thickness optimization. A cancelled legacy run's reported evaluation count may omit interrupted refinement work; measured ray count still includes it."
                    : "Separate holdout evidence; never included in the original 12x5 gate and no success threshold is inferred.",
                RayCountMeaning = "Rays submitted to real sequential computation, including aiming and diagnostics, excluding cache hits. Independent validation/export costs are separate.",
                Results = results
            };
            var temporary = Path.Combine(output, "summary.json.tmp");
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(report, Json));
            File.Move(temporary, Path.Combine(output, "summary.json"), overwrite: true);
        }
    }

    private static DesignEvaluation Validate(InitialStructureSpecification spec, CandidateSnapshot candidate)
    {
        var optic = Optic.FromSnapshot(candidate.Optic);
        return new FlatStartDesignProblem(spec, candidate.Lineage.ElementCount, candidate.Optic)
            .Evaluate(optic, FlatStartDesignProblem.FullStage, true, CancellationToken.None);
    }
    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
