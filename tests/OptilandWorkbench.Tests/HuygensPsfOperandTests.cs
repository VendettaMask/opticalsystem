using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class HuygensPsfOperandTests
{
    public static TheoryData<string> Codes => new("STRH", "CEHX", "CEHY");

    [Theory]
    [InlineData("STRH", 1)]
    [InlineData("STRH", 2)]
    [InlineData("CEHX", 1)]
    [InlineData("CEHX", 2)]
    [InlineData("CEHY", 1)]
    [InlineData("CEHY", 2)]
    public void MonochromaticValueMatchesIndependentMomentsOfSharedPsf(string code, int field)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(code, field: field);
        var expected = Reference(optic, code, 1, field);
        Assert.Equal(expected, Evaluate(optic, row), 10);
        if (code == "CEHY" && field == 2) Assert.True(Math.Abs(expected) > 1); // Absolute image position, not PSF offset.
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void PolychromaticIntensityCombinationIsInvariantToCommonWeightScale(string code)
    {
        var optic = Optic.CreateCookeTriplet(); optic.Wavelengths[0].Weight = 2; optic.Wavelengths[1].Weight = 3; optic.Wavelengths[2].Weight = 0;
        var weights = optic.Wavelengths.Select(w => w.Weight).ToArray();
        var row = Row(code, wave: 0, field: 2); var expected = Reference(optic, code, 0, 2);
        Assert.Equal(expected, Evaluate(optic, row), 10);
        Assert.Equal(weights, optic.Wavelengths.Select(w => w.Weight));
        foreach (var wavelength in optic.Wavelengths) wavelength.Weight *= 1e200;
        Assert.Equal(expected, Evaluate(optic, row), 10);
    }

    [Fact]
    public void StrehlUsesHalfDefaultDeltaAndPeakRatherThanCenterSample()
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row("STRH", field: 2);
        var field = FieldCoordinates.Normalize(optic.Fields, optic.Fields[1].X, optic.Fields[1].Y);
        var delta = DiffractionEngine.DefaultHuygensImageDeltaMillimeters(optic, field, optic.Wavelengths[0], 32);
        var expected = DiffractionEngine.ComputeHuygensPsf(optic, field, optic.Wavelengths[0], 32, 32, delta / 2,
            aimAtStop: optic.RayAimingEnabled);
        var measured = HuygensPsfMetrics.Evaluate(optic, 1, 2, 1, 1, strehlSampling: true);
        Assert.Equal(delta / 2, measured.ImageDeltaMillimeters);
        Assert.Equal(expected.PeakStrehlRatio, Evaluate(optic, row), 12);
        Assert.True(expected.PeakStrehlRatio >= expected.StrehlRatio);
        Assert.True(Math.Abs(expected.PeakStrehlRatio - expected.StrehlRatio) > 1e-8);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void SixSlotZmxAndStaroptRoundTripPreserveParameterMeaning(string code)
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX"));
        var slots = code == "STRH" ? "1 1 2 0 0 0" : "1 2 0 1 1 0";
        var optic = OpticalFormatCatalog.Import(text + $"\n{code} {slots} .625 2.5 0 0\n", ".zmx");
        var imported = optic.MeritFunctionOperands.Last();
        Assert.Equal(code, imported.Type); Assert.False(imported.CompatibilityOnly); Assert.True(imported.Enabled);
        Assert.Equal(.625, imported.Target); Assert.Equal(2.5, imported.Weight);
        var expected = Evaluate(optic, imported);
        imported.Enabled = false; imported.CompatibilityOnly = true;
        var restored = Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands.Last();
        Assert.False(restored.Enabled); Assert.False(restored.CompatibilityOnly);
        Assert.Equal(imported.ZemaxIntegerParameters, restored.ZemaxIntegerParameters);
        Assert.Equal(imported.ZemaxDataParameters, restored.ZemaxDataParameters);
        restored.Enabled = true; Assert.Equal(expected, Evaluate(optic, restored), 10);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task NativeProjectAndEditorKeepAllActiveSlots(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var original = Row(code, wave: 0, field: 2);
        optic.MeritFunctionOperands.Add(original);
        var path = Path.Combine(Path.GetTempPath(), $"psf-operands-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var restored = Assert.Single((await StarOptProjectStore.LoadAsync(path)).Configurations[0].MeritFunctionOperands);
            Assert.Equal(original.ZemaxIntegerParameters, restored.ZemaxIntegerParameters);
            Assert.Equal(original.ZemaxDataParameters, restored.ZemaxDataParameters);
        }
        finally { File.Delete(path); }
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, false, code, 9, 2, 0, 0, 0, 0, 0, .625, 2.5, 0, 0, "PSF",
            ZemaxInt1: original.ZemaxIntegerParameters[0], ZemaxInt2: original.ZemaxIntegerParameters[1],
            ZemaxData1: original.ZemaxDataParameters[0], ZemaxData2: original.ZemaxDataParameters[1],
            ZemaxData3: original.ZemaxDataParameters[2], ZemaxData4: original.ZemaxDataParameters[3])]);
        var type = app.Optimization.GetMeritOperandTypes().Single(t => t.Code == code);
        Assert.False(type.CompatibilityOnly); Assert.Contains("Pol=0", type.Calculation);
        Assert.Equal(code == "STRH" ? 5 : 6, type.Parameters!.Count(p => p.IsEditable));
        var editor = new MeritOperandEditorRow(Assert.Single(app.Optimization.GetMeritFunction()), type);
        if (code == "STRH") editor.Parameter1 = 2; else editor.Parameter5 = 2;
        app.Optimization.SetMeritFunction([editor.ToDto()]);
        var saved = Assert.Single(app.Optimization.GetMeritFunction());
        Assert.Equal(2, code == "STRH" ? saved.ZemaxInt1 : saved.ZemaxData3);
        Assert.Equal(2, saved.Field); Assert.Equal(0, saved.Wavelength);
        Assert.Equal(.625, saved.Target); Assert.Equal(2.5, saved.Weight);
    }

    [Theory]
    [InlineData("STRH", -1, 1)]
    [InlineData("STRH", 99, 1)]
    [InlineData("STRH", 1, -1)]
    [InlineData("STRH", 1, 99)]
    [InlineData("CEHX", -1, 1)]
    [InlineData("CEHX", 99, 1)]
    [InlineData("CEHX", 1, -1)]
    [InlineData("CEHX", 1, 99)]
    [InlineData("CEHY", -1, 1)]
    [InlineData("CEHY", 99, 1)]
    [InlineData("CEHY", 1, -1)]
    [InlineData("CEHY", 1, 99)]
    public void InvalidReferencesFailAndCompatibilityRowsDoNotUpgrade(string code, int wave, int field)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(code, wave, field);
        Invalid(optic, row); row.Enabled = false; row.CompatibilityOnly = true; optic.MeritFunctionOperands.Add(row);
        Assert.True(Assert.Single(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands).CompatibilityOnly);
    }

    [Theory]
    [InlineData("STRH", 1, 1)]
    [InlineData("STRH", 2, 1)]
    [InlineData("STRH", 0, 0)]
    [InlineData("CEHX", 0, 1)]
    [InlineData("CEHX", 3, 1)]
    [InlineData("CEHX", 1, 0)]
    [InlineData("CEHY", 0, .5)]
    [InlineData("CEHY", 1, 2.5)]
    [InlineData("CEHY", 2, double.NaN)]
    public void InvalidFlagsAndDataSamplingFail(string code, int index, double value)
    {
        var row = Row(code); row.ZemaxDataParameters[index] = value;
        Invalid(Optic.CreateCookeTriplet(), row);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(3, 3)]
    [InlineData(5, 1)]
    [InlineData(1, 5)]
    public void InvalidOrExcessiveSamplingIsRejectedWithoutReduction(int pupil, int image)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HuygensPsfMetrics.Evaluate(Optic.CreateCookeTriplet(), 1, 1, pupil, image));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    [InlineData(1, 3)]
    public void PupilAndImageSamplingCanBeSelectedIndependently(int pupil, int image)
    {
        var optic = Optic.CreateCookeTriplet(); var result = HuygensPsfMetrics.Evaluate(optic, 1, 2, pupil, image);
        var expected = Reference(optic, "CEHY", 1, 2, 32 << (pupil - 1), 32 << (image - 1));
        Assert.Equal(expected, result.CentroidY, 10);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void MissingEnergyAfocalAndCancellationAreExplicit(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(code);
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel(); using var scope = ComputationCancellation.Push(cancellation.Token);
            Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(optic, row));
        }
        optic.ImageSpaceAfocal = true; Invalid(optic, row); optic.ImageSpaceAfocal = false;
        optic.SurfaceGroup.Items[1].PhysicalAperture = new RectangularAperture(.1, .1, 100, 100);
        Invalid(optic, row);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void ZeroSpectralWeightsFailForPolychromaticButNotSelectedMonochromatic(string code)
    {
        var optic = Optic.CreateCookeTriplet(); foreach (var w in optic.Wavelengths) w.Weight = 0;
        Invalid(optic, Row(code, wave: 0));
        Assert.True(double.IsFinite(Evaluate(optic, Row(code, wave: 1))));
    }

    [Fact]
    public void SharedImageFrameIsTangentToTiltedImageAndCentroidUsesLocalCoordinates()
    {
        var optic = Optic.CreateCookeTriplet(); var image = optic.SurfaceGroup.Items[^1];
        image.CoordinateSystem = new CoordinateSystem(image.CoordinateSystem.Origin + new Vector3D(1, -2, 0), 4, 0, 17);
        var wave = optic.Wavelengths[0]; var frame = DiffractionEngine.CreateHuygensImageFrame(optic, (0, 0), wave);
        Assert.Equal(1, frame.TangentX.Length, 12); Assert.Equal(1, frame.TangentY.Length, 12);
        var lx = image.CoordinateSystem.ToLocalDirection(frame.TangentX);
        var ly = image.CoordinateSystem.ToLocalDirection(frame.TangentY);
        Assert.InRange(Math.Abs(lx.Z) + Math.Abs(ly.Z), 0, 1e-12);
        var result = HuygensPsfMetrics.Evaluate(optic, 1, 1, 1, 1);
        var field = (Hx: 0.0, Hy: 0.0);
        var psf = DiffractionEngine.ComputeHuygensPsf(optic, field, wave, 32, 32, result.ImageDeltaMillimeters);
        var (dx, dy) = Moments(psf.Values);
        var center = image.CoordinateSystem.ToLocalPoint(frame.Center);
        Assert.Equal(center.X + dx * result.ImageDeltaMillimeters, result.CentroidX, 10);
        Assert.Equal(center.Y + dy * result.ImageDeltaMillimeters, result.CentroidY, 10);
        Assert.True(Math.Abs(result.CentroidX) > .1); Assert.True(Math.Abs(result.CentroidY) > .1);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void GeometryAndApodizationChangesRecomputeRatherThanReuseStalePsf(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(code, field: 2);
        if (code == "CEHX") { optic.Fields[1].X = 2; optic.Fields[1].Y = 0; }
        var before = Evaluate(optic, row); var thickness = optic.SurfaceGroup.Items[^2].Thickness;
        optic.SurfaceGroup.Items[^2].Thickness += 1; optic.SurfaceGroup.Renumber();
        var changed = Evaluate(optic, row); Assert.True(Math.Abs(before - changed) > 1e-8);
        optic.SurfaceGroup.Items[^2].Thickness = thickness; optic.SurfaceGroup.Renumber();
        Assert.Equal(before, Evaluate(optic, row), 10);
        optic.Apodization = new GaussianApodization(.3);
        Assert.True(Math.Abs(before - Evaluate(optic, row)) > 1e-8);
    }

    private static double Reference(Optic optic, string code, int wave, int fieldNumber, int pupilSize = 32, int imageSize = 32)
    {
        var wavelengths = wave == 0 ? optic.Wavelengths.Where(w => w.Weight > 0).ToArray() : new[] { optic.Wavelengths[wave - 1] };
        var field = FieldCoordinates.Normalize(optic.Fields, optic.Fields[fieldNumber - 1].X, optic.Fields[fieldNumber - 1].Y);
        var reference = wavelengths.FirstOrDefault(w => w.IsPrimary) ?? wavelengths[0];
        var delta = DiffractionEngine.DefaultHuygensImageDeltaMillimeters(optic, field, wavelengths.MaxBy(w => w.Nanometers)!, pupilSize);
        if (code == "STRH") delta /= 2;
        var grid = new double[imageSize, imageSize]; var totalWeight = 0.0;
        foreach (var wavelength in wavelengths)
        {
            var weight = (wave == 0 ? wavelength.Weight : 1) / (wavelength.Nanometers * wavelength.Nanometers);
            totalWeight += weight;
            var psf = DiffractionEngine.ComputeHuygensPsf(optic, field, wavelength, pupilSize, imageSize, delta,
                aimAtStop: optic.RayAimingEnabled, referenceWavelength: reference);
            for (var y = 0; y < imageSize; y++)
                for (var x = 0; x < imageSize; x++) grid[y, x] += weight * psf.Values[y, x];
        }
        if (code == "STRH") return grid.Cast<double>().Max() / (100 * totalWeight);
        var (dx, dy) = Moments(grid);
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(field.X, field.Y, 0, 0,
            reference.Micrometers, aimAtStop: optic.RayAimingEnabled);
        var chief = optic.SequentialRayTracer.TraceFinalSamples(bundle).Single()!;
        var center = optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalPoint(chief.Position);
        return code == "CEHX" ? center.X + dx * delta : center.Y + dy * delta;
    }
    private static (double X, double Y) Moments(double[,] grid)
    {
        var energy = 0.0; var xSum = 0.0; var ySum = 0.0; var size = grid.GetLength(0);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++) { var value = grid[y, x]; energy += value; xSum += value * x; ySum += value * y; }
        return (xSum / energy - size / 2, ySum / energy - size / 2);
    }
    private static MeritOperandDefinition Row(string code, int wave = 1, int field = 1) => new()
    {
        Type = code,
        Field = field,
        Wavelength = wave,
        Target = .625,
        Weight = 2.5,
        ZemaxIntegerParameters = code == "STRH" ? [1, wave] : [wave, field],
        ZemaxDataParameters = code == "STRH" ? [field, 0, 0, 0] : [0, 1, 1, 0]
    };
    private static double Evaluate(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.True(string.IsNullOrEmpty(result.Error), result.Error);
        Assert.Equal(row.Weight * Math.Pow(result.Value - row.Target, 2), result.Contribution, 8);
        return result.Value;
    }
    private static void Invalid(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.NotEmpty(result.Error);
        Assert.True(double.IsPositiveInfinity(result.Contribution));
    }
}
