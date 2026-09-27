using System.Text.Json;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Persistence;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class PhysicalStopSearchTests
{
    [Fact]
    public void HistoricalOmissionPreservesItsFingerprintAndCannotRankAgainstPhysicalStopScores()
    {
        var settings = JsonSerializer.Deserialize<FlatStartSettings>("{}")!;
        Assert.False(settings.UsePhysicalStop);
        Assert.DoesNotContain("UsePhysicalStop", JsonSerializer.Serialize(settings));
        var changed = settings with { UsePhysicalStop = true };
        Assert.NotEqual(ContentFingerprint.Compute(settings), ContentFingerprint.Compute(changed));
        var objective = new FlatStartObjectiveValue(FlatStartObjectiveKind.ConstrainedRealRaySumSquaresV2,
            FlatStartDesignProblem.FullStage, true, true, 1)
        { SamplingPolicy = FlatStartSamplingPolicy.UniformAreaGaussianV1, IndependentValidation = true };
        Assert.DoesNotContain("UsePhysicalStop", JsonSerializer.Serialize(objective));
        var candidate = new CandidateSnapshot { Evaluation = new() { FlatStartObjective = objective } };
        Assert.Equal(double.PositiveInfinity, FlatStartCandidateArchive.Score(Spec(), candidate));
        Assert.Equal(double.PositiveInfinity, FlatStartCandidateArchive.Score(Spec(), new CandidateSnapshot()));
    }

    [Fact]
    public void EveryStageRecalibratesTheStopWhileTheLensBodyFollowsTheBeam()
    {
        var spec = Spec();
        var root = new FlatStartProblem(spec, 3, .3).Root;
        var problem = new FlatStartDesignProblem(spec, 3, root);
        var vector = problem.Vector(root);
        var full = problem.CreateOptic(vector, FlatStartDesignProblem.FullStage);
        var result = problem.Evaluate(full, FlatStartDesignProblem.FullStage, true, default, true);
        Assert.True(full.RayAimingEnabled);
        Assert.True(result.Objective!.UsePhysicalStop);
        Assert.All(result.Fields, field => Assert.Equal(field.CheckAttemptedRays, field.CheckValidRays));
        Assert.Equal(ContentFingerprint.Compute(full.ToSnapshot()), ContentFingerprint.Compute(result.EvaluatedOptic!));
        CheckPupil(full);
        var stop = full.SurfaceGroup.Items.Single(s => s.IsStop);
        Assert.True(stop.MechanicalSemiDiameter > stop.SemiDiameter);
        var fullRadius = stop.SemiDiameter;
        var half = problem.CreateOptic(vector, new(.5, .5, true));
        problem.Evaluate(half, new(.5, .5, true), true, default);
        CheckPupil(half);
        Assert.Equal(fullRadius / 2, half.SurfaceGroup.Items.Single(s => s.IsStop).SemiDiameter, 11);

        // Move the stop behind a powered element: changing curvature changes the pupil map.
        full.SurfaceGroup.Items.Single(s => s.IsStop).IsStop = false;
        full.SurfaceGroup.Items[3].IsStop = true;
        full.SurfaceGroup.Items[1].Radius = 100;
        full.SurfaceGroup.Items[2].Radius = -100;
        problem.Evaluate(full, FlatStartDesignProblem.FullStage, true, default);
        CheckPupil(full);
        Assert.NotEqual(fullRadius, full.SurfaceGroup.Items[3].SemiDiameter);
    }

    [Fact]
    public void DiameterMarginChangesTheBodyWithoutChangingStopOrSpotMetrics()
    {
        (Optic Optic, DesignEvaluation Evaluation) Evaluate(double margin)
        {
            var spec = Spec() with { SemiDiameterMarginFactor = margin };
            var root = new FlatStartProblem(spec, 3, .3).Root;
            var problem = new FlatStartDesignProblem(spec, 3, root);
            var optic = problem.CreateOptic(problem.Vector(root), FlatStartDesignProblem.FullStage);
            return (optic, problem.Evaluate(optic, FlatStartDesignProblem.FullStage, true, default));
        }
        var small = Evaluate(1.05);
        var large = Evaluate(1.4);
        var a = small.Optic.SurfaceGroup.Items.Single(s => s.IsStop);
        var b = large.Optic.SurfaceGroup.Items.Single(s => s.IsStop);
        Assert.Equal(a.SemiDiameter, b.SemiDiameter);
        Assert.True(b.MechanicalSemiDiameter > a.MechanicalSemiDiameter);
        Assert.Equal(ContentFingerprint.Compute(small.Evaluation.Fields), ContentFingerprint.Compute(large.Evaluation.Fields));
    }

    [Fact]
    public void InvalidStopCalibrationPublishesNoOpticalMetricsOrContinuousResiduals()
    {
        var spec = Spec();
        var root = new FlatStartProblem(spec, 3, .3).Root;
        var problem = new FlatStartDesignProblem(spec, 3, root);
        var optic = problem.CreateOptic(problem.Vector(root), FlatStartDesignProblem.FullStage);
        var valid = problem.Evaluate(optic, FlatStartDesignProblem.FullStage, true, default);
        optic.Aperture.Value = .01;
        var rejected = problem.Evaluate(optic, FlatStartDesignProblem.FullStage, true, default);
        Assert.False(rejected.MeetsTargets);
        Assert.False(rejected.HasContinuousSearchResiduals);
        Assert.False(rejected.IsFeasible);
        Assert.Equal(valid.Residuals.Count, rejected.Residuals.Count);
        Assert.All(rejected.Residuals, value => Assert.True(double.IsFinite(value)));
        Assert.Contains(rejected.Violations, v => v.Code == "geometry.stop-calibration");
        Assert.All(rejected.Fields, f => { Assert.Null(f.RmsRadiusMillimeters); Assert.Equal(0, f.ValidRays); });
    }

    [Fact]
    public void GeometryChecksTheLensBodyBeyondItsSmallClearStop()
    {
        var spec = Spec();
        var geometry = new FlatStartProblem(spec, 3, .3);
        var optic = Optic.FromSnapshot(geometry.Root);
        var front = optic.SurfaceGroup.Items[1];
        var back = optic.SurfaceGroup.Items[2];
        front.Radius = 10;
        back.Radius = -10;
        front.SemiDiameter = back.SemiDiameter = .4;
        front.MechanicalSemiDiameter = back.MechanicalSemiDiameter = 6;
        Assert.Contains(geometry.GeometryViolations(optic), v => v.Code == "geometry.edge-thickness");
    }

    [Fact]
    public void FixedBodyCannotGrowSilentlyToContainALargerStop()
    {
        var settings = Spec().FlatStart! with { AutomaticLensDiameters = false };
        var optic = Optic.FromSnapshot(new FlatRootFactory().Create(Spec(), 3));
        var stop = optic.SurfaceGroup.Items.Single(s => s.IsStop);
        stop.MechanicalSemiDiameter = .2;
        var failure = FlatStartProblem.PreparePhysicalStop(optic, settings);
        Assert.Equal("geometry.stop-outside-body", failure!.Code);
        Assert.Equal(.2, stop.MechanicalSemiDiameter);
    }

    [Fact]
    public void ReportedWorkIncludesCoreAimingIterations()
    {
        var spec = Spec();
        using var measured = SequentialTraceMeasurement.Begin();
        var result = new FlatStartDesignService().Solve(spec, 3);
        Assert.Equal(measured.RayCount, result.TracedRayCount);
        Assert.True(result.TracedRayCount > result.FinalValidation!.Fields.Sum(f => f.AttemptedRays + f.CheckAttemptedRays));
    }

    [Fact]
    public async Task SearchCheckpointAndExportKeepTheExactPhysicalEvaluationAndMode()
    {
        var spec = Spec();
        var search = await new FlatStartSearchService().RunAsync(spec,
            new() { MaximumEvaluationsPerTrial = spec.Budget.MaximumEvaluations });
        FlatStartCheckpointValidation.Validate(search.Checkpoint);
        var trial = search.Checkpoint.Trials.First(t => t.Candidate is not null);
        var candidate = trial.Candidate!;
        var optic = Optic.FromSnapshot(candidate.Optic);
        Assert.True(optic.RayAimingEnabled);
        CheckPupil(optic);
        Assert.True(candidate.Evaluation.FlatStartObjective!.UsePhysicalStop);
        var problem = new FlatStartDesignProblem(spec, trial.Family.ElementCount, candidate.Optic, trial.Family);
        var fresh = problem.Evaluate(optic, FlatStartDesignProblem.FullStage, true, default, true);
        Assert.Equal(candidate.OpticFingerprint, ContentFingerprint.Compute(fresh.EvaluatedOptic!));
        Assert.Equal(ContentFingerprint.Compute(trial.FinalValidation), ContentFingerprint.Compute(fresh with { Residuals = [] }));
        foreach (var field in fresh.Fields.Where(f => f.ValidRays == f.AttemptedRays))
        {
            var spot = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, field.NormalizedFieldY,
                FlatStartDesignProblem.IntegrationPupils(spec.FlatStart!.SamplingPolicy, true, true), includeSurfaceTransmission: false);
            Assert.Equal(spot.Metrics!.RmsSpotRadius, field.RmsRadiusMillimeters);
        }
        var tampered = trial with { Candidate = candidate with { Optic = candidate.Optic with { RayAimingEnabled = false } } };
        Assert.Throws<InvalidDataException>(() => FlatStartCheckpointValidation.Validate(search.Checkpoint with
        { Trials = search.Checkpoint.Trials.Select(t => t == trial ? tampered : t).ToArray() }));
        var path = Path.Combine(Path.GetTempPath(), $"physical-stop-{Guid.NewGuid():N}.staropt");
        try
        {
            await new CandidateExportService().ExportStarOptAsync(candidate, path);
            var restored = await StarOptProjectStore.LoadAsync(path);
            Assert.Equal(candidate.OpticFingerprint, ContentFingerprint.Compute(restored.Configurations[0].ToSnapshot()));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void CheckPupil(Optic optic)
    {
        var expected = optic.Paraxial.EstimateEntrancePupilDiameter();
        var floated = Optic.FromSnapshot(optic.ToSnapshot());
        floated.Aperture.Kind = ApertureKind.FloatByStopSize;
        Assert.Equal(expected, floated.Paraxial.EstimateEntrancePupilDiameter(), 10);
        var stop = optic.SurfaceGroup.Items.Single(s => s.IsStop);
        Assert.Equal(stop.SemiDiameter, Assert.IsType<CircularAperture>(stop.PhysicalAperture).Radius);
    }

    internal static InitialStructureSpecification Spec() => new()
    {
        EffectiveFocalLengthMillimeters = 50,
        FNumber = 8,
        MaximumFieldAngleDegrees = 7,
        MinimumElementCount = 3,
        MaximumElementCount = 3,
        Wavelengths = [new() { Nanometers = 587.6, Weight = 1, IsPrimary = true }],
        FlatStart = new() { UsePhysicalStop = true, AutomaticLensDiameters = true, SamplingPolicy = FlatStartSamplingPolicy.UniformAreaGaussianV1 },
        Budget = new() { MaximumEvaluations = 180, InitialSeedCount = 1, MaximumParallelism = 1, TimeLimit = TimeSpan.FromMinutes(2) }
    };
}
