using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Engine.Optimization;
using OptilandWorkbench.InitialStructure.Persistence;

internal static class SearchVerification
{
    public static async Task Run(string[] args)
    {
        var json = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var inputs = JsonSerializer.Deserialize<Input[]>(File.ReadAllText(args[0]), json)!;
        if (args.Length > 2 && args[2] == "--verify")
        {
            var verified = new List<object>();
            foreach (var path in Directory.GetFiles(args[1], "*.family.json").Order())
            {
                var checkpoint = await new FlatStartSearchCheckpointStore().LoadAsync(path);
                var candidates = FlatStartSearchService.SelectCandidates(checkpoint);
                foreach (var candidate in candidates)
                {
                    var trial = checkpoint.Trials.First(t => t.Candidate?.CandidateId == candidate.CandidateId);
                    var problem = new FlatStartDesignProblem(checkpoint.Specification, trial.Family.ElementCount, candidate.Optic, trial.Family);
                    var fresh = problem.Evaluate(Optic.FromSnapshot(candidate.Optic), FlatStartDesignProblem.FullStage, true, default, true);
                    // Search checkpoints intentionally omit solve-local residual vectors.
                    // Rebuild their sum, then compare every published quantity and the exact snapshot.
                    if (fresh.Residuals.Sum(r => r * r) != fresh.Merit
                        || ContentFingerprint.Compute(fresh with { Residuals = [] }) != ContentFingerprint.Compute(trial.FinalValidation!)
                        || ContentFingerprint.Compute(fresh.EvaluatedOptic!) != candidate.OpticFingerprint)
                    {
                        File.WriteAllText(Path.Combine(args[1], "recalculation-mismatch.json"), JsonSerializer.Serialize(new
                        {
                            candidate.CandidateId,
                            trial.TrialId,
                            Expected = trial.FinalValidation,
                            Actual = fresh,
                            ExpectedSnapshot = candidate.Optic,
                            ActualSnapshot = fresh.EvaluatedOptic,
                            ExpectedFingerprint = candidate.OpticFingerprint,
                            ActualFingerprint = ContentFingerprint.Compute(fresh.EvaluatedOptic!)
                        }, json));
                        throw new Exception("Saved-candidate recalculation mismatch.");
                    }
                    var exportPath = Path.Combine(args[1], candidate.CandidateId + ".staropt");
                    await new CandidateExportService().ExportStarOptAsync(candidate, exportPath);
                    var loaded = await StarOptProjectStore.LoadAsync(exportPath);
                    var exportedHash = ContentFingerprint.Compute(loaded.Configurations[loaded.ActiveConfigurationIndex].ToSnapshot());
                    if (exportedHash != candidate.OpticFingerprint) throw new Exception("Export round-trip mismatch.");
                    var convergence = Enumerable.Range(0, 41).Select(index =>
                    {
                        var optic = Optic.FromSnapshot(candidate.Optic);
                        var check = SpotMetricEvaluator.EvaluatePupilSamplesWithChecks(optic, 0, index / 40.0,
                            ApertureSampler.GenerateGaussianQuadrature(16, 48),
                            new[] { new PupilSample(0, 0, 1) }.Concat(ApertureSampler.Generate(72, PupilSampling.Ring)).ToArray(),
                            includeSurfaceTransmission: false);
                        return new
                        {
                            Field = index / 40.0,
                            Rms = check.Integration.Metrics?.RmsSpotRadius,
                            check.MaximumRadius,
                            Lost = check.Integration.VignettedRayCount + check.Checks.VignettedRayCount
                        };
                    }).ToArray();
                    verified.Add(new
                    {
                        checkpoint.RunId,
                        candidate.CandidateId,
                        candidate.OpticFingerprint,
                        fresh.MeetsTargets,
                        RecalculationIdentical = true,
                        ExportIdentical = true,
                        ExportHash = Hash(exportPath),
                        Convergence = convergence
                    });
                }
            }
            File.WriteAllText(Path.Combine(args[1], "recalculation.json"), JsonSerializer.Serialize(verified, json));
            Console.WriteLine($"Verified {verified.Count} saved candidates and STAROPT exports.");
            return;
        }
        if (args.Length > 2 && args[2] is "--search" or "--wide-budget")
        {
            Directory.CreateDirectory(args[1]);
            var summaries = new List<object>();
            foreach (var input in args[2] == "--wide-budget" ? new[] { inputs[0] } : new[] { inputs[0], inputs[6] })
            {
                var specification = input.Specification with
                {
                    FlatStart = input.Specification.FlatStart! with { SamplingPolicy = FlatStartSamplingPolicy.UniformAreaGaussianV1 },
                    Budget = input.Specification.Budget with { MaximumParallelism = 1, MaximumEvaluations = args[2] == "--wide-budget" ? int.Parse(args[3]) : 10000, TimeLimit = TimeSpan.FromMinutes(args[2] == "--wide-budget" ? 20 : 10) }
                };
                var search = await new FlatStartSearchService().RunAsync(specification, new() { MaximumDisplayedCandidates = 10, DesignSearch = new() { Mode = DesignSearchMode.Full } });
                FlatStartCheckpointValidation.Validate(search.Checkpoint);
                var path = Path.Combine(args[1], $"f{specification.FNumber}.family.json");
                await new FlatStartSearchCheckpointStore().SaveAsync(search.Checkpoint, path);
                summaries.Add(new
                {
                    specification,
                    State = search.Checkpoint.State.ToString(),
                    search.Checkpoint.ChargedEvaluations,
                    search.Checkpoint.TracedRealRayCount,
                    Trials = search.Checkpoint.Trials.Count,
                    Retained = search.Candidates.Count,
                    Accepted = search.Candidates.Count(c => c.Status == CandidateStatus.LabAccepted),
                    Operations = search.Checkpoint.Trials.GroupBy(t => t.Operation).ToDictionary(g => g.Key, g => g.Count()),
                    ReducedStages = search.Checkpoint.Trials.Sum(t => t.Stages.Count(s => s.Stage.PupilFraction < 1)),
                    Candidates = search.Candidates.Select(c => new { c.CandidateId, Status = c.Status.ToString(), c.Lineage.ElementCount, c.Evaluation, c.Violations }),
                    File = Path.GetFileName(path),
                    Sha256 = Hash(path)
                });
                File.WriteAllText(Path.Combine(args[1], "summary.json"), JsonSerializer.Serialize(summaries, json));
                Console.WriteLine($"Search F/{specification.FNumber}: {search.Checkpoint.ChargedEvaluations} evaluations; {search.Candidates.Count(c => c.Status == CandidateStatus.LabAccepted)}/{search.Candidates.Count} retained accepted.");
            }
            return;
        }
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
