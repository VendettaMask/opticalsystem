using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class SurfacePhaseOperandTests
{
    public static TheoryData<string> Codes => new("SPHS", "PSLP", "DPHS", "QSLP");
    private static Optic Lens(IPhaseProfile? profile = null)
    {
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity, SemiDiameter = 5 },
            new OpticalSurface { Thickness = 10, SemiDiameter = 5, IsStop = true,
                InteractionModel = new PhaseInteractionModel(profile ?? Polynomial()) },
            new OpticalSurface { Thickness = 0, SemiDiameter = 5 }
        ]);
        return optic;
    }
    private static IPhaseProfile Polynomial() => new PolynomialPhaseProfile(new Dictionary<(int, int), double>
    { [(0, 0)] = 10 * Math.PI, [(1, 0)] = 4 * Math.PI, [(0, 1)] = -6 * Math.PI, [(2, 0)] = Math.PI, [(1, 1)] = .5 * Math.PI });
    private static MeritOperandDefinition Row(string code, int data = 1, int remove = 0, int orientation = 2) => new()
    {
        Type = code,
        Surface = 1,
        ZemaxIntegerParameters = [1, code is "DPHS" or "QSLP" ? data : 1],
        ZemaxDataParameters = code switch
        { "SPHS" => [3, 4, 0, remove], "PSLP" => [3, 4, remove, orientation], _ => [1, remove, orientation, 0] }
    };
    private static double Value(Optic optic, MeritOperandDefinition row)
    { var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.Empty(result.Error); return result.Value; }
    private static void Invalid(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.NotEmpty(result.Error);
        Assert.True(double.IsNaN(result.Value)); Assert.True(double.IsPositiveInfinity(result.Contribution));
        Assert.Throws<OptimizationEvaluationException>(() => MeritFunctionCatalog.EvaluateOptimizationValues(optic, [row]));
    }

    [Theory]
    [InlineData(0, 1.8)]
    [InlineData(1, -6.15)]
    [InlineData(2, 6)]
    [InlineData(3, -2.25)]
    [InlineData(4, 6.408002808988148)]
    public void PointPhaseAndAnalyticSlopeHavePhysicalUnitsAndSignedDirections(int orientation, double expected)
    {
        var optic = Lens();
        Assert.Equal(6.5, Value(optic, Row("SPHS")), 12);
        Assert.Equal(1.5, Value(optic, Row("SPHS", remove: 1)), 12);
        Assert.Equal(expected, Value(optic, Row("PSLP", orientation: orientation)), 11);
        Assert.Equal(expected, Value(optic, Row("PSLP", remove: 1, orientation: orientation)), 11);
    }

    [Theory]
    [InlineData("SPHS", 0)]
    [InlineData("SPHS", 1)]
    [InlineData("SPHS", 2)]
    [InlineData("PSLP", 0)]
    [InlineData("PSLP", 1)]
    [InlineData("PSLP", 2)]
    public void PointCoordinatesStayLocalAndApertureDoesNotClipThem(string code, int mode)
    {
        var optic = Lens(); var row = Row(code); var expected = Value(optic, row);
        var surface = optic.SurfaceGroup.Items[1]; surface.PhysicalAperture = new OffsetRadialAperture(1, 0, 50, 60);
        surface.CoordinateSystem = new CoordinateSystem(new(10, 20, 30), 12, 15, 20); surface.MechanicalSemiDiameter = 200;
        row.ZemaxIntegerParameters[1] = mode;
        if (mode == 2) { row.ZemaxDataParameters[0] /= 5; row.ZemaxDataParameters[1] /= 5; }
        Assert.Equal(expected, Value(optic, row), 12);
    }

    [Theory]
    [InlineData(0, -3)]
    [InlineData(1, -2)]
    [InlineData(2, 2)]
    [InlineData(3, -3)]
    [InlineData(4, 3.605551275463989)]
    public void VertexDirectionsUsePositiveYRadialAndNegativeXOrthogonal(int orientation, double expected)
    {
        var row = Row("PSLP", orientation: orientation); row.ZemaxDataParameters[0] = 0; row.ZemaxDataParameters[1] = 0;
        Assert.Equal(expected, Value(Lens(), row), 12);
    }

    [Theory]
    [InlineData("SPHS")]
    [InlineData("PSLP")]
    [InlineData("DPHS")]
    [InlineData("QSLP")]
    public void PrimaryWavelengthIncludesZeroWeightAndIgnoresUnrelatedRowWave(string code)
    {
        var optic = Lens(new WavelengthPhase()); optic.Wavelengths.Clear();
        optic.Wavelengths.Add(new() { Nanometers = 400, IsPrimary = false, Weight = 1 });
        optic.Wavelengths.Add(new() { Nanometers = 600, IsPrimary = true, Weight = 0 });
        var row = Row(code); row.Wavelength = 99; var initial = Value(optic, row);
        optic.Wavelengths[1].Nanometers = 900;
        Assert.Equal(initial * 1.5, Value(optic, row), 10);
        optic.Wavelengths[1].IsPrimary = false; Assert.Equal(initial * 2 / 3, Value(optic, row), 10);
        optic.Wavelengths.Clear(); Invalid(optic, row);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void RectangularPhaseStatisticsMatchClosedFormDiscreteMoments(int sampling)
    {
        var optic = Lens(new PolynomialPhaseProfile(new Dictionary<(int, int), double> { [(0, 0)] = 10 * Math.PI, [(1, 0)] = 4 * Math.PI, [(0, 1)] = -6 * Math.PI }));
        optic.SurfaceGroup.Items[1].PhysicalAperture = new RectangularAperture(2, 1, 3, -1);
        var m = (1 << (sampling + 4)); var varianceFactor = (m + 2.0) / (3 * m);
        var expected = new[] { Math.Sqrt(196 + 25 * varianceFactor), 14d, 7, 21, 1, 0, 5, -2, 0, 0, 0, 0 };
        for (var data = 1; data <= 12; data++)
        {
            var row = Row("DPHS", data); row.ZemaxDataParameters[0] = sampling;
            Assert.Equal(expected[data - 1], Value(optic, row), 10);
        }
        Assert.Equal(5, Value(optic, Row("DPHS", data: 9, remove: 1)), 12);
        Assert.Equal(2, Value(optic, Row("DPHS", data: 3, remove: 1)), 12);
        Assert.Equal(2, Value(optic, Row("QSLP", orientation: 2)), 12);
        Assert.Equal(3, Value(optic, Row("QSLP", orientation: 3)), 12); // RMS is unsigned.
        Assert.Equal(-3, Value(optic, Row("QSLP", data: 3, orientation: 3)), 12);
    }

    [Theory]
    [InlineData("circle", 0, 5)]
    [InlineData("annular", 0, 5)]
    [InlineData("offset", 3, 2)]
    [InlineData("ellipse", -2, 3)]
    public void StatisticsRespectPhysicalApertureShapeAndOffset(string kind, double centerX, double radiusX)
    {
        var optic = Lens(new LinearGratingPhaseProfile(1)); var surface = optic.SurfaceGroup.Items[1];
        surface.PhysicalAperture = kind switch
        { "circle" => new CircularAperture(5), "annular" => new AnnularAperture(5, 3), "offset" => new OffsetRadialAperture(2, 1, 3, 4), _ => new EllipticalAperture(3, 1, -2, 1) };
        Assert.Equal(centerX - radiusX, Value(optic, Row("DPHS", data: 3)), 12);
        Assert.Equal(centerX + radiusX, Value(optic, Row("DPHS", data: 4)), 12);
        Assert.Equal(1, Value(optic, Row("QSLP", orientation: 2)), 12);
        var stats = SurfacePhaseMetrics.Evaluate(surface, SurfacePhaseQuantity.Phase, 550);
        Assert.True(stats.Samples.SampleCount < 33 * 33);
    }

    [Theory]
    [InlineData("constant")]
    [InlineData("grating")]
    [InlineData("radial")]
    [InlineData("grid")]
    public void FormalPhaseProfileKindsUseTheirAnalyticDefinitions(string kind)
    {
        var coordinates = new[] { -5d, -1d, 2d, 5d }; var grid = new double[4, 4];
        for (var j = 0; j < 4; j++) for (var i = 0; i < 4; i++) grid[j, i] = 2 * Math.PI * (coordinates[i] * coordinates[i] + 2 * coordinates[j]);
        IPhaseProfile profile = kind switch
        {
            "constant" => new ConstantPhaseProfile(14 * Math.PI),
            "grating" => new LinearGratingPhaseProfile(2, Math.PI / 2, -3),
            "radial" => new RadialPhaseProfile([.2 * Math.PI]),
            _ => new GridPhaseProfile(coordinates, coordinates, grid)
        };
        var optic = Lens(profile);
        Assert.Equal(kind switch { "constant" => 7, "grating" => -6, "radial" => 2.5, _ => 17 }, Value(optic, Row("SPHS")), 10);
        Assert.Equal(kind switch { "constant" => 0, "grating" => -1.5, "radial" => .8, _ => 2 }, Value(optic, Row("PSLP", orientation: 3)), 10);
        if (kind == "grid") Assert.Equal(6, Value(optic, Row("PSLP", orientation: 2)), 10);
    }

    [Theory]
    [InlineData("SPHS", "surface")]
    [InlineData("PSLP", "mode")]
    [InlineData("SPHS", "data")]
    [InlineData("DPHS", "data")]
    [InlineData("QSLP", "sampling")]
    [InlineData("DPHS", "sampling")]
    [InlineData("PSLP", "orientation")]
    [InlineData("QSLP", "orientation")]
    [InlineData("SPHS", "remove")]
    [InlineData("PSLP", "remove")]
    [InlineData("DPHS", "remove")]
    [InlineData("QSLP", "remove")]
    [InlineData("SPHS", "nan")]
    [InlineData("PSLP", "nan")]
    public void InvalidAndPendingModesReturnErrors(string code, string fault)
    {
        var row = Row(code); var stats = code is "DPHS" or "QSLP";
        if (fault == "surface") row.ZemaxIntegerParameters[0] = 99;
        if (fault == "mode") row.ZemaxIntegerParameters[1] = 3;
        if (fault == "data") { if (stats) row.ZemaxIntegerParameters[1] = 13; else row.ZemaxDataParameters[2] = 1; }
        if (fault == "sampling") row.ZemaxDataParameters[0] = 6;
        if (fault == "orientation") row.ZemaxDataParameters[stats ? 2 : 3] = 5;
        if (fault == "remove") row.ZemaxDataParameters[stats ? 1 : code == "SPHS" ? 3 : 2] = 2;
        if (fault == "nan") row.ZemaxDataParameters[0] = double.NaN;
        Invalid(Lens(), row);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void NonPhaseSurfaceNeverReturnsFakeZeroAndUpdatesCancellationRemainLive(string code)
    {
        var optic = Lens(); var row = Row(code); var old = Value(optic, row);
        optic.SurfaceGroup.Items[1].InteractionModel = new PhaseInteractionModel(new LinearGratingPhaseProfile(.25));
        Assert.NotEqual(old, Value(optic, row)); row.Target = Value(optic, row) + .25; row.Weight = 2;
        Assert.Equal(.125, MeritFunctionCatalog.Evaluate(optic, row).Contribution, 11);
        optic.SurfaceGroup.Items[1].InteractionModel = new RefractiveReflectiveInteractionModel(); Invalid(optic, row);
        using var cts = new CancellationTokenSource(); cts.Cancel(); using var scope = ComputationCancellation.Push(cts.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(optic, row));
    }

    [Fact]
    public void ComputationBudgetAndNonFinitePhaseModelsFailExplicitly()
    {
        var optic = Lens(new RadialPhaseProfile(Enumerable.Repeat(1e-6, 4096))); var row = Row("DPHS"); row.ZemaxDataParameters[0] = 5;
        Invalid(optic, row);
        optic = Lens(new PolynomialPhaseProfile(new Dictionary<(int, int), double> { [(64, 0)] = 1e300 }));
        Invalid(optic, Row("SPHS")); Invalid(optic, Row("PSLP"));
        var coordinates = Enumerable.Range(0, 32).Select(i => (double)i).ToArray();
        optic = Lens(new GridPhaseProfile(coordinates, coordinates, new double[32, 32])); Invalid(optic, Row("QSLP"));
        Assert.Throws<ArgumentOutOfRangeException>(() => SurfacePhaseMetrics.AtPoint(Lens().SurfaceGroup.Items[1], SurfacePhaseQuantity.Phase, 0, 0, 0));
    }

    [Fact]
    public void LargeFiniteCoordinatesKeepDirectionNormalizedWithoutRadiusOverflow()
    {
        var optic = Lens(new LinearGratingPhaseProfile(1)); var row = Row("PSLP", orientation: 0);
        row.ZemaxDataParameters[0] = 1.7e308; row.ZemaxDataParameters[1] = 1.7e308;
        Assert.Equal(1 / Math.Sqrt(2), Value(optic, row), 12);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task SixSlotsAndPhaseModelSurviveProjectAndEditorRoundTrips(string code)
    {
        var optic = Lens(); var row = Row(code, data: 3, remove: 1, orientation: 4); optic.MeritFunctionOperands.Add(row);
        var path = Path.Combine(Path.GetTempPath(), $"phase-operand-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var copy = (await StarOptProjectStore.LoadAsync(path)).Configurations[0]; var saved = Assert.Single(copy.MeritFunctionOperands);
            Assert.False(saved.CompatibilityOnly); Assert.Equal(row.ZemaxDataParameters, saved.ZemaxDataParameters);
            Assert.Equal(Value(optic, row), Value(copy, saved), 12);
        }
        finally { File.Delete(path); }
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, false, code, 1, 0, 0, 0, 0, 0, 0, .25, 2, 0, 0, "phase",
            ZemaxInt1: 1, ZemaxInt2: row.ZemaxIntegerParameters[1], ZemaxData1: row.ZemaxDataParameters[0], ZemaxData2: row.ZemaxDataParameters[1],
            ZemaxData3: row.ZemaxDataParameters[2], ZemaxData4: row.ZemaxDataParameters[3])]);
        var type = app.Optimization.GetMeritOperandTypes().Single(t => t.Code == code);
        var editor = new MeritOperandEditorRow(Assert.Single(app.Optimization.GetMeritFunction()), type);
        Assert.True(editor.HasZemaxParameters); Assert.Equal(6, type.Parameters!.Count);
        Assert.False(editor.IsParameterEditable(6)); editor.Parameter3 = 2;
        app.Optimization.SetMeritFunction([editor.ToDto()]); var dto = Assert.Single(app.Optimization.GetMeritFunction());
        Assert.Equal(2, dto.ZemaxData1); Assert.Null(dto.ZemaxData5); Assert.Null(dto.ZemaxData6);
        Assert.Equal(.25, dto.Target); Assert.Equal(2, dto.Weight);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void UnverifiedNativeColumnsRemainDisabledAndDoNotAutoUpgrade(string code)
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX"));
        var imported = OpticalFormatCatalog.Import(text + $"\n{code} 1 1 3 4 0 0 .25 2 0 0\n", ".zmx");
        var row = imported.MeritFunctionOperands.Last(); Assert.True(row.CompatibilityOnly); Assert.False(row.Enabled);
        var saved = Optic.FromSnapshot(imported.ToSnapshot()).MeritFunctionOperands.Last(); Assert.True(saved.CompatibilityOnly);
        Assert.Equal(row.ZemaxDataParameters, saved.ZemaxDataParameters);
    }

    private sealed class WavelengthPhase : IPhaseProfile
    {
        public string Kind => "test-wavelength-phase";
        public double Efficiency => 1;
        public double Phase(double x, double y, double wavelengthNanometers) => 2 * Math.PI * wavelengthNanometers / 100 * (1 + x + 2 * y);
        public (double Dx, double Dy) Gradient(double x, double y, double wavelengthNanometers) => (2 * Math.PI * wavelengthNanometers / 100, 4 * Math.PI * wavelengthNanometers / 100);
        public double ParaxialGradient(double y, double wavelengthNanometers) => Gradient(0, y, wavelengthNanometers).Dy;
        public IPhaseProfile Clone() => new WavelengthPhase();
    }
}
