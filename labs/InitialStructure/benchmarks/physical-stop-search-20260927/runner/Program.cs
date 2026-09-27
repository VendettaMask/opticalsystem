using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Persistence;

var json = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
if (args.Length > 2 && args[2] == "--replay")
{
    await FrozenReplay.Run(args[0], args[1], json);
    return;
}
var specifications = JsonSerializer.Deserialize<InitialStructureSpecification[]>(File.ReadAllText(args[0]), json)!;
Directory.CreateDirectory(args[1]);
var summaries = new List<object>();
foreach (var original in specifications)
    foreach (var physical in new[] { false, true })
    {
        var spec = original with { FlatStart = original.FlatStart! with { UsePhysicalStop = physical } };
        var id = $"f{spec.FNumber}-{(physical ? "physical" : "legacy")}";
        var directory = Path.Combine(args[1], id);
        Directory.CreateDirectory(directory);
        var watch = Stopwatch.StartNew();
        var progress = 0;
        var search = await new FlatStartSearchService().RunAsync(spec, new() { MaximumDisplayedCandidates = 10 },
            checkpointSink: (checkpoint, _) =>
            {
                if (checkpoint.ChargedEvaluations >= progress + 2000)
                {
                    progress = checkpoint.ChargedEvaluations;
                    Console.WriteLine($"{id}: charged={progress}, trials={checkpoint.Trials.Count}, seconds={watch.Elapsed.TotalSeconds:F1}");
                }
                return ValueTask.CompletedTask;
            });
        FlatStartCheckpointValidation.Validate(search.Checkpoint);
        var checkpointPath = Path.Combine(directory, "search.family.json");
        await new FlatStartSearchCheckpointStore().SaveAsync(search.Checkpoint, checkpointPath);
        var restoredCheckpoint = await new FlatStartSearchCheckpointStore().LoadAsync(checkpointPath);
        if (ContentFingerprint.Compute(search.Checkpoint) != ContentFingerprint.Compute(restoredCheckpoint))
            throw new InvalidDataException("Checkpoint round-trip mismatch.");
        var verified = new List<object>();
        foreach (var candidate in search.Candidates)
        {
            var trial = search.Checkpoint.Trials.First(t => t.Candidate?.CandidateId == candidate.CandidateId);
            var optic = Optic.FromSnapshot(candidate.Optic);
            var fresh = new FlatStartDesignProblem(spec, trial.Family.ElementCount, candidate.Optic, trial.Family)
                .Evaluate(optic, FlatStartDesignProblem.FullStage, true, default, true);
            if (ContentFingerprint.Compute(fresh with { Residuals = [] }) != ContentFingerprint.Compute(trial.FinalValidation!)
                || ContentFingerprint.Compute(fresh.EvaluatedOptic!) != candidate.OpticFingerprint)
                throw new InvalidDataException($"Same-settings recalculation mismatch: {candidate.CandidateId}");
            var export = Path.Combine(directory, candidate.CandidateId + ".staropt");
            await new CandidateExportService().ExportStarOptAsync(candidate, export);
            var loaded = await StarOptProjectStore.LoadAsync(export);
            if (ContentFingerprint.Compute(loaded.Configurations[0].ToSnapshot()) != candidate.OpticFingerprint)
                throw new InvalidDataException("STAROPT round-trip mismatch.");
            var expectedPupil = optic.Paraxial.EstimateEntrancePupilDiameter();
            var floated = Optic.FromSnapshot(optic.ToSnapshot());
            floated.Aperture.Kind = ApertureKind.FloatByStopSize;
            var pupilError = Math.Abs(floated.Paraxial.EstimateEntrancePupilDiameter() - expectedPupil);
            var calibrated = !candidate.Violations.Any(v => v.Code is "geometry.stop-calibration" or "geometry.stop-outside-body");
            if (physical && (!optic.RayAimingEnabled || calibrated && pupilError > 1e-9))
                throw new InvalidDataException("Physical stop does not preserve the declared pupil.");
            verified.Add(new
            {
                candidate.CandidateId,
                candidate.OpticFingerprint,
                Status = candidate.Status.ToString(),
                candidate.Lineage.ElementCount,
                candidate.Evaluation,
                candidate.Violations,
                SameSettingsRecalculationIdentical = true,
                ExportIdentical = true,
                FloatedPupilErrorMillimeters = pupilError,
                Stop = candidate.Optic.Surfaces.Single(s => s.IsStop),
                ExportSha256 = Hash(export)
            });
        }
        summaries.Add(new
        {
            Id = id,
            Specification = spec,
            Algorithm = search.Checkpoint.Algorithm,
            State = search.Checkpoint.State.ToString(),
            search.Checkpoint.ChargedEvaluations,
            search.Checkpoint.TracedRealRayCount,
            ElapsedSeconds = watch.Elapsed.TotalSeconds,
            Accepted = search.Candidates.Count(c => c.Status == CandidateStatus.LabAccepted),
            Retained = search.Candidates.Count,
            Trials = search.Checkpoint.Trials.Count,
            FailedTrials = search.Checkpoint.Trials.Where(t => t.State == FamilyTrialState.Failed).Select(t => new { t.TrialId, t.Diagnostics }),
            BestWorstRmsMillimeters = search.Candidates.Where(c => c.Status != CandidateStatus.Rejected)
                .Select(c => c.Evaluation.RmsSpotRadiusMillimeters).Min(),
            CheckpointSha256 = Hash(checkpointPath),
            Candidates = verified
        });
        File.WriteAllText(Path.Combine(args[1], "summary.json"), JsonSerializer.Serialize(new
        {
            InputSha256 = Hash(args[0]),
            CoreSha256 = Hash(typeof(Optic).Assembly.Location),
            EngineSha256 = Hash(typeof(FlatStartSearchService).Assembly.Location),
            Runs = summaries
        }, json));
        Console.WriteLine($"DONE {id}: {search.Checkpoint.ChargedEvaluations} evaluations, {search.Candidates.Count(c => c.Status == CandidateStatus.LabAccepted)}/{search.Candidates.Count} accepted, {watch.Elapsed.TotalSeconds:F1}s");
    }

static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
