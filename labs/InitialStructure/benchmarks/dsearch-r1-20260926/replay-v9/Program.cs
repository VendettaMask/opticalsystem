using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Persistence;

// Independent mechanism replay, not a release acceptance protocol. Every ray,
// diagnostic and acceptance quantity is returned by the formal Core adapter.
var options = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
var inputs = JsonSerializer.Deserialize<Input[]>(File.ReadAllText(args[0]), options)!;
var results = new List<object>();
foreach (var input in inputs)
{
    var problem = new FlatStartDesignProblem(input.Specification, 4, input.Initial, input.Family);
    var initial = problem.Vector(input.Initial);
    DesignEvaluation Evaluate(double[] values) => problem.Evaluate(problem.CreateOptic(values, FlatStartDesignProblem.FullStage),
        FlatStartDesignProblem.FullStage, true, default);
    var entry = Evaluate(initial);
    var invalid = 0;
    var clippedContinuous = 0;
    var jacobians = 0;
    var accepted = 0;
    var result = FlatStartLocalSolver.Solve(problem.SolverCoordinates, initial, entry, 600,
        point =>
        {
            var evaluation = Evaluate(point);
            if (!evaluation.HasContinuousSearchResiduals || !evaluation.GeometryFeasible) invalid++;
            if (evaluation.HasContinuousSearchResiduals && !evaluation.IsFeasible) clippedContinuous++;
            return evaluation;
        }, true, () => false, default, (_, _, _) => accepted++, (_, stats) => jacobians += stats.JacobianBuildCount,
        () => problem.TracedRayCount);
    var final = Evaluate(result.Variables);
    var reference = input.Reference.GetProperty("Final");
    var matches = final.Merit == reference.GetProperty("Merit").GetDouble()
        && final.MeetsTargets == reference.GetProperty("MeetsTargets").GetBoolean()
        && final.EffectiveFocalLengthMillimeters == reference.GetProperty("EffectiveFocalLengthMillimeters").GetDouble()
        && result.Evaluations == input.Reference.GetProperty("EvaluationCount").GetInt32();
    if (!matches) throw new InvalidOperationException("Runtime solver differs from the isolated continuous-domain audit.");
    results.Add(new
    {
        Focal = input.Specification.EffectiveFocalLengthMillimeters,
        input.Family.BinaryStart!.Signs,
        InitialOpticSha256 = ContentFingerprint.Compute(input.Initial),
        result.Evaluations,
        Termination = result.Termination.ToString(),
        Jacobians = jacobians,
        AcceptedSteps = accepted,
        InvalidEvaluations = invalid,
        ContinuousClippedEvaluations = clippedContinuous,
        MatchesIsolatedAuditExactly = matches,
        Final = final with { Residuals = [] }
    });
    Console.WriteLine($"{input.Specification.EffectiveFocalLengthMillimeters} {input.Family.BinaryStart.Signs}: {result.Termination}; physical pass={final.MeetsTargets}; exact audit match={matches}");
}
var originalHash = Hash(args[1]);
var historical = await new FlatStartSearchCheckpointStore().LoadAsync(args[1]);
var selected = FlatStartSearchService.SelectCandidates(historical)[0];
var child = FlatStartSearchService.CreateRefinementCheckpoint(historical, selected.CandidateId, 120, TimeSpan.FromMinutes(1));
var continued = await new FlatStartSearchService().RunAsync(child.Specification, child.Options, checkpoint: child);
FlatStartCheckpointValidation.Validate(continued.Checkpoint);
if (continued.Candidates.Count == 0 || continued.Candidates.Any(c => c.Evaluation.FlatStartObjective is null)
    || originalHash != Hash(args[1])) throw new InvalidOperationException("Historical refinement failed.");
File.WriteAllText(args[2], JsonSerializer.Serialize(new
{
    Scope = "R1 runtime mechanism verification on eight unchanged independent starts. Not the original 60 or holdout 15 acceptance protocol.",
    Algorithm = FlatStartAlgorithm.Version,
    EngineSha256 = Hash(typeof(FlatStartDesignService).Assembly.Location),
    CoreSha256 = Hash(typeof(OptilandWorkbench.Core.Optic).Assembly.Location),
    InputSha256 = Hash(args[0]),
    BudgetPerSolve = 600,
    Results = results,
    HistoricalImport = new
    {
        Version = historical.Algorithm.Version,
        SourceSha256 = originalHash,
        SourceUnchanged = originalHash == Hash(args[1]),
        NewVersion = continued.Checkpoint.Algorithm.Version,
        continued.Checkpoint.ChargedEvaluations,
        NewCandidates = continued.Candidates.Select(c => new { c.Status, c.Evaluation.FlatStartObjective })
    }
}, options) + "\n");

static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
internal sealed record Input(InitialStructureSpecification Specification, FlatStartFamily Family, OpticSnapshot Initial, JsonElement Reference);
