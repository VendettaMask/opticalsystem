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
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class ParaxialManufacturingOperandTests
{
    private static readonly string[] Codes = [
        "PARX", "PARY", "PARZ", "PARR", "PARA", "PARB", "PARC", "PATX", "PATY",
        "PANA", "PANB", "PANC", "YNIP", "DMGT", "DMLT", "DMVA", "MNDT", "MXDT", "BLTH", "EFLA"];

    public static TheoryData<string> NewCodes => new(Codes);

    [Theory]
    [MemberData(nameof(NewCodes))]
    public async Task ImportAndNativeRoundTripKeepExecutableStatusAndEveryRawSlot(string code)
    {
        var optic = OpticalFormatCatalog.Import($"""
            MODE SEQ
            ENPD 10
            WAVM 1 0.5876 1
            WAVM 2 0.4861 1
            SURF 0
              DISZ 100
            SURF 1
              STOP
              DISZ 5
            SURF 2
              DISZ 0
            {code} 1 2 0.125 -0.25 0.375 -0.5 0.625 2.5 0 0
            """, ".zmx");
        var imported = Assert.Single(optic.MeritFunctionOperands);
        Assert.True(imported.Enabled);
        Assert.False(imported.CompatibilityOnly);
        Assert.Equal(ZemaxOperandSupportLevel.Executable, ZemaxOperandRegistry.Get(code).SupportLevel);
        var path = Path.Combine(Path.GetTempPath(), $"paraxial-operand-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var restored = (await StarOptProjectStore.LoadAsync(path)).Configurations[0];
            var row = Assert.Single(restored.MeritFunctionOperands);
            Assert.Equal(code, row.Type);
            Assert.Equal(new[] { 1, 2 }, row.ZemaxIntegerParameters);
            Assert.Equal(new[] { 0.125, -0.25, 0.375, -0.5 }, row.ZemaxDataParameters);
            Assert.Equal(0.625, row.Target);
            Assert.Equal(2.5, row.Weight);
            Assert.True(row.Enabled);
            Assert.False(row.CompatibilityOnly);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void HelpAndParameterMetadataExposeParaxialAndManufacturingSemantics()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var types = app.Optimization.GetMeritOperandTypes();
        foreach (var code in Codes)
        {
            var type = Assert.Single(types, item => item.Code == code);
            Assert.False(type.CompatibilityOnly);
            Assert.DoesNotContain("不会执行", type.Calculation);
            Assert.NotEqual("Zemax 兼容保留", type.Category);
        }
        Assert.True(ZemaxOperandRegistry.Get("PARX").UsesSlotAs("Int2", ZemaxOperandParameterValueKind.Wavelength));
        Assert.True(ZemaxOperandRegistry.Get("EFLA").UsesSlotAs("Int2", ZemaxOperandParameterValueKind.Wavelength));
        Assert.True(ZemaxOperandRegistry.Get("BLTH").UsesSlotAs("Int2", ZemaxOperandParameterValueKind.Flag));
        Assert.True(ZemaxOperandRegistry.Get("MNDT").UsesSlotAs("Int2", ZemaxOperandParameterValueKind.EndSurface));
        Assert.True(ZemaxOperandRegistry.Get("DMVA").UsesSlotAs("Data2", ZemaxOperandParameterValueKind.Flag));
    }

    [Theory]
    [InlineData("PARX", 3)]
    [InlineData("PARY", 4)]
    [InlineData("PARZ", 0)]
    [InlineData("PARR", 5)]
    [InlineData("PARA", -0.05970223141259935)]
    [InlineData("PARB", -0.07960297521679914)]
    [InlineData("PARC", 0.9950371902099893)]
    [InlineData("PATX", -0.06)]
    [InlineData("PATY", -0.08)]
    [InlineData("PANA", 0)]
    [InlineData("PANB", 0)]
    [InlineData("PANC", 1)]
    public void ThinLensParaxialRayMatchesIndependentVectorReference(string code, double expected)
    {
        AssertValue(ThinLens(), Row(code, 1, d3: 0.6, d4: 0.8), expected);
    }

    [Fact]
    public void ExplicitAxialCoordinatesAndOffAxisFieldsPropagateToImagePlane()
    {
        var optic = ThinLens();
        optic.Fields[0].Y = 5;
        AssertValue(optic, Row("PARY", 2, d4: 1), 0);
        AssertValue(optic, Row("PARY", 2, d2: 1), 50 * Math.Tan(5 * Math.PI / 180));
        AssertValue(optic, Row("PARX", 2, d1: 0.5), 50 * Math.Tan(2.5 * Math.PI / 180));
        optic.FieldDefinition = FieldDefinitionKind.ParaxialImageHeight;
        optic.Fields[0].Y = 4;
        AssertValue(optic, Row("PARY", 2, d2: 1), 4);
    }

    [Fact]
    public void FiniteObjectHeightAndCoincidentObjectHaveDistinctBehavior()
    {
        var optic = ThinLens();
        optic.SurfaceGroup.Items[0].Thickness = 100;
        optic.SurfaceGroup.Items[1].Thickness = 100;
        optic.SurfaceGroup.Renumber();
        optic.FieldDefinition = FieldDefinitionKind.ObjectHeight;
        optic.Fields[0].Y = 4;
        AssertValue(optic, Row("PARY", 0, d2: 1), -4);
        AssertValue(optic, Row("PATY", 0, d2: 1), 0.04);
        AssertValue(optic, Row("PARY", 2, d2: 1), 4);
        AssertValue(optic, Row("PARY", 2, d4: 1), 0);
        optic.SurfaceGroup.Items[0].Thickness = 0;
        optic.SurfaceGroup.Renumber();
        AssertInvalid(optic, Row("PARY", 1, d4: 1));
        AssertInvalid(optic, Row("YNIP", 1));
    }

    [Fact]
    public void LocalRotationTransformsParaxialCoordinatesAndSlopes()
    {
        var optic = ThinLens();
        optic.SurfaceGroup.Items[1].CoordinateSystem = new CoordinateSystem(Vector3D.Zero, RotationZDegrees: 90);
        AssertValue(optic, Row("PARX", 1, d3: 0.6, d4: 0.8), 4);
        AssertValue(optic, Row("PARY", 1, d3: 0.6, d4: 0.8), -3);
        AssertValue(optic, Row("PATX", 1, d3: 0.6, d4: 0.8), -0.08);
        AssertValue(optic, Row("PATY", 1, d3: 0.6, d4: 0.8), 0.06);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(-20)]
    public void NormalIsEvaluatedOnParaxialVertexPlaneInsteadOfRealRaySag(double radius)
    {
        var optic = Lens();
        optic.SurfaceGroup.Items[1].Radius = radius;
        AssertValue(optic, Row("PARZ", 1, d4: 1), 0);
        AssertValue(optic, Row("PANB", 1, d4: 1), -Math.Sign(radius) * 5 / Math.Sqrt(425));
        AssertValue(optic, Row("PANC", 1, d4: 1), 20 / Math.Sqrt(425));
        AssertValue(optic, Row("REAZ", 1, d4: 1), Math.Sign(radius) * (20 - Math.Sqrt(375)));
    }

    [Fact]
    public void YniUsesIncidentMediumSlopeAndSignedSurfaceCurvature()
    {
        var optic = Lens();
        optic.SurfaceGroup.Items[1].Thickness = 6;
        optic.SurfaceGroup.Renumber();
        AssertValue(optic, Row("YNIP", 1), 0.5);
        AssertValue(optic, Row("YNIP", 2), -0.9312);
        AssertInvalid(optic, Row("YNIP", 0));
    }

    [Theory]
    [InlineData("PARX")]
    [InlineData("YNIP")]
    [InlineData("EFLA")]
    public void UnsupportedDecenteredAndReflectedSystemsReportErrors(string code)
    {
        var optic = Lens();
        optic.SurfaceGroup.Items[1].CoordinateSystem = new CoordinateSystem(new Vector3D(1, 0, 0));
        AssertInvalid(optic, Row(code, 1));
        optic.SurfaceGroup.Items[1].CoordinateSystem = CoordinateSystem.Global;
        optic.SurfaceGroup.Items[1].InteractionModel = new RefractiveReflectiveInteractionModel(true);
        AssertInvalid(optic, Row(code, 1));
    }

    [Theory]
    [InlineData(1.1, 0)]
    [InlineData(0, -1.1)]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    public void InvalidNormalizedCoordinatesReportErrors(double field, double pupil)
    {
        AssertInvalid(ThinLens(), Row("PARY", 1, d2: field, d4: pupil));
    }

    [Fact]
    public void WavelengthDispersionAndLiveEditsAreUsedByParaxialOperands()
    {
        var optic = Lens();
        optic.Wavelengths[0].IsPrimary = false;
        optic.Wavelengths.Add(new Wavelength { Nanometers = 700, IsPrimary = true, Weight = 1 });
        optic.SurfaceGroup.Items[1].MaterialAfter = new CatalogGlassMaterial(
            "TEST:Dispersive", "TEST", "tabulated n", 400, 800,
            refractiveIndexWavelengthsNanometers: [400, 800], refractiveIndices: [1.5, 1.6]);
        using var batch = MeritFunctionCatalog.BeginEvaluationBatch();
        AssertValue(optic, Row("PATY", 1, 1, d4: 1), -(1.5469 - 1) * 5 / (1.5469 * 50));
        AssertValue(optic, Row("PATY", 1, 0, d4: 1), -(1.575 - 1) * 5 / (1.575 * 50));
        optic.SurfaceGroup.Items[1].Radius = 25;
        AssertValue(optic, Row("PATY", 1, 0, d4: 1), -(1.575 - 1) * 5 / (1.575 * 25));
        AssertInvalid(optic, Row("PATY", 1, 99));
    }

    [Theory]
    [InlineData("DMVA", 0, 0, 14)]
    [InlineData("DMVA", 1, 0, 10)]
    [InlineData("DMGT", 0, 15, 14)]
    [InlineData("DMGT", 0, 12, 12)]
    [InlineData("DMLT", 1, 9, 10)]
    [InlineData("DMLT", 1, 12, 12)]
    public void DiameterModesAndOneSidedResidualsUseSelectedAperture(string code, int mode, double target, double expected)
    {
        var row = Row(code, 1, d2: mode);
        row.Target = target;
        AssertValue(Lens(), row, expected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0.5)]
    [InlineData(2)]
    [InlineData(double.NaN)]
    public void InvalidDiameterModesDoNotSilentlyChooseAnAperture(double mode)
    {
        AssertInvalid(Lens(), Row("DMVA", 1, d2: mode));
        AssertInvalid(Lens(), Row("BLTH", 1, 4, d2: mode));
        AssertInvalid(Lens(), Row("MNDT", 1, 2, d2: mode));
    }

    [Fact]
    public void DiameterThicknessExtremesSkipAirAndHonorInclusiveRangeAndMode()
    {
        var optic = Lens();
        optic.SurfaceGroup.Items[2].MaterialAfter = new ConstantIndexMaterial("SecondGlass", 1.6);
        optic.SurfaceGroup.Items[2].Thickness = 10;
        optic.SurfaceGroup.Items[2].SemiDiameter = 4;
        optic.SurfaceGroup.Items[2].MechanicalSemiDiameter = 9;
        optic.SurfaceGroup.Renumber();
        var lower = Row("MNDT", 0, 2); lower.Target = 3;
        AssertValue(optic, lower, 1.8);
        lower.ZemaxDataParameters[1] = 1;
        AssertValue(optic, lower, 0.8);
        lower.Target = 0.5;
        AssertValue(optic, lower, 0.5);
        var upper = Row("MXDT", 0, 2); upper.Target = 1;
        AssertValue(optic, upper, 2.8);
        upper.Target = 3;
        AssertValue(optic, upper, 3);
        lower.Target = 5; lower.ZemaxIntegerParameters = [2, 2];
        AssertValue(optic, lower, 0.8);
        optic.SurfaceGroup.Items[2].InteractionModel = new RefractiveReflectiveInteractionModel(true);
        AssertInvalid(optic, lower);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidGlassThicknessIsNotSkipped(double thickness)
    {
        var optic = Lens();
        optic.SurfaceGroup.Items[1].Thickness = thickness;
        AssertInvalid(optic, Row("MXDT", 1, 1));
        AssertInvalid(optic, Row("BLTH", 1));
    }

    [Fact]
    public void EmptyAndReversedGlassRangesAreErrors()
    {
        var optic = Lens();
        AssertInvalid(optic, Row("MNDT", 2, 2));
        AssertInvalid(optic, Row("MXDT", 2, 1));
        AssertInvalid(optic, Row("MXDT", 1, -1));
        var row = Row("MXDT", 1); row.Target = 1;
        AssertValue(optic, row, 2.8);
    }

    [Fact]
    public void DiameterThicknessFilterUsesActualIndexRatherThanCatalogClassOrName()
    {
        var optic = Lens();
        optic.SurfaceGroup.Items[1].MaterialAfter = new CatalogGlassMaterial(
            "TEST:Unity", "TEST", "tabulated n", 400, 800,
            refractiveIndexWavelengthsNanometers: [400, 800], refractiveIndices: [1, 1]);
        AssertInvalid(optic, Row("MXDT", 1, 1));
        optic.SurfaceGroup.Items[1].MaterialAfter = new ConstantIndexMaterial("Air", 1.5);
        var row = Row("MXDT", 1, 1); row.Target = 1;
        AssertValue(optic, row, 2.8);
        optic.SurfaceGroup.Items[1].MaterialAfter = new ConstantIndexMaterial("Invalid", -1);
        AssertInvalid(optic, row);
    }

    [Theory]
    [InlineData(20, -20, 5)]
    [InlineData(-20, 20, 6.270166537925832)]
    [InlineData(double.PositiveInfinity, double.PositiveInfinity, 5)]
    public void BlankThicknessBoundsBothPhysicalFaces(double frontRadius, double backRadius, double expected)
    {
        var optic = Lens();
        optic.SurfaceGroup.Items[1].Radius = frontRadius;
        optic.SurfaceGroup.Items[2].Radius = backRadius;
        AssertValue(optic, Row("BLTH", 1, 4, d2: 1), expected);
        if (frontRadius < 0)
            // Mechanical radius 7 includes a flat annulus after the clear radius 5.
            AssertValue(optic, Row("BLTH", 1, 4), 5 + 2 * (20 - Math.Sqrt(375)));
    }

    [Theory]
    [InlineData(0, 6)]
    [InlineData(1, 7)]
    [InlineData(2, 5)]
    [InlineData(3, 5)]
    [InlineData(4, 7)]
    public void BlankThicknessDistinguishesAllFourSignedAxes(int code, double expected)
    {
        var optic = Lens();
        optic.SurfaceGroup.Items[1].Geometry = new PlaneGeometry();
        optic.SurfaceGroup.Items[2].Geometry = new PolynomialGeometry(new Dictionary<(int, int), double>
        { [(1, 0)] = 0.4, [(0, 1)] = 0.2 });
        AssertValue(optic, Row("BLTH", 1, code, d2: 1), expected);
    }

    [Fact]
    public void BlankThicknessSamplesInteriorExtremaAndEachFacesOwnRadius()
    {
        var optic = Lens();
        optic.SurfaceGroup.Items[1].Geometry = new PlaneGeometry();
        optic.SurfaceGroup.Items[2].Geometry = new PolynomialGeometry(new Dictionary<(int, int), double>
        { [(0, 2)] = 0.1, [(0, 4)] = -0.004 });
        var result = MeritFunctionCatalog.Evaluate(optic, Row("BLTH", 1, 0, d2: 1));
        Assert.Empty(result.Error);
        Assert.InRange(result.Value, 5.6249, 5.625); // Analytic extremum at y=sqrt(12.5), not vertex or edge.
        optic.SurfaceGroup.Items[2].Geometry = new PolynomialGeometry(new Dictionary<(int, int), double> { [(0, 1)] = 0.2 });
        optic.SurfaceGroup.Items[2].SemiDiameter = 3;
        AssertValue(optic, Row("BLTH", 1, 0, d2: 1), 5.6);
    }

    [Fact]
    public void BlankThicknessRejectsInvalidAxisDecenterAndSagDomain()
    {
        var optic = Lens();
        AssertInvalid(optic, Row("BLTH", 1, 5));
        AssertInvalid(optic, Row("BLTH", 2));
        optic.SurfaceGroup.Items[1].Radius = 2;
        AssertInvalid(optic, Row("BLTH", 1));
        optic.SurfaceGroup.Items[1].Radius = 50;
        optic.SurfaceGroup.Items[2].CoordinateSystem = new CoordinateSystem(new Vector3D(1, 0, 5));
        AssertInvalid(optic, Row("BLTH", 1));
    }

    [Theory]
    [InlineData(50, -50, 5, 50.84745762711865)]
    [InlineData(-50, 50, 5, -49.18032786885246)]
    [InlineData(double.PositiveInfinity, -50, 5, 100)]
    [InlineData(50, -50, 0, 50)]
    public void ElementAirFocalLengthMatchesThickLensLensmakerReference(double frontRadius, double backRadius, double thickness, double expected)
    {
        var optic = Lens();
        optic.SurfaceGroup.Items[1].Radius = frontRadius;
        optic.SurfaceGroup.Items[2].Radius = backRadius;
        optic.SurfaceGroup.Items[1].Thickness = thickness;
        optic.SurfaceGroup.Renumber();
        AssertValue(optic, Row("EFLA", 1), expected);
        optic.SurfaceGroup.Items[0].MaterialAfter = new ConstantIndexMaterial("ObjectImmersion", 1.2);
        optic.SurfaceGroup.Items[2].MaterialAfter = new ConstantIndexMaterial("ImageImmersion", 1.3);
        AssertValue(optic, Row("EFLA", 1), expected);
    }

    [Fact]
    public void ElementAirFocalLengthUsesSelectedWavelengthAndRejectsUndefinedPower()
    {
        var optic = Lens();
        optic.SurfaceGroup.Items[1].MaterialAfter = new CatalogGlassMaterial(
            "TEST:Dispersive", "TEST", "tabulated n", 400, 800,
            refractiveIndexWavelengthsNanometers: [400, 800], refractiveIndices: [1.5, 1.6]);
        optic.Wavelengths.Add(new Wavelength { Nanometers = 800, Weight = 1, IsPrimary = false });
        AssertValue(optic, Row("EFLA", 1, 2), 1 / (0.6 * (0.04 - 0.6 * 5 / (1.6 * 2500))));
        optic.SurfaceGroup.Items[1].Radius = double.PositiveInfinity;
        optic.SurfaceGroup.Items[2].Radius = double.PositiveInfinity;
        AssertInvalid(optic, Row("EFLA", 1));
        AssertInvalid(ThinLens(), Row("EFLA", 1));
        AssertInvalid(optic, Row("EFLA", 0));
        optic.SurfaceGroup.Items[1].Radius = 50;
        optic.SurfaceGroup.Items[1].Thickness = -1;
        AssertInvalid(optic, Row("EFLA", 1));
    }

    [Theory]
    [MemberData(nameof(NewCodes))]
    public void EveryNewOperandReportsInvalidSurfaceInsteadOfZero(string code) => AssertInvalid(Lens(), Row(code, 999, 999));

    [Theory]
    [InlineData("PARY")]
    [InlineData("YNIP")]
    [InlineData("BLTH")]
    [InlineData("EFLA")]
    public void ExpensiveSharedComputationsHonorCancellation(string code)
    {
        using var token = new CancellationTokenSource();
        token.Cancel();
        using var scope = ComputationCancellation.Push(token.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(Lens(), Row(code, 1)));
    }

    [Fact]
    public void PreviouslyPreservedRowsUpgradeWithoutEnablingDisabledRows()
    {
        var optic = Lens();
        var row = Row("DMVA", 1); row.Enabled = false; row.CompatibilityOnly = true;
        optic.MeritFunctionOperands.Add(row);
        var restored = Optic.FromSnapshot(optic.ToSnapshot());
        var updated = Assert.Single(restored.MeritFunctionOperands);
        Assert.False(updated.Enabled);
        Assert.False(updated.CompatibilityOnly);
        updated.Enabled = true;
        AssertValue(restored, updated, 14);
    }

    [Fact]
    public void ElementFocalLengthOperandDrivesProductionDampedLeastSquares()
    {
        var optic = Lens();
        optic.SurfaceGroup.Items[1].RadiusVariable = true;
        var operand = Row("EFLA", 1); operand.Target = 60;
        optic.MeritFunctionOperands.Add(operand);
        var runtime = new WorkbenchRuntime(optic);
        var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 50);
        Assert.True(result.FinalMerit < result.InitialMerit);
        Assert.InRange(result.FinalMerit, 0, 1e-10);
        Assert.Equal(60, MeritFunctionCatalog.Evaluate(runtime.CurrentOptic, operand).Value, 5);
    }

    private static Optic Lens()
    {
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.Replace([
            Surface("Object", double.PositiveInfinity, new AirMaterial()),
            Surface("Front", 5, new ConstantIndexMaterial("Lens", 1.5)),
            Surface("Back", 50, new AirMaterial()),
            Surface("Image", 0, new AirMaterial())]);
        optic.SurfaceGroup.Items[1].Radius = 50;
        optic.SurfaceGroup.Items[2].Radius = -50;
        optic.SurfaceGroup.Items[1].IsStop = true;
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter;
        optic.Aperture.Value = 10;
        optic.RayAimingEnabled = false;
        return optic;
    }

    private static Optic ThinLens()
    {
        var optic = Lens();
        optic.SurfaceGroup.Replace([
            Surface("Object", double.PositiveInfinity, new AirMaterial()),
            Surface("Lens", 50, new AirMaterial()),
            Surface("Image", 0, new AirMaterial())]);
        optic.SurfaceGroup.Items[1].IsStop = true;
        optic.SurfaceGroup.Items[1].InteractionModel = new ThinLensInteractionModel(50);
        return optic;
    }

    private static OpticalSurface Surface(string label, double thickness, IMaterial after) => new()
    {
        Label = label,
        Thickness = thickness,
        Geometry = new PlaneGeometry(),
        SemiDiameter = 5,
        MechanicalSemiDiameter = 7,
        MaterialAfter = after,
        MaterialBefore = new AirMaterial(),
        InteractionModel = new RefractiveReflectiveInteractionModel(),
        CoatingModel = new NoneCoatingModel()
    };

    private static MeritOperandDefinition Row(string code, int first, int second = 0,
        double d1 = 0, double d2 = 0, double d3 = 0, double d4 = 0) => new()
        {
            Type = code,
            Surface = first,
            Wavelength = second,
            ZemaxIntegerParameters = [first, second],
            ZemaxDataParameters = [d1, d2, d3, d4],
            Weight = 2
        };

    private static void AssertValue(Optic optic, MeritOperandDefinition row, double expected)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.True(string.IsNullOrEmpty(result.Error), $"{row.Type}: {result.Error}");
        Assert.Equal(expected, result.Value, 9);
        Assert.Equal(row.Weight * Math.Pow(expected - row.Target, 2), result.Contribution, 7);
        var restored = MeritFunctionCatalog.Evaluate(Optic.FromSnapshot(optic.ToSnapshot()), row.Clone());
        Assert.True(string.IsNullOrEmpty(restored.Error), restored.Error);
        Assert.Equal(result.Value, restored.Value, 9);
    }

    private static void AssertInvalid(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.NotEmpty(result.Error);
        Assert.True(double.IsNaN(result.Value));
        Assert.True(double.IsPositiveInfinity(result.Contribution));
    }
}
