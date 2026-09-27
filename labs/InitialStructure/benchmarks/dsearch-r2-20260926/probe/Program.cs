using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Engine.Optimization;

var json = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
var inputs = JsonSerializer.Deserialize<Input[]>(File.ReadAllText(args[0]), json)!;
var rows = new List<object>();
var budget = args.Length > 2 ? int.Parse(args[2]) : 600;
foreach (var input in inputs)
    foreach (var mode in new[] { "dogleg", "scaled-dogleg", "regularized", "scaled-regularized" })
    {
        var problem = new FlatStartDesignProblem(input.Specification, 4, input.Initial, input.Family);
        var coordinates = problem.SolverCoordinates;
        var initial = coordinates.Encode(problem.Vector(input.Initial));
        DesignEvaluation Evaluate(IReadOnlyList<double> point) => problem.Evaluate(
            problem.CreateOptic(coordinates.Decode(point), FlatStartDesignProblem.FullStage), FlatStartDesignProblem.FullStage, true, default);
        var entry = Evaluate(initial);
        var latest = entry;
        var best = entry;
        var bestPoint = initial.ToArray();
        var invalid = 0;
        var clippedContinuous = 0;
        var solveOptions = new LeastSquaresOptions
        {
            MaximumEvaluations = budget,
            InitialRadius = .2,
            StepMethod = mode.EndsWith("regularized", StringComparison.Ordinal) ? LeastSquaresStepMethod.Regularized : LeastSquaresStepMethod.Dogleg,
            ScaleByJacobian = mode.StartsWith("scaled-", StringComparison.Ordinal)
        };
        var result = BoundedTrustRegionLeastSquares.Solve(initial, coordinates.Lower, coordinates.Upper,
            (point, _) =>
            {
                var before = problem.TracedRayCount;
                latest = Evaluate(point);
                var valid = latest.GeometryFeasible && latest.HasContinuousSearchResiduals;
                if (!valid) invalid++;
                if (valid && !latest.IsFeasible) clippedContinuous++;
                return new(latest.Residuals, valid, problem.TracedRayCount - before);
            }, solveOptions, acceptedStep: (point, _) =>
            {
                if (Rank(latest) < Rank(best) || Rank(latest) == Rank(best) && latest.Merit < best.Merit)
                {
                    best = latest;
                    bestPoint = point.ToArray();
                }
            });
        var final = Evaluate(result.Variables);
        var bestRevalidated = Evaluate(bestPoint);
        if (ContentFingerprint.Compute(best) != ContentFingerprint.Compute(bestRevalidated))
            throw new InvalidOperationException("Retained point failed fresh Core recalculation.");
        var reference = input.Reference.GetProperty("Final");
        bool? exactLegacy = mode == "dogleg" && budget == 600
            ? final.Merit == reference.GetProperty("Merit").GetDouble()
                && result.EvaluationCount == input.Reference.GetProperty("EvaluationCount").GetInt32()
            : null;
        if (exactLegacy == false) throw new InvalidOperationException("Default dogleg changed relative to frozen R1.");
        rows.Add(new
        {
            Focal = input.Specification.EffectiveFocalLengthMillimeters,
            input.Family.BinaryStart!.Signs,
            Mode = mode,
            InitialOpticSha256 = ContentFingerprint.Compute(input.Initial),
            Options = solveOptions,
            result.EvaluationCount,
            result.DifferenceEvaluationCount,
            result.JacobianBuildCount,
            result.FactorizationCount,
            AcceptedSteps = result.Trials.Count(t => t.Accepted),
            InvalidEvaluations = invalid,
            ContinuousClippedEvaluations = clippedContinuous,
            Termination = result.Termination.ToString(),
            ExactLegacyMatch = exactLegacy,
            Entry = entry with { Residuals = [] },
            Final = final with { Residuals = [] },
            BestAccepted = bestRevalidated with { Residuals = [] },
            BestRecalculationIdentical = true,
            BestOptic = problem.CreateOptic(coordinates.Decode(bestPoint), FlatStartDesignProblem.FullStage).ToSnapshot()
        });
        Console.WriteLine($"{input.Specification.EffectiveFocalLengthMillimeters} {input.Family.BinaryStart.Signs} {mode}: cost {final.Merit:G6}, final/best pass {final.MeetsTargets}/{best.MeetsTargets}, {result.Termination}");
        File.WriteAllText(args[1], JsonSerializer.Serialize(new
        {
            Scope = "R2 independent solver ablation, same frozen eight starts and optical objective. No acceptance tuning or full search protocol.",
            RuntimeAlgorithm = FlatStartAlgorithm.Version,
            EngineSha256 = Hash(typeof(FlatStartDesignService).Assembly.Location),
            CoreSha256 = Hash(typeof(OptilandWorkbench.Core.Optic).Assembly.Location),
            InputSha256 = Hash(args[0]),
            BudgetPerSolve = budget,
            Results = rows
        }, json) + "\n");
    }

static int Rank(DesignEvaluation evaluation) => evaluation.MeetsTargets ? 0 : evaluation.IsFeasible ? 1 : 2;
static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
internal sealed record Input(InitialStructureSpecification Specification, FlatStartFamily Family, OpticSnapshot Initial, JsonElement Reference);
