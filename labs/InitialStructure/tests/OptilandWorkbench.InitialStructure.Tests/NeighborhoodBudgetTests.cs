using OptilandWorkbench.Core;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class NeighborhoodBudgetTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(10000)]
    public void RootPlanHonorsSeedUpperLimitAndLeavesAnExplicitNeighborhoodBudget(int budget)
    {
        var spec = Specification(budget) with
        {
            MinimumElementCount = 3,
            MaximumElementCount = 8,
            Budget = Specification(budget).Budget with { InitialSeedCount = 128 }
        };
        foreach (var mode in new[] { DesignSearchMode.Quick, DesignSearchMode.Full })
        {
            var options = new FlatStartSearchOptions { DesignSearch = new() { Mode = mode } };
            var plan = DesignFormSearch.Preview(spec, options);
            Assert.InRange(plan.PlannedRoots, 1, 128);
            Assert.InRange(plan.ScreeningEvaluationsPerRoot, 1, options.MaximumEvaluationsPerTrial);
            Assert.Equal(budget, plan.PlannedRoots * plan.ScreeningEvaluationsPerRoot + plan.NeighborhoodEvaluationsReserved);
            Assert.True(plan.NeighborhoodEvaluationsReserved >= NeighborhoodBudget.RootReserve(spec, options));
            if (budget == 10000)
            {
                Assert.True(plan.PlannedRoots < 128);
                Assert.True(plan.NeighborhoodEvaluationsReserved >= NeighborhoodBudget.Operations(spec, options, 3)
                    .Sum(op => NeighborhoodBudget.Nominal(spec, options, op, 8)));
            }
        }
    }

    [Fact]
    public void OperatorRoundUsesImprovedParentWithoutResettingHistoryOrStarvingOtherBranches()
    {
        var spec = Specification(10000);
        var a = Parent(spec, "a", "+++", 1);
        var b = Parent(spec, "b", "+-+", 2);
        var trials = new List<FamilyTrial> { a, b };
        var checkpoint = new FlatStartSearchCheckpoint { Specification = spec, Trials = trials, UsableGlassNames = ["N-BK7", "N-F2"] };
        int[] expected = [0, 1, 3, 4, 2];
        foreach (var operation in expected)
        {
            var decision = DesignFormScheduler.Decide(checkpoint);
            Assert.Equal(operation, decision.Operation);
            Assert.StartsWith("a", decision.Parent!.TrialId);
            var parent = decision.Parent;
            var improved = parent with
            {
                TrialId = "a" + trials.Count,
                ParentTrialId = parent.TrialId,
                Operation = Name(operation),
                Candidate = parent.Candidate! with
                {
                    Evaluation = parent.Candidate!.Evaluation with
                    { FlatStartObjective = parent.Candidate.Evaluation.FlatStartObjective! with { SumSquares = 1.0 / trials.Count } }
                }
            };
            trials.Add(improved);
        }
        var next = DesignFormScheduler.Decide(checkpoint);
        Assert.Equal(0, next.Operation);
        Assert.Equal("b", next.Parent!.TrialId);
        var disabled = checkpoint with { UsableGlassNames = ["N-BK7"], Options = new() { DesignSearch = new() { ExploreStopPositions = false } } };
        Assert.Equal(new[] { 0, 4 }, NeighborhoodBudget.Operations(spec, disabled.Options, 1));
        Assert.Contains(DesignFormScheduler.Decide(disabled).Operation, new[] { 0, 4 });
    }

    [Theory]
    [InlineData(340)]
    [InlineData(800)]
    [InlineData(3760)]
    public void LongRefinementCannotConsumeTheFirstModelWindowOfUnvisitedOperators(int remaining)
    {
        var spec = Specification(10000) with { MinimumElementCount = 3, MaximumElementCount = 8 };
        var trials = new List<FamilyTrial>();
        var checkpoint = new FlatStartSearchCheckpoint { Specification = spec, Trials = trials, UsableGlassNames = ["N-BK7", "N-F2"] };
        var original = remaining;
        foreach (var operation in NeighborhoodBudget.Operations(spec, checkpoint.Options, 2))
        {
            var allocation = NeighborhoodBudget.Allocate(checkpoint, operation, 8, remaining);
            Assert.InRange(allocation, 68, Math.Min(remaining, checkpoint.Options.MaximumEvaluationsPerTrial));
            remaining -= allocation;
            trials.Add(new() { ParentTrialId = "root", Operation = Name(operation), ChargedEvaluations = allocation });
        }
        Assert.InRange(trials.Sum(t => t.ChargedEvaluations), 340, original);
    }

    [Fact]
    public async Task RealGlassAndStopNeighborsReoptimizeFullTargetsAndResumeIdentically()
    {
        var spec = Specification(1000);
        var options = new FlatStartSearchOptions { MaximumEvaluationsPerTrial = 120, AllowedGlassNames = ["N-BK7", "N-F2"] };
        var service = new FlatStartSearchService();
        var complete = await service.RunAsync(spec, options);
        FlatStartCheckpointValidation.Validate(complete.Checkpoint);
        Assert.Equal(2, complete.Checkpoint.Schedule!.PolicyVersion);
        Assert.Equal(1000, complete.Checkpoint.ChargedEvaluations);
        foreach (var operation in new[] { "refine", "glass-swap", "stop-surface-change", "parameter-perturbation", "glass-pair-swap" })
            Assert.Contains(complete.Checkpoint.Trials, t => t.Operation == operation && t.State == FamilyTrialState.Completed);
        Assert.DoesNotContain(complete.Checkpoint.Diagnostics, d => d.Code == "search.neighborhood-coverage");
        foreach (var trial in complete.Checkpoint.Trials.Where(t => t.ParentTrialId is not null))
        {
            var parent = complete.Checkpoint.Trials.Single(t => t.TrialId == trial.ParentTrialId);
            var start = Optic.FromSnapshot(trial.RefinementStartOptic!);
            if (trial.Operation.Contains("glass", StringComparison.Ordinal))
            {
                Assert.Equal(trial.Operation == "glass-swap" ? 1 : 2,
                    trial.Family.GlassNames.Zip(parent.Family.GlassNames).Count(pair => pair.First != pair.Second));
                for (var index = 0; index < trial.Family.ElementCount; index++)
                    Assert.Equal(trial.Family.GlassNames[index], trial.RefinementStartOptic!.Surfaces[2 * index + 1].Material);
            }
            if (trial.Operation == "stop-surface-change")
                Assert.NotEqual(parent.Family.StopSurfaceIndex, start.SurfaceGroup.Items.ToList().FindIndex(s => s.IsStop));
            Assert.True(trial.FinalValidation!.Objective!.IndependentValidation);
            if (trial.Candidate is { } candidate)
            {
                var problem = new FlatStartDesignProblem(spec, trial.Family.ElementCount, candidate.Optic, trial.Family);
                var fresh = problem.Evaluate(Optic.FromSnapshot(candidate.Optic), FlatStartDesignProblem.FullStage, true, default, true);
                Assert.Equal(ContentFingerprint.Compute(trial.FinalValidation), ContentFingerprint.Compute(fresh with { Residuals = [] }));
            }
        }
        Assert.Contains(complete.Checkpoint.Trials.Where(t => t.Operation is "glass-swap" or "stop-surface-change"),
            t => t.Stages.First().Operation == "full-target-refinement-entry");
        using var stop = new CancellationTokenSource();
        var cancelled = await service.RunAsync(spec, options, stop.Token, checkpointSink: (saved, _) =>
        {
            if (saved.Trials.Any(t => t.ParentTrialId is not null && t.State == FamilyTrialState.Completed)) stop.Cancel();
            return ValueTask.CompletedTask;
        });
        var resumed = await service.RunAsync(spec, options, checkpoint: cancelled.Checkpoint);
        Assert.Equal(complete.Checkpoint.Trials.Select(t => (t.Operation, t.ChargedEvaluations, t.Candidate?.OpticFingerprint)),
            resumed.Checkpoint.Trials.Select(t => (t.Operation, t.ChargedEvaluations, t.Candidate?.OpticFingerprint)));
    }

    [Fact]
    public async Task AreaV12KeepsItsOriginalPlanAndCannotResumeAsCurrent()
    {
        var spec = Specification(10000) with
        {
            MaximumElementCount = 8,
            Budget = Specification(10000).Budget with { InitialSeedCount = 128 }
        };
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var current = (await new FlatStartSearchService().RunAsync(spec, cancellationToken: cancellation.Token)).Checkpoint;
        var roots = DesignFormSearch.Roots(spec, current.Options, current.UsableGlassNames, "12");
        Assert.Equal(128, roots.Count);
        Assert.True(current.RootPlan.Count < roots.Count);
        var old = current with
        {
            Algorithm = current.Algorithm with { Version = "12" },
            RootPlan = roots,
            RootEvaluationQuota = DesignFormSearch.RootQuota(spec, current.Options, roots.Count, "12"),
            Schedule = current.Schedule! with { PolicyVersion = 1 }
        };
        FlatStartCheckpointValidation.Validate(old);
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => new FlatStartSearchService().RunAsync(spec, old.Options, checkpoint: old));
        Assert.Contains("historical search version", failure.Message);
        Assert.Throws<InvalidDataException>(() => FlatStartCheckpointValidation.Validate(current with { Schedule = current.Schedule! with { PolicyVersion = 1 } }));
    }

    private static InitialStructureSpecification Specification(int budget) => FlatStartQualityRegressionTests.Upgrade(FlatStartRefinementTests.Spec(budget)) with
    { FlatStart = new() { SamplingPolicy = FlatStartSamplingPolicy.UniformAreaGaussianV1, AutomaticLensDiameters = true } };

    private static string Name(int operation) => operation switch
    { 0 => "refine", 1 => "glass-swap", 2 => "glass-pair-swap", 3 => "stop-surface-change", _ => "parameter-perturbation" };

    private static FamilyTrial Parent(InitialStructureSpecification spec, string id, string signs, double merit) => new()
    {
        TrialId = id,
        Family = new() { GlassNames = ["N-BK7", "N-BK7", "N-BK7"], BinaryStart = new(signs, 500) },
        Candidate = new()
        {
            Optic = new FlatRootFactory().Create(spec, 3),
            Lineage = new() { InitialForm = signs, ElementCount = 3 },
            Status = CandidateStatus.TraceValid,
            Evaluation = new()
            {
                FlatStartObjective = new(FlatStartObjectiveKind.ConstrainedRealRaySumSquaresV2,
                FlatStartDesignProblem.FullStage, true, true, merit)
                { SamplingPolicy = FlatStartSamplingPolicy.UniformAreaGaussianV1, IndependentValidation = true }
            }
        }
    };
}
