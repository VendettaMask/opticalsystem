using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Tests;

public sealed class RayConstraintOperandTests
{
    public static TheoryData<string> Ranges => new("MNRI", "MNRE", "MXRI", "MXRE");
    public static TheoryData<string> Working => new("SFNO", "TFNO");

    [Theory]
    [MemberData(nameof(Ranges))]
    public void RangeBoundaryAccumulatesEverySnellAngleViolation(string code)
    {
        var row = Range(code); row.Target = code.StartsWith("MX") ? 10 : 40;
        var refracted = Math.Asin(.5 / 1.5) * 180 / Math.PI;
        var expected = code.StartsWith("MX") ? 30 + refracted - 10 : 30 + refracted - 40;
        Value(Plate(), row, expected);
        row.Target = code.StartsWith("MX") ? 40 : 10;
        Value(Plate(), row, row.Target);
    }

    [Theory]
    [InlineData("MNRI", 1, 30)]
    [InlineData("MXRI", 1, 30)]
    [InlineData("MNRE", 2, 30)]
    [InlineData("MXRE", 2, 30)]
    public void SingleSurfaceBoundaryReturnsActualViolatedAngle(string code, int surface, double expected)
    {
        var row = Range(code); row.ZemaxIntegerParameters = [surface, surface];
        row.Target = code.StartsWith("MX") ? 0 : 90;
        Value(Plate(), row, expected);
    }

    [Theory]
    [MemberData(nameof(Ranges))]
    public void SeventhParameterChangesTheTracedRayAndSurvivesNativeRoundTrip(string code)
    {
        var optic = Plate(); var row = Range(code); row.ZemaxIntegerParameters = [1, 1];
        row.ZemaxDataParameters = [1, 0, 0, 0, .5]; row.Target = code.StartsWith("MX") ? 0 : 90;
        optic.SurfaceGroup.Items[1].Geometry = new StandardGeometry(50);
        var first = Evaluate(optic, row); row.ZemaxDataParameters[4] = .8;
        var second = Evaluate(optic, row); Assert.True(Math.Abs(first - second) > .1);
        optic.MeritFunctionOperands.Add(row);
        var restored = Optic.FromSnapshot(optic.ToSnapshot());
        Assert.Equal(row.ZemaxDataParameters, restored.MeritFunctionOperands[0].ZemaxDataParameters);
        Assert.Equal(second, Evaluate(restored, restored.MeritFunctionOperands[0]), 10);
    }

