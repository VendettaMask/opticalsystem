using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class ContrastOperandTests
{
    private static MeritOperandDefinition Row(string code = "MECA", int field = 2, int wave = 1,
        double frequency = 30, double px = .35, double py = .25) => new()
        {
            Type = code,
            Field = field,
            Wavelength = wave,
            SpatialFrequency = frequency,
            Px = px,
            Py = py,
            ZemaxIntegerParameters = [0, wave],
            ZemaxDataParameters = [field, frequency, px, py]
        };
    private static double Value(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.True(string.IsNullOrEmpty(result.Error), result.Error);
        Assert.True(double.IsFinite(result.Value)); return result.Value;
    }
    private static (double Hx, double Hy) Field(Optic optic, int number) =>
        FieldCoordinates.Normalize(optic.Fields, optic.Fields[number - 1].X, optic.Fields[number - 1].Y);

    [Theory]
    [InlineData("MECS", false)]
    [InlineData("MECS", true)]
    [InlineData("MECT", false)]
    [InlineData("MECT", true)]
    public void DirectionalOperandsMeasureCommonReferenceOpdAtOriginalAndShiftedPoints(string code, bool aiming)
    {
        var optic = Optic.CreateCookeTriplet(); optic.RayAimingEnabled = aiming;
        optic.SurfaceGroup.Items[^2].Thickness += .8; optic.SurfaceGroup.Renumber();
        var row = Row(code); var field = Field(optic, 2); var wave = optic.Wavelengths[0];
        var separation = 2 * 30 * wave.Micrometers * .001 * DiffractionEngine.WorkingFNumber(optic, field, wave, aiming);
        (double X, double Y) shifted = code == "MECS" ? (.35 - separation, .25) : (.35, .25 - separation);
        var samples = WavefrontEngine.GenerateChiefRaySamples(optic, field, wave, [shifted, (.35, .25)], aimAtStop: aiming).Samples;
        Assert.All(samples, s => Assert.True(s.Intensity > 0));
        var expected = samples[1].OpdWaves - samples[0].OpdWaves;
        Assert.Equal(expected, Value(optic, row), 9);
        double RawPath(double x, double y)
        {
            var rays = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(field.Hx, field.Hy, x, y, wave.Micrometers, aimAtStop: aiming);
            var image = optic.SurfaceGroup.Items.Count - 1;
            using var trace = optic.SequentialRayTracer.Trace(rays, TraceRequest.Selected([image]));
            Assert.True(trace.TryGetSample(0, image, out var sample)); return sample.CumulativeOpticalPathLength;
        }
        var oldPathDifference = (RawPath(.35, .25) - RawPath(shifted.X, shifted.Y)) / (wave.Micrometers * .001);
        Assert.True(Math.Abs(oldPathDifference - expected) > 1e-3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AverageUsesBothSignedDirectionalValuesAndTheirOrdinaryMeritContribution(bool afocal)
    {
        var optic = Optic.CreateCookeTriplet(); optic.ImageSpaceAfocal = afocal;
        var frequency = afocal ? .1 : 30;
        var s = Value(optic, Row("MECS", frequency: frequency)); var t = Value(optic, Row("MECT", frequency: frequency));
        var row = Row(frequency: frequency); row.Target = .3; row.Weight = -2;
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.Empty(result.Error); Assert.Equal((s + t) / 2, result.Value, 11);
        Assert.Equal(2 * Math.Pow(result.Value - .3, 2), result.Contribution, 9);
    }

    [Theory]
    [InlineData("MECA")]
    [InlineData("MECS")]
    [InlineData("MECT")]
    public void SixRawSlotsOverrideStaleAliasesAndUnusedSurfaceDoesNotSelectEvaluationSurface(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(code);
        var expected = Value(optic, row);
        row.Wavelength = 99; row.Field = 99; row.SpatialFrequency = -4; row.Px = 8; row.Py = 9;
        row.Surface = 99; row.ZemaxIntegerParameters[0] = 99;
        Assert.Equal(expected, Value(optic, row), 12);
    }

    [Fact]
    public void PrimaryWavelengthSelectionRespondsToOrderedPrimaryState()
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(wave: 0);
        var initial = Value(optic, row);
        var changed = Optic.FromSnapshot(optic.ToSnapshot());
        for (var i = 0; i < changed.Wavelengths.Count; i++) changed.Wavelengths[i].IsPrimary = i == 2;
        var selected = Value(changed, row);
        var result = MeritFunctionCatalog.EvaluateAll(optic,
            [row, new() { Type = "PRIM", ZemaxIntegerParameters = [3, 0] }, row]);
        Assert.All(result, r => Assert.Empty(r.Error));
        Assert.Equal(initial, result[0].Value, 12); Assert.Equal(selected, result[2].Value, 12);
        Assert.Equal(initial, Value(optic, row), 12); Assert.NotEqual(initial, selected);
    }

    [Theory]
    [InlineData("wave-zero-table")]
    [InlineData("wave-negative")]
    [InlineData("wave-range")]
    [InlineData("field-zero")]
    [InlineData("field-range")]
    [InlineData("field-fraction")]
    [InlineData("frequency-negative")]
    [InlineData("frequency-nan")]
    [InlineData("frequency-infinity")]
    [InlineData("frequency-cutoff")]
    [InlineData("px-nan")]
    [InlineData("py-infinity")]
    [InlineData("pupil-outside")]
    [InlineData("shifted-outside")]
    public void InvalidRequestsFailExplicitlyWithoutClampingOrSuccessfulZero(string fault)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row();
        switch (fault)
        {
            case "wave-zero-table": optic.Wavelengths.Clear(); break;
            case "wave-negative": row.ZemaxIntegerParameters[1] = -1; break;
            case "wave-range": row.ZemaxIntegerParameters[1] = 99; break;
            case "field-zero": row.ZemaxDataParameters[0] = 0; break;
            case "field-range": row.ZemaxDataParameters[0] = 99; break;
            case "field-fraction": row.ZemaxDataParameters[0] = 1.5; break;
            case "frequency-negative": row.ZemaxDataParameters[1] = -1; break;
            case "frequency-nan": row.ZemaxDataParameters[1] = double.NaN; break;
            case "frequency-infinity": row.ZemaxDataParameters[1] = double.PositiveInfinity; break;
            case "frequency-cutoff": row.ZemaxDataParameters[1] = 1e8; break;
            case "px-nan": row.ZemaxDataParameters[2] = double.NaN; break;
            case "py-infinity": row.ZemaxDataParameters[3] = double.PositiveInfinity; break;
            case "pupil-outside": row.ZemaxDataParameters[2] = 1.1; break;
            case "shifted-outside": row.ZemaxDataParameters[2] = -.99; row.ZemaxDataParameters[3] = 0; break;
        }
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.NotEmpty(result.Error); Assert.True(double.IsNaN(result.Value)); Assert.True(double.IsPositiveInfinity(result.Contribution));
        Assert.Throws<OptimizationEvaluationException>(() => MeritFunctionCatalog.EvaluateOptimizationValues(optic, [row]));
    }

    [Theory]
    [InlineData("MECA")]
    [InlineData("MECS")]
    [InlineData("MECT")]
    public void ZeroFrequencyRequiresValidRaysAndBlockedPupilsCannotSucceed(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(code, field: 1, frequency: 0);
        Assert.Equal(0, Value(optic, row));
        optic.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(.001);
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.NotEmpty(result.Error); Assert.True(double.IsPositiveInfinity(result.Contribution));
    }

    [Fact]
    public void AverageCannotHideAnInvalidDirectionalPair()
    {
        var optic = Optic.CreateCookeTriplet(); var s = Row("MECS", field: 1, px: 0, py: -.95);
        Assert.True(double.IsFinite(Value(optic, s)));
        s.Type = "MECT"; Assert.NotEmpty(MeritFunctionCatalog.Evaluate(optic, s).Error);
        s.Type = "MECA"; Assert.NotEmpty(MeritFunctionCatalog.Evaluate(optic, s).Error);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(.25, .5)]
    [InlineData(.5, 1)]
    [InlineData(1, 0)]
    public void ContrastLossUsesPhaseInWaves(double difference, double expected) =>
        Assert.Equal(expected, new ContrastPairResult(difference, 0, difference, null).Loss, 12);

    [Fact]
    public void CutoffSeparatesEndpointsByFullPupilDiameterWithoutClamping()
    {
        Assert.Equal(0, ContrastMetrics.PupilSeparation(0, 100));
        Assert.Equal(2, ContrastMetrics.PupilSeparation(100, 100));
        Assert.True(ContrastMetrics.TryPair(0, 0, 2, true, true, out var pair));
        Assert.Equal((-1.0, 0.0), pair.First); Assert.Equal((1.0, 0.0), pair.Second);
        Assert.Throws<ArgumentOutOfRangeException>(() => ContrastMetrics.PupilSeparation(100.001, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => ContrastMetrics.PupilSeparation(10, 0));
        Assert.False(ContrastMetrics.TryPair(0, 0, 2.001, true, true, out _));
        Assert.False(ContrastMetrics.TryPair(0, double.NaN, 1, true, true, out _));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void MapAndOperandsShareRayPairsReferenceAndAiming(bool aiming, bool afocal)
    {
        var optic = Optic.CreateCookeTriplet(); optic.RayAimingEnabled = aiming; optic.ImageSpaceAfocal = afocal;
        var frequency = afocal ? .1 : 30;
        var map = new ContrastLossMapAnalysis(optic, sampling: 9, frequency: frequency, wavelengthNumber: 1, fieldNumber: 2, showOpd: true).GenerateData();
        var shift = (double)map.Values["PupilSeparation"];
        var wave = optic.Wavelengths[0]; var field = Field(optic, 2);
        var cutoff = afocal ? ImageSpaceAnalysisSupport.AfocalDiffractionPupilDiameterMillimeters(optic) / wave.Micrometers
            : 1 / (wave.Micrometers * .001 * DiffractionEngine.WorkingFNumber(optic, field, wave, aiming));
        Assert.Equal(cutoff, (double)map.Values["CutoffFrequency"], 10);
        Assert.Equal(afocal ? "cycles/mrad" : "cycles/mm", map.Values["FrequencyUnit"]);
        for (var direction = 0; direction < 2; direction++)
        {
            var point = Assert.Single(map.PlotSeries[direction].Points, p => p.X == .25 && p.Y == .25);
            var row = Row(direction == 0 ? "MECS" : "MECT", frequency: frequency,
                px: .25 + (direction == 0 ? shift / 2 : 0), py: .25 + (direction == 1 ? shift / 2 : 0));
            var difference = Value(optic, row);
            Assert.Equal(.5 * (1 - Math.Cos(2 * Math.PI * difference)), point.Value!.Value, 9);
            Assert.True(ContrastMetrics.TryPair(.25, .25, shift, direction == 0, true, out var pair));
            var result = ContrastMetrics.EvaluatePair(optic, field, wave, pair, (.25, .25));
            var phase = Assert.Single(map.PlotPanes![direction + 2].Series[0].Points, p => p.X == .25 && p.Y == .25).Value!.Value;
            Assert.Equal(Modulo((result.FirstWaves + result.SecondWaves) / 2), phase, 10);
            var original = (AnalysisSeries[])map.Values["UnshiftedPupilPhaseSeries"];
            Assert.Equal(Modulo(result.CenterWaves!.Value), original[direction].Points.Single(p => p.X == .25 && p.Y == .25).Value!.Value, 10);
        }
        static double Modulo(double value) => (value % 1 + 1) % 1;
    }

    [Fact]
    public void WorkingCutoffUsesTracedImageConeAndDiffersFromNominalFNumber()
    {
        var optic = Optic.CreateCookeTriplet(); var wave = optic.Wavelengths[0];
        optic.SurfaceGroup.Items[^2].Thickness += 5; optic.SurfaceGroup.Renumber();
        var cutoff = ContrastMetrics.CutoffFrequency(optic, Field(optic, 2), wave);
        var nominal = 1 / (wave.Micrometers * .001 * Math.Abs(optic.Paraxial.EstimateFNumber()));
        Assert.True(Math.Abs(cutoff - nominal) > 1);
        Assert.Equal(2 * 30 / cutoff, ContrastMetrics.PupilSeparation(optic, Field(optic, 2), wave, 30), 12);
    }

    [Theory]
    [InlineData("frequency-negative")]
    [InlineData("frequency-nan")]
    [InlineData("frequency-cutoff")]
    [InlineData("wave")]
    [InlineData("field")]
    public void InvalidMapSettingsAreUnavailableInsteadOfShowingClampedData(string fault)
    {
        var data = new ContrastLossMapAnalysis(Optic.CreateCookeTriplet(), sampling: 9,
            frequency: fault switch { "frequency-negative" => -1, "frequency-nan" => double.NaN, "frequency-cutoff" => 1e8, _ => 30 },
            wavelengthNumber: fault == "wave" ? 99 : 1, fieldNumber: fault == "field" ? 0 : 1).GenerateData();
        Assert.Empty(data.PlotSeries); Assert.Equal(AnalysisOutcome.Unavailable, data.Outcome); Assert.NotEmpty(data.OutcomeReason!);
    }

    [Theory]
    [InlineData(MeritPupilSampling.GaussianQuadrature)]
    [InlineData(MeritPupilSampling.RectangularArray)]
    public void WizardStoresOriginalEndpointsAndAllGeneratedPairsEvaluate(MeritPupilSampling sampling)
    {
        var optic = Optic.CreateCookeTriplet();
        var rows = MeritFunctionCatalog.CreateFromWizard(optic, new(MeritImageQuality.Contrast, sampling,
            PupilRings: 2, PupilArms: 6, PupilObscuration: 0, WeightScale: 1, UseAllWavelengths: false, IncludeCommonOperands: false, SpatialFrequency: 30))
            .Where(r => r.Enabled).ToArray();
        Assert.Contains(rows, r => r.Type == "MECS"); Assert.Contains(rows, r => r.Type == "MECT");
        Assert.All(rows, row =>
        {
            var wave = optic.Wavelengths[row.Wavelength - 1]; var field = Field(optic, row.Field);
            var shift = ContrastMetrics.PupilSeparation(optic, field, wave, 30);
            Assert.True(ContrastMetrics.TryPair(row.Px, row.Py, shift, row.Type == "MECS", false, out var pair));
            Assert.Equal(ContrastMetrics.EvaluatePair(optic, field, wave, pair).DifferenceWaves, Value(optic, row), 11);
        });
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e8)]
    public void WizardRejectsInvalidOrUnsupportedFrequency(double frequency) => Assert.ThrowsAny<ArgumentException>(() =>
        MeritFunctionCatalog.CreateFromWizard(Optic.CreateCookeTriplet(), new(MeritImageQuality.Contrast,
            MeritPupilSampling.GaussianQuadrature, 2, 6, 0, 1, false, false, SpatialFrequency: frequency)));

    [Fact]
    public void ImageFieldChangesAndRayCacheRevisionsCannotReuseStaleContrast()
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row();
        var cache = new RayTraceCache(4096, 100_000); optic.ConfigureRayTraceCache(cache, 1);
        var original = Value(optic, row); Value(optic, row); Assert.True(cache.Statistics.Hits > 0);
        var rows = MeritFunctionCatalog.EvaluateAll(optic, [row, new() { Type = "IMSF", ZemaxIntegerParameters = [6, 0] }, row,
            new() { Type = "IMSF", ZemaxIntegerParameters = [0, 0] },
            new() { Type = "FDMO", ZemaxIntegerParameters = [2, 0], ZemaxDataParameters = [.1, .2, 0, 0, 0, 0] }, row,
            new() { Type = "FDRE", ZemaxIntegerParameters = [2, 0] }, row]);
        Assert.All(rows, r => Assert.Empty(r.Error));
        Assert.Equal(Value(IntermediateImageSystem.Create(optic, 6, false), row), rows[2].Value, 10);
        Assert.NotEqual(original, rows[2].Value); Assert.NotEqual(original, rows[5].Value);
        Assert.Equal(original, rows[7].Value, 12); Assert.Equal(original, Value(optic, row), 12);
        optic.InvalidateRayTraceCache(); optic.SurfaceGroup.Items[^2].Thickness += .2; optic.SurfaceGroup.Renumber();
        optic.ConfigureRayTraceCache(cache, 2); Assert.NotEqual(original, Value(optic, row));
    }

    [Fact]
    public async Task ConcurrentEvaluationsAndCancellationKeepResultsIsolated()
    {
        var optics = new[] { Optic.CreateCookeTriplet(), Optic.CreateCookeTriplet() };
        optics[1].SurfaceGroup.Items[1].Radius *= 1.01;
        var expected = optics.Select(o => Value(o, Row())).ToArray();
        var actual = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Task.Run(() => Value(optics[i % 2], Row()))));
        for (var i = 0; i < actual.Length; i++) Assert.Equal(expected[i % 2], actual[i], 12);
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel(); using var scope = ComputationCancellation.Push(cts.Token);
            Assert.ThrowsAny<OperationCanceledException>(() => Value(optics[0], Row()));
            Assert.ThrowsAny<OperationCanceledException>(() => new ContrastLossMapAnalysis(optics[0]).GenerateData());
        }
        Assert.Equal(expected[0], Value(optics[0], Row()), 12);
    }

    [Theory]
    [InlineData("MECA")]
    [InlineData("MECS")]
    [InlineData("MECT")]
    public async Task LocalSixSlotsSurviveProjectAndEditorRoundtrip(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(code); row.Target = .125; row.Weight = 2;
        optic.MeritFunctionOperands.Add(row);
        var path = Path.Combine(Path.GetTempPath(), $"contrast-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var copy = (await StarOptProjectStore.LoadAsync(path)).Configurations[0]; var saved = Assert.Single(copy.MeritFunctionOperands);
            Assert.False(saved.CompatibilityOnly); Assert.Equal(row.ZemaxDataParameters, saved.ZemaxDataParameters);
            Assert.Equal(Value(optic, row), Value(copy, saved), 12);
        }
        finally { File.Delete(path); }
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, true, code, 0, 2, 1, 0, 0, .35, .25, .125, 2, 0, 0, "contrast",
            ZemaxInt1: 0, ZemaxInt2: 1, ZemaxData1: 2, ZemaxData2: 30, ZemaxData3: .35, ZemaxData4: .25)]);
        var type = app.Optimization.GetMeritOperandTypes().Single(t => t.Code == code);
        Assert.Equal(6, type.Parameters!.Count); Assert.False(type.Parameters[0].IsEditable);
        var editor = new MeritOperandEditorRow(Assert.Single(app.Optimization.GetMeritFunction()), type);
        editor.Parameter4 = 40; editor.Parameter5 = .4; app.Optimization.SetMeritFunction([editor.ToDto()]);
        var dto = Assert.Single(app.Optimization.GetMeritFunction());
        Assert.Equal(40, dto.SpatialFrequency); Assert.Equal(.4, dto.Px); Assert.Equal(.125, dto.Target); Assert.Equal(2, dto.Weight);
        Assert.Contains("共同主光线参考", type.Calculation);
    }

    [Theory]
    [InlineData("MECA", "wave")]
    [InlineData("MECA", "field")]
    [InlineData("MECS", "wave")]
    [InlineData("MECS", "field")]
    [InlineData("MECT", "wave")]
    [InlineData("MECT", "field")]
    public void SnapshotValidationUsesRawFieldAndWaveReferences(string code, string fault)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(code);
        if (fault == "wave") row.ZemaxIntegerParameters[1] = 99; else row.ZemaxDataParameters[0] = 99;
        optic.MeritFunctionOperands.Add(row);
        Assert.ThrowsAny<Exception>(() => Optic.FromSnapshot(optic.ToSnapshot()));
    }

    [Fact]
    public void UnverifiedNativeAverageStaysReadOnlyWhileKnownDirectionalSlotsRemainUsable()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX"));
        var optic = OpticalFormatCatalog.Import(text + "\nMECA 0 1 2 30 .35 .25 .125 2 0\nMECS 0 1 2 30 .35 .25 .125 2 0\nMECT 0 1 2 30 .35 .25 .125 2 0\n", ".zmx");
        var rows = optic.MeritFunctionOperands.TakeLast(3).ToArray();
        Assert.True(rows[0].CompatibilityOnly); Assert.False(rows[0].Enabled);
        Assert.All(rows.Skip(1), r => { Assert.False(r.CompatibilityOnly); Assert.Equal(30, r.SpatialFrequency); Assert.Equal(.35, r.Px); Value(optic, r); });
        var copy = Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands.TakeLast(3).ToArray();
        Assert.True(copy[0].CompatibilityOnly); Assert.False(copy[0].Enabled);
        Assert.Equal(rows[0].Comment, copy[0].Comment); Assert.Equal(rows[0].ZemaxDataParameters, copy[0].ZemaxDataParameters);
    }

    [Fact]
    public void FormalDlsCanRecoverImageDistanceFromDirectionalContrastTargets()
    {
        var optic = Optic.CreateCookeTriplet();
        foreach (var surface in optic.SurfaceGroup.Items) { surface.RadiusVariable = false; surface.ThicknessVariable = false; }
        var original = optic.SurfaceGroup.Items[^2].Thickness;
        optic.MeritFunctionOperands.Clear();
        foreach (var code in new[] { "MECS", "MECT" }) foreach (var px in new[] { .2, .5 })
        {
            var row = Row(code, field: 1, px: px); row.Target = Value(optic, row); optic.MeritFunctionOperands.Add(row);
        }
        optic.SurfaceGroup.Items[^2].Thickness += 1; optic.SurfaceGroup.Items[^2].ThicknessVariable = true; optic.SurfaceGroup.Renumber();
        var runtime = new WorkbenchRuntime(optic); var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 35);
        Assert.True(result.FinalMerit < result.InitialMerit * 1e-6);
        Assert.Equal(original, runtime.CurrentOptic.SurfaceGroup.Items[^2].Thickness, 3);
    }
}
