using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Persistence;

if (args.Length is not (2 or 4))
{
    Console.Error.WriteLine("Usage: InitialStructure.Benchmarks <flat-start-v1 directory> <new output directory> [exact-specification-filename seed]");
    return 1;
}
var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
    throw new IOException("Use a new output directory; existing evidence is never overwritten.");
var json = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
var protocolPath = Path.Combine(input, "protocol.json");
using var protocol = JsonDocument.Parse(await File.ReadAllTextAsync(protocolPath));
var frozen = protocol.RootElement;
var seeds = frozen.GetProperty("RandomSeeds").EnumerateArray().Select(value => value.GetInt64()).ToArray();
var specifications = Directory.GetFiles(Path.Combine(input, "specs"), "*.json").Order(StringComparer.Ordinal).ToArray();
var sampling = frozen.GetProperty("FinalValidation");
if (frozen.GetProperty("Version").GetInt32() != 1 || specifications.Length != 12
    || frozen.GetProperty("SpecificationCount").GetInt32() != 12
    || !seeds.SequenceEqual(new long[] { 101, 102, 103, 104, 105 })
    || frozen.GetProperty("MaximumEvaluationsPerRun").GetInt32() != 10_000
    || frozen.GetProperty("TimeLimitSeconds").GetInt32() != 600
    || frozen.GetProperty("RequiredSuccessfulSeedsPerSpecification").GetInt32() != 4
    || frozen.GetProperty("RequiredSuccessfulSpecifications").GetInt32() != 10
    || !sampling.GetProperty("NormalizedFields").EnumerateArray().Select(value => value.GetDouble()).SequenceEqual(new[] { 0, .25, .5, .7, .85, 1 })
    || sampling.GetProperty("PupilRings").GetInt32() != 6 || sampling.GetProperty("PupilAngles").GetInt32() != 16
    || !sampling.GetProperty("IncludePupilBoundary").GetBoolean() || !sampling.GetProperty("AllPositiveWeightWavelengths").GetBoolean()
    || !sampling.GetProperty("PerFieldGates").GetBoolean()
    || !frozen.GetProperty("AllowedMaterialChoices").EnumerateArray().Select(value => value.GetString()).SequenceEqual(new[] { "N-BK7", "N-F2", "N-SF6" }))
    throw new InvalidDataException("This runner implements only the frozen flat-start-v1 protocol.");
var inputs = specifications.Prepend(protocolPath).ToDictionary(path => Path.GetRelativePath(input, path).Replace('\\', '/'), Hash);
using var expectedStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("OptilandWorkbench.InitialStructure.Benchmarks.FrozenInputs.json")!;
var expectedInputs = JsonSerializer.Deserialize<Dictionary<string, string>>(expectedStream)!;
foreach (var entry in expectedInputs)
{
    var text = File.ReadAllText(Path.Combine(input, entry.Key)).Replace("\r\n", "\n", StringComparison.Ordinal);
    var canonicalHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    if (canonicalHash != entry.Value) throw new InvalidDataException($"Frozen input was changed: {entry.Key}");
}
var selectedSpecifications = specifications;
var selectedSeeds = seeds;
if (args.Length == 4)
{
    selectedSpecifications = specifications.Where(path => Path.GetFileName(path) == args[2]).ToArray();
    if (selectedSpecifications.Length != 1 || !long.TryParse(args[3], out var seed) || !seeds.Contains(seed))
        throw new ArgumentException("Choose an exact frozen specification filename and a protocol seed (101–105).");
    selectedSeeds = [seed];
}
Directory.CreateDirectory(output);
var runs = new List<RunResult>();
var created = DateTimeOffset.UtcNow;
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var binaries = new[] { typeof(Optic).Assembly, typeof(FlatStartSearchService).Assembly, typeof(CandidateSnapshot).Assembly, Assembly.GetExecutingAssembly() }
    .ToDictionary(assembly => assembly.GetName().Name!, assembly => Hash(assembly.Location));
