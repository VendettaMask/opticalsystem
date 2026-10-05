using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class WavefrontOperandTests
{
    public static TheoryData<string> Codes => new("RWCE", "RWCH", "RWRE", "RWRH", "MWCE", "MWCH", "MWRE", "MWRH");

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task ImportRoundTripAndLegacyUpgradePreserveAllSlots(string code)
    {
        var optic = OpticalFormatCatalog.Import($"""
            MODE SEQ
            ENPD 4
            WAVM 1 0.55 1
            SURF 0
              DISZ INFINITY
            SURF 1
              STOP
              DISZ 20
            SURF 2
              DISZ 0
            {code} 3 1 0.25 -0.5 0.125 -0.375 0.625 2.5 0 0
            """, ".zmx");
        var original = Assert.Single(optic.MeritFunctionOperands);
        Assert.False(original.CompatibilityOnly); Assert.True(original.Enabled);
        Assert.Equal([3, 1], original.ZemaxIntegerParameters);
        Assert.Equal([.25, -.5, .125, -.375], original.ZemaxDataParameters);
        original.Enabled = false; original.CompatibilityOnly = true;
        var path = Path.Combine(Path.GetTempPath(), $"wavefront-{Guid.NewGuid():N}.staropt");
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
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void EditorUsesSamplingRatherThanSurfaceAndExposesLimits(string code)
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, true, code, 0, 1, 1, 0, 0, 0, 0, .1, 2, 0, 0, "",
            ZemaxInt1: 3, ZemaxInt2: 1, ZemaxData1: 0, ZemaxData2: 0, ZemaxData3: .125, ZemaxData4: -.375)]);
        var dto = Assert.Single(app.Optimization.GetMeritFunction()); Assert.Empty(dto.Error);
        var type = Assert.Single(app.Optimization.GetMeritOperandTypes(), t => t.Code == code);
        Assert.False(type.CompatibilityOnly); Assert.Contains("waves", type.Calculation);
        Assert.Contains("尚无原生 MFE", type.Calculation);
        var descriptor = ZemaxOperandRegistry.Get(code);
        Assert.True(descriptor.UsesSlotAs("Int1", ZemaxOperandParameterValueKind.Integer));
        var editor = new MeritOperandEditorRow(dto, type);
        Assert.True(editor.HasZemaxParameters); Assert.True(editor.IsParameterEditable(0));
        app.Optimization.SetMeritFunction([editor.ToDto()]);
        var saved = Assert.Single(app.Optimization.GetMeritFunction());
        Assert.Empty(saved.Error); Assert.Equal(dto.Value, saved.Value, 10);
        Assert.Equal(dto.ZemaxData3, saved.ZemaxData3); Assert.Equal(dto.ZemaxData4, saved.ZemaxData4);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void PlaneWaveAgainstFiniteReferenceSphereMatchesAnalyticSag(string code)
    {
        var optic = Plane(); var row = Row(code); var pupils = Pupil(code);
        var opd = pupils.Select(p => (20 - Math.Sqrt(400 - 4 * (p.X * p.X + p.Y * p.Y))) / .00055).ToArray();
        var mean = pupils.Select((p, i) => p.Weight * opd[i]).Sum() / pupils.Sum(p => p.Weight);
        var expected = code[0] == 'M' ? opd.Max() - opd.Min()
            : Math.Sqrt(pupils.Select((p, i) => p.Weight * Math.Pow(opd[i] - mean, 2)).Sum() / pupils.Sum(p => p.Weight));
        Assert.True(expected > 1);
        Value(optic, row, expected, 7);
        // Raw image-plane OPL is constant here: a spread of raw OPL would incorrectly return zero.
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void AfocalPlaneWaveHasZeroWavefrontError(string code)
    {
        var optic = Plane(); optic.ImageSpaceAfocal = true;
        Value(optic, Row(code), 0, 8);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void InvalidSamplingFieldWaveAndCancellationFailExplicitly(string code)
    {
        var optic = Plane(); var row = Row(code);
        foreach (var sampling in new[] { 0, -1, code[2] == 'C' ? 33 : 257 })
        {
            row.ZemaxIntegerParameters[0] = sampling; Invalid(optic, row);
        }
        row = Row(code); row.ZemaxIntegerParameters[1] = 999; Invalid(optic, row);
        row.ZemaxIntegerParameters[1] = -1; Invalid(optic, row);
        row = Row(code); row.ZemaxDataParameters[0] = double.NaN; Invalid(optic, row);
        row.ZemaxDataParameters[0] = 1.1; Invalid(optic, row);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(optic, Row(code)));
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void VignettingIsRejectedByGaussianAndRemovedByRectangle(string code)
    {
        var optic = Plane(); optic.SurfaceGroup.Items[1].PhysicalAperture = new RectangularAperture(1, 3, .5, 0);
        if (code[2] == 'C') Invalid(optic, Row(code));
        else
        {
            var pupils = Pupil(code);
            var valid = pupils.Where(p => Math.Abs(2 * p.X - .5) <= 1 && Math.Abs(2 * p.Y) <= 3).ToArray();
            Assert.True(valid.Length < pupils.Count);
            var samples = valid.Select(p => new WavefrontSample(p.X, p.Y, 0, 0, 0,
                (20 - Math.Sqrt(400 - 4 * (p.X * p.X + p.Y * p.Y))) / .00055, 1)).ToArray();
            var stats = WavefrontStatistics.Measure(samples, valid,
                code[^1] == 'E' ? WavefrontReferenceKind.Centroid : WavefrontReferenceKind.ChiefRay);
            Value(optic, Row(code), code[0] == 'M' ? stats.PeakToValley : stats.Rms, 7);
        }
        optic.SurfaceGroup.Items[1].PhysicalAperture = new RectangularAperture(.1, .1, 100, 100);
        Invalid(optic, Row(code));
    }

    [Theory]
    [InlineData("RWCE")]
    [InlineData("RWCH")]
    [InlineData("RWRE")]
    [InlineData("RWRH")]
    public void PolychromaticRmsCombinesVariancesAndHonorsWeightEdits(string code)
    {
        var optic = Plane(); var row = Row(code); var first = Evaluate(optic, row);
        optic.Wavelengths[0].Weight = 1;
        optic.Wavelengths.Add(new Wavelength { Nanometers = 1100, Weight = 3 });
        row.ZemaxIntegerParameters[1] = 2; Value(optic, row, first / 2, 7);
        row.ZemaxIntegerParameters[1] = 0; Value(optic, row, first * Math.Sqrt((1 + 3.0 / 4) / 4), 7);
        optic.Wavelengths[0].Weight = 0; Value(optic, row, first / 2, 7);
        optic.Wavelengths[1].Weight = 0; Invalid(optic, row);
        row.ZemaxIntegerParameters[1] = 1; Value(optic, row, first, 7);
        row.ZemaxIntegerParameters[1] = 0; optic.Wavelengths[0].Weight = -1; Invalid(optic, row);
    }

    [Theory]
    [InlineData("MWCE")]
    [InlineData("MWCH")]
    [InlineData("MWRE")]
    [InlineData("MWRH")]
    public void PeakToValleyDoesNotGuessWaveZeroMeaning(string code)
    {
        var row = Row(code); row.ZemaxIntegerParameters[1] = 0; Invalid(Plane(), row);
    }

    [Theory]
    [InlineData(WavefrontReferenceKind.ChiefRay, 8.5, 7)]
    [InlineData(WavefrontReferenceKind.Centroid, 3, 4)]
    public void WeightedPolynomialHasIndependentRmsAndPeakToValley(WavefrontReferenceKind reference, double variance, double pv)
    {
        PupilSample[] pupil = [new(-1, 0, 1), new(1, 0, 1), new(0, -1, 2), new(0, 1, 2), new(0, 0, 2)];
        var samples = pupil.Select(p => new WavefrontSample(p.X, p.Y, 0, 0, 0,
            7 + 2 * p.X - 3 * p.Y + 4 * (p.X * p.X + p.Y * p.Y), 1)).ToArray();
        var result = WavefrontStatistics.Measure(samples, pupil, reference);
        Assert.Equal(Math.Sqrt(variance), result.Rms, 12); Assert.Equal(pv, result.PeakToValley, 12);
        var scaled = pupil.Select(p => p with { Weight = p.Weight * 1e200 }).ToArray();
        Assert.Equal(result.Rms, WavefrontStatistics.Measure(samples, scaled, reference).Rms, 12);
        Assert.Equal(result.Rms, RmsScanSupport.WeightedWavefrontRms(samples, pupil,
            reference == WavefrontReferenceKind.Centroid ? "centroid" : "chief"), 12);
    }

    [Fact]
    public void CollinearPupilRemovesObservableTiltWithoutInventingAPlane()
    {
        PupilSample[] pupil = [new(-.5, -1, 1), new(0, 0, 2), new(.5, 1, 3)];
        var samples = pupil.Select(p => new WavefrontSample(p.X, p.Y, 0, 0, 0, 5 + 3 * p.X, 1)).ToArray();
        var result = WavefrontStatistics.Measure(samples, pupil, WavefrontReferenceKind.Centroid);
        Assert.InRange(result.Rms, 0, 1e-12); Assert.InRange(result.PeakToValley, 0, 1e-12);
    }

    [Fact]
    public void StatisticsRejectBadInputsButIgnoreZeroIntensitySamples()
    {
        PupilSample[] pupil = [new(0, 0, 1), new(.5, 0, 1)];
        WavefrontSample[] samples = [new(0, 0, 0, 0, 0, 1, 1), new(.5, 0, 0, 0, 0, double.NaN, 0)];
        Assert.Equal(1, WavefrontStatistics.Measure(samples, pupil, WavefrontReferenceKind.ChiefRay).SampleCount);
        Assert.Throws<ArgumentException>(() => WavefrontStatistics.Measure(samples, [], WavefrontReferenceKind.ChiefRay));
        samples[1] = samples[1] with { Intensity = 1 };
        Assert.Throws<InvalidOperationException>(() => WavefrontStatistics.Measure(samples, pupil, WavefrontReferenceKind.ChiefRay));
        samples[1] = samples[1] with { OpdWaves = 0 }; pupil[1] = pupil[1] with { Weight = -1 };
        Assert.Throws<InvalidOperationException>(() => WavefrontStatistics.Measure(samples, pupil, WavefrontReferenceKind.ChiefRay));
    }

    [Fact]
    public void ExplicitZeroFieldRemainsAxialAndGeometryEditsInvalidateTraceResults()
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row("RWRE"); var original = Evaluate(optic, row);
        row.Field = optic.Fields.Count; Value(optic, row, original);
        row.ZemaxDataParameters[1] = .7;
        Assert.True(Math.Abs(Evaluate(optic, row) - original) > 1e-4);
        row.ZemaxDataParameters[1] = 0;
        optic.SurfaceGroup.Items[^2].Thickness += 3; optic.SurfaceGroup.Renumber();
        Assert.True(Math.Abs(Evaluate(optic, row) - original) > .01);
        optic.SurfaceGroup.Items[^2].Thickness -= 3; optic.SurfaceGroup.Renumber(); Value(optic, row, original);
    }

    [Fact]
    public void PrimaryWavelengthDefinesChiefReferenceEvenForAnotherSelectedWave()
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row("RWRH"); row.ZemaxDataParameters[1] = .7;
        var wavelength = optic.Wavelengths[0]; var primary = optic.Wavelengths.First(w => w.IsPrimary);
        var pupil = Pupil(row.Type);
        var wavefront = WavefrontEngine.GenerateChiefRaySamples(optic, (0, .7), wavelength,
            pupil.Select(p => (p.X, p.Y)).ToArray(), aimAtStop: optic.RayAimingEnabled, referenceWavelength: primary);
        Value(optic, row, WavefrontStatistics.Measure(wavefront.Samples, pupil, WavefrontReferenceKind.ChiefRay).Rms);
        var old = Evaluate(optic, row); primary.IsPrimary = false; wavelength.IsPrimary = true;
        Assert.True(Math.Abs(Evaluate(optic, row) - old) > 1e-6);
    }

    [Fact]
    public void FormalDlsCanOptimizeAnRmsWavefrontOperand()
    {
        var optic = Optic.CreateCookeTriplet();
        foreach (var surface in optic.SurfaceGroup.Items) { surface.RadiusVariable = false; surface.ThicknessVariable = false; }
        optic.SurfaceGroup.Items[^2].Thickness += 2; optic.SurfaceGroup.Items[^2].ThicknessVariable = true;
        optic.SurfaceGroup.Renumber(); optic.MeritFunctionOperands.Clear(); optic.MeritFunctionOperands.Add(Row("RWRE"));
        var runtime = new WorkbenchRuntime(optic); var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 35);
        Assert.True(result.FinalMerit < result.InitialMerit * .1);
    }

    private static Optic Plane()
    {
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity, Geometry = new PlaneGeometry(), MaterialAfter = new AirMaterial(), SemiDiameter = 50 },
            new OpticalSurface { Thickness = 20, Geometry = new PlaneGeometry(), MaterialAfter = new AirMaterial(), SemiDiameter = 50, IsStop = true },
            new OpticalSurface { Thickness = 0, Geometry = new PlaneGeometry(), MaterialAfter = new AirMaterial(), SemiDiameter = 50 }
        ]);
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter; optic.Aperture.Value = 4;
        optic.Fields.Clear(); optic.Fields.Add(new FieldPoint { Y = 5 });
        optic.Wavelengths.Clear(); optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        return optic;
    }
    private static IReadOnlyList<PupilSample> Pupil(string code) => code[2] == 'C'
        ? ApertureSampler.GenerateGaussianQuadrature(3, 6) : RayBundleMetrics.RectangularPupil(7);
    private static MeritOperandDefinition Row(string code) => new()
    {
        Type = code,
        Surface = 3,
        Wavelength = 1,
        Field = 1,
        Weight = 2,
        ZemaxIntegerParameters = [3, 1],
        ZemaxDataParameters = [0, 0, 0, 0]
    };
    private static double Evaluate(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.True(string.IsNullOrEmpty(result.Error), result.Error); return result.Value;
    }
    private static void Value(Optic optic, MeritOperandDefinition row, double expected, int precision = 9)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.True(string.IsNullOrEmpty(result.Error), result.Error);
        Assert.Equal(expected, result.Value, precision);
        Assert.Equal(row.Weight * Math.Pow(result.Value - row.Target, 2), result.Contribution, 8);
    }
    private static void Invalid(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.False(string.IsNullOrEmpty(result.Error)); Assert.True(double.IsPositiveInfinity(result.Contribution));
    }
}
