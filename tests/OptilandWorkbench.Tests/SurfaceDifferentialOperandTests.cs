using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
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

public sealed class SurfaceDifferentialOperandTests
{
    public static TheoryData<string> Codes => new("SCUR", "SDRV", "TRAI", "BSER");

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task ImportAndNativeProjectPreserveRawSlotsAndState(string code)
    {
        var optic = OpticalFormatCatalog.Import($"""
            MODE SEQ
            ENPD 10
            WAVM 1 0.5876 1
            SURF 0
              DISZ INFINITY
            SURF 1
              STOP
              DISZ 50
            SURF 2
              DISZ 0
            {code} 1 1 0.125 -0.25 0.375 -0.5 0.625 2.5 0 0
            """, ".zmx");
        var path = Path.Combine(Path.GetTempPath(), $"differential-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var row = Assert.Single((await StarOptProjectStore.LoadAsync(path)).Configurations[0].MeritFunctionOperands);
            Assert.Equal(code, row.Type);
            Assert.Equal(new[] { 1, 1 }, row.ZemaxIntegerParameters);
            Assert.Equal(new[] { 0.125, -0.25, 0.375, -0.5 }, row.ZemaxDataParameters);
            Assert.Equal(0.625, row.Target); Assert.Equal(2.5, row.Weight);
            Assert.True(row.Enabled); Assert.False(row.CompatibilityOnly);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void InvalidReferencesAreErrors(string code) => Invalid(Lens(), Row(code, 999, 999));

    [Theory]
    [MemberData(nameof(Codes))]
    public void OldCompatibilityRowsUpgradeWithoutEnabling(string code)
    {
        var optic = Lens();
        var row = Row(code, 1, 0); row.CompatibilityOnly = true; row.Enabled = false;
        optic.MeritFunctionOperands.Add(row);
        var restored = Assert.Single(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands);
        Assert.False(restored.CompatibilityOnly); Assert.False(restored.Enabled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SphericalDerivativesDistinguishTangentialAndSagittal(int data)
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].Geometry = new StandardGeometry(100);
        var root = Math.Sqrt(10000 - 25);
        var expected = new[] { 5 / root, 0, 10000 / Math.Pow(root, 3), 1 / root }[data];
        Value(optic, Row("SDRV", 1, data, 3, 4), expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void SphereCurvaturesIncludeSignDifferencesAndRadialProduct(int data)
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].Geometry = new StandardGeometry(-100);
        var expected = data is 0 or 1 or 4 or 5 ? -0.01 : data is 8 or 9 ? 0.05 : 0;
        Value(optic, Row("SCUR", 1, data, 3, 4), expected);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public void EvenAndOddAspheresUseCurrentCoefficients(bool odd, int data)
    {
        var optic = Lens();
        optic.SurfaceGroup.Items[1].Geometry = odd
            ? new OddAsphereGeometry(double.PositiveInfinity, 0, [0, 0.02, 0.003, -0.0001])
            : new EvenAsphereGeometry(double.PositiveInfinity, 0, [0.02, -0.0001]);
        // z = .02 r² + (.003 r³ for Odd) - .0001 r⁴ at r=5.
        var slope = 0.2 - 0.05 + (odd ? 0.225 : 0);
        var second = 0.04 - 0.03 + (odd ? 0.09 : 0);
        Value(optic, Row("SDRV", 1, data, 3, 4), new[] { slope, 0, second, slope / 5 }[data]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MixedPolynomialTermsProjectInLocalDirections(int data)
    {
        var optic = Lens();
        optic.SurfaceGroup.Items[1].Geometry = new PolynomialGeometry(new Dictionary<(int, int), double>
        { [(2, 0)] = 1, [(0, 2)] = 2, [(1, 1)] = 3, [(1, 0)] = 4, [(0, 1)] = 5 });
        optic.SurfaceGroup.Items[1].CoordinateSystem = new(new(10, 20, 30), 15, 25, 35);
        Value(optic, Row("SDRV", 1, data, 2, 3), new[] { 103 / Math.Sqrt(13), -5 / Math.Sqrt(13), 80.0 / 13, -2.0 / 13 }[data]);
    }

    [Fact]
    public void PolynomialDirectionalCurvatureUsesSurfaceNormalMetric()
    {
        var surface = Lens().SurfaceGroup.Items[1];
        surface.Geometry = new PolynomialGeometry(new Dictionary<(int, int), double> { [(2, 0)] = 0.5, [(0, 2)] = 1 });
        var jet = SurfaceDifferentialMetrics.At(surface, 2, 3);
        Assert.Equal(1 / (Math.Sqrt(41) * 5), jet.NormalCurvature(1, 0), 12);
        Assert.Equal(2 / (Math.Sqrt(41) * 37), jet.NormalCurvature(0, 1), 12);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(9)]
    public void MaximumCurvatureScansFiftyPointsAndFindsInternalExtrema(int data)
    {
        var optic = Lens();
        optic.SurfaceGroup.Items[1].Geometry = new EvenAsphereGeometry(double.PositiveInfinity, 0, [0.01, -0.0001]);
        var expected = Enumerable.Range(0, 50).Select(i =>
        {
            var r = 20 * i / 49.0;
            var slope = .02 * r - .0004 * r * r * r;
            var second = .02 - .0012 * r * r;
            var t = second / Math.Pow(1 + slope * slope, 1.5);
            var s = (.02 - .0004 * r * r) / Math.Sqrt(1 + slope * slope);
            return data == 9 ? Math.Abs(r * s) : Math.Abs(t - s);
        }).Max();
        Value(optic, Row("SCUR", 1, data, 0, 20), expected);
        if (data != 9)
        {
            var endpoint = MeritFunctionCatalog.Evaluate(optic, Row("SCUR", 1, data - 1, 0, 20));
            Assert.True(expected > Math.Abs(endpoint.Value) + .01);
        }
    }

    [Fact]
    public void PlaneAndAsphereVertexAreDefinedOutsideAperture()
    {
        var optic = Lens();
        for (var data = 0; data < 10; data++) Value(optic, Row("SCUR", 1, data, 200, -300), 0);
        optic.SurfaceGroup.Items[1].Geometry = new EvenAsphereGeometry(100, -1, [.02]);
        Value(optic, Row("SDRV", 1, 2), .05); Value(optic, Row("SDRV", 1, 3), .05);
        optic.SurfaceGroup.Items[1].Geometry = new OddAsphereGeometry(100, 0, [0, .02, .001]);
        Value(optic, Row("SCUR", 1, 0), .05);
    }

    [Theory]
    [InlineData("SCUR", -1, 0, 0)]
    [InlineData("SCUR", 10, 0, 0)]
    [InlineData("SDRV", 4, 0, 0)]
    [InlineData("SDRV", 0, double.NaN, 0)]
    [InlineData("SCUR", 0, double.PositiveInfinity, 0)]
    public void InvalidDataOrCoordinatesNeverBecomeZero(string code, int data, double x, double y) => Invalid(Lens(), Row(code, 1, data, x, y));

    [Fact]
    public void UnsupportedSingularAndNonFiniteSurfacesAreRejected()
    {
        var optic = Lens();
        optic.SurfaceGroup.Items[1].Geometry = new BiconicGeometry(100, 200);
        Invalid(optic, Row("SCUR", 1));
        optic.SurfaceGroup.Items[1].Geometry = new StandardGeometry(5);
        Invalid(optic, Row("SDRV", 1, 0, 3, 4)); Invalid(optic, Row("SCUR", 1, 1, 0, 6));
        optic.SurfaceGroup.Items[1].Geometry = new OddAsphereGeometry(100, 0, [.01]);
        Invalid(optic, Row("SDRV", 1)); Invalid(optic, Row("SCUR", 1, 3, 0, 2));
        optic.SurfaceGroup.Items[1].Geometry = new EvenAsphereGeometry(100, 0, [double.NaN]);
        Invalid(optic, Row("SCUR", 1));
    }

    [Fact]
    public void IntermediateAberrationUsesRequestedPlaneAndPrimaryChief()
    {
        var optic = Lens();
        Value(optic, Row("TRAI", 1, 0, 0, 0, 0, 1), 5);
        Value(optic, Row("TRAI", 2, 0, 0, 0, 0, 1), 0);
        optic.SurfaceGroup.Items[1].CoordinateSystem = new(new(2, 3, 0), RotationZDegrees: 20);
        Value(optic, Row("TRAI", 1, 0, 0, 0, .6, .8), 5);
        Invalid(optic, Row("TRAI", 0));
        Invalid(optic, Row("TRAI", 1, 0, 0, 0, 0, 1.1));
        optic.SurfaceGroup.Items[0].Thickness = 100; optic.SurfaceGroup.Renumber();
        Value(optic, Row("TRAI", 0), 0);
    }

    [Fact]
    public void BoresightErrorTracksDecenteredThinLensAndImageReference()
    {
        var optic = Lens();
        Value(optic, Row("BSER", 0), 0);
        optic.SurfaceGroup.Items[1].CoordinateSystem = new(new(3, 4, 0));
        Value(optic, Row("BSER", 0), .1);
        optic.SurfaceGroup.Items[2].CoordinateSystem = new(new(3, 4, 50));
        Value(optic, Row("BSER", 0), 0);
    }

    [Fact]
    public void IntermediateAberrationUsesPrimaryChiefAcrossDifferentWavelengths()
    {
        var optic = DispersiveLens(); optic.Fields[0].Y = 20;
        var separation = 50 * Math.Tan(20 * Math.PI / 180) * (1 / 1.5 - 1 / 1.6);
        Value(optic, Row("TRAI", 2, 2, 0, 1), separation);
        Value(optic, Row("TRAI", 2, 1, 0, 1), 0);
        Value(optic, Row("TRAI", 2, 0, 0, 1), 0);
    }

    [Fact]
    public void BoresightSelectsRayWavelengthAndAlwaysUsesAxialField()
    {
        var optic = DispersiveLens(); optic.Fields[0].Y = 20;
        optic.SurfaceGroup.Items[1].CoordinateSystem = new(new(3, 4, 0));
        Value(optic, Row("BSER", 0, 1), .1 / 1.5);
        Value(optic, Row("BSER", 0, 2), .1 / 1.6);
        Value(optic, Row("BSER", 0, 0), .1 / 1.5);
    }

    [Fact]
    public void SurfaceEditsImmediatelyChangeDifferentialValues()
    {
        var optic = Lens(); var row = Row("SDRV", 1, 2);
        optic.SurfaceGroup.Items[1].Geometry = new EvenAsphereGeometry(double.PositiveInfinity, 0, [.01]);
        Value(optic, row, .02);
        optic.SurfaceGroup.Items[1].Geometry = new EvenAsphereGeometry(double.PositiveInfinity, 0, [.03]);
        Value(optic, row, .06);
    }

    [Fact]
    public void BoresightRejectsAfocalTiltedUnsupportedAndClippedSystems()
    {
        var optic = Lens(); optic.ImageSpaceAfocal = true;
        Invalid(optic, Row("BSER", 0)); optic.ImageSpaceAfocal = false;
        optic.SurfaceGroup.Items[1].CoordinateSystem = new(Vector3D.Zero, RotationYDegrees: 2);
        Invalid(optic, Row("BSER", 0)); optic.SurfaceGroup.Items[1].CoordinateSystem = CoordinateSystem.Global;
        optic.SurfaceGroup.Items[1].Geometry = new EvenAsphereGeometry(100, 0, []);
        Invalid(optic, Row("BSER", 0)); optic.SurfaceGroup.Items[1].Geometry = new PlaneGeometry();
        optic.SurfaceGroup.Items[1].InteractionModel = new RefractiveReflectiveInteractionModel();
        Invalid(optic, Row("BSER", 0));
    }

    [Theory]
    [InlineData("TRAI")]
    [InlineData("BSER")]
    public void ClippedRaysAreNotSuccessfulValues(string code)
    {
        var optic = Lens();
        optic.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(0.5);
        optic.SurfaceGroup.Items[1].CoordinateSystem = new(new(3, 4, 0));
        Invalid(optic, Row(code, 1, 0, 0, 0, 0, 1));
    }

    [Fact]
    public void RayCacheReusesAndInvalidatesIntermediateSamples()
    {
        var optic = Lens(); var cache = new RayTraceCache(32, 512); optic.ConfigureRayTraceCache(cache, opticRevision: 1);
        var row = Row("TRAI", 1, 0, 0, 0, 0, 1);
        Value(optic, row, 5); Value(optic, row, 5); Assert.True(cache.Statistics.Hits > 0);
        optic.Aperture.Value = 8; Value(optic, row, 4);
    }

    [Fact]
    public void ApplicationEditingRetainsDataFlagInsteadOfTreatingItAsWavelength()
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, true, "SCUR", 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, "",
            ZemaxInt1: 1, ZemaxInt2: 9, ZemaxData1: 0, ZemaxData2: 1)]);
        var row = Assert.Single(app.Optimization.GetMeritFunction());
        Assert.Empty(row.Error); Assert.Equal(9, row.ZemaxInt2);
        app.Optimization.SetMeritFunction([row]);
        Assert.Equal(row.Value, Assert.Single(app.Optimization.GetMeritFunction()).Value);
        foreach (var code in new[] { "SCUR", "SDRV", "TRAI", "BSER" })
            Assert.False(Assert.Single(app.Optimization.GetMeritOperandTypes(), t => t.Code == code).CompatibilityOnly);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void CancellationPropagates(string code)
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(Lens(), Row(code, 1)));
    }

