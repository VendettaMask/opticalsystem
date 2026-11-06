using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Tolerancing;

namespace OpticalSystemDesign.Tests;

public sealed class StagedTolerancingTests
{
    [Fact]
    public void EachStageOptimizesOnlyItsOwnVariablesAndKeepsEarlierAdjustment()
    {
        var x = 0d;
        var y = 0d;
        var tolerancing = new Tolerancing();
        tolerancing.AddOperand(new Operand("combined", 2, 1, () => x + y));
        tolerancing.AddOperand(new Operand("second", 1, 1, () => y));
        tolerancing.AddCompensator(new DelegateVariable("second", () => y, next => y = next, -4, 4), 2);
        tolerancing.AddCompensator(new DelegateVariable("first", () => x, next => x = next, -4, 4), 1);

        var result = tolerancing.EvaluateCompensated(16, CancellationToken.None);
        var stages = Assert.IsAssignableFrom<IReadOnlyList<ToleranceStageValue>>(result.Stages);
        Assert.Equal(new[] { 1, 2 }, stages.Select(stage => stage.Stage));
        Assert.InRange(x, 1.9999, 2.0001);
        Assert.InRange(y, .4999, .5001);
        Assert.Equal(x, Assert.Single(stages[0].Compensators).Value);
        Assert.Equal(y, Assert.Single(stages[1].Compensators).Value);
        Assert.Equal(result.Criterion, stages[1].Criterion);
        Assert.Throws<ArgumentOutOfRangeException>(() => tolerancing.EvaluateCompensated(251, CancellationToken.None));
    }

    [Fact]
    public void StageObjectivesChangeTheOptimizedResidualsAndLeaveFinalCriterionIndependent()
    {
        double x = 0, y = 0;
        var final = new Tolerancing();
        final.AddOperand(new Operand("final", 0, 1, () => x + y));
        final.AddCompensator(new DelegateVariable("x", () => x, value => x = value, -5, 5), 1);
        final.AddCompensator(new DelegateVariable("y", () => y, value => y = value, -5, 5), 2);
        var first = new Tolerancing();
        first.AddOperand(new Operand("center", 2, 1, () => x));
        var second = new Tolerancing();
        second.AddOperand(new Operand("focus", 3, 1, () => x + y));
        final.SetStageObjective(1, first);
        final.SetStageObjective(2, second);

        var result = final.EvaluateCompensated(12, CancellationToken.None);
        Assert.InRange(x, 1.99999, 2.00001);
        Assert.InRange(y, .99999, 1.00001);
        Assert.InRange(result.Criterion, 2.99999, 3.00001);
        Assert.All(result.Stages!, stage => Assert.InRange(stage.Criterion, 0, 1e-5));
    }

