using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Tolerancing;

namespace OpticalSystemDesign.Tests;

public sealed class JointConfigurationTolerancingTests
{
    [Fact]
    public void SharedIsoRealizationUsesSamePhysicalSagDespiteDifferentApertures()
    {
        var first = Optic.CreateCookeTriplet();
        var second = Optic.CreateCookeTriplet();
        var a = first.SurfaceGroup.Items[1];
        var b = second.SurfaceGroup.Items[1];
        a.PhysicalAperture = new CircularAperture(6);
        b.PhysicalAperture = new CircularAperture(4);
        var nominalA = a.Geometry;
        var nominalB = b.Geometry;

        SharedToleranceExecution.ApplyTrial([(first, Build(first)), (second, Build(second))], 17, 3, CancellationToken.None);

        foreach (var (x, y) in new[] { (0d, 0d), (2d, 0d), (0d, 3d), (2d, 2d), (4d, 0d) })
            Assert.Equal(a.Geometry.Sag(x, y) - nominalA.Sag(x, y), b.Geometry.Sag(x, y) - nominalB.Sag(x, y), 13);
        Assert.NotEqual(nominalA.Sag(2, 2), a.Geometry.Sag(2, 2));
        Assert.Equal(6, Assert.IsType<CircularAperture>(a.PhysicalAperture).Radius);
        Assert.Equal(4, Assert.IsType<CircularAperture>(b.PhysicalAperture).Radius);

        static Tolerancing Build(Optic optic)
        {
            var study = optic.CreateTolerancing();
            study.AddPerturbation(new SurfaceFormPerturbation("ISOB", 1, SurfaceFormMode.IsoIrregularityPv,
                1600, false, 0, 0, IsoSurfaceFormUnit.Nanometers, .6328, IsoSurfaceFormStatistics.TruncatedGaussian));
            study.AddPerturbation(new SurfaceFormPerturbation("ISOC", 1, SurfaceFormMode.IsoRotationalIrregularityPv,
                600, false, 0, 0, IsoSurfaceFormUnit.Nanometers, .6328, IsoSurfaceFormStatistics.TruncatedGaussian));
            return study;
        }
    }

    [Fact]
    public void JointPreflightIdentifiesConfigurationAndRowWhenEntityDoesNotMatch()
    {
        var reference = Optic.CreateCookeTriplet().ToSnapshot();
        var incompatible = Optic.CreateCookeTriplet().ToSnapshot();
        incompatible.Surfaces[1] = incompatible.Surfaces[1] with { Label = "different entity" };
        var request = new TolerancingRequestDto(1, 0, 0, 1, 4, 0,
            [new(1, true, ToleranceOperandKind.Radius, 1, -.01, .01)],
            ConfigurationSettings: new(ToleranceConfigurationScope.All));
        var error = Assert.Throws<ArgumentException>(() => WorkbenchRuntime.ValidateJointTolerancing(
            new([reference, incompatible], 1, [], [], []), request));
        Assert.Contains("配置 2", error.Message);
        Assert.Contains("公差第 1 行", error.Message);
        Assert.Contains("面 1", error.Message);
    }

    [Fact]
    public void SharedPowerFringesUseTheReferenceAperture()
    {
        var first = Optic.CreateCookeTriplet();
        var second = Optic.CreateCookeTriplet();
        var a = first.SurfaceGroup.Items[1];
        var b = second.SurfaceGroup.Items[1];
        a.PhysicalAperture = new CircularAperture(6);
        b.PhysicalAperture = new CircularAperture(4);
        var original = 1 / a.Radius;
        SharedToleranceExecution.ApplyEndpoint([(first, Build(first)), (second, Build(second))], 0, true, 1);
        Assert.Equal(.0006328 * 2 / 36, 1 / a.Radius - original, 14);
        Assert.Equal(1 / a.Radius, 1 / b.Radius, 14);

        static Tolerancing Build(Optic optic)
        {
            var study = optic.CreateTolerancing();
            study.AddPerturbation(new SurfaceFringePerturbation("TFRN", 1, SurfaceFringeMode.Power, .6328, -2, 2, false));
            return study;
        }
    }

