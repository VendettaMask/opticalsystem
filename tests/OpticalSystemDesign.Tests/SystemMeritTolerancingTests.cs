using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Tolerancing;

namespace OpticalSystemDesign.Tests;

public sealed class SystemMeritTolerancingTests
{
    [Fact]
    public void OrderedResidualsPreserveRowReferencesAndSkipTargets()
    {
        var optic = Optic.CreateCookeTriplet();
        var thickness = optic.SurfaceGroup.Items[1].Thickness;
        MeritOperandDefinition[] rows =
        [
            new() { Type = "THIC", Surface = 1, Weight = 0 },
            new() { Type = "THIC", Surface = 2, Weight = 0 },
            new() { Type = "DIFF", ZemaxIntegerParameters = [1, 2], Target = thickness - optic.SurfaceGroup.Items[2].Thickness - .2, Weight = 4 },
            new() { Type = "GOTO", Surface = 6, Weight = 0 },
            new() { Type = "THIC", Surface = 1, Target = 999, Weight = 3 },
            new() { Type = "BLNK", Weight = 0 }
        ];
        var residuals = MeritFunctionCatalog.CreateToleranceResiduals(optic, rows);
        Assert.Equal(.2, residuals[0].Evaluate(), 12);
        Assert.Equal(0, residuals[1].Evaluate());
        Assert.Equal(.16, residuals.Sum(row => row.Squared()), 12);
        optic.SurfaceGroup.Items[1].Thickness += .15;
        Assert.Equal(.35, residuals[0].Evaluate(), 12);

        rows[0].Surface = 99;
        var objective = new Tolerancing { StrictMerit = true };
        foreach (var residual in residuals) objective.AddOperand(residual);
        Assert.Equal(double.PositiveInfinity, objective.Criterion());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomMeritDrivesCompensationAndReplaysTheFrozenEditorTable(bool joint)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var surfaces = app.Prescription.GetSurfaces();
        var focus = surfaces.Count - 2;
        var targetGap = surfaces[focus].Thickness;
        const double spacing = .15;
        if (joint)
        {
            var second = app.MultiConfiguration.Add();
            app.MultiConfiguration.SetThickness(second, focus, targetGap + spacing);
            app.MultiConfiguration.Activate(second);
        }
        var definitions = DifferenceTable(focus, targetGap - surfaces[1].Thickness);
        app.Optimization.SetMeritFunction(definitions);
        var revision = app.Events.Revision;
        var navigation = app.MultiConfiguration.GetNavigationState();
        var compensator = new ToleranceCompensatorDto(focus, ToleranceCompensatorKind.Thickness, -.4, .4,
            Configuration: joint ? 1 : 0);
        var request = new TolerancingRequestDto(focus, 0, 0, 1, 21, 4,
            [new(1, true, ToleranceOperandKind.Thickness, focus, .05, .05)],
            Criterion: ToleranceCriterion.SystemMerit, YieldLimit: .002, MaxDegreeOfParallelism: 1,
            Mode: ToleranceAnalysisMode.Sensitivity,
            AdditionalCompensators: joint ? [compensator, compensator with { Configuration = 2 }] : [compensator],
            ConfigurationSettings: joint ? new(ToleranceConfigurationScope.All) : null,
            StageObjectives: [new(1, ToleranceCriterion.SystemMerit)]);
        var validation = app.Tolerancing.ValidateStudy(request);
        Assert.True(validation.IsValid, string.Join(", ", validation.Messages));
        var uncompensated = await app.Tolerancing.RunAsync(request with { CompensationIterations = 0, StageObjectives = null });
        var raw = Assert.Single(uncompensated.TrialRows);
        Assert.Equal(joint ? .05 + spacing : .05, raw.CriterionValue!.Value, 12);
        Assert.False(raw.Passed);

        var result = await app.Tolerancing.RunAsync(request);
        var trial = Assert.Single(result.TrialRows);
        var stage = Assert.Single(trial.Stages!);
        Assert.Equal(ToleranceCriterion.SystemMerit, stage.Objective!.Criterion);
        Assert.InRange(stage.Criterion, 0, .00001);
        Assert.True(trial.Passed);
        Assert.All(stage.Compensators, variable => Assert.InRange(variable.Value, targetGap - .00001, targetGap + .00001));
        Assert.Equal(joint ? 2 : 1, stage.Compensators.Count);
        Assert.Equal(revision, app.Events.Revision);
        Assert.Equal(navigation, app.MultiConfiguration.GetNavigationState());
        Assert.Equal(ToleranceCriterion.SystemMerit, Assert.Single(Assert.Single(result.SensitivityRows).NegativeStages!).Objective!.Criterion);

        if (joint)
        {
            var mixed = await app.Tolerancing.RunAsync(request with
            {
                Criterion = ToleranceCriterion.RmsSpotRadius, YieldLimit = 1, Mode = ToleranceAnalysisMode.SkipSensitivity
            });
            var mixedTrial = Assert.Single(mixed.TrialRows);
            Assert.Equal(stage.Criterion, Assert.Single(mixedTrial.Stages!).Criterion, 12);
            Assert.InRange(mixedTrial.CriterionValue!.Value, .001, 1);
            Assert.True(mixedTrial.Passed);
        }

        var chart = ToleranceChartBuilder.Histogram(result, ToleranceCriterion.SystemMerit);
        Assert.Equal(AnalysisAxisQuantity.MeritValue, Assert.Single(chart.Series).XQuantity);

        var sample = Assert.IsType<ToleranceSampleDto>(result.WorstSample);
        Assert.Equal(4, sample.SchemaVersion);
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SYSTEM_MERIT_CAPTURE_DIR");
        var capture = joint && !string.IsNullOrWhiteSpace(directory);
        var path = capture ? Path.Combine(directory!, "cooke-system-merit.startolsample.json") : Path.GetTempFileName();
        try
        {
            if (capture) Directory.CreateDirectory(directory!);
            await app.Tolerancing.SaveSampleAsync(sample, path);
            app.Optimization.SetMeritFunction(DifferenceTable(focus, targetGap + 2));
            var replay = await app.Tolerancing.ReplaySampleAsync(path);
            Assert.True(replay.MatchesRecorded);
            Assert.Contains("系统评价函数", TolerancingPanel.BuildSampleReplayText(replay));

            app.Optimization.SetMeritFunction([Row(1, "CONF", 2, 0, 0), .. definitions]);
            var invalid = app.Tolerancing.ValidateStudy(request);
            Assert.False(invalid.IsValid);
            Assert.Contains("CONF 第 1 行", Assert.Single(invalid.Messages));
            app.Optimization.SetMeritFunction([]);
            Assert.False(app.Tolerancing.ValidateStudy(request).IsValid);
        }
        finally { if (!capture) File.Delete(path); }
    }

    internal static MeritOperandRowDto[] DifferenceTable(int focus, double target) =>
    [
        Row(1, "THIC", focus, 0, 0),
        Row(2, "THIC", 1, 0, 0),
        Row(3, "DIFF", 1, target, 4) with { ZemaxInt1 = 1, ZemaxInt2 = 2 }
    ];

    private static MeritOperandRowDto Row(int index, string type, int surface, double target, double weight) =>
        new(index, true, type, surface, 0, 0, 0, 0, 0, 0, target, weight, 0, 0, "");
}
