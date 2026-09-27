using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Engine.Optimization;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class FlatStartQualityRegressionTests
{
    [Fact]
    public void HistoricalSamplingIsExplicitlyCompatibleAndCannotRankAgainstAreaScores()
    {
        var old = JsonSerializer.Deserialize<FlatStartSettings>("{}")!;
        Assert.Equal(FlatStartSamplingPolicy.LegacyEqualRings, old.SamplingPolicy);
        Assert.DoesNotContain("SamplingPolicy", JsonSerializer.Serialize(old));
        var spec = Upgrade(Inputs()[6].Specification);
        var legacy = new CandidateSnapshot
        {
            Evaluation = new()
            {
                FlatStartObjective = new(
                    FlatStartObjectiveKind.RealRaySumSquaresV1, FlatStartDesignProblem.FullStage, true, true, .1)
            }
        };
        Assert.Equal(double.PositiveInfinity, FlatStartCandidateArchive.Score(spec, legacy));
        Assert.Throws<InitialStructureSpecificationException>(() => SpecificationValidator.Validate(spec with
        { FlatStart = spec.FlatStart! with { SamplingPolicy = (FlatStartSamplingPolicy)123 } }));
    }

    [Fact]
    public async Task AreaCheckpointRequiresIndependentValidationAndPreservesItsSamplingIdentity()
    {
        var spec = Upgrade(FlatStartRefinementTests.Spec()) with
        { Budget = new() { MaximumEvaluations = 100, InitialSeedCount = 1, MaximumParallelism = 1 } };
        var checkpoint = (await new FlatStartSearchService().RunAsync(spec)).Checkpoint;
        FlatStartCheckpointValidation.Validate(checkpoint);
        var trial = checkpoint.Trials.First(t => t.Candidate is not null);
        var objective = trial.FinalValidation!.Objective!;
        Assert.True(objective.IndependentValidation);
        foreach (var changed in new[] { objective with { IndependentValidation = false },
            objective with { SamplingPolicy = FlatStartSamplingPolicy.LegacyEqualRings } })
        {
            var candidate = trial.Candidate! with { Evaluation = trial.Candidate!.Evaluation with { FlatStartObjective = changed } };
            Assert.Equal(double.PositiveInfinity, FlatStartCandidateArchive.Score(spec, candidate));
            Assert.Throws<InvalidDataException>(() => FlatStartCheckpointValidation.Validate(checkpoint with
            {
                Trials = checkpoint.Trials.Select(t => t != trial ? t : t with
                { Candidate = candidate, FinalValidation = t.FinalValidation! with { Objective = changed } }).ToArray()
            }));
        }
    }

    [Theory]
    [InlineData(6, .05334365992115461, false)]
    [InlineData(7, .04985148216661106, true)]
    [InlineData(8, .05284006366762616, false)]
    public void PreviouslyAcceptedPrescriptionsUseAreaIntegralAndIndependentCoreValidation(int index, double expected, bool rmsPass)
    {
        var input = Inputs()[index];
        var spec = Upgrade(input.Specification);
        var problem = new FlatStartDesignProblem(spec, input.Family.ElementCount, input.Initial, input.Family);
        var optic = Optic.FromSnapshot(input.Initial);
        var validation = problem.Evaluate(optic, FlatStartDesignProblem.FullStage, true, default, independentValidation: true);
        Assert.Equal(21, validation.Fields.Count);
        Assert.InRange(Math.Abs(expected - validation.Fields.Max(f => f.RmsRadiusMillimeters)!.Value), 0, 1e-12);
        Assert.Equal(rmsPass, validation.Fields.All(f => f.RmsRadiusMillimeters <= spec.MaximumRmsSpotRadiusMillimeters));
        Assert.True(validation.Objective!.IndependentValidation);
        Assert.Equal(FlatStartObjectiveKind.ConstrainedRealRaySumSquaresV2, validation.Objective.Kind);
        foreach (var field in validation.Fields)
        {
            var core = SpotMetricEvaluator.EvaluatePupilSamplesWithChecks(optic, 0, field.NormalizedFieldY,
                FlatStartDesignProblem.IntegrationPupils(spec.FlatStart!.SamplingPolicy, true, true),
                FlatStartDesignProblem.CheckPupils(true, true), includeSurfaceTransmission: false);
            Assert.Equal(core.Integration.Metrics!.RmsSpotRadius, field.RmsRadiusMillimeters);
            Assert.Equal(core.MaximumRadius, field.MaximumRadiusMillimeters);
            Assert.Equal(320, field.AttemptedRays);
            Assert.Equal(65, field.CheckAttemptedRays);
            Assert.Equal(field.CheckAttemptedRays, field.CheckValidRays);
        }
        var fingerprint = ContentFingerprint.Compute(validation.EvaluatedOptic!);
        var fresh = problem.Evaluate(Optic.FromSnapshot(validation.EvaluatedOptic!), FlatStartDesignProblem.FullStage, true, default, true);
        Assert.Equal(fingerprint, ContentFingerprint.Compute(fresh.EvaluatedOptic!));
        Assert.Equal(ContentFingerprint.Compute(validation), ContentFingerprint.Compute(fresh));
        if (!rmsPass) Assert.False(validation.MeetsTargets);
    }

    [Theory]
    [InlineData(0, 7)]
    [InlineData(1, 11)]
    [InlineData(2, 13)]
    [InlineData(3, 17)]
    public void AutoDiameterEdgeBoundaryProvidesDerivativesWithoutRelaxingFinalGeometry(int index, int formerlyBlockedColumn)
    {
        var input = Inputs()[index];
        var spec = Upgrade(input.Specification);
        var problem = new FlatStartDesignProblem(spec, input.Family.ElementCount, input.Initial, input.Family);
        var coordinates = problem.SolverCoordinates;
        var initial = problem.Vector(input.Initial);
        var point = coordinates.Encode(initial);
        point[formerlyBlockedColumn] += 6.055454452393343e-6;
        var probe = Evaluate(coordinates.Decode(point));
        Assert.True(probe.HasContinuousSearchResiduals);
        Assert.All(probe.Residuals, residual => Assert.True(double.IsFinite(residual)));
        if (!probe.GeometryFeasible) Assert.False(probe.MeetsTargets);
        var builds = 0;
        var result = FlatStartLocalSolver.Solve(coordinates, initial, Evaluate(initial), 160, Evaluate,
            true, () => false, default, (_, _, _) => { }, (_, statistics) => builds += statistics.JacobianBuildCount);
        Assert.True(builds > 0);
        Assert.NotEqual(LeastSquaresTermination.DerivativeUnavailable, result.Termination);
        Assert.Equal(.5, spec.FlatStart!.MinimumEdgeThicknessMillimeters);
        DesignEvaluation Evaluate(double[] values) => problem.Evaluate(problem.CreateOptic(values, FlatStartDesignProblem.FullStage),
            FlatStartDesignProblem.FullStage, true, default);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    public void ScreeningSpendsBudgetOnReducedAndFullStagesAndReservesIndependentValidation(int count)
    {
        var input = Inputs()[count - 3];
        var spec = Upgrade(input.Specification) with { Budget = input.Specification.Budget with { MaximumEvaluations = 260 } };
        var result = new FlatStartDesignService().Solve(spec, count, family: input.Family, screenOnly: true);
        var stages = result.Steps.Where(s => s.Operation == "progressive-stage-entry").ToArray();
        Assert.Equal(FlatStartContinuation.ProgressiveStages, stages.Select(s => s.Stage));
        Assert.True(stages[1].EvaluationNumber > stages[0].EvaluationNumber + 1);
        Assert.True(stages[2].EvaluationNumber > stages[1].EvaluationNumber + 1);
        Assert.True(result.FinalValidation!.Objective!.IndependentValidation);
        Assert.Equal(21, result.FinalValidation.Fields.Count);
        Assert.InRange(result.EvaluationCount, 1, 260);
    }

    internal static InitialStructureSpecification Upgrade(InitialStructureSpecification specification) => specification with
    { FlatStart = specification.FlatStart! with { SamplingPolicy = FlatStartSamplingPolicy.UniformAreaGaussianV1 } };

    internal static QualityInput[] Inputs()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OptilandWorkbench.slnx"))) directory = directory.Parent;
        return JsonSerializer.Deserialize<QualityInput[]>(File.ReadAllText(Path.Combine(directory!.FullName,
            "labs/InitialStructure/benchmarks/quality-audit-20260926/inputs.json")),
            new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals })!;
    }

    internal sealed record QualityInput(string Id, InitialStructureSpecification Specification, FlatStartFamily Family, OpticSnapshot Initial);
}
