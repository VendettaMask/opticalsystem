using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Engine.Optimization;
using OptilandWorkbench.InitialStructure.Persistence;

// Read-only diagnostics: all optics and geometry use the existing formal Core adapter.
var json = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
var output = args[0];
Directory.CreateDirectory(output);
var sources = new List<object>();
var inputs = new List<Input>();
foreach (var path in args.Skip(1))
{
    var checkpoint = await new FlatStartSearchCheckpointStore().LoadAsync(path);
    var selected = FlatStartSearchService.SelectCandidates(checkpoint);
    sources.Add(new
    {
        Path = path,
        Sha256 = Hash(path),
        checkpoint.RunId,
        checkpoint.Algorithm,
        checkpoint.State,
        checkpoint.Specification,
        checkpoint.ChargedEvaluations,
        Forms = DesignFormSearch.Preview(checkpoint.Specification, checkpoint.Options),
        Retained = selected.Select(c => new { c.CandidateId, c.Status, c.Evaluation }),
        Trials = checkpoint.Trials.Select(t => new
        {
            t.Index,
            t.Operation,
            t.ParentTrialId,
            t.Family,
            t.ChargedEvaluations,
            Status = t.Candidate?.Status,
            Evaluation = t.Candidate?.Evaluation,
            Stages = t.Stages.Select(s => new { s.Operation, s.Stage }),
            t.Diagnostics
        })
    });
    // Fixed selection: best dense-objective candidate for each element count, no post-hoc exclusion.
    foreach (var group in checkpoint.Trials.Where(t => t.Candidate is not null).GroupBy(t => t.Family.ElementCount))
    {
        var trial = group.OrderBy(t => FlatStartCandidateArchive.Rank(t.Candidate!.Status))
            .ThenBy(t => FlatStartCandidateArchive.Score(checkpoint.Specification, t.Candidate!)).ThenBy(t => t.Index).First();
        inputs.Add(new($"{checkpoint.RunId}/{trial.TrialId}", checkpoint.Specification, trial.Family, trial.Candidate!.Optic));
    }
}
File.WriteAllText(Path.Combine(output, "sources.json"), JsonSerializer.Serialize(sources, json));
File.WriteAllText(Path.Combine(output, "inputs.json"), JsonSerializer.Serialize(inputs, json));

var algebra = new List<object>();
foreach (var relax in new[] { false, true })
{
    var options = new LeastSquaresOptions { MaximumEvaluations = 301, InitialRadius = .2, StepMethod = LeastSquaresStepMethod.Regularized };
    LeastSquaresEvaluation Function(IReadOnlyList<double> x, CancellationToken _) => new([x[0] - 10, 10 * (x[1] - 10)]);
    var result = AuditTrustRegion.Solve([0, 0], [-100, -100], [100, 100], Function, options, relaxRadiusBoundary: relax);
    if (!relax)
    {
        var actual = BoundedTrustRegionLeastSquares.Solve([0, 0], [-100, -100], [100, 100], Function, options);
        if (ContentFingerprint.Compute(actual) != ContentFingerprint.Compute(result)) throw new Exception("Audit copy differs from production solver.");
    }
    algebra.Add(new
    {
        RelaxRadiusBoundary = relax,
        result.Variables,
        Cost = result.Evaluation!.Residuals.Sum(r => r * r),
        result.EvaluationCount,
        result.JacobianBuildCount,
        Termination = result.Termination.ToString(),
        RadiusExpansions = result.Trials.Count(t => t.NextRadius > t.Radius),
        result.Trials
    });
}
File.WriteAllText(Path.Combine(output, "algebra.json"), JsonSerializer.Serialize(algebra, json));

