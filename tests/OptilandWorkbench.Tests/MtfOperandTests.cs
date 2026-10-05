using System.Numerics;
using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class MtfOperandTests
{
    public static TheoryData<string> Codes => new("GMTA", "GMTS", "GMTT", "GMTN", "GMTX",
        "MTFA", "MTFS", "MTFT", "MTFN", "MTFX", "MSWA", "MSWS", "MSWT", "MSWN", "MSWX");

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task ImportedRowsPreserveSlotsAndOnlyValidReferencesUpgrade(string code)
    {
        var parameters = code.StartsWith("GMT", StringComparison.Ordinal) ? "1 20 1 1" : "1 20 1 0";
        var optic = OpticalFormatCatalog.Import($"""
            MODE SEQ
            ENPD 4
            WAVM 1 0.55 1
            SURF 0
              DISZ INFINITY
            SURF 1
              STOP
              CURV 0.02
              GLAS 1.5
              DISZ 50
            SURF 2
              DISZ 0
            {code} 1 1 {parameters} 0.625 2.5 0 0
            """, ".zmx");
        var original = Assert.Single(optic.MeritFunctionOperands);
        Assert.True(original.Enabled); Assert.False(original.CompatibilityOnly);
        original.Enabled = false; original.CompatibilityOnly = true;
        var path = Path.Combine(Path.GetTempPath(), $"mtf-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var row = Assert.Single((await StarOptProjectStore.LoadAsync(path)).Configurations[0].MeritFunctionOperands);
            Assert.Equal(original.ZemaxIntegerParameters, row.ZemaxIntegerParameters);
            Assert.Equal(original.ZemaxDataParameters, row.ZemaxDataParameters);
            Assert.Equal(.625, row.Target); Assert.Equal(2.5, row.Weight);
            Assert.False(row.Enabled); Assert.False(row.CompatibilityOnly);
            original.ZemaxIntegerParameters[1] = 999;
            Assert.True(Assert.Single(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands).CompatibilityOnly);
            original.ZemaxIntegerParameters[1] = 1; original.ZemaxDataParameters[0] = 999;
            Assert.True(Assert.Single(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands).CompatibilityOnly);
            original.ZemaxDataParameters[0] = 1.5;
            Assert.True(Assert.Single(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands).CompatibilityOnly);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void EditorPreservesSamplingFieldFrequencyAndCalculationHelp(string code)
    {
        using var app = WorkbenchApplication.Create("cooke"); var definition = Row(code);
        app.Optimization.SetMeritFunction([new(1, true, code, 0, 1, 1, 0, 0, 0, 0, .5, 2, 0, 0, "",
            ZemaxInt1: 1, ZemaxInt2: 1, ZemaxData1: 1, ZemaxData2: 20,
            ZemaxData3: definition.ZemaxDataParameters[2], ZemaxData4: definition.ZemaxDataParameters[3])]);
        var dto = Assert.Single(app.Optimization.GetMeritFunction()); Assert.Empty(dto.Error);
        var type = Assert.Single(app.Optimization.GetMeritOperandTypes(), t => t.Code == code);
        Assert.False(type.CompatibilityOnly); Assert.Contains("Grid=1", type.Calculation);
        var descriptor = ZemaxOperandRegistry.Get(code);
        Assert.True(descriptor.UsesSlotAs("Int1", ZemaxOperandParameterValueKind.Integer));
        Assert.True(descriptor.UsesSlotAs("Data1", ZemaxOperandParameterValueKind.Field));
        Assert.True(descriptor.UsesSlotAs("Data2", ZemaxOperandParameterValueKind.SpatialFrequency));
        var editor = new MeritOperandEditorRow(dto, type);
        Assert.True(editor.HasZemaxParameters); Assert.True(editor.IsParameterEditable(0));
        app.Optimization.SetMeritFunction([editor.ToDto()]);
        var saved = Assert.Single(app.Optimization.GetMeritFunction()); Assert.Empty(saved.Error);
        Assert.Equal(dto.Value, saved.Value, 10); Assert.Equal(1, saved.Field); Assert.Equal(20, saved.SpatialFrequency);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void DcIsUnityAndAboveCutoffIsZeroForScaledOrDiffractiveMtf(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(code, 0);
        if (code.StartsWith("GMT", StringComparison.Ordinal)) row.ZemaxDataParameters[2] = 0;
        Value(optic, row, 1, 10); row.ZemaxDataParameters[1] = 1e8; Value(optic, row, 0, 10);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void InvalidParametersUnsupportedGridAndCancellationFail(string code)
    {
        var optic = Optic.CreateCookeTriplet();
        foreach (var sampling in new[] { -1, 0, 6 })
        {
            var row = Row(code); row.ZemaxIntegerParameters[0] = sampling; Invalid(optic, row);
        }
        foreach (var wave in new[] { -1, 999 })
        {
            var row = Row(code); row.ZemaxIntegerParameters[1] = wave; Invalid(optic, row);
        }
        foreach (var field in new[] { -1.0, 1.5, 999 })
        {
            var row = Row(code); row.ZemaxDataParameters[0] = field; Invalid(optic, row);
        }
        foreach (var frequency in new[] { -1.0, double.NaN, double.PositiveInfinity })
        {
            var row = Row(code); row.ZemaxDataParameters[1] = frequency; Invalid(optic, row);
        }
        var invalidGrid = Row(code); invalidGrid.ZemaxDataParameters[code.StartsWith("GMT", StringComparison.Ordinal) ? 3 : 2] = 0;
        Assert.Contains("Grid=1", Invalid(optic, invalidGrid));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(optic, Row(code)));
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void EmptyPupilAndAllZeroSpectralWeightsNeverReturnSuccess(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(code); row.ZemaxIntegerParameters[1] = 0;
        foreach (var wave in optic.Wavelengths) wave.Weight = 0;
        Invalid(optic, row); row.ZemaxIntegerParameters[1] = 1; Assert.True(double.IsFinite(Evaluate(optic, row)));
        row.ZemaxIntegerParameters[1] = 0; optic.Wavelengths[0].Weight = -1; Invalid(optic, row);
        row.ZemaxIntegerParameters[1] = 1;
        optic.SurfaceGroup.Items[1].PhysicalAperture = new RectangularAperture(.1, .1, 100, 100);
        Invalid(optic, row);
    }

    [Theory]
    [InlineData("GMT")]
    [InlineData("MTF")]
    [InlineData("MSW")]
    public void DirectionReducersUseActualTangentialAndSagittalValues(string prefix)
    {
        var optic = Optic.CreateCookeTriplet();
        double Metric(string suffix)
        {
            var row = Row(prefix + suffix); row.ZemaxDataParameters[0] = 2; return Evaluate(optic, row);
        }
        var t = Metric("T"); var s = Metric("S"); Assert.True(Math.Abs(t - s) > 1e-6);
        Assert.Equal((t + s) / 2, Metric("A"), 10);
        Assert.Equal(Math.Min(t, s), Metric("N"), 10); Assert.Equal(Math.Max(t, s), Metric("X"), 10);
    }

    [Theory]
    [InlineData("GMTA")]
    [InlineData("GMTS")]
    [InlineData("GMTT")]
    [InlineData("GMTN")]
    [InlineData("GMTX")]
    public void DefocusedThinLensMatchesIndependentDiscreteFourierSum(string code)
    {
        var optic = ThinLens(); var row = Row(code, .5);
        var pupil = ApertureSampler.Generate(32 * 32, PupilSampling.UniformGrid);
        var expected = pupil.Average(p => Math.Cos(2 * Math.PI * .5 * .4 * p.X));
        Value(optic, row, expected, 9);
        row.ZemaxDataParameters[2] = 0;
        var ratio = .5 * .00055 * 12.5;
        var diffractionLimit = 2 / Math.PI * (Math.Acos(ratio) - ratio * Math.Sqrt(1 - ratio * ratio));
        Value(optic, row, expected * diffractionLimit, 9);
        row.ZemaxDataParameters[0] = 0; Invalid(optic, row);
    }

    [Theory]
    [InlineData("MTFT")]
    [InlineData("MTFS")]
    public void ComplexOutputAndPhaseDegreesAreConsistent(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(code); row.ZemaxDataParameters[0] = 2;
        optic.Fields[1].X = 7;
        var amplitude = Evaluate(optic, row); row.ZemaxDataParameters[3] = 1; var real = Evaluate(optic, row);
        row.ZemaxDataParameters[3] = 2; var imaginary = Evaluate(optic, row);
        row.ZemaxDataParameters[3] = 3; var phase = Evaluate(optic, row);
        Assert.Equal(Math.Sqrt(real * real + imaginary * imaginary), amplitude, 12);
        Assert.Equal(Math.Atan2(imaginary, real) * 180 / Math.PI, phase, 10);
        Assert.True(Math.Abs(phase) > .01);
        row.ZemaxDataParameters[3] = 4; Invalid(optic, row);
        row.ZemaxDataParameters[3] = .5; Invalid(optic, row);
    }

    [Theory]
    [InlineData("MTFT")]
    [InlineData("MTFS")]
    public void PolychromaticMtfCombinesComplexValuesBeforeTakingMagnitude(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(code, 30); row.ZemaxDataParameters[0] = 2;
        optic.Fields[1].X = 7;
        optic.Wavelengths[0].Weight = 2; optic.Wavelengths[1].Weight = 3; optic.Wavelengths[2].Weight = 0;
        var sum = Complex.Zero; var sumMagnitude = 0.0;
        for (var wave = 1; wave <= 2; wave++)
        {
            row.ZemaxIntegerParameters[1] = wave; row.ZemaxDataParameters[3] = 1; var real = Evaluate(optic, row);
            row.ZemaxDataParameters[3] = 2; var value = new Complex(real, Evaluate(optic, row));
            sum += optic.Wavelengths[wave - 1].Weight * value; sumMagnitude += optic.Wavelengths[wave - 1].Weight * value.Magnitude;
        }
        row.ZemaxIntegerParameters[1] = 0; row.ZemaxDataParameters[3] = 0;
        Assert.Equal(sum.Magnitude / 5, Evaluate(optic, row), 12);
        Assert.True(sumMagnitude / 5 - sum.Magnitude / 5 > 1e-6);
        optic.Wavelengths[0].Weight = 0; row.ZemaxIntegerParameters[1] = 2; var single = Evaluate(optic, row);
        row.ZemaxIntegerParameters[1] = 0; Value(optic, row, single, 10);
    }

    [Theory]
    [InlineData("MTFT")]
    [InlineData("MTFS")]
    [InlineData("MSWT")]
    [InlineData("MSWS")]
    public void FieldZeroDiffractionLimitMatchesCircularPupilReference(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(code); row.ZemaxDataParameters[0] = 0; row.ZemaxIntegerParameters[0] = 3;
        var wave = optic.Wavelengths[0];
        var psf = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 128, 256, ignoreOpd: true);
        var cutoff = 1 / (wave.Micrometers * .001 * psf.WorkingFNumber);
        var frequency = .2 * cutoff; row.ZemaxDataParameters[1] = frequency;
        double Circular(double ratio) => ratio >= 1 ? 0 : 2 / Math.PI * (Math.Acos(ratio) - ratio * Math.Sqrt(1 - ratio * ratio));
        var expected = Circular(.2);
        if (code.StartsWith("MSW", StringComparison.Ordinal))
            expected = 4 / Math.PI * (Circular(.2) - Circular(.6) / 3);
        Assert.InRange(Math.Abs(Evaluate(optic, row) - expected), 0, .02);
    }

    [Theory]
    [InlineData("GMTT")]
    [InlineData("MTFT")]
    [InlineData("MSWT")]
    public void AfocalFrequencyUsesCyclesPerMilliradianAndHasFiniteDc(string code)
    {
        var optic = Optic.CreateCookeTriplet(); optic.ImageSpaceAfocal = true; var row = Row(code, 0);
        Value(optic, row, 1, 10);
        row.ZemaxDataParameters[1] = .05; var value = Evaluate(optic, row); Assert.InRange(value, 0, 1.05);
        var kind = code[0] == 'G' ? MtfMetricKind.Geometric : code.StartsWith("MSW", StringComparison.Ordinal) ? MtfMetricKind.SquareWave : MtfMetricKind.Fourier;
        var direct = MtfMetrics.EvaluateGrid(optic, kind, 1, 1, 1, .05, scaleGeometric: false);
        Assert.Equal(direct.Tangential, value, 10);
    }

    [Fact]
    public void SquareWaveUsesHarmonicsAndDiffersFromSinusoidalModulation()
    {
        var optic = Optic.CreateCookeTriplet(); var sine = Row("MTFT", 20); sine.ZemaxDataParameters[0] = 0;
        var square = Row("MSWT", 20); square.ZemaxDataParameters[0] = 0;
        Assert.True(Evaluate(optic, square) > Evaluate(optic, sine) + .01);
    }

    [Fact]
    public void GeometryAndSpectralChangesCannotReuseAStaleMtf()
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row("MTFA"); row.ZemaxIntegerParameters[1] = 0;
        var original = Evaluate(optic, row); var surface = optic.SurfaceGroup.Items[^2];
        surface.Thickness += 2; optic.SurfaceGroup.Renumber(); Assert.True(Math.Abs(Evaluate(optic, row) - original) > .01);
        surface.Thickness -= 2; optic.SurfaceGroup.Renumber(); Value(optic, row, original);
        optic.Wavelengths[0].Weight = 100; Assert.True(Math.Abs(Evaluate(optic, row) - original) > 1e-5);
    }

    [Theory]
    [InlineData(FftMtfDataType.Modulation, .3952847075210474)]
    [InlineData(FftMtfDataType.Real, .125)]
    [InlineData(FftMtfDataType.Imaginary, .375)]
    [InlineData(FftMtfDataType.Phase, 1.2490457723982544)]
    public void DifferentWavelengthGridsAreSampledOnlyOnce(FftMtfDataType type, double expected)
    {
        var a = new MtfResult([0, 1, 2], [1, 1, 0], [1, 1, 0], 2,
            [Complex.One, -Complex.One, Complex.Zero], [Complex.One, -Complex.One, Complex.Zero]);
        var b = new MtfResult([0, 2, 4], [1, 1, 0], [1, 1, 0], 4,
            [Complex.One, Complex.ImaginaryOne, Complex.Zero], [Complex.One, Complex.ImaginaryOne, Complex.Zero]);
        var result = MtfMethodEvaluator.SamplePolychromaticAtFrequency([
            (new Wavelength { Nanometers = 500, Weight = 1 }, a),
            (new Wavelength { Nanometers = 600, Weight = 3 }, b)], 1, type);
        Assert.Equal(expected, result.Tangential, 12); Assert.Equal(expected, result.Sagittal, 12);
    }

    [Fact]
    public void SharedSamplerRejectsMissingComplexDataAndZeroWeights()
    {
        var result = new MtfResult([0, 1], [1, 0], [1, 0], 1);
        Assert.Throws<AnalysisDataUnavailableException>(() => MtfMethodEvaluator.SamplePolychromaticAtFrequency(
            [(new Wavelength { Nanometers = 500, Weight = 1 }, result)], .5, FftMtfDataType.Modulation));
        Assert.Throws<AnalysisDataUnavailableException>(() => MtfMethodEvaluator.SamplePolychromaticAtFrequency(
            [(new Wavelength { Nanometers = 500, Weight = 0 }, result)], .5, FftMtfDataType.Modulation));
    }

    [Theory]
    [InlineData("MNAI")]
    [InlineData("MXAI")]
    [InlineData("CENX")]
    public void FractionalRawFieldCannotBeSavedAsAnExecutableLegacyRow(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(code); row.ZemaxDataParameters[0] = .125;
        optic.MeritFunctionOperands.Clear(); optic.MeritFunctionOperands.Add(row);
        Assert.Throws<InvalidDataException>(() => OpticSnapshotValidator.Validate(optic.ToSnapshot()));
        row.CompatibilityOnly = true;
        Assert.True(Assert.Single(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands).CompatibilityOnly);
    }

    [Theory]
    [InlineData("GMTT")]
    [InlineData("MTFT")]
    [InlineData("MSWT")]
    public void IncreasingSamplingChangesResultsAndDoesNotMutateTheSystem(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(code, 40); row.ZemaxDataParameters[0] = 2;
        var coarse = Evaluate(optic, row); row.ZemaxIntegerParameters[0] = 2; var medium = Evaluate(optic, row);
        Assert.True(Math.Abs(coarse - medium) > 1e-6);
        row.ZemaxIntegerParameters[0] = 1; Value(optic, row, coarse);
        row.ZemaxIntegerParameters[1] = 0;
        var weights = optic.Wavelengths.Select(w => w.Weight).ToArray();
        Evaluate(optic, row); Assert.Equal(weights, optic.Wavelengths.Select(w => w.Weight));
    }

    [Fact]
    public void FormalDlsCanImproveGeometricMtfByRefocusing()
    {
        var optic = ThinLens(); optic.SurfaceGroup.Items[1].ThicknessVariable = true;
        var row = Row("GMTA", .5); row.Target = 1; optic.MeritFunctionOperands.Add(row);
        var runtime = new WorkbenchRuntime(optic); var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 60);
        Assert.True(result.FinalMerit < result.InitialMerit * .01);
        Assert.InRange(Evaluate(runtime.CurrentOptic, row), .995, 1.000000001);
    }

    private static Optic ThinLens()
    {
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity, Geometry = new PlaneGeometry(), MaterialAfter = new AirMaterial(), SemiDiameter = 50 },
            new OpticalSurface { Thickness = 40, Geometry = new PlaneGeometry(), MaterialAfter = new AirMaterial(), InteractionModel = new ThinLensInteractionModel(50), SemiDiameter = 50, IsStop = true },
            new OpticalSurface { Thickness = 0, Geometry = new PlaneGeometry(), MaterialAfter = new AirMaterial(), SemiDiameter = 50 }
        ]);
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter; optic.Aperture.Value = 4;
        optic.Fields.Clear(); optic.Fields.Add(new FieldPoint { Y = 0 });
        optic.Wavelengths.Clear(); optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        return optic;
    }
    private static MeritOperandDefinition Row(string code, double frequency = 20) => new()
    {
        Type = code,
        Field = 1,
        Wavelength = 1,
        Weight = 2,
        ZemaxIntegerParameters = [1, 1],
        ZemaxDataParameters = code.StartsWith("GMT", StringComparison.Ordinal) ? [1, frequency, 1, 1] : [1, frequency, 1, 0]
    };
    private static double Evaluate(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.True(string.IsNullOrEmpty(result.Error), result.Error); return result.Value;
    }
    private static void Value(Optic optic, MeritOperandDefinition row, double expected, int precision = 9)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.True(string.IsNullOrEmpty(result.Error), result.Error);
        Assert.Equal(expected, result.Value, precision); Assert.Equal(row.Weight * Math.Pow(result.Value - row.Target, 2), result.Contribution, 9);
    }
    private static string Invalid(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.False(string.IsNullOrEmpty(result.Error));
        Assert.True(double.IsPositiveInfinity(result.Contribution)); return result.Error;
    }
}