    [Fact]
    public void UnmeasurableStageInvalidatesTheRunBeforeTheNextActuatorMoves()
    {
        double x = 0, y = 0;
        var study = new Tolerancing();
        study.AddOperand(new Operand("final", 2, 1, () => y));
        study.AddCompensator(new DelegateVariable("x", () => x, value => x = value, -5, 5), 1);
        study.AddCompensator(new DelegateVariable("y", () => y, value => y = value, -5, 5), 2);
        var failed = new Tolerancing { HigherIsBetter = true };
        failed.AddOperand(new Operand("lost ray", 1, 1, () => 0));
        failed.SetCriterionEvaluator(() => double.NegativeInfinity);
        study.SetStageObjective(1, failed);

        var result = study.EvaluateCompensated(5, CancellationToken.None);
        Assert.Equal(0, y);
        Assert.Equal(double.PositiveInfinity, result.Criterion);
        var stage = Assert.Single(result.Stages!);
        Assert.Equal(1, stage.Stage);
        Assert.Equal(double.NegativeInfinity, stage.Criterion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CookeSwitchesFromRmsToMtfAndReplaysTheSameStages(bool joint)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var surfaces = app.Prescription.GetSurfaces();
        var focus = surfaces.Count - 2;
        if (joint)
        {
            var second = app.MultiConfiguration.Add();
            app.MultiConfiguration.SetThickness(second, focus, surfaces[focus].Thickness + .25);
            app.MultiConfiguration.SetThickness(second, 2, surfaces[2].Thickness + .15);
        }
        var revision = app.Events.Revision;
        var threshold = joint ? .08 : .30;
        var finalMtf = new ToleranceMtfSettingsDto(30, Method: ToleranceMtfMethod.Geometric,
            FieldLimits: [new(2, threshold)]);
        var stageMtf = finalMtf with { Frequency = 18, FieldLimits = null };
        ToleranceCompensatorDto center = new(1, ToleranceCompensatorKind.ElementDecenterX, -.12, .12, 2);
        ToleranceCompensatorDto refocus = new(focus, ToleranceCompensatorKind.Thickness, -.8, .8, Stage: 2,
            Configuration: joint ? 1 : 0);
        var request = new TolerancingRequestDto(1, 0, 0, 1, 193, 3,
            [new(1, true, ToleranceOperandKind.ElementDecenterX, 1, .04, .06, EndSurfaceNumber: 2)],
            Criterion: ToleranceCriterion.Mtf, MaxDegreeOfParallelism: 1,
            Mode: joint ? ToleranceAnalysisMode.Sensitivity : ToleranceAnalysisMode.SkipSensitivity, MtfSettings: finalMtf,
            AdditionalCompensators: joint ? [center, refocus, refocus with { Configuration = 2 }] : [center, refocus],
            ConfigurationSettings: joint ? new(ToleranceConfigurationScope.All) : null,
            StageObjectives: [new(1, ToleranceCriterion.RmsSpotRadius), new(2, ToleranceCriterion.Mtf, stageMtf)]);
        var validation = app.Tolerancing.ValidateStudy(request);
        Assert.True(validation.IsValid, string.Join(", ", validation.Messages));
        Assert.False(app.Tolerancing.ValidateStudy(request with { CompensationIterations = 0 }).IsValid);
        Assert.False(app.Tolerancing.ValidateStudy(request with
        {
            StageObjectives = [new(3, ToleranceCriterion.RmsSpotRadius)]
        }).IsValid);
        Assert.False(app.Tolerancing.ValidateStudy(request with
        {
            StageObjectives = [new(2, ToleranceCriterion.Mtf, finalMtf)]
        }).IsValid);
        var baseline = await app.Tolerancing.RunAsync(request with
        {
            Criterion = ToleranceCriterion.RmsSpotRadius, MtfSettings = null, StageObjectives = null,
            Mode = ToleranceAnalysisMode.SkipSensitivity, AdditionalCompensators = [center]
        });
        var result = await app.Tolerancing.RunAsync(request);
        var trial = Assert.Single(result.TrialRows);
        var stages = trial.Stages!;
        Assert.Equal(new[] { 1, 2 }, stages.Select(stage => stage.Stage));
        var first = Assert.Single(stages[0].Compensators);
        var expectedFirst = Assert.Single(Assert.Single(baseline.TrialRows).Compensators!);
        Assert.Equal(expectedFirst.Value, first.Value, 12);
        Assert.Equal(first.Value, trial.Compensators!.Single(adjustment => adjustment.Name == first.Name).Value, 12);
        Assert.Equal(ToleranceCriterion.RmsSpotRadius, stages[0].Objective!.Criterion);
        Assert.Equal(18, stages[1].Objective!.MtfSettings!.Frequency);
        Assert.Equal(joint ? 2 : 1, stages[1].Compensators.Count);
        Assert.All(stages[1].Compensators, item => Assert.InRange(item.Value, item.Minimum, item.Maximum));
        var stageFields = joint ? stages[1].Configurations!.Select(config => config.Fields!) : [stages[1].Fields!];
        var finalFields = joint ? trial.Configurations!.Select(config => config.Fields!) : [trial.Fields!];
        Assert.All(stageFields, fields =>
        {
            Assert.Equal(3, fields.Count);
            Assert.True(fields.Single(field => field.FieldNumber == 2).Value > threshold);
        });
        var limitingFinal = finalFields.Min(fields => fields.Single(field => field.FieldNumber == 2).Value);
        Assert.True(limitingFinal < threshold);
        Assert.False(trial.Passed);
        Assert.Equal(limitingFinal - threshold, trial.AcceptanceMargin!.Value, 12);
        if (joint)
        {
            var endpoints = Assert.Single(result.SensitivityRows);
            Assert.Equal(new[] { 1, 2 }, endpoints.NegativeStages!.Select(stage => stage.Stage));
            Assert.Equal(new[] { 1, 2 }, endpoints.PositiveStages!.Select(stage => stage.Stage));
            Assert.True(Math.Abs(stages[1].Compensators[0].Value - stages[1].Compensators[1].Value) > .1);
        }
        Assert.Equal(result.MtfSettings!.Frequency, finalMtf.Frequency);
        Assert.Equal(revision, app.Events.Revision);

        var sample = Assert.IsType<ToleranceSampleDto>(result.WorstSample);
        Assert.Equal(3, sample.SchemaVersion);
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_STAGE_OBJECTIVE_CAPTURE_DIR");
        var capture = joint && !string.IsNullOrWhiteSpace(directory);
        var path = capture ? Path.Combine(directory!, "cooke-stage-objectives.startolsample.json") : Path.GetTempFileName();
        try
        {
            if (capture) Directory.CreateDirectory(directory!);
            await app.Tolerancing.SaveSampleAsync(sample, path);
            using var receiver = WorkbenchApplication.Create("tessar");
            var replay = await receiver.Tolerancing.ReplaySampleAsync(path);
            Assert.True(replay.MatchesRecorded);
            Assert.Contains("MTF 18 cycles/mm", TolerancingPanel.BuildSampleReplayText(replay));
        }
        finally { if (!capture) File.Delete(path); }
    }

    [Fact]
    public async Task WorstJointIsoSampleReplaysFromFileWithEarlierStagesLocked()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var before = app.Prescription.GetSurfaces();
        var revision = app.Events.Revision;
        var focus = before.Count - 2;
        var request = new TolerancingRequestDto(1, 0, 0, 3, 173, 3,
            [new(1, true, ToleranceOperandKind.IsoIrregularityPv, 1, -1500, 1500),
             new(2, true, ToleranceOperandKind.IsoRotationalIrregularityPv, 1, -600, 600),
             new(3, true, ToleranceOperandKind.ElementDecenterX, 1, .15, .20, EndSurfaceNumber: 2),
             new(4, true, ToleranceOperandKind.Compensator, focus, -.1, .1)],
            YieldLimit: .05, MaxDegreeOfParallelism: 2, Mode: ToleranceAnalysisMode.SkipSensitivity,
            AdditionalCompensators:
            [new(1, ToleranceCompensatorKind.ElementDecenterX, -.1, .1, 2),
             new(1, ToleranceCompensatorKind.ElementTiltY, -.1, .1, 2, new(0, 0, 4), Stage: 2)]);

        Assert.True(app.Tolerancing.ValidateStudy(request).IsValid);
        Assert.False(app.Tolerancing.ValidateStudy(request with { CompensationIterations = 167 }).IsValid);
        Assert.False(app.Tolerancing.ValidateStudy(request with
        {
            AdditionalCompensators = [request.AdditionalCompensators![0] with { Stage = 9 }]
        }).IsValid);
        var result = await app.Tolerancing.RunAsync(request);
        Assert.Equal(new[] { 1, 2, 3 }, result.NominalStages!.Select(stage => stage.Stage));
        Assert.Equal(3, result.TrialRows.Count);
        Assert.All(result.TrialRows, trial =>
        {
            var stages = Assert.IsAssignableFrom<IReadOnlyList<ToleranceStageValueDto>>(trial.Stages);
            Assert.Equal(new[] { 1, 2, 3 }, stages.Select(stage => stage.Stage));
            foreach (var stage in stages)
            {
                var adjusted = Assert.Single(stage.Compensators);
                Assert.InRange(adjusted.Value, adjusted.Minimum, adjusted.Maximum);
                Assert.Equal(adjusted.Value, trial.Compensators!.Single(c => c.Name == adjusted.Name).Value, 10);
            }
        });
        Assert.Equal(before, app.Prescription.GetSurfaces());
        Assert.Equal(revision, app.Events.Revision);

        var sample = Assert.IsType<ToleranceSampleDto>(result.WorstSample);
        Assert.Equal(result.TrialRows.OrderBy(trial => trial.AcceptanceMargin).First(), sample.Recorded);
        var path = Path.GetTempFileName();
        try
        {
            await app.Tolerancing.SaveSampleAsync(sample, path);
            using var receiver = WorkbenchApplication.Create("tessar");
            var receiverBefore = receiver.Prescription.GetSurfaces();
            var receiverRevision = receiver.Events.Revision;
            var replay = await receiver.Tolerancing.ReplaySampleAsync(path);
            Assert.True(replay.MatchesRecorded);
            Assert.Equal(sample.Recorded.CriterionValue, replay.Replayed.CriterionValue);
            Assert.Equal(new[] { 1, 2, 3 }, replay.Replayed.Stages!.Select(stage => stage.Stage));
            Assert.Contains("装调阶段 3", TolerancingPanel.BuildSampleReplayText(replay));
            Assert.Equal(receiverBefore, receiver.Prescription.GetSurfaces());
            Assert.Equal(receiverRevision, receiver.Events.Revision);

            await app.Tolerancing.SaveSampleAsync(sample with
            {
                Recorded = sample.Recorded with { CompensatedMerit = "999" }
            }, path);
            Assert.False((await receiver.Tolerancing.ReplaySampleAsync(path)).MatchesRecorded);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await app.Tolerancing.SaveSampleAsync(
                sample with { Recorded = sample.Recorded with { Trial = 4 } }, path));
        }
        finally { File.Delete(path); }
    }
}
