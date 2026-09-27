using System.Text.Json;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Engine.Optimization;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class FlatStartObjectiveTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ContinuousTrialsCrossPhysicalBoundaryButMissingRayDataCannotSupplyDerivatives(bool continuousProbes)
    {
        var map = new FlatStartSolverCoordinates(FlatStartRefinementTests.Spec(), 1, 2);
        var initial = map.Decode(new double[4]);
        var probeCalls = 0;
        DesignEvaluation Evaluate(double[] point)
        {
            var atStart = point.SequenceEqual(initial);
            if (!atStart) probeCalls++;
            var residual = point[0] - .4;
            var meets = Math.Abs(residual) < 1e-7;
            return new()
            {
                GeometryFeasible = true,
                HasContinuousSearchResiduals = continuousProbes || atStart,
                IsFeasible = meets,
                Residuals = [residual],
                Merit = residual * residual,
                // Deliberately contradictory legacy summaries must not steer the solve.
                ConstraintResiduals = [point[0] + 1],
                TransmissionResiduals = [1],
                Violations = meets ? [] : [new("synthetic.throughput", ConstraintSeverity.Hard, "Physical gate fails.")]
            };
        }
        var result = FlatStartLocalSolver.Solve(map, initial, Evaluate(initial), 200, Evaluate,
            false, () => false, default, (_, _, _) => { }, (_, _) => { });
        Assert.True(probeCalls > 0);
        if (continuousProbes)
        {
            Assert.True(result.Evaluation.MeetsTargets);
            Assert.Equal(.4, result.Variables[0], 7);
        }
        else
        {
            Assert.Equal(LeastSquaresTermination.DerivativeUnavailable, result.Termination);
            Assert.Equal(initial, result.Variables);
            Assert.False(result.Evaluation.MeetsTargets);
        }
    }

    [Fact]
    public void ArchiveUsesDenseRayObjectiveEvenWhenWorstSpotSummaryPrefersAnotherCandidate()
    {
        var spec = FlatStartRefinementTests.Spec();
        var good = new CandidateSnapshot
        {
            Status = CandidateStatus.TraceValid,
            Evaluation = new()
            {
                RmsSpotRadiusMillimeters = 1,
                MaximumSpotRadiusMillimeters = 2,
                FlatStartObjective = new(FlatStartObjectiveKind.RealRaySumSquaresV1, FlatStartDesignProblem.FullStage, true, true, .5)
            }
        };
        var bad = good with
        {
            Evaluation = good.Evaluation with
            {
                RmsSpotRadiusMillimeters = .01,
                MaximumSpotRadiusMillimeters = .02,
                FlatStartObjective = good.Evaluation.FlatStartObjective! with { SumSquares = 2 }
            }
        };
        Assert.True(FlatStartCandidateArchive.Score(spec, good) < FlatStartCandidateArchive.Score(spec, bad));
        foreach (var invalid in new[]
        {
            good.Evaluation.FlatStartObjective! with { DenseSampling = false },
            good.Evaluation.FlatStartObjective! with { Stage = new(.3, 0, false) },
            good.Evaluation.FlatStartObjective! with { HasContinuousResiduals = false }
        }) Assert.Equal(double.PositiveInfinity, FlatStartCandidateArchive.Score(spec,
            good with { Evaluation = good.Evaluation with { FlatStartObjective = invalid } }));
        Assert.True(FlatStartCandidateArchive.Rank(CandidateStatus.LabAccepted) < FlatStartCandidateArchive.Rank(good.Status));
        Assert.DoesNotContain("FlatStartObjective", JsonSerializer.Serialize(new EvaluationVector()));
        Assert.DoesNotContain("Objective", JsonSerializer.Serialize(new DesignEvaluation()));
    }

    [Fact]
    public async Task DenseObjectiveMatchesCoreEvaluationAndCheckpointRejectsScopeOrValueTampering()
    {
        var source = (await new FlatStartSearchService().RunAsync(FlatStartRefinementTests.Spec(), FlatStartRefinementTests.Options)).Checkpoint;
        FlatStartCheckpointValidation.Validate(source);
        var trial = source.Trials.First(t => t.Candidate is not null);
        var candidate = trial.Candidate!;
        var problem = new FlatStartDesignProblem(source.Specification, trial.Family.ElementCount, candidate.Optic, trial.Family);
        var recomputed = problem.Evaluate(problem.CreateOptic(problem.Vector(candidate.Optic), FlatStartDesignProblem.FullStage),
            FlatStartDesignProblem.FullStage, true, default);
        Assert.Equal(recomputed.Objective, candidate.Evaluation.FlatStartObjective);
        Assert.Equal(recomputed.Residuals.Sum(r => r * r), recomputed.Merit);
        Assert.Equal(recomputed.Merit, FlatStartCandidateArchive.Score(source.Specification, candidate));
        var objective = candidate.Evaluation.FlatStartObjective!;
        foreach (var altered in new[] { objective with { SumSquares = objective.SumSquares + 1 }, objective with { DenseSampling = false }, null })
            Assert.Throws<InvalidDataException>(() => FlatStartCheckpointValidation.Validate(source with
            {
                Trials = source.Trials.Select(t => t != trial ? t : trial with
                { Candidate = candidate with { Evaluation = candidate.Evaluation with { FlatStartObjective = altered } } }).ToArray()
            }));
    }
}
