using System.Text.Json;
using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class IlluminationOperandTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void CapturedFileReportsOperandDifferencesWithoutInventingAnAcceptanceGate()
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(fixtures, "zemax-123456.ZMX")), ".zmx");
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, "zemax-123456-relative-illumination.json")));
        var series = native.RootElement.GetProperty("dataSeries")[0];
        var indices = new[] { 0, 10, 20 };
        optic.Fields.Clear();
        foreach (var index in indices) optic.Fields.Add(new FieldPoint { Y = series.GetProperty("x")[index].GetDouble() });
        var wave = optic.Wavelengths.Select((value, index) => (value, index))
            .Single(pair => Math.Abs(pair.value.Micrometers - .44) < 1e-12).index + 1;
        var definitions = new List<MeritOperandDefinition> { new() { Type = "CVIG" } };
        for (var field = 1; field <= 3; field++)
        {
            definitions.Add(Row("RELI", field, wave: wave)); definitions.Add(Row("EFNO", field, wave: wave));
        }
        var rows = MeritFunctionCatalog.EvaluateAll(optic, definitions);
        Assert.All(rows, row => Assert.Empty(row.Error));
        var records = indices.Select((index, i) => new
        {
            Field = optic.Fields[i].Y,
            RelativeIllumination = rows[1 + 2 * i].Value,
            NativeRelativeIllumination = series.GetProperty("y")[index][0].GetDouble(),
            EffectiveFNumber = rows[2 + 2 * i].Value,
            NativeEffectiveFNumber = series.GetProperty("y")[index][1].GetDouble()
        }).ToArray();
        output.WriteLine("IlluminationOperandComparison=" + JsonSerializer.Serialize(records));
        Assert.All(records, record => Assert.True(record.EffectiveFNumber > 0 && double.IsFinite(record.EffectiveFNumber)));
        // This is an analysis capture, not a native MFE operand-row capture.
        Assert.Equal(1, records[0].RelativeIllumination, 12);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(.25)]
    public void EffectiveFNumberUsesWeightedSolidAngleAndMeritContribution(double transmission)
    {
        var optic = Cone();
        optic.SurfaceGroup.Items[1].CoatingModel = new SimpleCoatingModel(transmission);
        // Samp=10 has 80 cell centers in the unit disk; the air cone has sin²(theta)=1/101.
        var expected = .5 * Math.Sqrt(Math.PI / (80 * .04 / 101 * transmission));
        var row = Row("EFNO"); row.Target = 4; row.Weight = 2;
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.Empty(result.Error); Assert.Equal(expected, result.Value, 10);
        Assert.Equal(2 * Math.Pow(expected - 4, 2), result.Contribution, 10);
        Assert.Equal(1, Value(optic, Row("RELI")), 12);
    }

    [Fact]
    public void RelativeIlluminationUsesActualAxisEvenWithoutAxisFieldAndCanExceedOne()
    {
        var optic = Cone(); optic.Fields[0].X = 5; optic.Fields.Add(new FieldPoint { X = -5 });
        optic.SurfaceGroup.Items[^1].CoordinateSystem = optic.SurfaceGroup.Items[^1].CoordinateSystem with { RotationYDegrees = 30 };
        var a = Value(optic, Row("RELI", field: 1, samp: 40));
        var b = Value(optic, Row("RELI", field: 2, samp: 40));
        Assert.True(Math.Max(a, b) > 1.01);
        var efA = Value(optic, Row("EFNO", field: 1, samp: 40));
        var efB = Value(optic, Row("EFNO", field: 2, samp: 40));
        Assert.Equal(a / b, Math.Pow(efB / efA, 2), 10);
    }

    [Fact]
    public void VignettingAndCvIgNeverChangeSourceOrRemovePhysicalApertures()
    {
        var optic = Cone(); optic.Fields[0].Y = 5;
        var full = Value(optic, Row("EFNO"));
        optic.SurfaceGroup.Items[1].PhysicalAperture = new AnnularAperture(.9, .2);
        var ef = Value(optic, Row("EFNO")); Assert.True(ef > full);
        var ri = Value(optic, Row("RELI"));
        var field = optic.Fields[0];
        field.VignetteFactorX = .2; field.VignetteFactorY = .3;
        field.VignetteDecenterX = .1; field.VignetteDecenterY = -.1; field.VignetteAngleDegrees = 23;
        var before = Snapshot(optic);
        Assert.Equal(ri, Value(optic, Row("RELI")), 12);
        Invalid(optic, Row("EFNO"));
        var result = MeritFunctionCatalog.EvaluateAll(optic, [new() { Type = "CVIG" }, Row("EFNO"), Row("RELI")]);
        Assert.All(result, item => Assert.Empty(item.Error));
        Assert.Equal(ef, result[1].Value, 12); Assert.Equal(ri, result[2].Value, 12);
        Assert.Equal(before, Snapshot(optic));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PolarizedModeUsesFormalPowerChainIncludingPhysicalFilms(bool films)
    {
        var optic = Cone(); var glass = new ConstantIndexMaterial("transparent-test-glass", 1.5);
        optic.SurfaceGroup.Items[1].MaterialAfter = glass; optic.SurfaceGroup.Items[2].MaterialBefore = glass;
        if (films)
            optic.SurfaceGroup.Items[1].CoatingModel = new CoherentMultilayerCoating([new(new ConstantIndexMaterial("film", 1.3), 110)]);
        var expected = IlluminationMetrics.Evaluate(optic, (0, 0), .55, 10, usePolarization: true,
            sampling: IlluminationSamplingKind.UniformImageCosine).EffectiveFNumber;
        Assert.Equal(expected, Value(optic, Row("EFNO", pol: 1)), 12);
        Assert.Equal(1, Value(optic, Row("RELI", pol: 1)), 12);
        if (!films) Assert.True(expected > Value(optic, Row("EFNO")) * 1.01);
    }

    [Theory]
    [InlineData("EFNO")]
    [InlineData("RELI")]
    public void DarkOrUnsupportedComplexTransportIsAnErrorAndCancellationPropagates(string code)
    {
        var optic = Cone(); optic.SurfaceGroup.Items[1].CoatingModel = new SimpleCoatingModel(0);
        Invalid(optic, Row(code));
        optic = Cone(); var glass = new ConstantIndexMaterial("absorbing", 1.5, .01);
        optic.SurfaceGroup.Items[1].MaterialAfter = glass; optic.SurfaceGroup.Items[2].MaterialBefore = glass;
        Invalid(optic, Row(code, pol: 1));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        using var scope = ComputationCancellation.Push(cancel.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(Cone(), Row(code)));
    }

    [Theory]
    [InlineData(4, 1, 1, 0)]
    [InlineData(129, 1, 1, 0)]
    [InlineData(10, 0, 1, 0)]
    [InlineData(10, -1, 1, 0)]
    [InlineData(10, 2, 1, 0)]
    [InlineData(10, 1, 0, 0)]
    [InlineData(10, 1, 2, 0)]
    [InlineData(10, 1, .5, 0)]
    [InlineData(10, 1, double.NaN, 0)]
    [InlineData(10, 1, 1, -1)]
    [InlineData(10, 1, 1, 2)]
    [InlineData(10, 1, 1, .5)]
    [InlineData(10, 1, 1, double.NaN)]
    public void InvalidParametersAreNotClampedOrSilentlyReplaced(int samp, int wave, double field, double pol)
    {
        foreach (var code in new[] { "RELI", "EFNO" }) Invalid(Cone(), Row(code, field, samp, wave, pol));
    }

    [Theory]
    [InlineData("EFNO")]
    [InlineData("RELI")]
    public void RawSlotsWinOverAliasesAndAfocalIsRejected(string code)
    {
        var optic = Cone(); var row = Row(code); var expected = Value(optic, row);
        row.Surface = 999; row.Wavelength = 999; row.Field = 999; row.Hx = 100; row.Hy = -100;
        row.ZemaxDataParameters[2] = 13.5; row.ZemaxDataParameters[3] = -7.25;
        Assert.Equal(expected, Value(optic, row), 12);
        optic.ImageSpaceAfocal = true; Invalid(optic, row);
    }

    [Theory]
    [InlineData("RELI")]
    [InlineData("EFNO")]
    public void OrderedFieldAndImageChangesAreIsolatedAndRestored(string code)
    {
        var optic = Cone(); optic.Fields[0].Y = 5; var before = Snapshot(optic);
        var row = Row(code); var expected = Value(optic, row);
        var rows = MeritFunctionCatalog.EvaluateAll(optic, [row,
            new() { Type = "FDMO", ZemaxIntegerParameters = [1, 0], ZemaxDataParameters = [0, 0, 0, 0, 0, 0] }, row,
            new() { Type = "FDRE", ZemaxIntegerParameters = [1, 0] },
            new() { Type = "IMSF", ZemaxIntegerParameters = [1, 0] }, row,
            new() { Type = "IMSF", ZemaxIntegerParameters = [0, 0] }, row]);
        Assert.All(rows, result => Assert.Empty(result.Error));
        Assert.Equal(expected, rows[0].Value, 12); Assert.Equal(expected, rows[^1].Value, 12);
        Assert.True(Math.Abs(rows[2].Value - expected) > 1e-4);
        Assert.True(double.IsFinite(rows[5].Value)); Assert.Equal(before, Snapshot(optic));
    }

    [Theory]
    [InlineData("EFNO")]
    [InlineData("RELI")]
    public async Task LocalEditorAndStarOptRoundTripKeepValuesRawSlotsAndSupport(string code)
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, true, code, 0, 1, 1, 0, 0, 0, 0, .25, 2, 0, 0, "illumination",
            ZemaxInt1: 10, ZemaxInt2: 1, ZemaxData1: 1, ZemaxData2: 0, ZemaxData3: 13.5, ZemaxData4: -7.25)]);
        var dto = Assert.Single(app.Optimization.GetMeritFunction()); Assert.Empty(dto.Error);
        var type = Assert.Single(app.Optimization.GetMeritOperandTypes(), item => item.Code == code);
        Assert.False(type.CompatibilityOnly); Assert.Equal("照度与有效 F 数", type.Category);
        Assert.Contains("尚未验证原生等价", type.Calculation);
        var editor = new MeritOperandEditorRow(dto, type);
        Assert.True(editor.IsParameterEditable(0)); Assert.False(editor.IsParameterEditable(4));
        editor.Parameter1 = 20; editor.Parameter3 = 2;
        app.Optimization.SetMeritFunction([editor.ToDto()]);
        var edited = Assert.Single(app.Optimization.GetMeritFunction()); Assert.Empty(edited.Error);
        Assert.Equal(20, edited.ZemaxInt1); Assert.Equal(2, edited.ZemaxData1);
        Assert.Equal(13.5, edited.ZemaxData3); Assert.Equal(-7.25, edited.ZemaxData4);
        var optic = Cone(); var row = Row(code); optic.MeritFunctionOperands.Add(row);
        var path = Path.Combine(Path.GetTempPath(), $"illumination-operand-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var restored = (await StarOptProjectStore.LoadAsync(path)).Configurations[0];
            var saved = Assert.Single(restored.MeritFunctionOperands);
            Assert.True(saved.Enabled); Assert.False(saved.CompatibilityOnly);
            Assert.Equal(row.ZemaxIntegerParameters, saved.ZemaxIntegerParameters);
            Assert.Equal(row.ZemaxDataParameters, saved.ZemaxDataParameters);
            Assert.Equal(Value(optic, row), Value(restored, saved), 12);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("EFNO")]
    [InlineData("RELI")]
    public async Task NativeRowsRetainRawRecordAndNeverAutoUpgrade(string code)
    {
        var optic = OpticalFormatCatalog.Import($"""
            MODE SEQ
            ENPD 2
            WAVM 1 0.55 1
            SURF 0
              DISZ 10
            SURF 1
              STOP
              DISZ 10
            SURF 2
              DISZ 0
            {code} 10 1 1 0 13.5 -7.25 0.25 2 0 0
            """, ".zmx");
        var row = Assert.Single(optic.MeritFunctionOperands);
        Assert.True(row.CompatibilityOnly); Assert.False(row.Enabled);
        var path = Path.Combine(Path.GetTempPath(), $"native-illumination-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var restored = (await StarOptProjectStore.LoadAsync(path)).Configurations[0];
            var saved = Assert.Single(restored.MeritFunctionOperands);
            Assert.True(saved.CompatibilityOnly); Assert.False(saved.Enabled);
            Assert.Equal(row.Comment, saved.Comment); Assert.Equal(row.ZemaxDataParameters, saved.ZemaxDataParameters);
            Assert.Equal(row.ZemaxIntegerParameters, saved.ZemaxIntegerParameters);
            saved.Enabled = true; Invalid(restored, saved);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void EffectiveFNumberDrivesProductionDlsThickness()
    {
        var optic = Cone();
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = 5 },
            new OpticalSurface { Thickness = 5, ThicknessVariable = true },
            new OpticalSurface { IsStop = true, Thickness = 10, SemiDiameter = 1 },
            new OpticalSurface { Thickness = 0 }
        ]);
        var row = Row("EFNO"); row.Target = .5 * Math.Sqrt(Math.PI * 145 / 3.2);
        optic.MeritFunctionOperands.Add(row);
        var runtime = new WorkbenchRuntime(optic);
        var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 40);
        Assert.True(result.FinalMerit < result.InitialMerit * 1e-6);
        Assert.Equal(7, runtime.CurrentOptic.SurfaceGroup.Items[1].Thickness, 4);
        Assert.InRange(Math.Abs(Value(runtime.CurrentOptic, row) - row.Target), 0, 1e-5);
    }

    private static Optic Cone()
    {
        var optic = new Optic(); optic.Aperture.Value = 2;
        optic.Fields.Add(new FieldPoint()); optic.Wavelengths.Add(new Wavelength { Nanometers = 550 });
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = 10 },
            new OpticalSurface { IsStop = true, Thickness = 10, SemiDiameter = 1 },
            new OpticalSurface { Thickness = 0 }
        ]);
        return optic;
    }

    private static MeritOperandDefinition Row(string code, double field = 1, int samp = 10, int wave = 1, double pol = 0) => new()
    {
        Type = code,
        ZemaxIntegerParameters = [samp, wave],
        ZemaxDataParameters = [field, pol, 0, 0]
    };
    private static double Value(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.True(string.IsNullOrEmpty(result.Error), result.Error); return result.Value;
    }
    private static void Invalid(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.NotEmpty(result.Error); Assert.True(double.IsNaN(result.Value)); Assert.True(double.IsPositiveInfinity(result.Contribution));
        Assert.Throws<OptimizationEvaluationException>(() => MeritFunctionCatalog.EvaluateOptimizationValues(optic, [row]));
    }
    private static string Snapshot(Optic optic) => JsonSerializer.Serialize(optic.ToSnapshot(),
        new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
}