    [Fact]
    public void CurvatureOperandDrivesProductionDls()
    {
        var optic = Lens(); var surface = optic.SurfaceGroup.Items[1];
        surface.InteractionModel = new RefractiveReflectiveInteractionModel(); surface.Radius = 50;
        surface.MaterialAfter = new ConstantIndexMaterial("Glass", 1.5); surface.RadiusVariable = true;
        var row = Row("SCUR", 1, 0, 0, 2); row.Target = .01; optic.MeritFunctionOperands.Add(row);
        var runtime = new WorkbenchRuntime(optic);
        var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 50);
        Assert.True(result.FinalMerit < result.InitialMerit);
        Assert.Equal(.01, MeritFunctionCatalog.Evaluate(runtime.CurrentOptic, row).Value, 6);
    }

    private static Optic Lens()
    {
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.Replace([Surface(double.PositiveInfinity), Surface(50), Surface(0)]);
        optic.SurfaceGroup.Items[1].InteractionModel = new ThinLensInteractionModel(50);
        optic.SurfaceGroup.Items[1].IsStop = true;
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter; optic.Aperture.Value = 10; optic.RayAimingEnabled = false;
        return optic;
    }

    private static Optic DispersiveLens()
    {
        var optic = Lens(); optic.Wavelengths.Clear();
        optic.Wavelengths.Add(new Wavelength { Nanometers = 500, IsPrimary = true, Weight = 1 });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 600, IsPrimary = false, Weight = 1 });
        optic.SurfaceGroup.Items[1].MaterialAfter = new CatalogGlassMaterial("TEST:Dispersive", "TEST", "tabulated n", 400, 800,
            refractiveIndexWavelengthsNanometers: [500, 600], refractiveIndices: [1.5, 1.6]);
        return optic;
    }

    private static OpticalSurface Surface(double thickness) => new()
    {
        Thickness = thickness,
        Geometry = new PlaneGeometry(),
        SemiDiameter = 100,
        MaterialAfter = new AirMaterial(),
        InteractionModel = new RefractiveReflectiveInteractionModel()
    };
    private static MeritOperandDefinition Row(string code, int first, int second = 0, double d1 = 0, double d2 = 0, double d3 = 0, double d4 = 0) => new()
    {
        Type = code,
        Surface = first,
        Wavelength = second,
        ZemaxIntegerParameters = [first, second],
        ZemaxDataParameters = [d1, d2, d3, d4],
        Weight = 2
    };
    private static void Value(Optic optic, MeritOperandDefinition row, double expected)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.True(string.IsNullOrEmpty(result.Error), result.Error);
        Assert.Equal(expected, result.Value, 9);
        Assert.Equal(2 * Math.Pow(expected - row.Target, 2), result.Contribution, 6);
        var restored = MeritFunctionCatalog.Evaluate(Optic.FromSnapshot(optic.ToSnapshot()), row.Clone());
        Assert.True(string.IsNullOrEmpty(restored.Error), restored.Error); Assert.Equal(result.Value, restored.Value, 9);
    }
    private static void Invalid(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.NotEmpty(result.Error); Assert.True(double.IsNaN(result.Value)); Assert.True(double.IsPositiveInfinity(result.Contribution));
    }
}
