using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Engine.Optimization;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class FlatStartPolicyTests
{
    [Fact]
    public void ContinuationTriesFullTargetThenReducesDifficultyAndRestoresFullTarget()
    {
        var policy = new FlatStartContinuation();
        Assert.Equal(FlatStartDesignProblem.FullStage, policy.Proposed);
        var previous = policy.Proposed;
        for (var index = 0; index < 4; index++)
        {
            policy.Backoff();
            Assert.True(policy.Proposed.PupilFraction < previous.PupilFraction);
            Assert.True(policy.Proposed.FieldFraction < previous.FieldFraction);
            previous = policy.Proposed;
        }
        Assert.False(policy.Proposed.AllWavelengths);
        policy.Accept();
        Assert.Equal(previous, policy.Established);
        Assert.Equal(FlatStartDesignProblem.FullStage, policy.Proposed);
        for (var index = 0; index < 8; index++) policy.Backoff();
        Assert.True(policy.Exhausted);
    }

    [Fact]
    public void LocalGeometryRestorationSwitchesToOneRayObjectiveAndKeepsAcceptanceSeparate()
    {
        var map = new FlatStartSolverCoordinates(FlatStartRefinementTests.Spec(), 1, 2);
        var initial = map.Decode(new double[4]);
        var phases = new List<OpticalSolvePhase>();
        var accepted = new List<(double X, OpticalSolvePhase Phase)>();
        var calls = 0;
        var result = FlatStartLocalSolver.Solve(map, initial, Evaluate(initial), 500,
            point => { calls++; return Evaluate(point); }, true, () => false, default,
            (point, _, phase) => accepted.Add((point[0], phase)), (phase, _) => phases.Add(phase));
        Assert.Equal(calls, result.Evaluations);
        Assert.Equal(new[] { OpticalSolvePhase.Geometry, OpticalSolvePhase.RealRay }, phases);
        Assert.False(result.Evaluation.MeetsTargets);
        Assert.Equal(.8, result.Variables[0], 7);
        Assert.Contains(accepted, step => step.Phase == OpticalSolvePhase.RealRay && step.X > .6);

        // Synthetic adapter data test phase semantics; it is never an optical reference.
        static DesignEvaluation Evaluate(double[] point)
        {
            var x = point[0];
            var meets = x >= .3 - 1e-9 && x <= .6 + 1e-9;
            return new()
            {
                GeometryFeasible = x >= .1 - 1e-9,
                IsFeasible = x >= .2 - 1e-9,
                HasContinuousSearchResiduals = true,
                GeometryResiduals = [Math.Max(0, .1 - x)],
                TransmissionResiduals = [Math.Max(0, .2 - x)],
                ConstraintResiduals = [Math.Max(0, .3 - x)],
                ImageResiduals = [x - .8],
                Residuals = [x - .8],
                Merit = (x - .8) * (x - .8),
                Violations = meets ? [] : [new("synthetic", ConstraintSeverity.Hard, "Outside synthetic target.")]
            };
        }
    }

    [Fact]
    public void LocalAdapterChargesEveryProbeAndHonorsCancellation()
    {
        var map = new FlatStartSolverCoordinates(FlatStartRefinementTests.Spec(), 1, 2);
        var initial = map.Decode(new double[4]);
        var evaluation = new DesignEvaluation { GeometryResiduals = [1], Violations = [new("test", ConstraintSeverity.Hard, "test")] };
        var count = 0;
        var result = FlatStartLocalSolver.Solve(map, initial, evaluation, 3,
            _ => { count++; return evaluation; }, false, () => false, default, (_, _, _) => { }, (_, _) => { });
        Assert.Equal(3, count);
        Assert.Equal(count, result.Evaluations);
        Assert.Equal(LeastSquaresTermination.EvaluationLimit, result.Termination);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => FlatStartLocalSolver.Solve(map, initial, evaluation, 3,
            _ => throw new InvalidOperationException("No evaluation after cancellation."), false, () => false,
            cancellation.Token, (_, _, _) => { }, (_, _) => { }));
    }

    [Fact]
    public void ScreeningPromotionNeighborhoodExplorationAndRestartAreMeasuredDecisions()
    {
        var spec = FlatStartRefinementTests.Spec();
        var parent = Parent(spec);
        var checkpoint = new FlatStartSearchCheckpoint { Specification = spec, Trials = [parent], UsableGlassNames = ["N-BK7", "N-F2"] };
        Assert.Equal(0, FlatStartScheduler.Decide(checkpoint).Operation);
        var children = new List<FamilyTrial>();
        string[] names = ["refine", "parameter-perturbation", "glass-swap", "stop-surface-change", "glass-pair-swap"];
        foreach (var name in names)
        {
            Assert.Equal(FlatStartScheduler.Operation(name), FlatStartScheduler.Decide(checkpoint).Operation);
            children.Add(parent with { TrialId = name, ParentTrialId = parent.TrialId, Operation = name, Index = children.Count + 1 });
            checkpoint = checkpoint with { Trials = new[] { parent }.Concat(children).ToArray() };
        }
        Assert.Null(FlatStartScheduler.Decide(checkpoint).Parent);
        var options = new FlatStartSearchOptions();
        Assert.True(FlatStartScheduler.Allocation(spec, options, "flat-root", 3)
            < FlatStartScheduler.Allocation(spec, options, "refine", 3));
    }

    [Fact]
    public void GlobalStagnationRequiresRestartsAndNoValidatedImprovement()
    {
        var spec = FlatStartRefinementTests.Spec();
        var parent = Parent(spec);
        var trials = Enumerable.Range(0, 49).Select(index => parent with
        { Index = index, TrialId = $"trial-{index:D4}", Operation = index % 20 == 0 ? "flat-root-retry" : "refine" }).ToArray();
        var checkpoint = new FlatStartSearchCheckpoint { Specification = spec, Trials = trials };
        Assert.True(FlatStartScheduler.IsStagnant(checkpoint));
        Assert.False(FlatStartScheduler.IsStagnant(checkpoint with { Trials = trials.Take(47).ToArray() }));
        trials[^1] = trials[^1] with { Candidate = parent.Candidate! with { Status = CandidateStatus.LabAccepted } };
        Assert.False(FlatStartScheduler.IsStagnant(checkpoint with { Trials = trials }));
    }

    [Fact]
    public async Task SchedulerStateTamperingIsRejectedBeforeResuming()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var checkpoint = (await new FlatStartSearchService().RunAsync(FlatStartRefinementTests.Spec(), cancellationToken: stop.Token)).Checkpoint;
        FlatStartCheckpointValidation.Validate(checkpoint);
        foreach (var altered in new[]
        {
            checkpoint.Schedule! with { NextDecisionIndex = 1 }, checkpoint.Schedule! with { NextRandomSeed = 0 },
            checkpoint.Schedule! with { PolicyVersion = 2 }
        }) Assert.Throws<InvalidDataException>(() => FlatStartCheckpointValidation.Validate(checkpoint with { Schedule = altered }));
    }

    private static FamilyTrial Parent(InitialStructureSpecification specification)
    {
        var snapshot = new FlatRootFactory().Create(specification, specification.MinimumElementCount);
        return new()
        {
            TrialId = "root",
            TracedRealRayCount = 100,
            Candidate = new()
            {
                Optic = snapshot,
                Status = CandidateStatus.TraceValid,
                Lineage = new() { ElementCount = specification.MinimumElementCount },
                Evaluation = new()
                {
                    EffectiveFocalLengthMillimeters = 50,
                    ValidRayFraction = 1,
                    RmsSpotRadiusMillimeters = 1,
                    MaximumSpotRadiusMillimeters = 2
                }
            }
        };
    }
}
