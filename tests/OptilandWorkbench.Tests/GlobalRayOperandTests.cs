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

public sealed class GlobalRayOperandTests
{
    public static TheoryData<string> Codes => new("GLCX", "GLCY", "GLCZ", "GLCA", "GLCB", "GLCC", "GLCR",
        "RAGX", "RAGY", "RAGZ", "RAGA", "RAGB", "RAGC", "DXDX", "DXDY", "DYDX", "DYDY");

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task ZmxAndNativeRoundTripPreserveParametersAndReference(string code)
    {
        var optic = OpticalFormatCatalog.Import($"""
            MODE SEQ
            ENPD 10
            GLRS 2
            WAVM 1 0.55 1
            SURF 0
              DISZ INFINITY
            SURF 1
              STOP
              DISZ 10
            SURF 2
              DISZ 0
            {code} 1 1 0.125 -0.25 0.375 -0.5 0.625 2.5 0 0
            """, ".zmx");
        var path = Path.Combine(Path.GetTempPath(), $"global-ray-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var restored = (await StarOptProjectStore.LoadAsync(path)).Configurations[0];
            Assert.Equal(2, restored.GlobalReferenceSurfaceNumber);
            var row = Assert.Single(restored.MeritFunctionOperands);
            Assert.Equal(code, row.Type); Assert.Equal(new[] { 1, 1 }, row.ZemaxIntegerParameters);
            Assert.Equal(new[] { .125, -.25, .375, -.5 }, row.ZemaxDataParameters);
            Assert.Equal(.625, row.Target); Assert.Equal(2.5, row.Weight);
            Assert.True(row.Enabled); Assert.False(row.CompatibilityOnly);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void OldCompatibilityRowsUpgradeWithoutEnabling(string code)
    {
        var optic = Plate(); var row = Row(code); row.CompatibilityOnly = true; row.Enabled = false;
        optic.MeritFunctionOperands.Add(row);
        var restored = Assert.Single(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands);
        Assert.False(restored.CompatibilityOnly); Assert.False(restored.Enabled);
    }

    [Theory]
    [InlineData("GLCX")]
    [InlineData("RAGX")]
    [InlineData("DXDX")]
    public void InvalidLegacyReferencesRemainReadOnlyAndCanBeSavedAgain(string code)
    {
        var optic = Plate(); var row = Row(code, 999, 999); row.CompatibilityOnly = true; row.Enabled = false;
        optic.MeritFunctionOperands.Add(row);
        var restored = Optic.FromSnapshot(optic.ToSnapshot());
        Assert.True(restored.MeritFunctionOperands[0].CompatibilityOnly);
        Assert.True(Optic.FromSnapshot(restored.ToSnapshot()).MeritFunctionOperands[0].CompatibilityOnly);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void ApplicationEditingHelpAndCancellationAreConnected(string code)
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, true, code, 1, 1, 0, 0, 0, 0, 0, 0, 1, 0, 0, "", ZemaxInt1: 1, ZemaxInt2: 1)]);
        var row = Assert.Single(app.Optimization.GetMeritFunction()); Assert.Empty(row.Error);
        app.Optimization.SetMeritFunction([row]); Assert.Equal(row.Value, Assert.Single(app.Optimization.GetMeritFunction()).Value);
        Assert.False(Assert.Single(app.Optimization.GetMeritOperandTypes(), t => t.Code == code).CompatibilityOnly);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(Plate(), Row(code)));
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void InvalidSurfaceOrWavelengthFailsWithoutZeroFallback(string code) => Invalid(Plate(), Row(code, 999, 999));

    [Theory]
    [InlineData("GLCX", 6)]
    [InlineData("GLCY", -3)]
    [InlineData("GLCZ", 9)]
    [InlineData("GLCA", -1)]
    [InlineData("GLCB", 0)]
    [InlineData("GLCC", 0)]
    public void SurfaceValuesUseTranslatedAndRotatedReference(string code, double expected)
    {
        var optic = Frames(); Value(optic, Row(code, 2), expected);
        optic.GlobalReferenceSurfaceNumber = 2;
        Value(optic, Row(code, 2), code == "GLCC" ? 1 : 0);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, -1)]
    [InlineData(4, -1)]
    [InlineData(5, 0)]
    [InlineData(6, 0)]
    [InlineData(7, 0)]
    [InlineData(8, 1)]
    [InlineData(9, 0)]
    public void RotationMatrixIsRowMajorAndMapsLocalToReference(int data, double expected)
    {
        var optic = Frames(); Value(optic, Row("GLCR", 2, data), expected);
        Value(optic, Row("GLCR", 1, data), data is 1 or 5 or 9 ? 1 : 0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(10)]
    public void InvalidMatrixComponentIsRejected(int data) => Invalid(Frames(), Row("GLCR", 2, data));

    [Fact]
    public void ReferenceSelectionFollowsLiveFramesWithoutChangingGeometry()
    {
        var optic = Frames(); var before = optic.SurfaceGroup.Items[2].CoordinateSystem;
        Value(optic, Row("GLCX", 2), 6);
        optic.SurfaceGroup.Items[1].CoordinateSystem = new(new(1, 4, 3), RotationZDegrees: 90);
        Value(optic, Row("GLCX", 2), 4);
        optic.GlobalReferenceSurfaceNumber = 2;
        Value(optic, Row("GLCX", 2), 0); Assert.Equal(before, optic.SurfaceGroup.Items[2].CoordinateSystem);
    }

    [Theory]
    [InlineData("NORX", -1, 0)]
    [InlineData("NORY", 0, 0)]
    [InlineData("NORZ", 0, 1)]
    public void GlobalNormalUsesSameReferenceAsGlobalSurfaceAxis(string code, double expected, double local)
    {
        var optic = Frames(); var row = Row(code, 2, 0, 0, 0, 1);
        Value(optic, row, expected);
        optic.GlobalReferenceSurfaceNumber = 2; Value(optic, row, local);
        optic.GlobalReferenceSurfaceNumber = -1; Invalid(optic, row);
    }

    [Theory]
    [InlineData("RAGX", 2)]
    [InlineData("RAGY", -1)]
    [InlineData("RAGZ", -10)]
    [InlineData("RAGA", 1.0 / 3)]
    [InlineData("RAGB", 0)]
    public void RayCoordinatesAndSnellDirectionsUseSelectedReference(string code, double expected)
    {
        var optic = Plate(); optic.Fields[0].Y = 30; optic.GlobalReferenceSurfaceNumber = 2;
        optic.SurfaceGroup.Items[2].CoordinateSystem = new(new(0, 0, 10), RotationZDegrees: 90);
        Value(optic, Row(code, 1, 1, 0, 1, .2, .4), expected);
        Value(optic, Row("RAGC", 1, 1, 0, 1, .2, .4), Math.Sqrt(8) / 3);
    }

    [Fact]
    public void GlobalRayDirectionRetainsReflectionAndTotalInternalReflection()
    {
        var mirror = Plate(); mirror.Fields[0].Y = 30;
        mirror.SurfaceGroup.Items[1].IsReflective = true;
        Value(mirror, Row("RAGB", 1, 1, 0, 1), .5);
        Value(mirror, Row("RAGC", 1, 1, 0, 1), -Math.Sqrt(3) / 2);
        var tir = Plate(); tir.Fields[0].Y = 60;
        tir.SurfaceGroup.Items[0].MaterialAfter = new ConstantIndexMaterial("Dense", 1.5);
        tir.SurfaceGroup.Items[1].MaterialAfter = new AirMaterial();
        Value(tir, Row("RAGB", 1, 1, 0, 1), Math.Sqrt(3) / 2);
        Value(tir, Row("RAGC", 1, 1, 0, 1), -.5);
    }

    [Fact]
    public void ReferenceMutationDoesNotReuseStaleMeasurementButSharesTrace()
    {
        var optic = Plate(); var cache = new RayTraceCache(); optic.ConfigureRayTraceCache(cache, 1);
        Value(optic, Row("RAGZ", 2), 10);
        optic.GlobalReferenceSurfaceNumber = 2; Value(optic, Row("RAGZ", 2), 0);
        Assert.True(cache.Statistics.Hits > 0);
        optic.SurfaceGroup.Items[2].CoordinateSystem = new(new(0, 0, 12));
        optic.GlobalReferenceSurfaceNumber = 1; Value(optic, Row("RAGZ", 2), 12);
    }

    [Fact]
    public void FiniteAndZeroObjectConjugatesCanBeReferencesButInfiniteCannot()
    {
        var optic = Plate(); optic.GlobalReferenceSurfaceNumber = 0;
        Invalid(optic, Row("GLCZ", 1)); Invalid(optic, Row("RAGZ", 1));
        optic.SurfaceGroup.Items[0].Thickness = 10; optic.SurfaceGroup.Renumber();
        Value(optic, Row("GLCZ", 1), 10);
        optic.SurfaceGroup.Items[0].Thickness = 0; optic.SurfaceGroup.Renumber();
        Value(optic, Row("GLCZ", 1), 0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void InvalidReferenceDoesNotFallBackToFirstSurface(int reference)
    {
        var optic = Plate(); optic.GlobalReferenceSurfaceNumber = reference;
        Invalid(optic, Row("GLCX")); Invalid(optic, Row("RAGX"));
    }

    [Fact]
    public void NonFiniteFramesAndVignettedRaysAreRejected()
    {
        var optic = Plate(); optic.SurfaceGroup.Items[1].CoordinateSystem = new(new(double.NaN, 0, 0));
        Invalid(optic, Row("GLCX")); Invalid(optic, Row("RAGX"));
        optic = Plate(); optic.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(1);
        Invalid(optic, Row("RAGX", 1, 1, 0, 0, .9, 0));
    }

    [Fact]
    public void ImportedCoordinateBreakRemapsPhysicalReferencesAndLeavesBreakReferencesInvalid()
    {
        var text = """
            MODE SEQ
            ENPD 2
            GLRS 3
            WAVM 1 0.55 1
            SURF 0
              DISZ INFINITY
            SURF 1
              STOP
              DISZ 10
            SURF 2
              TYPE COORDBRK
              PARM 1 2
              PARM 2 3
              DISZ 0
            SURF 3
              DISZ 0
            GLCX 3 0 0 0 0 0 0 1 0 0
            GLCY 1 0 0 0 0 0 0 1 0 0
            RAGZ 3 1 0 0 0 0 0 1 0 0
            """;
        var optic = OpticalFormatCatalog.Import(text, ".zmx"); Assert.Equal(2, optic.GlobalReferenceSurfaceNumber);
        Assert.Equal(2, optic.MeritFunctionOperands[0].ZemaxIntegerParameters[0]);
        Value(optic, optic.MeritFunctionOperands[0], 0); Value(optic, optic.MeritFunctionOperands[1], -3);
        Value(optic, optic.MeritFunctionOperands[2], 0);
        Assert.Throws<NotSupportedException>(() => OpticalFormatCatalog.Import(text + "\nGLCZ 2 0 0 0 0 0 0 1 0 0", ".zmx"));
        Assert.Throws<NotSupportedException>(() => OpticalFormatCatalog.Import(text.Replace("GLRS 3", "GLRS 2"), ".zmx"));
    }

    [Theory]
    [InlineData("DXDX", 1)]
    [InlineData("DXDY", 0)]
    [InlineData("DYDX", 0)]
    [InlineData("DYDY", 1)]
    public void RayFanDerivativeMatchesIndependentThinLensAndPupilScaling(string code, double expected)
    {
        var optic = ThinLens(); var row = Row(code, 999, 1, .2, .3, .1, -.2);
        Value(optic, row, expected, 6); optic.Aperture.Value = 20; Value(optic, row, 2 * expected, 6);
        optic.SurfaceGroup.Items[1].Thickness = 50; optic.SurfaceGroup.Renumber(); Value(optic, row, 0, 6);
    }

    [Theory]
    [InlineData("DXDX", 0)]
    [InlineData("DXDY", 1)]
    [InlineData("DYDX", -1)]
    [InlineData("DYDY", 0)]
    public void CrossDerivativesUseImageLocalAxes(string code, double expected)
    {
        var optic = ThinLens(); optic.SurfaceGroup.Items[2].CoordinateSystem = new(new(0, 0, 40), RotationZDegrees: 90);
        Value(optic, Row(code, 0, 1), expected, 6);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-.9999)]
    [InlineData(0)]
    [InlineData(.9999)]
    [InlineData(1)]
    public void FivePointDerivativeHasIndependentPolynomialReferenceIncludingPupilRim(double coordinate)
    {
        var value = RayFanDerivativeMetrics.Differentiate(x => 3 * Math.Pow(x, 4) + 2 * x * x - 5 * x + 1, coordinate, 0);
        Assert.Equal(12 * Math.Pow(coordinate, 3) + 4 * coordinate - 5, value, 7);
        Value(ThinLens(), Row("DXDX", 0, 1, 0, 0, coordinate, 0), 1, 6);
    }

    [Fact]
    public void DerivativeRejectsUnavailableNeighbourhoodAndDoesNotHideRayFailures()
    {
        Invalid(ThinLens(), Row("DXDX", 0, 1, 0, 0, 0, 1));
        Invalid(ThinLens(), Row("DXDY", 0, 1, 0, 0, 1, 0));
        Invalid(ThinLens(), Row("DXDX", 0, 1, 0, 0, .8, .8));
        var optic = ThinLens(); optic.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(1);
        Invalid(optic, Row("DXDX", 0, 1, 0, 0, .9, 0));
        Assert.Throws<InvalidOperationException>(() => RayFanDerivativeMetrics.Differentiate(x => double.NaN, 0, 0));
        Assert.Throws<InvalidOperationException>(() => RayFanDerivativeMetrics.Differentiate(x => x >= 0 ? 1 : -1, 0, 0));
    }

    [Fact]
    public void DerivativeCacheInvalidatesOnThicknessChangeAndPrimaryWaveIsAccepted()
    {
        var optic = ThinLens(); var cache = new RayTraceCache(); optic.ConfigureRayTraceCache(cache, 1);
        var row = Row("DXDX", 0, 0); Value(optic, row, 1, 6); Value(optic, row, 1, 6);
        Assert.True(cache.Statistics.Hits > 0);
        optic.SurfaceGroup.Items[1].Thickness = 30; optic.SurfaceGroup.Renumber(); Value(optic, row, 2, 6);
    }

    [Theory]
    [InlineData("GLCZ", 30)]
    [InlineData("DXDX", 0)]
    public void NewMeasurementsDriveProductionDlsThicknessOptimization(string code, double target)
    {
        var optic = ThinLens(); optic.SurfaceGroup.Items[1].ThicknessVariable = true;
        var row = Row(code, 2, 1); row.Target = target; optic.MeritFunctionOperands.Add(row);
        var runtime = new WorkbenchRuntime(optic); var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 70);
        Assert.True(result.FinalMerit < result.InitialMerit); Assert.Equal(target, Evaluate(runtime.CurrentOptic, row), 5);
        Assert.Equal(code == "GLCZ" ? 30 : 50, runtime.CurrentOptic.SurfaceGroup.Items[1].Thickness, 4);
    }

    private static Optic Frames()
    {
        var optic = Plate();
        optic.SurfaceGroup.Items[1].CoordinateSystem = new(new(1, 2, 3), RotationZDegrees: 90);
        optic.SurfaceGroup.Items[2].CoordinateSystem = new(new(4, 8, 12), RotationXDegrees: 90);
        return optic;
    }
    private static Optic Plate()
    {
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity, Geometry = new PlaneGeometry(), MaterialAfter = new AirMaterial(), SemiDiameter = 50 },
            new OpticalSurface { Thickness = 10, Geometry = new PlaneGeometry(), MaterialAfter = new ConstantIndexMaterial("Glass", 1.5), SemiDiameter = 50, IsStop = true },
            new OpticalSurface { Thickness = 0, Geometry = new PlaneGeometry(), MaterialAfter = new AirMaterial(), SemiDiameter = 50 }
        ]);
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter; optic.Aperture.Value = 10;
        optic.Fields.Clear(); optic.Fields.Add(new FieldPoint { Y = 5 });
        optic.Wavelengths.Clear(); optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        return optic;
    }
    private static Optic ThinLens()
    {
        var optic = Plate(); optic.SurfaceGroup.Items[1].MaterialAfter = new AirMaterial();
        optic.SurfaceGroup.Items[1].InteractionModel = new ThinLensInteractionModel(50);
        optic.SurfaceGroup.Items[1].Thickness = 40; optic.SurfaceGroup.Renumber(); return optic;
    }
    private static MeritOperandDefinition Row(string code, int surface = 1, int wave = 1, double hx = 0, double hy = 0, double px = 0, double py = 0) => new()
    {
        Type = code,
        Surface = surface,
        Wavelength = wave,
        Hx = hx,
        Hy = hy,
        Px = px,
        Py = py,
        ZemaxIntegerParameters = [surface, wave],
        ZemaxDataParameters = [hx, hy, px, py],
        Weight = 2
    };
    private static double Evaluate(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.True(string.IsNullOrEmpty(result.Error), result.Error); return result.Value;
    }
    private static void Value(Optic optic, MeritOperandDefinition row, double expected, int precision = 9)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.True(string.IsNullOrEmpty(result.Error), result.Error);
        Assert.Equal(expected, result.Value, precision);
        Assert.Equal(row.Weight * Math.Pow(result.Value - row.Target, 2), result.Contribution, 6);
        Assert.Equal(result.Value, Evaluate(Optic.FromSnapshot(optic.ToSnapshot()), row.Clone()), precision);
    }
    private static void Invalid(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.NotEmpty(result.Error);
        Assert.True(double.IsNaN(result.Value)); Assert.True(double.IsPositiveInfinity(result.Contribution));
    }
}
