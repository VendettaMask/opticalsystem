using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class SurfacePointOperandTests
{
    public static TheoryData<string> Codes => new("SSAG", "SSLP", "SCRV");
    private static Optic Lens()
    {
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity, SemiDiameter = 5 },
            new OpticalSurface { Geometry = new StandardGeometry(100), Thickness = 10, SemiDiameter = 5, IsStop = true },
            new OpticalSurface { Thickness = 0, SemiDiameter = 5 }
        ]);
        return optic;
    }
    private static MeritOperandDefinition Row(string code, int orientation = 0, int remove = 0) => new()
    {
        Type = code,
        Surface = 1,
        ZemaxIntegerParameters = [1, 1],
        ZemaxDataParameters = code == "SSAG" ? [3, 4, 0, remove, 0] : [3, 4, 0, remove, 0, orientation]
    };
    private static double Value(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.Empty(result.Error); return result.Value;
    }
    private static void Invalid(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.NotEmpty(result.Error);
        Assert.True(double.IsNaN(result.Value)); Assert.True(double.IsPositiveInfinity(result.Contribution));
        Assert.Throws<OptimizationEvaluationException>(() => MeritFunctionCatalog.EvaluateOptimizationValues(optic, [row]));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 100)]
    [InlineData(2, 100)]
    [InlineData(3, 100)]
    [InlineData(0, -100)]
    [InlineData(1, -100)]
    [InlineData(2, -100)]
    [InlineData(3, -100)]
    public void SphereHasSignedAnalyticSagSlopeAndNormalCurvature(int orientation, double radius)
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].Radius = radius;
        var root = Math.Sqrt(radius * radius - 25); var sign = Math.Sign(radius);
        Assert.Equal(sign * 25 / (Math.Abs(radius) + root), Value(optic, Row("SSAG")), 12);
        Assert.Equal(sign * (orientation switch { 0 => 5, 1 => 0, 2 => 3, _ => 4 }) / root, Value(optic, Row("SSLP", orientation)), 12);
        Assert.Equal(1 / radius, Value(optic, Row("SCRV", orientation)), 12);
    }

    [Theory]
    [InlineData("SSAG", 0)]
    [InlineData("SSLP", 0)]
    [InlineData("SSLP", 1)]
    [InlineData("SSLP", 2)]
    [InlineData("SSLP", 3)]
    [InlineData("SCRV", 0)]
    [InlineData("SCRV", 1)]
    [InlineData("SCRV", 2)]
    [InlineData("SCRV", 3)]
    public void RemovingBaseSpherePrecedesSlopeAndCurvatureCalculation(string code, int orientation)
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].Geometry = new EvenAsphereGeometry(100, 0, [.01]);
        var expected = code == "SSAG" ? .25 : code == "SSLP"
            ? (orientation switch { 0 => .1, 1 => 0, 2 => .06, _ => .08 })
            : .02 / (Math.Sqrt(1.01) * (1 + Math.Pow(orientation switch { 0 => .1, 1 => 0, 2 => .06, _ => .08 }, 2)));
        Assert.Equal(expected, Value(optic, Row(code, orientation, 1)), 11);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PolynomialDirectionUsesMixedSecondDerivative(int orientation)
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].Geometry = new PolynomialGeometry(new Dictionary<(int, int), double>
        { [(2, 0)] = .03, [(1, 1)] = .02, [(0, 2)] = -.01 });
        var (dx, dy) = orientation switch { 0 => (.6, .8), 1 => (-.8, .6), 2 => (1d, 0d), _ => (0d, 1d) };
        var slope = .26 * dx - .02 * dy;
        var curvature = (.06 * dx * dx + .04 * dx * dy - .02 * dy * dy) / (Math.Sqrt(1 + .26 * .26 + .02 * .02) * (1 + slope * slope));
        Assert.Equal(.35, Value(optic, Row("SSAG")), 12);
        Assert.Equal(slope, Value(optic, Row("SSLP", orientation)), 12);
        Assert.Equal(curvature, Value(optic, Row("SCRV", orientation)), 12);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void CoordinatesAreLocalAndNotClippedOrScaledByDiameterMode(string code)
    {
        var optic = Lens(); var row = Row(code, 2); var expected = Value(optic, row);
        var surface = optic.SurfaceGroup.Items[1]; surface.SemiDiameter = 1; surface.MechanicalSemiDiameter = 20;
        surface.PhysicalAperture = new OffsetRadialAperture(1, 0, 50, 50);
        surface.CoordinateSystem = new CoordinateSystem(new(3, 4, 5), 12, 15, 20);
        Assert.Equal(expected, Value(optic, row), 12);
        row.ZemaxIntegerParameters[1] = 0; Assert.Equal(expected, Value(optic, row), 12);
        row.ZemaxDataParameters[4] = 3; Assert.Equal(expected, Value(optic, row), 12);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void PlaneAndRemovedSphereReturnPhysicalZero(string code)
    {
        var optic = Lens(); Assert.Equal(0, Value(optic, Row(code, 2, 1)));
        optic.SurfaceGroup.Items[1].Geometry = new PlaneGeometry(); Assert.Equal(0, Value(optic, Row(code, 2)));
    }

    [Theory]
    [InlineData("SSAG", "surface")]
    [InlineData("SSLP", "mode")]
    [InlineData("SCRV", "mode")]
    [InlineData("SSAG", "offaxis")]
    [InlineData("SSLP", "offaxis")]
    [InlineData("SCRV", "offaxis")]
    [InlineData("SSAG", "bfs")]
    [InlineData("SSLP", "remove")]
    [InlineData("SCRV", "remove")]
    [InlineData("SSLP", "orientation")]
    [InlineData("SCRV", "orientation")]
    [InlineData("SSAG", "domain")]
    [InlineData("SSLP", "domain")]
    [InlineData("SCRV", "domain")]
    [InlineData("SSAG", "nan")]
    public void InvalidAndPendingModesRemainErrors(string code, string fault)
    {
        var optic = Lens(); var row = Row(code);
        if (fault == "surface") row.ZemaxIntegerParameters[0] = 99;
        if (fault == "mode") row.ZemaxIntegerParameters[1] = 2;
        if (fault == "offaxis") row.ZemaxDataParameters[2] = 1;
        if (fault == "remove") row.ZemaxDataParameters[3] = 2;
        if (fault == "bfs") row.ZemaxDataParameters[4] = 4;
        if (fault == "orientation") row.ZemaxDataParameters[5] = code == "SSLP" ? 5 : 4;
        if (fault == "domain") row.ZemaxDataParameters[0] = 101;
        if (fault == "nan") row.ZemaxDataParameters[1] = double.NaN;
        Invalid(optic, row);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void UpdatesCancellationAndMeritContributionUseCurrentGeometry(string code)
    {
        var optic = Lens(); var row = Row(code, 2); var before = Value(optic, row);
        optic.SurfaceGroup.Items[1].Radius = 50; Assert.NotEqual(before, Value(optic, row));
        row.Target = Value(optic, row) + .25; row.Weight = 2; Assert.Equal(.125, MeritFunctionCatalog.Evaluate(optic, row).Contribution, 12);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(optic, row));
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task SevenAndEightParametersSurviveProjectAndEditorRoundTrips(string code)
    {
        var optic = Lens(); var row = Row(code, 2, 1); optic.MeritFunctionOperands.Add(row);
        var path = Path.Combine(Path.GetTempPath(), $"surface-point-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var copy = (await StarOptProjectStore.LoadAsync(path)).Configurations[0]; var saved = Assert.Single(copy.MeritFunctionOperands);
            Assert.False(saved.CompatibilityOnly); Assert.Equal(row.ZemaxDataParameters, saved.ZemaxDataParameters);
            Assert.Equal(Value(optic, row), Value(copy, saved), 12);
        }
        finally { File.Delete(path); }
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, false, code, 1, 0, 0, 0, 0, 0, 0, .25, 2, 0, 0, "point",
            ZemaxInt1: 1, ZemaxInt2: 1, ZemaxData1: 3, ZemaxData2: 4, ZemaxData3: 0, ZemaxData4: 1, ZemaxData5: 2,
            ZemaxData6: code == "SSAG" ? null : 2)]);
        var type = app.Optimization.GetMeritOperandTypes().Single(t => t.Code == code);
        var editor = new MeritOperandEditorRow(Assert.Single(app.Optimization.GetMeritFunction()), type);
        Assert.True(editor.HasZemaxParameters); Assert.Equal(code == "SSAG" ? 7 : 8, type.Parameters!.Count);
        Assert.Equal(code != "SSAG", editor.IsParameterEditable(7));
        if (code != "SSAG") { Assert.Contains("Orientation", editor.ParameterLabel(7)); editor.Parameter8 = 3; }
        editor.Parameter7 = 3; app.Optimization.SetMeritFunction([editor.ToDto()]); var dto = Assert.Single(app.Optimization.GetMeritFunction());
        Assert.Equal(3, dto.ZemaxData5); Assert.Equal(.25, dto.Target); Assert.Equal(2, dto.Weight);
        if (code != "SSAG") Assert.Equal(3, dto.ZemaxData6);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void NativeRowsDoNotAcquireMissingExtendedSlotsOrAutoUpgrade(string code)
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX"));
        var imported = OpticalFormatCatalog.Import(text + $"\n{code} 1 1 3 4 0 0 .25 2 0 0\n", ".zmx");
        var row = imported.MeritFunctionOperands.Last(); Assert.True(row.CompatibilityOnly); Assert.False(row.Enabled);
        Assert.Equal(4, row.ZemaxDataParameters.Length);
        var copy = Optic.FromSnapshot(imported.ToSnapshot()).MeritFunctionOperands.Last(); Assert.True(copy.CompatibilityOnly);
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, false, code, 1, 0, 0, 0, 0, 0, 0, .25, 2, 0, 0, "native", CompatibilityOnly: true,
            ZemaxInt1: 1, ZemaxInt2: 1, ZemaxData1: 3, ZemaxData2: 4, ZemaxData3: 0, ZemaxData4: 0)]);
        var type = app.Optimization.GetMeritOperandTypes().Single(t => t.Code == code);
        var editor = new MeritOperandEditorRow(Assert.Single(app.Optimization.GetMeritFunction()), type);
        Assert.False(editor.IsParameterEditable(7)); app.Optimization.SetMeritFunction([editor.ToDto()]);
        var dto = Assert.Single(app.Optimization.GetMeritFunction()); Assert.True(dto.CompatibilityOnly);
        Assert.Null(dto.ZemaxData5); Assert.Null(dto.ZemaxData6);
    }

    [Theory]
    [InlineData("SSLP")]
    [InlineData("SCRV")]
    public void MissingEighthParameterIsRejectedBySnapshotValidation(string code)
    {
        var optic = Lens(); var row = Row(code); row.ZemaxDataParameters = [3, 4, 0, 0, 0]; optic.MeritFunctionOperands.Add(row);
        Assert.Throws<InvalidDataException>(() => Optic.FromSnapshot(optic.ToSnapshot()));
        row.CompatibilityOnly = true; row.Enabled = false;
        Assert.Equal(5, Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands[0].ZemaxDataParameters.Length);
    }
}
