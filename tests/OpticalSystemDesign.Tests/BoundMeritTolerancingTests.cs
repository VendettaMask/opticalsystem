using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

public sealed class BoundMeritTolerancingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentTablesDriveAlignmentFocusAndFinalAcceptance(bool joint)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var surfaces = app.Prescription.GetSurfaces();
        var focus = surfaces.Count - 2;
        var focusTarget = surfaces[focus].Thickness + .1;
        if (joint)
        {
            var second = app.MultiConfiguration.Add();
            app.MultiConfiguration.SetThickness(second, focus, surfaces[focus].Thickness + .18);
        }
        var alignment = Capture(app, "A 定心", Row(1, "GLCX", 3, .03));
        var refocus = Capture(app, "B 调焦", Row(1, "THIC", focus, focusTarget));
        var acceptance = Capture(app, "C 验收",
            Row(1, "GLCX", 3, .03), Row(2, "THIC", focus, focusTarget + .025));
        app.Optimization.SetMeritFunction([]);
        var revision = app.Events.Revision;
        var focusVariable = new ToleranceCompensatorDto(focus, ToleranceCompensatorKind.Thickness, -.4, .4,
            Stage: 2, Configuration: joint ? 1 : 0);
        ToleranceCompensatorDto center = new(3, ToleranceCompensatorKind.ElementDecenterX, -.15, .15, 4);
        var request = new TolerancingRequestDto(focus, 0, 0, 1, 241, 4,
            [new(1, true, ToleranceOperandKind.ElementDecenterX, 3, .06, .06, EndSurfaceNumber: 4)],
            Criterion: ToleranceCriterion.SystemMerit, YieldLimit: .01, MaxDegreeOfParallelism: 1,
            Mode: ToleranceAnalysisMode.Sensitivity,
            AdditionalCompensators: joint ? [center, focusVariable, focusVariable with { Configuration = 2 }] : [center, focusVariable],
            ConfigurationSettings: joint ? new(ToleranceConfigurationScope.All) : null,
            StageObjectives: [new(1, ToleranceCriterion.SystemMerit, MeritTable: alignment),
                new(2, ToleranceCriterion.SystemMerit, MeritTable: refocus)],
            MeritTable: acceptance);
        var validation = app.Tolerancing.ValidateStudy(request);
        Assert.True(validation.IsValid, string.Join(", ", validation.Messages));
        Assert.False(app.Tolerancing.ValidateStudy(request with { MeritTable = null }).IsValid);
        Assert.False(app.Tolerancing.ValidateStudy(request with { Criterion = ToleranceCriterion.RmsSpotRadius }).IsValid);
        var wrongStage = request.StageObjectives![0] with { Criterion = ToleranceCriterion.RmsSpotRadius };
        Assert.False(app.Tolerancing.ValidateStudy(request with { StageObjectives = [wrongStage, request.StageObjectives[1]] }).IsValid);

        var running = app.Tolerancing.RunAsync(request);
        var capturedRows = Assert.IsType<OpticalSystemDesign.Core.Serialization.MeritOperandSnapshot[]>(alignment.Rows);
        capturedRows[0] = capturedRows[0] with { Target = .2 };
        var result = await running;
        var trial = Assert.Single(result.TrialRows);
        var stages = trial.Stages!;
        Assert.Equal(new[] { alignment.Name, refocus.Name }, stages.Select(stage => stage.MeritTableName));
        Assert.All(stages, stage => Assert.InRange(stage.Criterion, 0, 1e-6));
        Assert.All(stages[1].Compensators, adjustment => Assert.InRange(Math.Abs(focusTarget - adjustment.Value), 0, 1e-7));
        Assert.Equal(joint ? 2 : 1, stages[1].Compensators.Count);
        var first = Assert.Single(stages[0].Compensators);
        Assert.Equal(first.Value, trial.Compensators!.Single(value => value.Name == first.Name).Value, 12);
        var finalValue = .025 / Math.Sqrt(2);
        Assert.InRange(Math.Abs(finalValue - trial.CriterionValue!.Value), 0, 1e-7);
        Assert.InRange(Math.Abs(.01 - finalValue - trial.AcceptanceMargin!.Value), 0, 1e-7);
        Assert.False(trial.Passed);
        Assert.Equal(revision, app.Events.Revision);
        Assert.Equal(alignment.Name, Assert.Single(result.SensitivityRows).NegativeStages![0].MeritTableName);

        var sample = Assert.IsType<ToleranceSampleDto>(result.WorstSample);
        Assert.Equal(5, sample.SchemaVersion);
        Assert.Equal(.03, Assert.Single(sample.Request.StageObjectives![0].MeritTable!.Rows).Target, 12);
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_BOUND_MERIT_CAPTURE_DIR");
        var capture = joint && !string.IsNullOrWhiteSpace(directory);
        var path = capture ? Path.Combine(directory!, "cooke-bound-merit.startolsample.json") : Path.GetTempFileName();
        try
        {
            if (capture) Directory.CreateDirectory(directory!);
            await app.Tolerancing.SaveSampleAsync(sample, path);
            using var other = WorkbenchApplication.Create("tessar");
            var replay = await other.Tolerancing.ReplaySampleAsync(path);
            Assert.True(replay.MatchesRecorded);
            var report = TolerancingPanel.BuildSampleReplayText(replay);
            Assert.Contains(alignment.Name, report);
            Assert.Contains(refocus.Name, report);
            Assert.Contains(acceptance.Name, report);
        }
        finally { if (!capture) File.Delete(path); }
    }

    private static ToleranceMeritTableDto Capture(WorkbenchApplication app, string name, params MeritOperandRowDto[] rows)
    {
        app.Optimization.SetMeritFunction(rows);
        return app.Tolerancing.CaptureMeritTable(name);
    }

    internal static MeritOperandRowDto Row(int index, string type, int surface, double target) =>
        new(index, true, type, surface, 0, 0, 0, 0, 0, 0, target, 1, 0, 0, "");
}
