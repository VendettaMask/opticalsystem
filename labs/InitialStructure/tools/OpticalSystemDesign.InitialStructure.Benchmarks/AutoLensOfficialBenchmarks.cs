using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

internal static class AutoLensOfficialBenchmarks
{
    public static async Task<int> Run(string[] args)
    {
        if (args.Length is not (1 or 2))
            throw new ArgumentException("--autolens-official <new output directory> [exact preset id]");
        var output = Path.GetFullPath(args[0]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Use a new output directory; previous evidence is never overwritten.");
        var presets = AutoLensOfficialExamples.Create();
        var selected = args.Length == 2 ? presets.Where(preset => preset.Id == args[1]).ToArray() : presets.ToArray();
        if (selected.Length == 0) throw new ArgumentException("Unknown preset id.");
        Directory.CreateDirectory(output);
        var json = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var runs = new List<OfficialExampleRun>();
        var expectedRuns = selected.Sum(preset => preset.ValidationSeeds.Count);
        var created = DateTimeOffset.UtcNow;
        var binaries = new[] { typeof(Optic).Assembly, typeof(AutoLensOfficialExamples).Assembly,
            typeof(FlatStartSearchService).Assembly, typeof(CandidateExportService).Assembly, typeof(AutoLensOfficialBenchmarks).Assembly }
            .ToDictionary(assembly => Path.GetFileName(assembly.Location), assembly => Hash(assembly.Location));
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            await SaveSummary();
            foreach (var preset in selected)
                foreach (var seed in preset.ValidationSeeds)
                {
                    if (cancellation.IsCancellationRequested) return 3;
                    var directory = Path.Combine(output, preset.Id, "seed-" + seed);
                    Directory.CreateDirectory(directory);
                    var spec = preset.Specification with { Budget = preset.Specification.Budget with { RandomSeed = seed } };
                    await Save(Path.Combine(directory, "input.json"), new { Preset = preset, Specification = spec, preset.Options });
                    var watch = Stopwatch.StartNew();
                    FlatStartSearchResult? search = null;
                    var errors = new List<string>();
                    try
                    {
                        search = await new FlatStartSearchService().RunAsync(spec, preset.Options, cancellation.Token,
                            checkpointSink: async (checkpoint, token) =>
                                await new FlatStartSearchCheckpointStore().SaveAsync(checkpoint, Path.Combine(directory, "search.family.json"), token));
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        errors.Add(exception.ToString());
                    }
                    var searchSeconds = watch.Elapsed.TotalSeconds;
                    FamilyTrial? chosen = search?.Checkpoint.Trials.Where(trial => trial.Candidate is not null)
                        .OrderBy(trial => FlatStartCandidateArchive.Rank(trial.Candidate!.Status))
                        .ThenBy(trial => FlatStartCandidateArchive.Score(spec, trial.Candidate!)).FirstOrDefault();
                    DesignEvaluation? independent = null;
                    bool? snapshotIdentical = null, replayIdentical = null, exportIdentical = null;
                    long verificationRays = 0;
                    if (chosen?.Candidate is { } candidate)
                    {
                        await Save(Path.Combine(directory, "selected-candidate.json"), candidate);
                        try
                        {
                            var problem = new FlatStartDesignProblem(spec, chosen.Family.ElementCount, candidate.Optic, chosen.Family);
                            independent = problem.Evaluate(Optic.FromSnapshot(candidate.Optic), FlatStartDesignProblem.FullStage, true, default, true);
                            verificationRays += problem.TracedRayCount;
                            replayIdentical = ContentFingerprint.Compute(independent with { Residuals = [] })
                                == ContentFingerprint.Compute(chosen.FinalValidation! with { Residuals = [] });
                            snapshotIdentical = ContentFingerprint.Compute(independent.EvaluatedOptic!) == candidate.OpticFingerprint;
                            if (replayIdentical != true || snapshotIdentical != true) errors.Add("Independent full-target replay differs from the selected result.");
                            var export = await new CandidateExportService().ExportStarOptAsync(candidate, Path.Combine(directory, "selected.staropt"));
                            var restored = await StarOptProjectStore.LoadAsync(export);
                            var optic = restored.Configurations[restored.ActiveConfigurationIndex];
                            exportIdentical = ContentFingerprint.Compute(optic.ToSnapshot()) == candidate.OpticFingerprint;
                            var reloadProblem = new FlatStartDesignProblem(spec, chosen.Family.ElementCount, candidate.Optic, chosen.Family);
                            var reloaded = reloadProblem.Evaluate(optic, FlatStartDesignProblem.FullStage, true, default, true);
                            verificationRays += reloadProblem.TracedRayCount;
                            if (exportIdentical != true || ContentFingerprint.Compute(reloaded with { Residuals = [] })
                                != ContentFingerprint.Compute(independent with { Residuals = [] })) errors.Add("STAROPT round-trip replay differs.");
                            await Save(Path.Combine(directory, "independent.json"), independent);
                            await Save(Path.Combine(directory, "reload.json"), reloaded);
                        }
                        catch (Exception exception) when (exception is not OutOfMemoryException) { errors.Add(exception.ToString()); }
                    }
                    var checkpoint = search?.Checkpoint;
                    var passed = independent?.MeetsTargets == true && chosen?.Candidate?.Status == CandidateStatus.LabAccepted
                        && errors.Count == 0 && snapshotIdentical == true && replayIdentical == true && exportIdentical == true;
                    var fields = independent?.Fields ?? [];
                    var rms = fields.Count > 0 && fields.All(field => field.RmsRadiusMillimeters.HasValue)
                        ? fields.Max(field => field.RmsRadiusMillimeters) : null;
                    var radius = fields.Count > 0 && fields.All(field => field.MaximumRadiusMillimeters.HasValue)
                        ? fields.Max(field => field.MaximumRadiusMillimeters) : null;
                    runs.Add(new(preset.Id, seed, passed, checkpoint?.State.ToString() ?? "EngineError",
                        chosen?.Candidate?.Status.ToString(), checkpoint?.ChargedEvaluations ?? 0,
                        checkpoint?.TracedRealRayCount ?? 0, searchSeconds, watch.Elapsed.TotalSeconds - searchSeconds,
                        verificationRays, independent?.EffectiveFocalLengthMillimeters, independent?.FNumber, rms, radius,
                        independent?.Violations ?? [], snapshotIdentical, replayIdentical, exportIdentical,
                        checkpoint?.Trials.Select(trial => new OfficialTrialWork(trial.TrialId, trial.Operation, trial.State.ToString(),
                            trial.CompletedEvaluations, trial.AutoLensState?.Phase.ToString(), trial.AutoLensState?.Adam.Iteration,
                            trial.AutoLensState?.TrainingSteps.Count, trial.AutoLensState?.PlannedCurriculumIterations,
                            trial.AutoLensState?.PlannedRefinementIterations)).ToArray() ?? [], errors));
                    await Save(Path.Combine(directory, "result.json"), runs[^1]);
                    await SaveSummary();
                    Console.WriteLine($"{runs.Count}/{expectedRuns} {preset.Id} seed={seed}: {(passed ? "DESIGN PASS" : "TARGETS NOT MET")}, "
                        + $"{runs[^1].Evaluations} evaluations, {searchSeconds:F1}s, RMS={rms}, violations={independent?.Violations.Count}, errors={errors.Count}");
                }
            return runs.Count == expectedRuns && runs.All(run => run.DesignAccepted) ? 0 : 2;
        }
        finally { Console.CancelKeyPress -= cancel; }

        async Task SaveSummary() => await Save(Path.Combine(output, "summary.json"), new
        {
            Protocol = AutoLensOfficialExamples.ProtocolVersion,
            AutoLensOfficialExamples.UpstreamCommit,
            AutoLensOfficialExamples.SourceUrl,
            CreatedUtc = created,
            UpdatedUtc = DateTimeOffset.UtcNow,
            Machine = new { OS = RuntimeInformation.OSDescription, Runtime = RuntimeInformation.FrameworkDescription, Environment.ProcessorCount },
            BinarySha256 = binaries,
            PresetFingerprint = ContentFingerprint.Compute(presets),
            Presets = selected,
            ExpectedRuns = expectedRuns,
            CompletedRuns = runs.Count,
            CompleteRequestedProtocol = runs.Count == expectedRuns,
            CompleteCatalogProtocol = args.Length == 1 && runs.Count == expectedRuns,
            AcceptedDesigns = runs.Count(run => run.DesignAccepted),
            AllRequestedDesignsAccepted = runs.Count == expectedRuns && runs.All(run => run.DesignAccepted),
            UpstreamNumericalEquivalence = false,
            OfficialEightUltraPrescriptionsReproduced = false,
            ActualSensorDiagonalCoverageCertified = false,
            Scope = "Native CPU target adaptation. 4000 evaluations/60 seconds per independent seed; full-aperture dense physical replay and STAROPT replay are outside search budgets. Original upstream prescriptions, seed ids and unspecified parameters are not invented. Failed candidates remain failed.",
            Results = runs
        });
        async Task Save<T>(string path, T value)
        {
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, json));
            File.Move(temporary, path, overwrite: true);
        }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private sealed record OfficialTrialWork(string Id, string Operation, string State, int? Evaluations,
        string? Phase, int? PhaseIteration, int? AcceptedAdamSteps, int? PlannedCurriculumIterations, int? PlannedRefinementIterations);
    private sealed record OfficialExampleRun(string Preset, long Seed, bool DesignAccepted, string SearchState,
        string? CandidateStatus, int Evaluations, long SearchRays, double SearchSeconds, double VerificationSeconds,
        long VerificationRays, double? EflMillimeters, double? FNumber, double? WorstRmsMillimeters, double? MaximumRadiusMillimeters,
        IReadOnlyList<ConstraintViolation> Violations, bool? SnapshotIdentical, bool? IndependentReplayIdentical, bool? StarOptSnapshotIdentical,
        IReadOnlyList<OfficialTrialWork> Trials, IReadOnlyList<string> VerificationErrors);
}
