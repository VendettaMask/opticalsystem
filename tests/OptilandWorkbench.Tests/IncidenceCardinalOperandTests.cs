using System.Globalization;
using System.Text.RegularExpressions;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
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

public sealed class IncidenceCardinalOperandTests
{
    private static readonly string[] Codes = ["MNAI", "MXAI", "PMVA", "PMGT", "PMLT", "GCOS", "CVOL", "CARD"];
    public static TheoryData<string> NewCodes => new(Codes);

    [Theory]
    [MemberData(nameof(NewCodes))]
    public async Task ZmxAndStaroptPreserveAllSlotsAndExecutableState(string code)
    {
        var fieldSlot = code is "MNAI" or "MXAI" ? "1" : "0.125";
        var optic = OpticalFormatCatalog.Import($"""
            MODE SEQ
            ENPD 10
            WAVM 1 0.5876 1
            WAVM 2 0.4861 1
            SURF 0
              DISZ INFINITY
            SURF 1
              STOP
              DISZ 5
            SURF 2
              DISZ 0
            {code} 1 2 {fieldSlot} -0.25 0.375 -0.5 0.625 2.5 0 0
            """, ".zmx");
        var row = Assert.Single(optic.MeritFunctionOperands);
        Assert.True(row.Enabled);
        Assert.False(row.CompatibilityOnly);
        var path = Path.Combine(Path.GetTempPath(), $"incidence-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var restored = Assert.Single((await StarOptProjectStore.LoadAsync(path)).Configurations[0].MeritFunctionOperands);
            Assert.Equal(code, restored.Type);
            Assert.Equal(new[] { 1, 2 }, restored.ZemaxIntegerParameters);
            Assert.Equal(new[] { code is "MNAI" or "MXAI" ? 1.0 : 0.125, -0.25, 0.375, -0.5 }, restored.ZemaxDataParameters);
            Assert.Equal(0.625, restored.Target);
            Assert.Equal(2.5, restored.Weight);
            Assert.True(restored.Enabled);
            Assert.False(restored.CompatibilityOnly);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void HelpDistinguishesParametersWavelengthsAndAllSelectors()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var types = app.Optimization.GetMeritOperandTypes();
        foreach (var code in Codes)
        {
            var item = Assert.Single(types, type => type.Code == code);
            Assert.False(item.CompatibilityOnly);
            Assert.DoesNotContain("不会执行", item.Calculation);
            Assert.Equal(ZemaxOperandSupportLevel.Executable, ZemaxOperandRegistry.Get(code).SupportLevel);
        }
        Assert.True(ZemaxOperandRegistry.Get("CARD").UsesSlotAs("Int2", ZemaxOperandParameterValueKind.EndSurface));
        Assert.True(ZemaxOperandRegistry.Get("CARD").UsesSlotAs("Data1", ZemaxOperandParameterValueKind.Wavelength));
        Assert.True(ZemaxOperandRegistry.Get("PMVA").UsesSlotAs("Int2", ZemaxOperandParameterValueKind.Integer));
        Assert.True(ZemaxOperandRegistry.Get("MNAI").UsesSlotAs("Data1", ZemaxOperandParameterValueKind.Field));
        Assert.Contains("0=all", ZemaxOperandRegistry.Get("MNAI").Parameters[1].DisplayName);
    }

    [Theory]
    [MemberData(nameof(NewCodes))]
    public void InvalidSurfaceReferencesNeverBecomeSuccessfulZero(string code) => Invalid(Plate(), Row(code, 999, 999));

    [Theory]
    [InlineData(false, 1, 0.001)]
    [InlineData(false, 2, -0.0002)]
    [InlineData(false, 8, 0)]
    [InlineData(true, 1, 0.001)]
    [InlineData(true, 2, -0.0002)]
    [InlineData(true, 8, 0)]
    public void ParameterValuesReadCurrentPolynomialCoefficients(bool odd, int parameter, double expected)
    {
        var optic = Plate();
        optic.SurfaceGroup.Items[1].Geometry = odd
            ? new OddAsphereGeometry(50, -1, [0.001, -0.0002])
            : new EvenAsphereGeometry(50, -1, [0.001, -0.0002]);
        Value(optic, Row("PMVA", 1, parameter), expected);
        var lower = Row("PMGT", 1, parameter); lower.Target = expected + 1;
        Value(optic, lower, expected);
        lower.Target = expected - 1;
        Value(optic, lower, lower.Target);
        var upper = Row("PMLT", 1, parameter); upper.Target = expected - 1;
        Value(optic, upper, expected);
        upper.Target = expected + 1;
        Value(optic, upper, upper.Target);
    }

    [Fact]
    public void ImportedAsphereParameterFollowsSubsequentGeometryEdits()
    {
        var optic = OpticalFormatCatalog.Import("""
            MODE SEQ
            ENPD 10
            WAVM 1 0.5876 1
            SURF 0
              DISZ INFINITY
            SURF 1
              TYPE EVENASPH
              CURV 0.02
              PARM 2 0.0004
              STOP
              DISZ 10
            SURF 2
              DISZ 0
            PMVA 1 2 0 0 0 0 0 1 0 0
            """, ".zmx");
        var row = Assert.Single(optic.MeritFunctionOperands);
        Value(optic, row, 0.0004);
        optic.SurfaceGroup.Items[1].Geometry = new EvenAsphereGeometry(50, 0, [0, 0.0007]);
        Value(optic, row, 0.0007);
        Invalid(optic, Row("PMVA", 1, 0));
        Invalid(optic, Row("PMVA", 1, 9));
        optic.SurfaceGroup.Items[1].Geometry = new StandardGeometry(50);
        Invalid(optic, row);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3.5)]
    public void GlassCostUsesAgfOtherDataAndSurvivesSnapshots(double cost)
    {
        var metadata = Assert.Single(ZemaxAgfCatalogReader.Import($"""
            NM TEST 1 0 1.5 60 0 0 0
            GC This comment is not a cost
            CD 2.25 0 0 0 0 0
            OD {cost.ToString(CultureInfo.InvariantCulture)} 1 2 3 4 5
            LD 0.4 0.8
            """, "TEST").Glasses);
        var optic = Plate();
        optic.SurfaceGroup.Items[1].MaterialAfter = Catalog(metadata);
        Value(optic, Row("GCOS", 1), cost);
        Invalid(optic, Row("GCOS", 2));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void MissingGlassCostIsNotSubstitutedWithZero(double cost)
    {
        var optic = Plate();
        optic.SurfaceGroup.Items[1].MaterialAfter = Catalog(new OpticalGlassDefinition { OtherData = [cost] });
        Invalid(optic, Row("GCOS", 1));
        optic.SurfaceGroup.Items[1].MaterialAfter = Catalog(new OpticalGlassDefinition());
        Invalid(optic, Row("GCOS", 1));
    }

    [Theory]
    [InlineData(0, 490)]
    [InlineData(1, 250)]
    public void CylinderVolumeUsesVertexSpanAndSelectedMaximumRadius(int mode, double multipleOfPi)
    {
        var optic = Plate();
        optic.SurfaceGroup.Items[1].SemiDiameter = 3;
        optic.SurfaceGroup.Items[1].MechanicalSemiDiameter = 4;
        optic.SurfaceGroup.Items[2].SemiDiameter = 5;
        optic.SurfaceGroup.Items[2].MechanicalSemiDiameter = 7;
        optic.SurfaceGroup.Items[2].Thickness = 123;
        optic.SurfaceGroup.Renumber();
        Value(optic, Row("CVOL", 1, 2, d2: mode), multipleOfPi * Math.PI);
        optic.SurfaceGroup.Items[1].Radius = 20;
        Value(optic, Row("CVOL", 1, 2, d2: mode), multipleOfPi * Math.PI);
        Value(optic, Row("CVOL", 1, 1, d2: mode), 0);
        Invalid(optic, Row("CVOL", 2, 1));
        Invalid(optic, Row("CVOL", 0, 2));
        Invalid(optic, Row("CVOL", 1, 2, d2: 0.5));
        optic.SurfaceGroup.Items[2].CoordinateSystem = new CoordinateSystem(new Vector3D(1, 0, 10));
        Invalid(optic, Row("CVOL", 1, 2));
    }

    [Theory]
    [InlineData(0, -50)]
    [InlineData(1, 50)]
    [InlineData(2, -50)]
    [InlineData(3, 50)]
    [InlineData(4, 0)]
    [InlineData(5, 0)]
    [InlineData(6, -100)]
    [InlineData(7, 100)]
    [InlineData(8, 0)]
    [InlineData(9, 0)]
    [InlineData(10, -100)]
    [InlineData(11, 100)]
    public void CardinalPlanesMatchThinLensConjugates(int data, double expected)
    {
        var optic = Plate();
        optic.SurfaceGroup.Items[1].MaterialAfter = new AirMaterial();
        optic.SurfaceGroup.Items[1].InteractionModel = new ThinLensInteractionModel(50);
        Value(optic, Row("CARD", 1, 1, d3: data), expected);
        Value(optic, Row("CARD", 1, 1, d2: 1, d3: data), expected);
    }

    [Theory]
    [InlineData(0, -100)]
    [InlineData(1, 150)]
    [InlineData(2, -100)]
    [InlineData(3, 150)]
    [InlineData(4, 0)]
    [InlineData(5, 0)]
    [InlineData(6, -200)]
    [InlineData(7, 300)]
    [InlineData(8, 50)]
    [InlineData(9, 50)]
    [InlineData(10, -250)]
    [InlineData(11, 250)]
    public void CardinalPlanesUseUnequalMediaAndDistinctNodalPoints(int data, double expected)
    {
        var optic = Plate();
        optic.SurfaceGroup.Items[1].Radius = 50;
        Value(optic, Row("CARD", 1, 1, d3: data), expected);
    }

    [Fact]
    public void CardinalGroupReferencePlanesExcludeTrailingSpace()
    {
        var optic = Plate();
        optic.SurfaceGroup.Items[1].Radius = 50;
        optic.SurfaceGroup.Items[2].Radius = -50;
        var focal = 1 / (0.5 * (0.04 - 0.5 * 10 / (1.5 * 2500)));
        Value(optic, Row("CARD", 1, 2, d3: 1), focal);
        var before = MeritFunctionCatalog.Evaluate(optic, Row("CARD", 1, 2, d3: 3)).Value;
        optic.SurfaceGroup.Items[2].Thickness = 250;
        optic.SurfaceGroup.Renumber();
        Value(optic, Row("CARD", 1, 2, d3: 3), before);
        optic.SurfaceGroup.Items[0].Thickness = 100;
        optic.SurfaceGroup.Renumber();
        Value(optic, Row("CARD", 1, 2, d3: 3), before);
    }

    [Fact]
    public void CardinalDataMatchesExistingZemax123456CapturedSettings()
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(fixtures, "zemax-123456.ZMX")), ".zmx");
        var text = File.ReadAllText(Path.Combine(fixtures, "zemax-123456-cardinal-points.txt"));
        var captured = Regex.Matches(text, @"(?m)^.*?:\s*([-+]?\d+\.\d+)\s+([-+]?\d+\.\d+)\s*$")
            .SelectMany(m => new[] { double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) }).ToArray();
        Assert.Equal(12, captured.Length);
        var wave = optic.Wavelengths.ToList().FindIndex(w => Math.Abs(w.Nanometers - 440) < 1e-8) + 1;
        Assert.True(wave > 0);
        for (var data = 0; data < 12; data++)
        {
            var result = MeritFunctionCatalog.Evaluate(optic, Row("CARD", 1, 22, d1: wave, d2: 0, d3: data));
            Assert.True(string.IsNullOrEmpty(result.Error), result.Error);
            Assert.True(Math.Abs(result.Value - captured[data]) <= 1e-6, $"CARD {data}: {result.Value:G17} vs {captured[data]:G17}");
        }
    }

    [Theory]
    [InlineData(0, 0, 12)]
    [InlineData(0, 2, 0)]
    [InlineData(0.5, 0, 0)]
    [InlineData(99, 0, 0)]
    [InlineData(0, 0, 1.5)]
    public void CardinalDataRejectsInvalidModes(double wave, double orientation, double data) =>
        Invalid(Plate(), Row("CARD", 1, 2, d1: wave, d2: orientation, d3: data));

    [Fact]
    public void CardinalDataRejectsAfocalAsphericReflectedAndReversedGroups()
    {
        var optic = Plate();
        Invalid(optic, Row("CARD", 1, 2));
        Invalid(optic, Row("CARD", 2, 1));
        Invalid(optic, Row("CARD", 0, 2));
        optic.SurfaceGroup.Items[1].Geometry = new EvenAsphereGeometry(50, 0, [0.001]);
        Invalid(optic, Row("CARD", 1, 2));
        optic.SurfaceGroup.Items[1].Geometry = new StandardGeometry(50);
        optic.SurfaceGroup.Items[1].InteractionModel = new RefractiveReflectiveInteractionModel(true);
        Invalid(optic, Row("CARD", 1, 2));
    }

    [Theory]
    [InlineData("MNAI", 40, 30)]
    [InlineData("MNAI", 20, 20)]
    [InlineData("MXAI", 20, 30)]
    [InlineData("MXAI", 40, 40)]
    public void IncidenceExtremaHonorBoundaryContribution(string code, double target, double expected)
    {
        var optic = Plate(); optic.Fields[0].Y = 30;
        var row = Row(code, 1, d1: 1); row.Target = target;
        Value(optic, row, expected);
    }

    [Fact]
    public void IncidenceSelectionUsesActualIncidentMediumAndReportsWinningSurface()
    {
        var optic = Plate(); optic.Fields[0].Y = 30;
        var minimum = Row("MNAI", 0, 0, 1); minimum.Target = 90;
        Value(optic, minimum, Math.Asin(1.0 / 3) * 180 / Math.PI);
        Value(optic, Row("MNAI", 0, 0, 1, d3: 4), 2);
        Value(optic, Row("MXAI", 1, 0, 1, d3: 1), 0);
        Value(optic, Row("MXAI", 1, 0, 1, d3: 2), 1);
        Value(optic, Row("MXAI", 1, 0, 1, d3: 3), 1);
    }

    [Fact]
    public void AllWavelengthsAndFieldsAreScannedAndZeroDoesNotMeanPrimary()
    {
        var optic = Plate(); optic.Fields[0].Y = 10;
        optic.Fields.Add(new FieldPoint { X = 0, Y = 30 });
        optic.Wavelengths.Clear();
        optic.Wavelengths.Add(new Wavelength { Nanometers = 400, IsPrimary = true });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 800, IsPrimary = false });
        optic.SurfaceGroup.Items[1].MaterialAfter = new CatalogGlassMaterial("TEST:Dispersion", "TEST", "tabulated n", 400, 800,
            refractiveIndexWavelengthsNanometers: [400, 800], refractiveIndices: [1.6, 1.5]);
        Value(optic, Row("MXAI", 2), Math.Asin(1.0 / 3) * 180 / Math.PI);
        Value(optic, Row("MXAI", 2, d3: 2), 2);
        Value(optic, Row("MXAI", 2, d3: 3), 2);
        Value(optic, Row("MXAI", 2, 1), Math.Asin(0.5 / 1.6) * 180 / Math.PI);
    }

    [Fact]
    public void SymmetryRestrictsMarginalRaysAndInvalidRaysAreNotSkipped()
    {
        var optic = Plate(); optic.Fields[0].Y = 5;
        optic.SurfaceGroup.Items[1].Radius = 20;
        Value(optic, Row("MXAI", 1, d1: 1, d2: 1, d3: 1), 1);
        Value(optic, Row("MXAI", 1, d1: 1, d2: 2, d3: 1), 3);
        var y = MeritFunctionCatalog.Evaluate(optic, Row("MXAI", 1, d1: 1, d2: 1));
        var x = MeritFunctionCatalog.Evaluate(optic, Row("MXAI", 1, d1: 1, d2: 2));
        Assert.True(y.Value > x.Value);
        optic.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(1);
        Invalid(optic, Row("MXAI", 1, d1: 1));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0.5, 0, 0)]
    [InlineData(99, 0, 0)]
    [InlineData(1, 3, 0)]
    [InlineData(1, 0, 5)]
    public void IncidenceSelectorsRejectInvalidIntegers(double field, double symmetry, double data) =>
        Invalid(Plate(), Row("MNAI", 1, d1: field, d2: symmetry, d3: data));

    [Theory]
    [InlineData("MNAI")]
    [InlineData("MXAI")]
    [InlineData("CARD")]
    [InlineData("CVOL")]
    public void SharedComputationsHonorCancellation(string code)
    {
        using var token = new CancellationTokenSource(); token.Cancel();
        using var scope = ComputationCancellation.Push(token.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(Plate(), Row(code, 1, code is "MNAI" or "MXAI" ? 1 : 2)));
    }

    [Fact]
    public void ApplicationEditingKeepsCardinalEndSurfaceSeparateFromWavelength()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var input = new MeritOperandRowDto(1, true, "CARD", 1, 1, 6, 0, 0, 0, 0, 0, 1, 0, 0, "",
            ZemaxInt1: 1, ZemaxInt2: 6, ZemaxData1: 2, ZemaxData2: 0, ZemaxData3: 1, ZemaxData4: 0);
        app.Optimization.SetMeritFunction([input]);
        var row = Assert.Single(app.Optimization.GetMeritFunction());
        Assert.Empty(row.Error);
        Assert.Equal(6, row.ZemaxInt2);
        Assert.Equal(2, row.ZemaxData1);
        app.Optimization.SetMeritFunction([row]);
        var restored = Assert.Single(app.Optimization.GetMeritFunction());
        Assert.Empty(restored.Error);
        Assert.Equal(row.Value, restored.Value, 12);
        Assert.Equal(row.ZemaxInt2, restored.ZemaxInt2);
        Assert.Equal(row.ZemaxData1, restored.ZemaxData1);
    }

    [Theory]
    [MemberData(nameof(NewCodes))]
    public void PreservedRowsBecomeEditableWithoutEnablingThem(string code)
    {
        var optic = Plate();
        var row = Row(code, 1, 1); row.CompatibilityOnly = true; row.Enabled = false;
        optic.MeritFunctionOperands.Add(row);
        var restored = Assert.Single(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands);
        Assert.False(restored.CompatibilityOnly);
        Assert.False(restored.Enabled);
    }

    [Fact]
    public void IncidenceReusesBoundedTraceCacheWithoutKeepingStaleSurfaceSamples()
    {
        var optic = Plate(); optic.Fields[0].Y = 30;
        var cache = new RayTraceCache(maximumEntries: 16, maximumSamples: 256);
        optic.ConfigureRayTraceCache(cache, opticRevision: 1);
        using var batch = MeritFunctionCatalog.BeginEvaluationBatch();
        var row = Row("MNAI", 0); row.Target = 90;
        Value(optic, row, Math.Asin(1.0 / 3) * 180 / Math.PI);
        Value(optic, row, Math.Asin(1.0 / 3) * 180 / Math.PI);
        Assert.True(cache.Statistics.Hits > 0);
        optic.SurfaceGroup.Items[1].MaterialAfter = new ConstantIndexMaterial("Changed", 2);
        Value(optic, row, Math.Asin(0.25) * 180 / Math.PI);
    }

    [Fact]
    public void CardinalRejectsInconsistentFrameSpacingAndInvalidInternalIndex()
    {
        var optic = Plate(); optic.SurfaceGroup.Items[1].Radius = 50;
        optic.SurfaceGroup.Items[2].CoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 20));
        Invalid(optic, Row("CARD", 1, 2));
        optic.SurfaceGroup.Renumber();
        optic.SurfaceGroup.Items[1].MaterialAfter = new ConstantIndexMaterial("Invalid", -1);
        Invalid(optic, Row("CARD", 1, 2));
    }

    [Fact]
    public void CardinalOperandDrivesProductionDampedLeastSquares()
    {
        var optic = Plate();
        optic.SurfaceGroup.Items[1].Radius = 50;
        optic.SurfaceGroup.Items[2].Radius = -50;
        optic.SurfaceGroup.Items[1].RadiusVariable = true;
        var row = Row("CARD", 1, 2, d3: 1); row.Target = 60;
        optic.MeritFunctionOperands.Add(row);
        var runtime = new WorkbenchRuntime(optic);
        var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 50);
        Assert.True(result.FinalMerit < result.InitialMerit);
        Assert.InRange(result.FinalMerit, 0, 1e-10);
        Assert.Equal(60, MeritFunctionCatalog.Evaluate(runtime.CurrentOptic, row).Value, 5);
    }

    private static CatalogGlassMaterial Catalog(OpticalGlassDefinition metadata) => new("TEST:Glass", "TEST", "tabulated n", 400, 800,
        refractiveIndexWavelengthsNanometers: [400, 800], refractiveIndices: [1.5, 1.5], zemaxData: metadata);

    private static Optic Plate()
    {
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.Replace([
            Surface("Object", double.PositiveInfinity, new AirMaterial()),
            Surface("Front", 10, new ConstantIndexMaterial("Glass", 1.5)),
            Surface("Back", 30, new AirMaterial()),
            Surface("Image", 0, new AirMaterial())]);
        optic.SurfaceGroup.Items[1].IsStop = true;
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter; optic.Aperture.Value = 10;
        optic.RayAimingEnabled = false;
        return optic;
    }

    private static OpticalSurface Surface(string label, double thickness, IMaterial after) => new()
    {
        Label = label,
        Thickness = thickness,
        Geometry = new PlaneGeometry(),
        SemiDiameter = 100,
        MaterialBefore = new AirMaterial(),
        MaterialAfter = after,
        InteractionModel = new RefractiveReflectiveInteractionModel(),
        CoatingModel = new NoneCoatingModel()
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
        Assert.True(string.IsNullOrEmpty(result.Error), $"{row.Type}: {result.Error}");
        Assert.Equal(expected, result.Value, 9);
        Assert.Equal(row.Weight * Math.Pow(expected - row.Target, 2), result.Contribution, 6);
        var restored = MeritFunctionCatalog.Evaluate(Optic.FromSnapshot(optic.ToSnapshot()), row.Clone());
        Assert.True(string.IsNullOrEmpty(restored.Error), restored.Error);
        Assert.Equal(result.Value, restored.Value, 9);
    }

    private static void Invalid(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.NotEmpty(result.Error); Assert.True(double.IsNaN(result.Value));
        Assert.True(double.IsPositiveInfinity(result.Contribution));
    }
}