var rows = new List<object>();
foreach (var input in inputs)
    foreach (var relax in new[] { false, true })
    {
        var problem = new FlatStartDesignProblem(input.Specification, input.Family.ElementCount, input.Initial, input.Family);
        var coordinates = problem.SolverCoordinates;
        var initial = coordinates.Encode(problem.Vector(input.Initial));
        DesignEvaluation Evaluate(IReadOnlyList<double> point) => problem.Evaluate(
            problem.CreateOptic(coordinates.Decode(point), FlatStartDesignProblem.FullStage), FlatStartDesignProblem.FullStage, true, default);
        var entry = Evaluate(initial);
        var latest = entry;
        var best = entry;
        var bestPoint = initial.ToArray();
        var invalid = new Dictionary<string, int>();
        LeastSquaresEvaluation Function(IReadOnlyList<double> point, CancellationToken _)
        {
            var before = problem.TracedRayCount;
            latest = Evaluate(point);
            var valid = latest.GeometryFeasible && latest.HasContinuousSearchResiduals;
            if (!valid)
                foreach (var code in latest.Violations.Select(v => v.Code).Where(c => c.StartsWith("geometry.")).Distinct())
                    invalid[code] = invalid.GetValueOrDefault(code) + 1;
            return new(latest.Residuals, valid, problem.TracedRayCount - before);
        }
        void Accept(IReadOnlyList<double> point, LeastSquaresEvaluation _)
        {
            if (Rank(latest) < Rank(best) || Rank(latest) == Rank(best) && latest.Merit < best.Merit)
            { best = latest; bestPoint = point.ToArray(); }
        }
        var options = new LeastSquaresOptions
        {
            MaximumEvaluations = 600,
            InitialRadius = .2,
            StepMethod = LeastSquaresStepMethod.Regularized,
            ScaleByJacobian = true
        };
        var result = relax ? AuditTrustRegion.Solve(initial, coordinates.Lower, coordinates.Upper, Function, options,
            acceptedStep: Accept, relaxRadiusBoundary: true)
            : BoundedTrustRegionLeastSquares.Solve(initial, coordinates.Lower, coordinates.Upper, Function, options, acceptedStep: Accept);
        var final = Evaluate(result.Variables);
        var fresh = Evaluate(bestPoint);
        if (ContentFingerprint.Compute(best) != ContentFingerprint.Compute(fresh)
            || ContentFingerprint.Compute(best.EvaluatedOptic!) != ContentFingerprint.Compute(fresh.EvaluatedOptic!))
            throw new Exception("Independent Core reevaluation differs.");
        rows.Add(new
        {
            input.Id,
            input.Family.ElementCount,
            input.Family.BinaryStart!.Signs,
            RelaxRadiusBoundary = relax,
            InputSha256 = ContentFingerprint.Compute(input.Initial),
            EntrySnapshotIdentical = ContentFingerprint.Compute(input.Initial) == ContentFingerprint.Compute(entry.EvaluatedOptic!),
            Options = options,
            Entry = Summary(entry),
            Final = Summary(final),
            Best = Summary(best),
            BestSnapshot = best.EvaluatedOptic,
            FreshRecalculationIdentical = true,
            InvalidGeometryCodes = invalid,
            result.EvaluationCount,
            result.DifferenceEvaluationCount,
            result.JacobianBuildCount,
            result.FactorizationCount,
            result.WorkUnits,
            Termination = result.Termination.ToString(),
            AcceptedSteps = result.Trials.Count(t => t.Accepted),
            RadiusExpansions = result.Trials.Count(t => t.NextRadius > t.Radius),
            GoodModelWithoutExpansion = result.Trials.Count(t => t.ReductionRatio > .75 && t.NextRadius == t.Radius),
            Trials = result.Trials
        });
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
        {
            Scope = "Exploratory local ablation, not a complete search or external precision certification.",
            RuntimeAlgorithm = FlatStartAlgorithm.Version,
            CoreSha256 = Hash(typeof(Optic).Assembly.Location),
            EngineSha256 = Hash(typeof(FlatStartDesignService).Assembly.Location),
            BudgetPerSolve = 600,
            Results = rows
        }, json));
        Console.WriteLine($"{input.Id} n={input.Family.ElementCount} relaxed={relax}: {entry.Merit:G5} -> {best.Merit:G5}, pass={best.MeetsTargets}; {result.Termination}; expansions={result.Trials.Count(t => t.NextRadius > t.Radius)}");
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
    e.Violations
};
static int Rank(DesignEvaluation e) => e.MeetsTargets ? 0 : e.IsFeasible ? 1 : 2;
static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
internal sealed record Input(string Id, InitialStructureSpecification Specification, FlatStartFamily Family, OpticSnapshot Initial);