await SaveSummary();
foreach (var path in selectedSpecifications)
{
    var template = JsonSerializer.Deserialize<InitialStructureSpecification>(await File.ReadAllTextAsync(path), json)!;
    if (template.Budget.MaximumEvaluations != 10_000 || template.Budget.TimeLimit != TimeSpan.FromSeconds(600)
        || template.Budget.MaximumParallelism != 1 || template.FlatStart is null)
        throw new InvalidDataException("Frozen specifications must retain the protocol budget, serial execution and full-target gates.");
    foreach (var seed in selectedSeeds)
    {
        if (cancellation.IsCancellationRequested) break;
        var spec = template with { Budget = template.Budget with { RandomSeed = seed } };
        var directory = Path.Combine(output, Path.GetFileNameWithoutExtension(path), seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        var clock = Stopwatch.StartNew();
        double? firstAccepted = null;
        var result = await new FlatStartSearchService().RunAsync(spec, cancellationToken: cancellation.Token, checkpointSink: (checkpoint, _) =>
        {
            if (firstAccepted is null && checkpoint.Trials.Any(trial => trial.Candidate?.Status == CandidateStatus.LabAccepted))
                firstAccepted = TimeSpan.FromTicks(checkpoint.ElapsedComputeTicks).TotalSeconds;
            return ValueTask.CompletedTask;
        });
        var searchSeconds = clock.Elapsed.TotalSeconds;
        FlatStartCheckpointValidation.Validate(result.Checkpoint);
        await new FlatStartSearchCheckpointStore().SaveAsync(result.Checkpoint, Path.Combine(directory, "checkpoint.json"));
        var candidates = result.Checkpoint.Trials.Where(trial => trial.Candidate is not null)
            .DistinctBy(trial => trial.Candidate!.OpticFingerprint).ToArray();
        var accepted = new List<FamilyTrial>();
        long verificationRays = 0;
        var verificationErrors = new List<string>();
        foreach (var trial in candidates.Where(trial => trial.Candidate!.Status == CandidateStatus.LabAccepted))
        {
            var problem = new FlatStartDesignProblem(spec, trial.Family.ElementCount, trial.Candidate!.Optic, trial.Family);
            var independent = problem.Evaluate(Optic.FromSnapshot(trial.Candidate.Optic), FlatStartDesignProblem.FullStage, true, CancellationToken.None);
            verificationRays += problem.TracedRayCount;
            if (independent.MeetsTargets && ContentFingerprint.Compute(independent with { Residuals = [] }) == ContentFingerprint.Compute(trial.FinalValidation!))
                accepted.Add(trial);
            else verificationErrors.Add(trial.TrialId + ": independent validation disagrees");
        }
        var selected = accepted.OrderBy(trial => FlatStartCandidateArchive.Score(spec, trial.Candidate!)).FirstOrDefault()
            ?? candidates.OrderBy(trial => FlatStartCandidateArchive.Rank(trial.Candidate!.Status))
                .ThenBy(trial => FlatStartCandidateArchive.Score(spec, trial.Candidate!)).FirstOrDefault();
        double? reloadError = null;
        bool? reloadIdentical = null;
        if (selected is not null)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "selected-candidate.json"), JsonSerializer.Serialize(selected.Candidate, json));
            var export = await new CandidateExportService().ExportStarOptAsync(selected.Candidate!, Path.Combine(directory, "selected.staropt"));
            var document = await StarOptProjectStore.LoadAsync(export);
            var optic = document.Configurations[document.ActiveConfigurationIndex];
            reloadIdentical = ContentFingerprint.Compute(optic.ToSnapshot()) == selected.Candidate!.OpticFingerprint;
            var problem = new FlatStartDesignProblem(spec, selected.Family.ElementCount, optic.ToSnapshot(), selected.Family);
            var reloaded = problem.Evaluate(optic, FlatStartDesignProblem.FullStage, true, CancellationToken.None);
            verificationRays += problem.TracedRayCount;
            reloadError = reloaded.Fields.Zip(selected.FinalValidation!.Fields)
                .SelectMany(pair => new[] { Error(pair.First.RmsRadiusMillimeters, pair.Second.RmsRadiusMillimeters),
                    Error(pair.First.MaximumRadiusMillimeters, pair.Second.MaximumRadiusMillimeters) }).Max();
            if (reloadIdentical != true || reloadError != 0) verificationErrors.Add("STAROPT reload differs from the selected candidate.");
        }
        var failedTrials = result.Checkpoint.Trials.Count(trial => trial.State == FamilyTrialState.Failed);
        runs.Add(new(Path.GetFileName(path), seed,
            accepted.Count > 0 && verificationErrors.Count == 0 && failedTrials == 0,
            result.Checkpoint.State.ToString(), result.Checkpoint.ChargedEvaluations, result.Checkpoint.TracedRealRayCount,
            searchSeconds, firstAccepted, accepted.Count, accepted.Select(trial => FlatStartCandidateArchive.FamilyKey(trial.Candidate!)).Distinct().Count(),
            selected?.Candidate?.CandidateId, selected?.FinalValidation, verificationRays, clock.Elapsed.TotalSeconds - searchSeconds,
            reloadIdentical, reloadError, failedTrials, verificationErrors));
        await SaveSummary();
        Console.WriteLine($"{Path.GetFileName(path)} / {seed}: {(runs[^1].Success ? "PASS" : "FAIL")} {result.Checkpoint.ChargedEvaluations} evaluations, {result.Checkpoint.TracedRealRayCount} rays, {searchSeconds:F2}s");
    }
}
await SaveSummary();
return SearchGate() ? 0 : 2;

