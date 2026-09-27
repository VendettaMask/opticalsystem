using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class FlatStartProgressiveRecoveryTests
{
    [Fact]
    public void FailedFullFieldEntryRecoversThroughMeasuredBridgeAndStillEnforcesFinalTargets()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OptilandWorkbench.slnx"))) directory = directory.Parent;
        var specifications = JsonSerializer.Deserialize<InitialStructureSpecification[]>(File.ReadAllText(Path.Combine(directory!.FullName,
            "labs/InitialStructure/benchmarks/physical-stop-search-20260927/specifications.json")),
            new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals })!;
        var specification = specifications[0] with
        {
            FlatStart = specifications[0].FlatStart! with { UsePhysicalStop = true },
            Budget = specifications[0].Budget with { MaximumEvaluations = 800 }
        };
        var family = DesignFormSearch.Roots(specifications[0], new(), ["N-BK7", "N-F2", "N-SF6"])[6];
        var result = new FlatStartDesignService().Solve(specification, family.ElementCount, family: family);
        var failed = Assert.Single(result.Steps, s => s.Operation == "progressive-stage-entry"
            && s.Stage == FlatStartDesignProblem.FullStage);
        Assert.False(failed.Evaluation.HasContinuousSearchResiduals);
        Assert.Contains(result.Steps, s => s.Operation == "progressive-bridge-entry"
            && s.Evaluation.HasContinuousSearchResiduals && s.Stage.FieldFraction > .5 && s.Stage.FieldFraction < 1);
        Assert.Contains(result.Steps, s => s.Operation == "progressive-stage-retry"
            && s.Stage == FlatStartDesignProblem.FullStage && s.Evaluation.HasContinuousSearchResiduals);
        Assert.InRange(result.EvaluationCount, failed.EvaluationNumber + 1, 800);
        Assert.True(result.TracedRayCount > 0);
        var final = result.FinalValidation!;
        Assert.True(final.HasContinuousSearchResiduals);
        Assert.True(final.IsFeasible);
        Assert.True(final.Objective!.IndependentValidation);
        Assert.True(final.Objective.UsePhysicalStop);
        Assert.Equal(FlatStartDesignProblem.FullStage, final.Objective.Stage);
        Assert.Equal(21, final.Fields.Count);
        Assert.All(final.Fields, f =>
        {
            Assert.Equal(f.AttemptedRays, f.ValidRays);
            Assert.Equal(f.CheckAttemptedRays, f.CheckValidRays);
        });
        // Recovery is not acceptance: this frozen difficult start still misses the RMS target.
        Assert.False(final.MeetsTargets);
        Assert.Equal(CandidateStatus.TraceValid, result.Candidate!.Status);
        Assert.True(result.Candidate.Evaluation.RmsSpotRadiusMillimeters > specification.MaximumRmsSpotRadiusMillimeters);
        var problem = new FlatStartDesignProblem(specification, family.ElementCount, result.Candidate.Optic, family);
        var fresh = problem.Evaluate(Optic.FromSnapshot(result.Candidate.Optic), FlatStartDesignProblem.FullStage, true, default, true);
        Assert.Equal(ContentFingerprint.Compute(final), ContentFingerprint.Compute(fresh));
        Assert.Equal(result.Candidate.OpticFingerprint, ContentFingerprint.Compute(fresh.EvaluatedOptic!));
    }
}