    [Fact]
    public void PairedAcceptanceCountsOnlyTrialsThatPassAtBothEnds()
    {
        ToleranceConfigurationResultDto At(int config, double margin) => new(config, 1, null, null, margin >= 0, margin);
        ToleranceConfigurationResultDto[][] trials =
        [
            [At(1, .1), At(2, .2)],
            [At(1, .1), At(2, -.2)],
            [At(1, -.1), At(2, .2)],
            [At(1, .1), At(2, double.NegativeInfinity) with { Criterion = double.PositiveInfinity }]
        ];
        var margins = trials.Select(row => WorkbenchRuntime.JointAcceptanceMargin(row, true)).ToArray();
        Assert.Equal(.75, trials.Count(row => row[0].Passed == true) / (double)trials.Length);
        Assert.Equal(.5, trials.Count(row => row[1].Passed == true) / (double)trials.Length);
        Assert.Equal(.25, margins.Count(margin => margin >= 0) / (double)trials.Length);
        Assert.Equal(double.NegativeInfinity, margins[3]);
    }

    [Fact]
    public async Task ConfigurationSpecificMtfFieldLimitsDetermineJointAcceptance()
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.MultiConfiguration.Add();
        var request = new TolerancingRequestDto(1, 0, 0, 1, 97, 0,
            [new(1, true, ToleranceOperandKind.Thickness, 1, -.0001, .0001)],
            Criterion: ToleranceCriterion.Mtf, Mode: ToleranceAnalysisMode.SkipSensitivity, MaxDegreeOfParallelism: 1,
            MtfSettings: new(30, Method: ToleranceMtfMethod.Geometric, SeparateFields: true),
            ConfigurationSettings: new(ToleranceConfigurationScope.All, Limits: [new(1, 1e-6), new(2, 1e-6, [new(2, 1)])]));
        var result = await app.Tolerancing.RunAsync(request);
        var trial = Assert.Single(result.TrialRows);
        var configurations = trial.Configurations!;
        Assert.True(configurations[0].Passed, configurations[0].Error);
        Assert.False(configurations[1].Passed);
        Assert.Equal(2, configurations[1].LimitingField);
        Assert.False(trial.Passed);
        Assert.Equal(configurations[1].AcceptanceMargin, trial.AcceptanceMargin);
        Assert.Equal(configurations[1].Fields!.Single(field => field.FieldNumber == 2).Value - 1, trial.AcceptanceMargin!.Value, 12);
        SaveEvidence("cooke-mtf-field-acceptance.json", new { request.ConfigurationSettings, result.TrialRows });
    }

    [Fact]
    public async Task TwoConfigurationStudyUsesOneActuatorAndReplaysTheJointWorstTrial()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var focus = app.Prescription.GetSurfaces().Count - 2;
        var nominalGap = app.Prescription.GetSurfaces()[focus].Thickness;
        var second = app.MultiConfiguration.Add();
        app.MultiConfiguration.SetThickness(second, focus, nominalGap + .4);
        app.MultiConfiguration.SetThickness(second, 2, app.Prescription.GetSurfaces()[2].Thickness + .35);
        var revision = app.Events.Revision;
        var navigation = app.MultiConfiguration.GetNavigationState();
        var before = app.MultiConfiguration.GetThicknessDifferences();
        var settings = new ToleranceConfigurationSettingsDto(ToleranceConfigurationScope.All,
            Limits: [new(1, 1), new(2, 1e-7)]);
        var request = new TolerancingRequestDto(1, 0, 0, 3, 431, 3,
            [new(1, true, ToleranceOperandKind.IsoIrregularityPv, 1, -1500, 1500),
             new(2, true, ToleranceOperandKind.IsoRotationalIrregularityPv, 1, -600, 600),
             new(3, true, ToleranceOperandKind.ElementDecenterX, 1, .04, .06, EndSurfaceNumber: 2),
             new(4, true, ToleranceOperandKind.Compensator, focus, -.8, .8)],
            MaxDegreeOfParallelism: 1, Mode: ToleranceAnalysisMode.SkipSensitivity,
            AdditionalCompensators:
            [new(1, ToleranceCompensatorKind.ElementDecenterX, -.2, .2, 2),
             new(1, ToleranceCompensatorKind.ElementTiltY, -.1, .1, 2, new(0, 0, 4), Stage: 2)],
            ConfigurationSettings: settings);
        Assert.True(app.Tolerancing.ValidateStudy(request).IsValid,
            string.Join(", ", app.Tolerancing.ValidateStudy(request).Messages));
        var serial = await app.Tolerancing.RunAsync(request);
        var parallel = await app.Tolerancing.RunAsync(request with { MaxDegreeOfParallelism = 2 });
        Assert.Equal(serial.TrialRows.Select(row => row.CriterionValue), parallel.TrialRows.Select(row => row.CriterionValue));
        Assert.Equal(serial.TrialRows.Select(row => row.AcceptanceMargin), parallel.TrialRows.Select(row => row.AcceptanceMargin));
        Assert.Equal("0.0%", serial.Statistics!.Yield);
        var chart = OpticalSystemDesign.App.Panels.ToleranceChartBuilder.Yield(serial, ToleranceCriterion.RmsSpotRadius, 0);
        Assert.Contains("0 / 3", chart.Summary);
        Assert.Contains(chart.Series, series => series.Name == "全部配置合格（余量 ≥ 0）"
            && series.Points.All(point => point.X == 0));
        Assert.All(serial.TrialRows, row =>
        {
            Assert.False(row.Passed);
            var configs = Assert.IsAssignableFrom<IReadOnlyList<ToleranceConfigurationResultDto>>(row.Configurations);
            Assert.True(configs[0].Passed, configs[0].Error);
            Assert.False(configs[1].Passed);
            var stages = Assert.IsAssignableFrom<IReadOnlyList<ToleranceStageValueDto>>(row.Stages);
            Assert.Equal(new[] { 1, 2, 3 }, stages.Select(stage => stage.Stage));
            Assert.Single(stages[0].Compensators);
            Assert.Equal(2, stages[2].Compensators.Count);
            Assert.True(Math.Abs(stages[2].Compensators[0].Value - stages[2].Compensators[1].Value) > .001);
            var delta = Assert.Single(stages[0].Compensators).Value;
            var stage1 = Assert.IsAssignableFrom<IReadOnlyList<ToleranceConfigurationResultDto>>(stages[0].Configurations);
            var starting = row.UncompensatedConfigurations!;
            for (var i = 0; i < 2; i++)
            {
                var after = stage1[i].Compensators![0].Value;
                Assert.Equal(delta, after - starting[i].Compensators![0].Value, 11);
                Assert.Equal(after, configs[i].Compensators![0].Value, 11);
            }
        });
        Assert.Equal(1, serial.ConfigurationYields![0].Yield);
        Assert.Equal(0, serial.ConfigurationYields[1].Yield);
        Assert.Equal(serial.WorstSample!.Recorded.Trial, parallel.WorstSample!.Recorded.Trial);
        Assert.Equal(serial.TrialRows.OrderBy(row => row.AcceptanceMargin).First().Trial, serial.WorstSample.Recorded.Trial);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.Tolerancing.RunAsync(request, canceled.Token));
        Assert.Equal(revision, app.Events.Revision);
        Assert.Equal(navigation, app.MultiConfiguration.GetNavigationState());
        Assert.Equal(before.Select(row => row.Values[1]), app.MultiConfiguration.GetThicknessDifferences().Select(row => row.Values[1]));

        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_JOINT_TOLERANCE_CAPTURE_DIR");
        var path = Path.GetTempFileName();
        try
        {
            await app.Tolerancing.SaveSampleAsync(serial.WorstSample, path);
            using var other = WorkbenchApplication.Create("tessar");
            var otherRevision = other.Events.Revision;
            var replayed = await other.Tolerancing.ReplaySampleAsync(path);
            Assert.True(replayed.MatchesRecorded);
            Assert.Equal(2, replayed.Replayed.Configurations!.Count);
            Assert.Equal(otherRevision, other.Events.Revision);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                await app.Documents.SaveAsync(Path.Combine(directory, "cooke-two-configurations.staropt"));
                await app.Tolerancing.SaveSampleAsync(serial.WorstSample, Path.Combine(directory, "cooke-worst.startolsample.json"));
                SaveEvidence("cooke-joint-trials.json", new
                {
                    request.Seed, serial.Statistics, serial.ConfigurationYields, serial.NominalConfigurations, serial.TrialRows
                });
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task JointInverseReportsDifferentLimitingConfigurationsForOppositeEndpoints()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var focus = app.Prescription.GetSurfaces().Count - 2;
        var nominal = app.Prescription.GetSurfaces()[focus].Thickness;
        var second = app.MultiConfiguration.Add();
        app.MultiConfiguration.SetThickness(second, focus, nominal + 1.2);
        app.MultiConfiguration.SetThickness(0, focus, nominal - 1.2);
        var request = new TolerancingRequestDto(focus, 0, 0, 0, 91, 0,
            [new(1, true, ToleranceOperandKind.Thickness, focus, -.8, .8)],
            Mode: ToleranceAnalysisMode.InverseIncrement, InverseValue: .0002,
            ConfigurationSettings: new(ToleranceConfigurationScope.All));
        var result = await app.Tolerancing.RunAsync(request);
        var row = Assert.Single(result.InverseRows!);
        Assert.Equal(1, row.Minimum.LimitingConfiguration);
        Assert.Equal(2, row.Maximum.LimitingConfiguration);
        Assert.True(row.Minimum.AcceptanceMargin >= 0);
        Assert.True(row.Maximum.AcceptanceMargin >= 0);
        var tightened = Assert.Single(result.AdjustedOperands!);
        Assert.InRange(tightened.Minimum, -.7999, 0);
        Assert.InRange(tightened.Maximum, 0, .7999);
        SaveEvidence("cooke-joint-inverse.json", new { result.InverseRows, result.AdjustedOperands, result.SensitivityRows });
    }

    private static void SaveEvidence<T>(string name, T data)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_JOINT_TOLERANCE_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), System.Text.Json.JsonSerializer.Serialize(data,
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
            }));
    }

    [Fact]
    public async Task ConfigurationThicknessRowsFollowExplicitPickupAndRejectOverlappingWrites()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var focus = app.Prescription.GetSurfaces().Count - 2;
        var initial = app.Prescription.GetSurfaces()[focus].Thickness;
        var second = app.MultiConfiguration.Add();
        app.MultiConfiguration.ReplaceOperandRows([new(MultiConfigurationParameterKind.Thickness, focus)], app.Events.Revision);
        app.MultiConfiguration.SetOperandPickup(1, second, new(1, 0, 2, -initial), app.Events.Revision);
        var row = new ToleranceMceAdjustmentDto(1, 1, focus, .2, .2);
        var request = new TolerancingRequestDto(focus, 0, 0, 1, 31, 0,
            Mode: ToleranceAnalysisMode.SkipSensitivity, MaxDegreeOfParallelism: 1,
            ConfigurationSettings: new(ToleranceConfigurationScope.All, MceRows: [row]));
        var forward = await app.Tolerancing.RunAsync(request);
        var pair = Assert.Single(forward.TrialRows).Configurations!;
        Assert.Equal(2, pair.Count);
        for (var i = 0; i < 2; i++)
        {
            using var equivalent = WorkbenchApplication.Create("cooke");
            var expected = await equivalent.Tolerancing.RunAsync(new(focus, 0, 0, 1, 31, 0,
                [new(1, true, ToleranceOperandKind.Thickness, focus, .2 * (i + 1), .2 * (i + 1))],
                Mode: ToleranceAnalysisMode.SkipSensitivity));
            Assert.Equal(expected.TrialRows[0].CriterionValue!.Value, pair[i].Criterion, 11);
        }

        var cmco = row with { Compensator = true, Minimum = -.4, Maximum = .4, Stage = 2 };
        var compensated = await app.Tolerancing.RunAsync(request with
        {
            CompensationIterations = 4,
            ConfigurationSettings = request.ConfigurationSettings! with { MceRows = [row, cmco] }
        });
        var adjustment = Assert.Single(Assert.Single(compensated.TrialRows).Compensators!);
        Assert.Contains("CMCO", adjustment.Name);
        Assert.InRange(adjustment.Value, initial - .4, initial + .4);
        var adjustedPair = Assert.Single(compensated.TrialRows).Configurations!;
        for (var i = 0; i < adjustedPair.Count; i++)
        {
            using var equivalent = WorkbenchApplication.Create("cooke");
            var delta = (adjustment.Value - initial) * (i + 1);
            var expected = await equivalent.Tolerancing.RunAsync(new(focus, 0, 0, 1, 31, 0,
                [new(1, true, ToleranceOperandKind.Thickness, focus, delta, delta)],
                Mode: ToleranceAnalysisMode.SkipSensitivity));
            Assert.Equal(expected.TrialRows[0].CriterionValue!.Value, adjustedPair[i].Criterion, 11);
        }

        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_JOINT_TOLERANCE_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            await app.Documents.SaveAsync(Path.Combine(directory, "cooke-thickness-pickup.staropt"));
            SaveEvidence("cooke-mce-pickup.json", new
            {
                PickupScale = 2, Uncompensated = forward.TrialRows, Compensated = compensated.TrialRows
            });
        }

        Assert.False(app.Tolerancing.ValidateStudy(request with
        {
            ConfigurationSettings = request.ConfigurationSettings! with { MceRows = [row with { Configuration = 2 }] }
        }).IsValid);
        Assert.False(app.Tolerancing.ValidateStudy(request with
        {
            ConfigurationSettings = request.ConfigurationSettings! with { MceRows = [row with { SurfaceNumber = focus - 1 }] }
        }).IsValid);
        Assert.False(app.Tolerancing.ValidateStudy(request with
        {
            CompensationIterations = 4,
            AdditionalCompensators = [new(focus, ToleranceCompensatorKind.Thickness, -.1, .1)],
            ConfigurationSettings = request.ConfigurationSettings! with { MceRows = [row, cmco] }
        }).IsValid);
    }
}
