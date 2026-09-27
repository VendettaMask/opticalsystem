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
            verified.Add(new
            {
                checkpoint.RunId,
                candidate.CandidateId,
                candidate.OpticFingerprint,
                fresh.MeetsTargets,
                RecalculationIdentical = true,
                ExportIdentical = true,
                ExportHash = Hash(exportPath)
            });
        }
    }
    File.WriteAllText(Path.Combine(args[1], "recalculation.json"), JsonSerializer.Serialize(verified, json));
    Console.WriteLine($"Verified {verified.Count} saved candidates and STAROPT exports.");
    return;
}
if (args.Length > 2 && args[2] == "--search")
{
    Directory.CreateDirectory(args[1]);
    var summaries = new List<object>();
    foreach (var input in new[] { inputs[0], inputs[6] })
    {
        var specification = input.Specification with
        {
            FlatStart = input.Specification.FlatStart! with { SamplingPolicy = FlatStartSamplingPolicy.UniformAreaGaussianV1 },
            Budget = input.Specification.Budget with { MaximumParallelism = 1, MaximumEvaluations = 10000, TimeLimit = TimeSpan.FromMinutes(10) }
        };
        var search = await new FlatStartSearchService().RunAsync(specification, new() { MaximumDisplayedCandidates = 10 });
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
var rows = new List<object>();
foreach (var input in inputs)
{
    var specification = input.Specification with
    { FlatStart = input.Specification.FlatStart! with { SamplingPolicy = FlatStartSamplingPolicy.UniformAreaGaussianV1 } };
    var problem = new FlatStartDesignProblem(specification, input.Family.ElementCount, input.Initial, input.Family);
    var initial = problem.Vector(input.Initial);
    var entry = Evaluate(initial);
    var best = entry;
    var bestVector = initial.ToArray();
    var statistics = new List<object>();
    var acceptedSteps = new List<object>();
    var result = FlatStartLocalSolver.Solve(problem.SolverCoordinates, initial, entry, 600, Evaluate, true,
        () => false, default, (vector, evaluation, phase) =>
        {
            acceptedSteps.Add(new { Phase = phase.ToString(), evaluation.Merit, evaluation.GeometryFeasible, evaluation.MeetsTargets });
            if (Rank(evaluation) < Rank(best) || Rank(evaluation) == Rank(best) && evaluation.Merit < best.Merit)
            { best = evaluation; bestVector = vector.ToArray(); }
        }, (phase, stats) => statistics.Add(new
        {
            Phase = phase.ToString(),
            Termination = stats.Termination.ToString(),
            stats.EvaluationCount,
            stats.DifferenceEvaluationCount,
            stats.JacobianBuildCount,
            stats.WorkUnits,
            stats.Trials
        }), () => problem.TracedRayCount);
    var fresh = Evaluate(bestVector);
    if (ContentFingerprint.Compute(best) != ContentFingerprint.Compute(fresh)) throw new Exception("Recalculation mismatch.");
    var validation = problem.Evaluate(Optic.FromSnapshot(best.EvaluatedOptic!), FlatStartDesignProblem.FullStage, true, default, true);
    var converged = Enumerable.Range(0, 41).Select(i =>
    {
        var optic = Optic.FromSnapshot(validation.EvaluatedOptic!);
        var assessment = SpotMetricEvaluator.EvaluatePupilSamplesWithChecks(optic, 0, i / 40.0,
            ApertureSampler.GenerateGaussianQuadrature(16, 48),
            new[] { new PupilSample(0, 0, 1) }.Concat(ApertureSampler.Generate(72, PupilSampling.Ring)).ToArray(),
            includeSurfaceTransmission: false);
        return new
        {
            Field = i / 40.0,
            Rms = assessment.Integration.Metrics?.RmsSpotRadius,
            assessment.MaximumRadius,
            Lost = assessment.Integration.VignettedRayCount + assessment.Checks.VignettedRayCount
        };
    }).ToArray();
    rows.Add(new
    {
        input.Id,
        input.Family.ElementCount,
        InputHash = ContentFingerprint.Compute(input.Initial),
        Entry = Summary(entry),
        Best = Summary(best),
        IndependentValidation = Summary(validation),
        BestSnapshot = validation.EvaluatedOptic,
        result.Evaluations,
        Termination = result.Termination.ToString(),
        RecalculationIdentical = true,
        Statistics = statistics,
        AcceptedSteps = acceptedSteps,
        Convergence = converged
    });
    File.WriteAllText(args[1], JsonSerializer.Serialize(new
    {
        Algorithm = FlatStartAlgorithm.Version,
        Scope = "Twelve frozen local cases, 600 solver evaluations each plus explicitly separate validation/convergence; not a full search success rate.",
        CoreHash = Hash(typeof(Optic).Assembly.Location),
        EngineHash = Hash(typeof(FlatStartDesignService).Assembly.Location),
        Results = rows
    }, json));
    Console.WriteLine($"n={input.Family.ElementCount}, F/{specification.FNumber}: merit {entry.Merit:G6}->{best.Merit:G6}; RMS {entry.Fields.Max(f => f.RmsRadiusMillimeters):G6}->{validation.Fields.Max(f => f.RmsRadiusMillimeters):G6}; pass={validation.MeetsTargets}; {result.Termination}; steps={acceptedSteps.Count}");

    DesignEvaluation Evaluate(double[] vector) => problem.Evaluate(problem.CreateOptic(vector, FlatStartDesignProblem.FullStage),
        FlatStartDesignProblem.FullStage, true, default);
}
static object Summary(DesignEvaluation e) => new
{
    e.Merit,
    e.MeetsTargets,
    e.GeometryFeasible,
    e.HasContinuousSearchResiduals,
    e.EffectiveFocalLengthMillimeters,
    e.FNumber,
    e.Fields,
    e.Violations,
    e.Objective
};
static int Rank(DesignEvaluation e) => e.MeetsTargets ? 0 : e.IsFeasible ? 1 : 2;
static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
internal sealed record Input(string Id, InitialStructureSpecification Specification, FlatStartFamily Family, OpticSnapshot Initial);
