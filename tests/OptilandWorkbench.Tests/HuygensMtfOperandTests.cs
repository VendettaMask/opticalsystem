using System.Numerics;
using System.Text.Json;
using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;
using Xunit.Abstractions;

namespace OptilandWorkbench.Tests;

public sealed class HuygensMtfOperandTests(ITestOutputHelper output)
{
    public static TheoryData<string> Codes => new("MTHA", "MTHS", "MTHT", "MTHN", "MTHX");

    [Theory]
    [MemberData(nameof(Codes))]
    public void SevenSlotEditorAndHelpPreserveDeltaTargetAndWeight(string code)
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, false, code, 1, 1, 0, 0, 0, 0, 0, .625, 2.5, 0, 0, "Huygens",
            ZemaxInt1: 1, ZemaxInt2: 0, ZemaxData1: 1, ZemaxData2: 30,
            ZemaxData3: 0, ZemaxData4: 0, ZemaxData5: .8)]);
        var dto = Assert.Single(app.Optimization.GetMeritFunction());
        var type = Assert.Single(app.Optimization.GetMeritOperandTypes(), t => t.Code == code);
        Assert.False(type.CompatibilityOnly); Assert.Contains("Pol=0", type.Calculation);
        Assert.Contains("原生 MFE", type.Calculation); Assert.Equal(7, type.Parameters!.Count);
        var editor = new MeritOperandEditorRow(dto, type);
        Assert.True(editor.IsParameterEditable(6)); Assert.Contains("Image delta", editor.ParameterLabel(6));
        editor.Parameter7 = 1.2; app.Optimization.SetMeritFunction([editor.ToDto()]);
        var saved = Assert.Single(app.Optimization.GetMeritFunction());
        Assert.Equal(1.2, saved.ZemaxData5); Assert.Equal(.625, saved.Target); Assert.Equal(2.5, saved.Weight);
        Assert.Equal(0, saved.Wavelength); Assert.Equal(1, saved.Field);
        Assert.Equal("SpatialFrequency", type.Parameters[3].ValueKind);
        Assert.Equal("µm", type.Parameters[6].Unit);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task FullStaroptRoundTripUpgradesValidDisabledRows(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(code, wave: 0, delta: .9);
        row.Enabled = false; row.CompatibilityOnly = true; optic.MeritFunctionOperands.Add(row);
        var path = Path.Combine(Path.GetTempPath(), $"huygens-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var loaded = Assert.Single((await StarOptProjectStore.LoadAsync(path)).Configurations[0].MeritFunctionOperands);
            Assert.False(loaded.Enabled); Assert.False(loaded.CompatibilityOnly);
            Assert.Equal(row.ZemaxDataParameters, loaded.ZemaxDataParameters);
            Assert.Equal(row.ZemaxIntegerParameters, loaded.ZemaxIntegerParameters);
            Assert.Equal(row.Target, loaded.Target); Assert.Equal(row.Weight, loaded.Weight);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void NativeExtendedZmxRecordStaysReadOnlyAcrossEditorAndSnapshot(string code)
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX"));
        text += $"\n{code} 1 0 1 50 0 0 .625 2.5 91 92 .375 .825\n";
        var optic = OpticalFormatCatalog.Import(text, ".zmx");
        var original = optic.MeritFunctionOperands.Last();
        Assert.Equal(code, original.Type); Assert.True(original.CompatibilityOnly); Assert.False(original.Enabled);
        Assert.EndsWith("91 92 .375 .825", original.Comment);
        Assert.Equal(4, original.ZemaxDataParameters.Length);
        Assert.True(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands.Last().CompatibilityOnly);
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, false, code, 1, 1, 0, 1, 50, 0, 0, .625, 2.5, 0, 0, original.Comment,
            CompatibilityOnly: true, ZemaxInt1: 1, ZemaxInt2: 0,
            ZemaxData1: 1, ZemaxData2: 50, ZemaxData3: 0, ZemaxData4: 0)]);
        var dto = Assert.Single(app.Optimization.GetMeritFunction());
        var type = Assert.Single(app.Optimization.GetMeritOperandTypes(), t => t.Code == code);
        var editor = new MeritOperandEditorRow(dto, type);
        Assert.True(editor.CompatibilityOnly); Assert.False(editor.IsParameterEditable(6));
        app.Optimization.SetMeritFunction([editor.ToDto()]);
        var saved = Assert.Single(app.Optimization.GetMeritFunction());
        Assert.True(saved.CompatibilityOnly); Assert.Null(saved.ZemaxData5); Assert.Equal(original.Comment, saved.Comment);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void TruncatedLegacyRowsNeverInventImageDelta(int count)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row("MTHA");
        row.ZemaxDataParameters = row.ZemaxDataParameters.Take(count).ToArray();
        Invalid(optic, row); row.Enabled = false; row.CompatibilityOnly = true;
        optic.MeritFunctionOperands.Add(row);
        var restored = Assert.Single(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands);
        Assert.True(restored.CompatibilityOnly); Assert.Equal(count, restored.ZemaxDataParameters.Length);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void EachDirectionMatchesIndependentDftOfSharedMonochromaticPsf(string code)
    {
        var optic = Optic.CreateCookeTriplet(); const double delta = 1.7;
        // At an exact endpoint-span grid node, the shared spline must equal the DFT bin.
        var frequency = 3 * 1000.0 / (63 * delta);
        var row = Row(code, frequency, delta: delta); row.ZemaxDataParameters[0] = 2;
        var field = FieldCoordinates.Normalize(optic.Fields, optic.Fields[1].X, optic.Fields[1].Y);
        var psf = DiffractionEngine.ComputeHuygensPsf(optic, field, optic.Wavelengths[0],
            32, 32, delta / 1000, aimAtStop: optic.RayAimingEnabled);
        var expected = IndependentDft(psf.Values, bin: 3, transformSize: 64);
        Value(optic, row, Select(code, expected));
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void PolychromaticCombinationUsesWeightedPsfBeforeTakingModulus(string code)
    {
        var optic = Optic.CreateCookeTriplet(); const double delta = 1.1;
        optic.Wavelengths[0].Weight = 1; optic.Wavelengths[1].Weight = 3; optic.Wavelengths[2].Weight = 0;
        var combined = new double[32, 32];
        var field = FieldCoordinates.Normalize(optic.Fields, optic.Fields[1].X, optic.Fields[1].Y);
        foreach (var wave in optic.Wavelengths.Take(2))
        {
            var psf = DiffractionEngine.ComputeHuygensPsf(optic, field, wave, 32, 32, delta / 1000,
                aimAtStop: optic.RayAimingEnabled, referenceWavelength: optic.Wavelengths.First(w => w.IsPrimary));
            for (var y = 0; y < 32; y++)
                for (var x = 0; x < 32; x++) combined[y, x] += wave.Weight / (wave.Nanometers * wave.Nanometers) * psf.Values[y, x];
        }
        var row = Row(code, 2 * 1000.0 / (63 * delta), wave: 0, delta: delta); row.ZemaxDataParameters[0] = 2;
        Value(optic, row, Select(code, IndependentDft(combined, 2, 64)));
        var before = Evaluate(optic, row);
        foreach (var wave in optic.Wavelengths) wave.Weight *= 1e200;
        Value(optic, row, before);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void ZeroFrequencyIsOneAndFrequencyBeyondSampledGridIsZero(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(code, 0);
        Value(optic, row, 1); row.ZemaxDataParameters[1] = 1e9; Value(optic, row, 0);
    }

    [Fact]
    public void AutomaticDeltaUsesSelectedFieldAndLongestPositiveWeightWavelength()
    {
        var optic = Optic.CreateCookeTriplet(); optic.Wavelengths[0].Weight = 0;
        var row = Row("MTHT", wave: 0); row.ZemaxDataParameters[0] = 2;
        var field = FieldCoordinates.Normalize(optic.Fields, optic.Fields[1].X, optic.Fields[1].Y);
        var wavelength = optic.Wavelengths.Where(w => w.Weight > 0).MaxBy(w => w.Nanometers)!;
        var automatic = Evaluate(optic, row);
        row.ZemaxDataParameters[4] = 1000 * DiffractionEngine.DefaultHuygensImageDeltaMillimeters(optic, field, wavelength, 32);
        Value(optic, row, automatic);
    }

    [Fact]
    public void ImageDeltaAndSamplingChangeResultsWithoutMutatingTheSystem()
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row("MTHT", 90, delta: .75);
        var weights = optic.Wavelengths.Select(w => w.Weight).ToArray();
        var first = Evaluate(optic, row); row.ZemaxDataParameters[4] = 2;
        Assert.True(Math.Abs(first - Evaluate(optic, row)) > 1e-6);
        row.ZemaxDataParameters[4] = .75; row.ZemaxIntegerParameters[0] = 2;
        Assert.True(Math.Abs(first - Evaluate(optic, row)) > 1e-6);
        row.ZemaxIntegerParameters[0] = 1; Value(optic, row, first);
        Assert.Equal(weights, optic.Wavelengths.Select(w => w.Weight));
        optic.SurfaceGroup.Items[^2].Thickness += 1; optic.SurfaceGroup.Renumber();
        Assert.True(Math.Abs(first - Evaluate(optic, row)) > 1e-6);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1.5)]
    [InlineData(0, 999)]
    [InlineData(1, -1)]
    [InlineData(1, double.NaN)]
    [InlineData(1, double.PositiveInfinity)]
    [InlineData(2, 1)]
    [InlineData(2, -1)]
    [InlineData(2, .5)]
    [InlineData(3, 1)]
    [InlineData(3, -1)]
    [InlineData(3, .5)]
    [InlineData(4, double.Epsilon)]
    [InlineData(4, -1)]
    [InlineData(4, double.NaN)]
    [InlineData(4, double.PositiveInfinity)]
    public void InvalidDataAndUnsupportedModesAreErrors(int index, double value)
    {
        var row = Row("MTHA"); row.ZemaxDataParameters[index] = value; Invalid(Optic.CreateCookeTriplet(), row);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 3)]
    [InlineData(0, int.MaxValue)]
    [InlineData(1, -1)]
    [InlineData(1, 999)]
    public void InvalidSamplingAndWaveReferencesAreErrors(int index, int value)
    {
        var row = Row("MTHA"); row.ZemaxIntegerParameters[index] = value; Invalid(Optic.CreateCookeTriplet(), row);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    public void InvalidPolychromaticWeightsNeverBecomeUniformWeights(double weight)
    {
        var optic = Optic.CreateCookeTriplet();
        if (!double.IsFinite(weight))
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => optic.Wavelengths[0].Weight = weight);
            return;
        }
        foreach (var w in optic.Wavelengths) w.Weight = weight;
        Invalid(optic, Row("MTHA", wave: 0));
        // A selected monochromatic measurement does not use spectral weights.
        Assert.InRange(Evaluate(optic, Row("MTHA", wave: 1)), 0, 1);
    }

    [Fact]
    public void AfocalAndCancelledEvaluationAreExplicit()
    {
        var optic = Optic.CreateCookeTriplet(); optic.ImageSpaceAfocal = true;
        Invalid(optic, Row("MTHA")); optic.ImageSpaceAfocal = false;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(optic, Row("MTHA")));
    }

    [Fact]
    public void BlockedPupilDoesNotReturnSuccessfulZero()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[1].PhysicalAperture = new RectangularAperture(.1, .1, 100, 100);
        Invalid(optic, Row("MTHA"));
    }

    [Fact]
    public void FormalDlsUsesHuygensOperandToRestoreImageFocus()
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row("MTHA", 30, delta: 1);
        row.Target = Evaluate(optic, row);
        optic.SurfaceGroup.Items[^2].Thickness += 1; optic.SurfaceGroup.Renumber();
        optic.SurfaceGroup.Items[^2].ThicknessVariable = true; optic.MeritFunctionOperands.Add(row);
        var runtime = new WorkbenchRuntime(optic);
        var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 20);
        Assert.True(result.FinalMerit < result.InitialMerit * .5,
            $"initial={result.InitialMerit:R}, final={result.FinalMerit:R}");
    }

    [Fact]
    public void EnabledApplicationRowRecalculatesAfterSeventhParameterEdit()
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, true, "MTHT", 1, 1, 1, 0, 0, 0, 0, .7, 2, 0, 0, "",
            ZemaxInt1: 1, ZemaxInt2: 1, ZemaxData1: 1, ZemaxData2: 90,
            ZemaxData3: 0, ZemaxData4: 0, ZemaxData5: .75)]);
        var before = Assert.Single(app.Optimization.GetMeritFunction()); Assert.Empty(before.Error);
        var type = Assert.Single(app.Optimization.GetMeritOperandTypes(), t => t.Code == "MTHT");
        var editor = new MeritOperandEditorRow(before, type) { Parameter7 = 2 };
        app.Optimization.SetMeritFunction([editor.ToDto()]);
        var after = Assert.Single(app.Optimization.GetMeritFunction()); Assert.Empty(after.Error);
        Assert.True(Math.Abs(after.Value - before.Value) > 1e-6);
    }

    [Fact]
    public void Captured123456SettingsCompareAtSelectedFieldsAndFrequencies()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(fixture, "zemax-123456.ZMX")), ".zmx");
        using var capture = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture, "zemax-123456-huygens-mtf.json")));
        var series = capture.RootElement.GetProperty("dataSeries");
        // Captured frequency plot used a shared automatic image delta resolved at its first field.
        // Recover that saved grid's pitch from its final frequency and recorded N=32 contract.
        var lastFrequency = series[0].GetProperty("x")[299].GetDouble();
        var delta = 31 * 1000.0 / (63 * lastFrequency);
        double maximumError = 0;
        foreach (var field in new[] { 1, 3, 5 })
        {
            foreach (var index in new[] { 0, 30, 90 })
            {
                var x = series[field - 1].GetProperty("x")[index].GetDouble();
                var y = series[field - 1].GetProperty("y")[index];
                var current = MtfMetrics.EvaluateHuygens(optic, 1, 0, field, x, delta);
                maximumError = Math.Max(maximumError, Math.Abs(current.Tangential - y[0].GetDouble()));
                maximumError = Math.Max(maximumError, Math.Abs(current.Sagittal - y[1].GetDouble()));
            }
        }
        output.WriteLine($"Huygens captured analysis: points=18, maximumAbsoluteError={maximumError:R}, regressionBudget=0.03; not a native MFE capture.");
        Assert.InRange(maximumError, 0, .03);
    }

    private static (double Tangential, double Sagittal) IndependentDft(double[,] values, int bin, int transformSize)
    {
        var t = Complex.Zero; var s = Complex.Zero; var total = 0.0;
        for (var y = 0; y < values.GetLength(0); y++)
            for (var x = 0; x < values.GetLength(1); x++)
            {
                var v = values[y, x]; total += v;
                t += v * Complex.FromPolarCoordinates(1, -2 * Math.PI * bin * y / transformSize);
                s += v * Complex.FromPolarCoordinates(1, -2 * Math.PI * bin * x / transformSize);
            }
        return (t.Magnitude / total, s.Magnitude / total);
    }
    private static double Select(string code, (double Tangential, double Sagittal) value) => code[^1] switch
    {
        'T' => value.Tangential,
        'S' => value.Sagittal,
        'N' => Math.Min(value.Tangential, value.Sagittal),
        'X' => Math.Max(value.Tangential, value.Sagittal),
        _ => (value.Tangential + value.Sagittal) / 2
    };
    private static MeritOperandDefinition Row(string code, double frequency = 30, int wave = 1, double delta = 0) => new()
    {
        Type = code,
        Field = 1,
        Wavelength = wave,
        Target = .7,
        Weight = 2,
        ZemaxIntegerParameters = [1, wave],
        ZemaxDataParameters = [1, frequency, 0, 0, delta]
    };
    private static double Evaluate(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.True(string.IsNullOrEmpty(result.Error), result.Error);
        Assert.Equal(row.Weight * Math.Pow(result.Value - row.Target, 2), result.Contribution, 10);
        return result.Value;
    }
    private static void Value(Optic optic, MeritOperandDefinition row, double expected) => Assert.Equal(expected, Evaluate(optic, row), 9);
    private static void Invalid(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.False(string.IsNullOrEmpty(result.Error)); Assert.True(double.IsPositiveInfinity(result.Contribution));
    }
}