bool SearchGate() => runs.Count == 60 && runs.GroupBy(run => run.Specification).Count(group => group.Count(run => run.Success) >= 4) >= 10;
async Task SaveSummary()
{
    var summary = new
    {
        SchemaVersion = 1,
        Algorithm = new AlgorithmIdentity("strict-flat-family-search", "4", "Managed CPU", true),
        CreatedUtc = created,
        UpdatedUtc = DateTimeOffset.UtcNow,
        Machine = new { OS = RuntimeInformation.OSDescription, Runtime = RuntimeInformation.FrameworkDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount },
        InputSha256 = inputs,
        BinarySha256 = binaries,
        ExpectedRuns = 60,
        CompletedRuns = runs.Count,
        FullProtocolExecuted = runs.Count == 60,
        RequiredSuccessfulSpecifications = 10,
        RequiredSuccessfulSeedsPerSpecification = 4,
        SearchAcceptanceGatePassed = SearchGate(),
        P5ReleaseAccepted = false,
        RemainingReleaseEvidence = "This runner establishes the search gate only. Old-prototype comparison, new-prescription external numerical validation and engineering checks remain separate requirements.",
        FirstAcceptedTimeMeaning = "First completed search batch reporting a dense-validated accepted candidate; null means none. Independent verification follows outside the search budget.",
        Results = runs
    };
    var temporary = Path.Combine(output, "summary.json.tmp");
    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(summary, json));
    File.Move(temporary, Path.Combine(output, "summary.json"), overwrite: true);
}
static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
static double Error(double? left, double? right) => left == right ? 0
    : left is { } a && right is { } b ? Math.Abs(a - b) : double.PositiveInfinity;

internal sealed record RunResult(string Specification, long Seed, bool Success, string State, int Evaluations, long SearchRays,
    double SearchSeconds, double? FirstAcceptedSeconds, int AcceptedDistinctCandidates, int AcceptedFamilies,
    string? SelectedCandidateId, DesignEvaluation? SelectedValidation, long VerificationRays, double VerificationSeconds,
    bool? ReloadSnapshotIdentical, double? ReloadMaximumSpotErrorMillimeters, int FailedTrials, IReadOnlyList<string> VerificationErrors);
