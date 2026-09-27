using System.Security.Cryptography;
using System.Text.Json;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Engine.Optimization;

// Audit executable only. Uses the existing test friend-assembly identity to inspect
// the frozen production adapter/solver. No optical formulas or production edits.
var output = args.Single();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
var rows = new List<object>();
var specifications = new[] { (Focal: 62.0, FNumber: 6.0, Field: 7.0), (Focal: 113.0, FNumber: 7.0, Field: 4.0) };
var signs = new[] { "++++", "++--", "+-+-", "----" };
var modes = new[] { "current-target-hinges", "joint-ray-vector", "joint-ray-norm", "joint-ray-vector-continuous" };
foreach (var target in specifications)
{
    var spec = new InitialStructureSpecification
    {
        Name = $"Independent route probe {target.Focal} mm",
        EffectiveFocalLengthMillimeters = target.Focal,
        FNumber = target.FNumber,
        MaximumFieldAngleDegrees = target.Field,
        MinimumElementCount = 4,
        MaximumElementCount = 4,
        MaximumTrackLengthMillimeters = 190,
        MinimumCenterThicknessMillimeters = 3,
        MinimumAirGapMillimeters = 2,
        MinimumBackFocusMillimeters = 8,
        MaximumRmsSpotRadiusMillimeters = .05,
        MaximumSpotRadiusMillimeters = .15,
        FlatStart = new(),
        Budget = new() { MaximumEvaluations = 256, InitialSeedCount = 1, MaximumParallelism = 1, RandomSeed = 731 }
    };
    foreach (var form in signs)
    {
        var family = new FlatStartFamily
        {
            GlassNames = ["N-BK7", "N-F2", "N-BK7", "N-F2"],
            CenterThicknesses = [3, 3, 3, 3],
            AirGaps = [2, 2, 2],
            StopSurfaceIndex = 5,
            BinaryStart = new(form, 10 * target.Focal)
        };
        var warmup = new FlatStartDesignService().Solve(spec, 4, family: family, screenOnly: true);
        var initial = warmup.Candidate!.Optic;
        foreach (var mode in modes)
        {
            var problem = new FlatStartDesignProblem(spec, 4, initial, family);
            var coordinates = problem.SolverCoordinates;
            var point = coordinates.Encode(problem.Vector(initial));
            DesignEvaluation Evaluate(IReadOnlyList<double> x) => problem.Evaluate(
                problem.CreateOptic(coordinates.Decode(x), FlatStartDesignProblem.FullStage),
                FlatStartDesignProblem.FullStage, true, CancellationToken.None);
            IReadOnlyList<double> Residuals(DesignEvaluation e) => mode switch
            {
                "current-target-hinges" => e.ConstraintResiduals,
                "joint-ray-vector" or "joint-ray-vector-continuous" => e.Residuals,
                _ => new[] { Math.Sqrt(e.Residuals.Sum(r => r * r)) }
            };
            var entry = Evaluate(point);
            var entryRays = problem.TracedRayCount;
            var invalidCauses = new Dictionary<string, int>();
            var result = BoundedTrustRegionLeastSquares.Solve(point, coordinates.Lower, coordinates.Upper,
                (x, _) =>
                {
                    var before = problem.TracedRayCount;
                    var e = Evaluate(x);
                    var valid = e.HasContinuousSearchResiduals && (mode == "joint-ray-vector-continuous"
                        ? e.GeometryFeasible : e.IsFeasible);
                    if (!valid)
                    {
                        var cause = !e.GeometryFeasible ? "geometry"
                            : !e.HasContinuousSearchResiduals ? "missing-continuous-ray-data" : "throughput";
                        invalidCauses[cause] = invalidCauses.GetValueOrDefault(cause) + 1;
                    }
                    return new(Residuals(e), valid,
                        problem.TracedRayCount - before);
                }, new() { MaximumEvaluations = 600, InitialRadius = .2 });
            var final = Evaluate(result.Variables);
            rows.Add(new
            {
                Target = spec,
                Form = form,
                Mode = mode,
                InitialOpticSha256 = ContentFingerprint.Compute(initial),
                WarmupEvaluations = warmup.EvaluationCount,
                Initial = Summary(entry),
                Final = Summary(final),
                Dimension = point.Length,
                ResidualCount = Residuals(entry).Count,
                NonzeroResidualsAtEntry = Residuals(entry).Count(r => Math.Abs(r) > 1e-12),
                result.EvaluationCount,
                result.DifferenceEvaluationCount,
                result.JacobianBuildCount,
                AcceptedSteps = result.Trials.Count(t => t.Accepted),
                InvalidEvaluationsByCause = invalidCauses,
                Termination = result.Termination.ToString(),
                EntryAnalysisRaySamples = entryRays,
                SolveAnalysisRaySamples = result.WorkUnits,
                FinalValidationRaySamples = problem.TracedRayCount - entryRays - result.WorkUnits
            });
            Console.WriteLine($"{target.Focal} {form} {mode}: {result.Termination}, accepted={final.MeetsTargets}, RMS={final.Fields.Max(f => f.RmsRadiusMillimeters):G6}, EFL={final.EffectiveFocalLengthMillimeters:G6}");
            Save();
        }
    }
}

void Save() => File.WriteAllText(output, JsonSerializer.Serialize(new
{
    Scope = "Independent diagnostic ablation; not an acceptance run, not PSD, no production behavior changes.",
    Algorithm = FlatStartAlgorithm.Version,
    EngineSha256 = Hash(typeof(FlatStartDesignService).Assembly.Location),
    CoreSha256 = Hash(typeof(OptilandWorkbench.Core.Optic).Assembly.Location),
    BudgetPerSolve = 600,
    WarmupBudgetPerStart = 256,
    Sampling = "Existing Core-backed full stage: six fields, 97 pupil samples per wavelength, all three wavelengths. Recorded ray counts are requested analysis samples, not total aiming/diagnostic cost.",
    NormAblation = "joint-ray-norm and joint-ray-vector have identical squared objective value at every valid point; only residual representation changes. current-target-hinges uses a different objective.",
    DomainAblation = "joint-ray-vector-continuous retains the joint-ray-vector objective, including existing Core aperture-clearance penalties, while admitting continuous Core diagnostics below the physical throughput gate. Final MeetsTargets still applies every unchanged physical gate.",
    Results = rows
}, new JsonSerializerOptions { WriteIndented = true }) + "\n");

static object Summary(DesignEvaluation e) => new
{
    e.MeetsTargets,
    e.IsFeasible,
    e.HasContinuousSearchResiduals,
    e.EffectiveFocalLengthMillimeters,
    e.FNumber,
    WorstRmsMillimeters = e.Fields.Max(f => f.RmsRadiusMillimeters),
    WorstRadiusMillimeters = e.Fields.Max(f => f.MaximumRadiusMillimeters),
    e.Merit,
    TargetHingeCost = e.ConstraintResiduals.Sum(r => r * r),
    e.Violations
};

static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