    [Theory]
    [MemberData(nameof(Ranges))]
    public async Task StaroptAndDisabledCompatibilityUpgradePreserveFullSevenParameters(string code)
    {
        var optic = Plate(); var row = Range(code); row.Enabled = false; row.CompatibilityOnly = true;
        optic.MeritFunctionOperands.Add(row);
        var path = Path.Combine(Path.GetTempPath(), $"ray-constraint-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var restored = Assert.Single((await StarOptProjectStore.LoadAsync(path)).Configurations[0].MeritFunctionOperands);
            Assert.Equal(row.ZemaxDataParameters, restored.ZemaxDataParameters);
            Assert.False(restored.Enabled); Assert.False(restored.CompatibilityOnly);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [MemberData(nameof(Ranges))]
    public void UnverifiedNativeExtendedTextStaysReadOnlyWithExactOriginalTail(string code)
    {
        var line = $"{code} 1 2 1 0 1 0 35 2 91 92 .375 .625";
        var optic = OpticalFormatCatalog.Import(Source(line), ".zmx");
        var row = Assert.Single(optic.MeritFunctionOperands);
        Assert.True(row.CompatibilityOnly); Assert.False(row.Enabled);
        Assert.EndsWith("91 92 .375 .625", row.Comment);
        Assert.Equal(4, row.ZemaxDataParameters.Length);
        var restored = Assert.Single(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands);
        Assert.True(restored.CompatibilityOnly); Assert.Equal(row.Comment, restored.Comment);
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, false, code, 1, 0, 2, 1, 0, 1, 0, 35, 2, 0, 0, row.Comment,
            CompatibilityOnly: true, ZemaxInt1: 1, ZemaxInt2: 2, ZemaxData1: 1, ZemaxData2: 0, ZemaxData3: 1, ZemaxData4: 0)]);
        var dto = Assert.Single(app.Optimization.GetMeritFunction());
        var type = Assert.Single(app.Optimization.GetMeritOperandTypes(), t => t.Code == code);
        var editor = new MeritOperandEditorRow(dto, type);
        Assert.True(editor.CompatibilityOnly); Assert.False(editor.IsParameterEditable(6));
        app.Optimization.SetMeritFunction([editor.ToDto()]);
        var saved = Assert.Single(app.Optimization.GetMeritFunction());
        Assert.True(saved.CompatibilityOnly); Assert.Null(saved.ZemaxData5); Assert.Equal(row.Comment, saved.Comment);
    }

    [Theory]
    [MemberData(nameof(Ranges))]
    public void EditorPublishesAndEditsAllSevenTypedSlotsWithoutMovingTarget(string code)
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, true, code, 1, 0, 2, 0, 0, 0, 0, 70, 2, 0, 0, "seven",
            ZemaxInt1: 1, ZemaxInt2: 2, ZemaxData1: 1, ZemaxData2: .1, ZemaxData3: .2, ZemaxData4: .3, ZemaxData5: .4)]);
        var dto = Assert.Single(app.Optimization.GetMeritFunction());
        var type = Assert.Single(app.Optimization.GetMeritOperandTypes(), t => t.Code == code);
        Assert.False(type.CompatibilityOnly); Assert.Equal(7, type.Parameters!.Count);
        Assert.Equal("Wavelength", type.Parameters[2].ValueKind);
        var editor = new MeritOperandEditorRow(dto, type);
        Assert.True(editor.HasZemaxParameters); Assert.True(editor.IsParameterEditable(6)); Assert.Equal("Py", editor.ParameterLabel(6));
        Assert.False(editor.IsParameterEditable(7)); Assert.False(editor.IsParameterEditable(-1));
        editor.Parameter7 = .6; app.Optimization.SetMeritFunction([editor.ToDto()]);
        var saved = Assert.Single(app.Optimization.GetMeritFunction());
        Assert.Equal(.6, saved.ZemaxData5); Assert.Equal(70, saved.Target); Assert.Equal(2, saved.Weight);
        Assert.Equal(1, saved.Wavelength); Assert.Equal(2, saved.ZemaxInt2); Assert.Equal(.6, saved.Py);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 1)]
    [InlineData(1, 999)]
    [InlineData(-1, 2)]
    public void InvalidOrInfiniteSurfaceRangeIsNotSuccessfulZero(int first, int last)
    {
        var row = Range("MNRI"); row.ZemaxIntegerParameters = [first, last]; Invalid(Plate(), row);
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(0, 99)]
    [InlineData(0, .5)]
    [InlineData(1, 1.1)]
    [InlineData(2, -1.1)]
    [InlineData(3, 1.1)]
    [InlineData(4, double.NaN)]
    [InlineData(4, double.PositiveInfinity)]
    public void InvalidRawParametersAreErrors(int index, double value)
    {
        var row = Range("MXRE"); row.ZemaxDataParameters[index] = value; Invalid(Plate(), row);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void InvalidRawReferencesCannotUpgradeOrPassSnapshotValidation(int problem)
    {
        var optic = Plate(); var row = Range("MNRI");
        if (problem == 0) row.ZemaxIntegerParameters[0] = 999;
        if (problem == 1) row.ZemaxIntegerParameters[1] = 999;
        if (problem == 2) row.ZemaxDataParameters[0] = 999;
        if (problem == 3) row.ZemaxDataParameters = [1, 0, 1, 0];
        optic.MeritFunctionOperands.Add(row);
        var error = Assert.Throws<InvalidDataException>(() => OpticSnapshotValidator.Validate(optic.ToSnapshot()));
        if (problem < 2) Assert.Contains("999", error.Message);
        row.CompatibilityOnly = true;
        Assert.True(Assert.Single(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands).CompatibilityOnly);
    }

    [Theory]
    [MemberData(nameof(Ranges))]
    public void VignettedRayAndCancellationNeverProduceAValidRange(string code)
    {
        var optic = Plate(); optic.SurfaceGroup.Items[2].PhysicalAperture = new CircularAperture(.01);
        Invalid(optic, Range(code));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        using var scope = ComputationCancellation.Push(cancel.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(optic, Range(code)));
    }

    [Theory]
    [MemberData(nameof(Ranges))]
    public void ZeroWavelengthMeansPrimaryAndMaterialMutationInvalidatesTrace(string code)
    {
        var optic = Plate(); var row = Range(code); row.Target = code.StartsWith("MX") ? 0 : 90;
        var before = Evaluate(optic, row); row.ZemaxDataParameters[0] = 0;
        Assert.Equal(before, Evaluate(optic, row), 12);
        optic.SurfaceGroup.Items[1].MaterialAfter = new ConstantIndexMaterial("Changed", 1.7);
        Assert.True(Math.Abs(before - Evaluate(optic, row)) > .1);
    }

    [Theory]
    [MemberData(nameof(Ranges))]
    public void FoldedMirrorAnglesUseRecordedPropagationDirections(string code)
    {
        var optic = Plate(); var mirror = optic.SurfaceGroup.Items[1];
        mirror.IsReflective = true; mirror.Thickness = -10; mirror.MaterialAfter = new AirMaterial();
        optic.SurfaceGroup.Items[2].Thickness = -20; optic.SurfaceGroup.Renumber();
        var row = Range(code); row.Target = code.StartsWith("MX") ? 0 : 90;
        Value(optic, row, code.StartsWith("MX") ? 60 : -30);
    }

    [Theory]
    [InlineData("SFNO", 0)]
    [InlineData("TFNO", 0)]
    [InlineData("SFNO", 1)]
    [InlineData("TFNO", 1)]
    public void WorkingFNumberMatchesIndependentThinLensDirections(string code, int field)
    {
        var optic = Lens(); var row = WorkingRow(code, field);
        var t = field == 0 ? 0 : Math.Tan(Math.PI / 6); const double p = .1;
        var angles = code == "TFNO"
            ? new[] { Math.Atan(t + p) - Math.Atan(t), Math.Atan(t) - Math.Atan(t - p) }
            : new[] { Math.Atan(p / Math.Sqrt(1 + t * t)), Math.Atan(p / Math.Sqrt(1 + t * t)) };
        var expected = 1 / (angles.Sum(Math.Sin));
        Value(optic, row, expected);
    }

    [Theory]
    [MemberData(nameof(Working))]
    public void RealWorkingFNumberRespondsToFieldAndVignettingButIgnoresPhysicalAperture(string code)
    {
        var optic = Lens(); var row = WorkingRow(code, 1); var initial = Evaluate(optic, row);
        row.ZemaxIntegerParameters[0] = 0; var axial = Evaluate(optic, row);
        Assert.True(Math.Abs(initial - axial) > .1);
        row.ZemaxIntegerParameters[0] = 1; optic.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(.01);
        Assert.Equal(initial, Evaluate(optic, row), 10);
        optic.Fields[0].VignetteFactorX = .2; optic.Fields[0].VignetteFactorY = .2;
        Assert.True(Evaluate(optic, row) > initial);
    }

    [Theory]
    [MemberData(nameof(Working))]
    public void WorkingFNumberRejectsAfocalWrongAzimuthParallelAndInvalidReferences(string code)
    {
        var row = WorkingRow(code, 1); var optic = Lens(); optic.ImageSpaceAfocal = true; Invalid(optic, row);
        optic.ImageSpaceAfocal = false; optic.Fields[0].X = 10; Invalid(optic, row);
        Invalid(Plate(), row);
        optic = Lens(); row.ZemaxIntegerParameters[0] = 999; Invalid(optic, row);
        row.ZemaxIntegerParameters = [0, 999]; Invalid(optic, row);
    }

    [Theory]
    [MemberData(nameof(Working))]
    public void WorkingFNumberImportAndEditorUseFieldInInt1(string code)
    {
        var optic = OpticalFormatCatalog.Import(Source($"{code} 1 1 0 0 0 0 5.5 2 0 0"), ".zmx");
        var imported = Assert.Single(optic.MeritFunctionOperands);
        Assert.False(imported.CompatibilityOnly); Assert.True(imported.Enabled); Assert.Equal(1, imported.Field);
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, true, code, 0, 0, 0, 0, 0, 0, 0, 5.5, 2, 0, 0, "", ZemaxInt1: 2, ZemaxInt2: 1)]);
        var dto = Assert.Single(app.Optimization.GetMeritFunction()); Assert.Empty(dto.Error); Assert.Equal(2, dto.Field);
        var type = Assert.Single(app.Optimization.GetMeritOperandTypes(), t => t.Code == code);
        var editor = new MeritOperandEditorRow(dto, type); Assert.Contains("Field", editor.ParameterLabel(0));
        app.Optimization.SetMeritFunction([editor.ToDto()]); Assert.Equal(dto.Value, Assert.Single(app.Optimization.GetMeritFunction()).Value, 10);
    }

    [Theory]
    [MemberData(nameof(Working))]
    public void WorkingFNumberPrimaryWaveAndSnapshotRoundTripAgree(string code)
    {
        var optic = Optic.CreateCookeTriplet(); foreach (var w in optic.Wavelengths) w.IsPrimary = false;
        optic.Wavelengths[1].IsPrimary = true;
        var row = WorkingRow(code, 2); row.ZemaxIntegerParameters[1] = 2;
        var expected = Evaluate(optic, row); row.ZemaxIntegerParameters[1] = 0;
        Value(optic, row, expected);
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); using var scope = ComputationCancellation.Push(cancel.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(optic, row));
    }

    [Theory]
    [MemberData(nameof(Working))]
    public void StrictWorkingFNumberDoesNotCapLargeFiniteResults(string code)
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].InteractionModel = new ThinLensInteractionModel(200_000);
        var value = Evaluate(optic, WorkingRow(code, 0)); Assert.InRange(value, 19_990, 20_010);
    }

    [Theory]
    [MemberData(nameof(Working))]
    public void StrictWorkingFNumberDoesNotReplaceFailedFullPupilWithSmallerZone(string code)
    {
        var optic = Lens(); optic.Aperture.Value = 25;
        optic.SurfaceGroup.Items[1].Geometry = new StandardGeometry(10);
        Invalid(optic, WorkingRow(code, 0));
        var fallback = DiffractionEngine.WorkingFNumbers(optic, (0, 0), optic.Wavelengths[0]);
        Assert.True(double.IsFinite(fallback.Sagittal));
    }

    [Theory]
    [MemberData(nameof(Ranges))]
    public void TiltedSurfaceMeasuresAngleAgainstItsLocalNormal(string code)
    {
        var optic = Plate(); var surface = optic.SurfaceGroup.Items[1];
        surface.CoordinateSystem = surface.CoordinateSystem with { RotationYDegrees = 15 };
        var row = Range(code); row.ZemaxIntegerParameters = [1, 1]; row.ZemaxDataParameters = [1, 0, 0, 0, 0];
        row.Target = code.StartsWith("MX") ? 0 : 90;
        var expected = code.EndsWith("I") ? 15 : Math.Asin(Math.Sin(Math.PI / 12) / 1.5) * 180 / Math.PI;
        Value(optic, row, expected);
    }

    [Fact]
    public void RangeConstraintDrivesFormalDampedLeastSquares()
    {
        var optic = Plate(); optic.SurfaceGroup.Items[1].Geometry = new StandardGeometry(50);
        optic.SurfaceGroup.Items[1].RadiusVariable = true;
        var row = Range("MXRI"); row.ZemaxIntegerParameters = [1, 1];
        row.ZemaxDataParameters = [1, 0, 0, 0, 1]; row.Target = 3;
        optic.MeritFunctionOperands.Add(row);
        var initial = MeritFunctionCatalog.Evaluate(optic, row).Contribution;
        var runtime = new WorkbenchRuntime(optic); runtime.OptimizeMarkedVariables("Damped Least Squares", 60);
        var final = MeritFunctionCatalog.Evaluate(runtime.CurrentOptic, runtime.CurrentOptic.MeritFunctionOperands[0]);
        Assert.Empty(final.Error); Assert.True(final.Contribution < initial * .01);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task TruncatedLegacyRawArraysDoNotAcquireData5FromEditorDisplay(int length)
    {
        var optic = Plate(); var row = Range("MXRI");
        row.Enabled = false; row.CompatibilityOnly = true;
        row.ZemaxDataParameters = row.ZemaxDataParameters.Take(length).ToArray(); optic.MeritFunctionOperands.Add(row);
        var path = Path.Combine(Path.GetTempPath(), $"truncated-angles-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            using var app = WorkbenchApplication.Create("blank"); await app.Documents.OpenAsync(path);
            var dto = Assert.Single(app.Optimization.GetMeritFunction());
            Assert.True(dto.CompatibilityOnly); Assert.Null(dto.ZemaxData5);
            var type = Assert.Single(app.Optimization.GetMeritOperandTypes(), t => t.Code == "MXRI");
            var editor = new MeritOperandEditorRow(dto, type);
            app.Optimization.SetMeritFunction([editor.ToDto()]);
            var saved = Assert.Single(app.Optimization.GetMeritFunction());
            Assert.True(saved.CompatibilityOnly); Assert.Null(saved.ZemaxData5);
        }
        finally { File.Delete(path); }
    }

    private static Optic Plate()
    {
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity, Geometry = new PlaneGeometry(), SemiDiameter = 100 },
            new OpticalSurface { Thickness = 10, Geometry = new PlaneGeometry(), SemiDiameter = 100, IsStop = true, MaterialAfter = new ConstantIndexMaterial("Glass", 1.5) },
            new OpticalSurface { Thickness = 30, Geometry = new PlaneGeometry(), SemiDiameter = 100 },
            new OpticalSurface { Thickness = 0, Geometry = new PlaneGeometry(), SemiDiameter = 100 }]);
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter; optic.Aperture.Value = 10;
        optic.Fields.Clear(); optic.Fields.Add(new FieldPoint { Y = 30 });
        optic.Wavelengths.Clear(); optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        return optic;
    }
    private static Optic Lens()
    {
        var optic = Plate(); optic.SurfaceGroup.Items[1].MaterialAfter = new AirMaterial();
        optic.SurfaceGroup.Items[1].InteractionModel = new ThinLensInteractionModel(50); return optic;
    }
    private static MeritOperandDefinition Range(string code) => new()
    {
        Type = code,
        Surface = 1,
        Wavelength = 2,
        ZemaxIntegerParameters = [1, 2],
        ZemaxDataParameters = [1, 0, 1, 0, 0],
        Target = 0,
        Weight = 2
    };
    private static MeritOperandDefinition WorkingRow(string code, int field) => new()
    {
        Type = code,
        Field = field,
        Wavelength = 1,
        ZemaxIntegerParameters = [field, 1],
        ZemaxDataParameters = [0, 0, 0, 0]
    };
    private static string Source(string row) => $"""
        MODE SEQ
        ENPD 10
        FTYP 0 0 1 1 0 0 0
        XFLN 0
        YFLN 30
        WAVM 1 .55 1
        SURF 0
          DISZ INFINITY
        SURF 1
          STOP
          DISZ 10
        SURF 2
          DISZ 0
        {row}
        """;
    private static double Evaluate(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.True(string.IsNullOrEmpty(result.Error), result.Error);
        Assert.True(double.IsFinite(result.Value)); return result.Value;
    }
    private static void Value(Optic optic, MeritOperandDefinition row, double expected)
    {
        Assert.Equal(expected, Evaluate(optic, row), 8);
        Assert.Equal(expected, Evaluate(Optic.FromSnapshot(optic.ToSnapshot()), row.Clone()), 8);
    }
    private static void Invalid(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.NotEmpty(result.Error); Assert.True(double.IsPositiveInfinity(result.Contribution));
    }
}
