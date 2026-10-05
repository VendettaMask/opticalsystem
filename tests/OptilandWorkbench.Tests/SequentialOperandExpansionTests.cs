using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Application.Runtime;
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
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class SequentialOperandExpansionTests
{
    private static readonly string[] Codes = [
        "REAZ", "REAA", "REAB", "REAC", "RENA", "RENB", "RENC", "RETX", "RETY",
        "RAID", "RAIN", "RAED", "RAEN", "OPTH", "PLEN", "SAGX", "SAGY", "NORX",
        "NORY", "NORZ", "NORD", "GTCE", "MNPD", "MXPD", "AMAG", "LINV", "PIMH", "OBSN"];

    public static TheoryData<string> NewCodes => new(Codes);

    [Theory]
    [MemberData(nameof(NewCodes))]
    public async Task NewOperandsImportAsExecutableAndPreserveRawSlotsInNativeRoundTrip(string code)
    {
        var source = $"""
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
            """;
        var optic = OpticalFormatCatalog.Import(source, ".zmx");
        var operand = Assert.Single(optic.MeritFunctionOperands);
        Assert.True(operand.Enabled);
        Assert.False(operand.CompatibilityOnly);
        Assert.Equal(ZemaxOperandSupportLevel.Executable, ZemaxOperandRegistry.Get(code).SupportLevel);
        var path = Path.Combine(Path.GetTempPath(), $"operand-{Guid.NewGuid():N}.staropt");
        Optic restored;
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            restored = (await StarOptProjectStore.LoadAsync(path)).Configurations[0];
        }
        finally { File.Delete(path); }
        var row = Assert.Single(restored.MeritFunctionOperands);
        Assert.Equal(code, row.Type);
        Assert.Equal(new[] { 1, 2 }, row.ZemaxIntegerParameters);
        Assert.Equal(new[] { 0.125, -0.25, 0.375, -0.5 }, row.ZemaxDataParameters);
        Assert.Equal(0.625, row.Target);
        Assert.Equal(2.5, row.Weight);
        Assert.True(row.Enabled);
        Assert.False(row.CompatibilityOnly);
    }

    [Fact]
    public void ApplicationPublishesExecutableHelpAndOperandSpecificParameterSlots()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var reference = application.Optimization.GetMeritOperandTypes();
        foreach (var code in Codes)
        {
            var item = Assert.Single(reference, entry => entry.Code == code);
            Assert.False(item.CompatibilityOnly);
            Assert.DoesNotContain("不会执行", item.Calculation);
        }
        Assert.True(ZemaxOperandRegistry.Get("PLEN").UsesSlotAs("Int2", ZemaxOperandParameterValueKind.EndSurface));
        Assert.True(ZemaxOperandRegistry.Get("NORZ").UsesSlotAs("Data3", ZemaxOperandParameterValueKind.Flag));
        Assert.True(ZemaxOperandRegistry.Get("PIMH").UsesSlotAs("Int2", ZemaxOperandParameterValueKind.Wavelength));
    }

    [Fact]
    public void PreviouslyPreservedRowsBecomeEditableWithoutEnablingThemSilently()
    {
        var optic = Plate();
        optic.MeritFunctionOperands.Add(new MeritOperandDefinition
        {
            Type = "REAC",
            Surface = 1,
            Enabled = false,
            CompatibilityOnly = true,
            ZemaxIntegerParameters = [1, 0],
            ZemaxDataParameters = [0, 0, 0, 0]
        });
        var restored = Optic.FromSnapshot(optic.ToSnapshot());
        var row = Assert.Single(restored.MeritFunctionOperands);
        Assert.False(row.Enabled);
        Assert.False(row.CompatibilityOnly);
        row.Enabled = true;
        AssertValue(restored, row, 1);
    }

    [Theory]
    [InlineData("REAZ", 0)]
    [InlineData("REAA", 0)]
    [InlineData("REAB", 1.0 / 3)]
    [InlineData("REAC", 0.9428090415820634)]
    [InlineData("RENA", 0)]
    [InlineData("RENB", 0)]
    [InlineData("RENC", 1)]
    [InlineData("RETX", 0)]
    [InlineData("RETY", 0.3535533905932738)]
    [InlineData("RAID", 30)]
    [InlineData("RAIN", 0.8660254037844386)]
    [InlineData("RAED", 19.47122063449069)]
    [InlineData("RAEN", 0.9428090415820634)]
    public void RefractionOperandsMatchIndependentSnellReference(string code, double expected)
    {
        var optic = Plate();
        optic.Fields[0].Y = 30;
        AssertValue(optic, Row(code, 1, 0, 0, 1), expected);
    }

    [Fact]
    public void ExplicitAxialCoordinatesDoNotSelectTheFirstOffAxisField()
    {
        var optic = Plate();
        optic.Fields[0].Y = 30;
        AssertValue(optic, Row("REAB", 1), 0);
    }

    [Fact]
    public void LocalDirectionAndGlobalNormalRespectSurfaceRotation()
    {
        var optic = Plate();
        optic.SurfaceGroup.Items[1].MaterialAfter = new AirMaterial();
        optic.SurfaceGroup.Items[1].CoordinateSystem = new CoordinateSystem(Vector3D.Zero, RotationYDegrees: 30);
        AssertValue(optic, Row("REAA", 1), -0.5);
        AssertValue(optic, Row("REAC", 1), Math.Sqrt(3) / 2);
        AssertValue(optic, Row("REAZ", 1), 0);
        optic.GlobalReferenceSurfaceNumber = 2; // Unrotated image frame for the global normal measurement.
        AssertValue(optic, Row("NORX", 1), 0);
        AssertValue(optic, Row("NORX", 1, 0, 0, 0, 1), 0.5);
        AssertValue(optic, Row("NORZ", 1, 0, 0, 0, 1), Math.Sqrt(3) / 2);
    }

    [Fact]
    public void ReflectedDirectionIsSignedWhileIncidenceAndExitanceArePositive()
    {
        var optic = Plate();
        optic.Fields[0].Y = 30;
        optic.SurfaceGroup.Items[1].InteractionModel = new RefractiveReflectiveInteractionModel(true);
        AssertValue(optic, Row("REAC", 1, 0, 0, 1), -Math.Sqrt(3) / 2);
        AssertValue(optic, Row("RAID", 1, 0, 0, 1), 30);
        AssertValue(optic, Row("RAED", 1, 0, 0, 1), 30);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TraceMetadataPreservesIncidentDirectionAcrossTotalInternalReflection(bool batched)
    {
        var optic = Plate();
        optic.SurfaceGroup.Items[0].MaterialAfter = new ConstantIndexMaterial("Dense", 1.5);
        optic.SurfaceGroup.Items[1].MaterialAfter = new AirMaterial();
        var direction = new Vector3D(0, Math.Sqrt(3) / 2, 0.5);
        using var trace = optic.SequentialRayTracer.Trace(
            new RealRayBundle([new RealRay(new Vector3D(0, 0, -10), direction, 587.6)]),
            TraceRequest.Selected([1]) with { UseBatchedBackend = batched });
        Assert.True(trace.TryGetSample(0, 1, out var sample));
        Assert.Equal(RayInteractionKind.TotalInternalReflection, sample.InteractionKind);
        Assert.Equal(direction.X, sample.IncidentDirection!.Value.X, 12);
        Assert.Equal(direction.Y, sample.IncidentDirection!.Value.Y, 12);
        Assert.Equal(direction.Z, sample.IncidentDirection!.Value.Z, 12);
        Assert.Equal(-0.5, sample.Direction.Z, 12);
        Assert.Equal(sample, RayTraceSampleValue.FromRayTraceSample(sample.ToRayTraceSample()));
        Assert.Equal(30, sample.PhaseInclusiveOpticalPathLength!.Value, 10);
    }

    [Theory]
    [InlineData(false, 15)]
    [InlineData(true, 25)]
    public void OpticalPathUsesCorrectFiniteOrInfiniteReference(bool finite, double expected)
    {
        var optic = Plate(finite);
        AssertValue(optic, Row("OPTH", 2), expected);
        AssertValue(optic, Row("PLEN", 1, 2), 15);
        AssertValue(optic, Row("PLEN", 2, 1), -15);
        AssertValue(optic, Row("PLEN", 2, 2), 0);
    }

    [Fact]
    public void OpticalPathIncludesPhaseInMillimetersBeforeRelativeOpdNormalization()
    {
        var optic = Plate(finite: true);
        optic.SurfaceGroup.Items[1].InteractionModel = new PhaseInteractionModel(new ConstantPhaseProfile(2 * Math.PI));
        AssertValue(optic, Row("OPTH", 2), 25 - 0.0005876);
        AssertValue(optic, Row("PLEN", 0, 2), 25 - 0.0005876);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(-20)]
    public void SphericalSagAndNormalMatchAnalyticalGeometry(double radius)
    {
        var optic = Plate();
        var surface = optic.SurfaceGroup.Items[1];
        surface.Geometry = new StandardGeometry(radius);
        surface.SemiDiameter = 5;
        var sag = Math.Sign(radius) * (20 - Math.Sqrt(375));
        AssertValue(optic, Row("SAGX", 1), sag);
        AssertValue(optic, Row("SAGY", 1), sag);
        AssertValue(optic, Row("NORX", 1, 0, 3, 4), -3 / radius);
        AssertValue(optic, Row("NORY", 1, 0, 3, 4), -4 / radius);
        AssertValue(optic, Row("NORZ", 1, 0, 3, 4), Math.Sqrt(375) / 20);
        AssertValue(optic, Row("REAZ", 1, 0, 0, 0, 0, 1), sag);
        AssertValue(optic, Row("RENB", 1, 0, 0, 0, 0, 1), -5 / radius);
    }

    [Fact]
    public void SagAxesDifferForBiconicGeometryAndNormalDistanceUsesActualIntersection()
    {
        var optic = Plate();
        var surface = optic.SurfaceGroup.Items[1];
        surface.Geometry = new BiconicGeometry(20, 40);
        surface.SemiDiameter = 5;
        AssertValue(optic, Row("SAGX", 1), 20 - Math.Sqrt(375));
        AssertValue(optic, Row("SAGY", 1), 40 - Math.Sqrt(1575));
        surface.Geometry = new PlaneGeometry();
        surface.CoordinateSystem = new CoordinateSystem(Vector3D.Zero, RotationYDegrees: 30);
        AssertValue(optic, Row("NORD", 1), 10 / (Math.Sqrt(3) / 2));
        optic.SurfaceGroup.Items[2].CoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, -10));
        AssertInvalid(optic, Row("NORD", 1));
    }

    [Fact]
    public void CatalogThermalAndPartialDispersionConstraintsUseCatalogValuesAndBoundaries()
    {
        var optic = Plate();
        optic.SurfaceGroup.Items[1].MaterialAfter = CatalogGlass(7.1, -0.02);
        optic.SurfaceGroup.Items[2].MaterialAfter = CatalogGlass(0, 0.03);
        AssertValue(optic, Row("GTCE", 1), 7.1);
        AssertValue(optic, Row("GTCE", 2), 0);
        AssertValue(optic, Row("MNPD", 1, 2), -0.02);
        AssertValue(optic, Row("MXPD", 1, 2), 0.03);
        var lower = Row("MNPD", 1, 2);
        lower.Target = -0.04;
        AssertValue(optic, lower, -0.04);
        var upper = Row("MXPD", 1, 2);
        upper.Target = 0.05;
        AssertValue(optic, upper, 0.05);
        optic.SurfaceGroup.Items[2].MaterialAfter = new AirMaterial();
        AssertValue(optic, Row("MXPD", 1, 2), 0); // Bound satisfied: returns the target.
        optic.SurfaceGroup.Items[1].MaterialAfter = CatalogGlass(null, null);
        AssertInvalid(optic, Row("GTCE", 1));
        AssertInvalid(optic, Row("MNPD", 1, 2));
        AssertInvalid(optic, Row("MXPD", 1, 2));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(80)]
    public void FirstOrderImageHeightUsesParaxialFocusRatherThanDefocusedImagePlane(double imageDistance)
    {
        var optic = ThinLens(imageDistance);
        AssertValue(optic, Row("PIMH", 0), 50 * Math.Tan(5 * Math.PI / 180));
        AssertValue(optic, Row("LINV", 0), 5 * Math.Tan(5 * Math.PI / 180));
        AssertValue(optic, Row("AMAG", 0), 1);
    }

    [Fact]
    public void TelescopeAngularMagnificationHasExpectedSignAndRatio()
    {
        var optic = ThinLens(75);
        optic.SurfaceGroup.Items[2].InteractionModel = new ThinLensInteractionModel(25);
        AssertValue(optic, Row("AMAG", 0), -2);
    }

    [Fact]
    public void ObjectNumericalApertureRequiresFiniteObjectAndUsesActualMarginalAngle()
    {
        var optic = ThinLens(100);
        AssertInvalid(optic, Row("OBSN", 0));
        optic.SurfaceGroup.Items[0].Thickness = 100;
        optic.SurfaceGroup.Renumber();
        AssertValue(optic, Row("OBSN", 0), Math.Sin(Math.Atan(0.05)));
        optic.SurfaceGroup.Items[1].CoordinateSystem = optic.SurfaceGroup.Items[1].CoordinateSystem with { RotationYDegrees = 5 };
        AssertInvalid(optic, Row("LINV", 0));
    }

    [Fact]
    public void InvalidParametersAndBlockedRaysAreErrorsRatherThanSuccessfulZeroValues()
    {
        var optic = Plate();
        AssertInvalid(optic, Row("REAC", 99));
        AssertInvalid(optic, Row("REAC", 1, 9));
        AssertInvalid(optic, Row("REAC", 1, 0, double.NaN));
        AssertInvalid(optic, Row("REAC", 1, 0, 0, 0, 1.1));
        AssertInvalid(optic, Row("NORZ", 1, 0, 0, 0, 2));
        AssertInvalid(optic, Row("OPTH", 0));
        AssertInvalid(optic, Row("PLEN", 0, 2));
        optic.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(0.1);
        AssertInvalid(optic, Row("REAC", 1, 0, 0, 0, 0, 1));
        optic.SurfaceGroup.Items[1].Geometry = new StandardGeometry(2);
        AssertInvalid(optic, Row("SAGX", 1));
        AssertInvalid(optic, Row("NORZ", 1, 0, 5));
    }

    [Fact]
    public void SameBatchDoesNotReuseStaleRaySamplesAfterMutation()
    {
        var optic = Plate();
        optic.Fields[0].Y = 30;
        var cache = new RayTraceCache(maximumEntries: 2, maximumSamples: 10);
        optic.ConfigureRayTraceCache(cache, opticRevision: 1);
        using var batch = MeritFunctionCatalog.BeginEvaluationBatch();
        AssertValue(optic, Row("REAB", 1, 0, 0, 1), 1.0 / 3);
        AssertValue(optic, Row("REAB", 1, 0, 0, 1), 1.0 / 3);
        Assert.True(cache.Statistics.Hits > 0);
        optic.SurfaceGroup.Items[1].MaterialAfter = new ConstantIndexMaterial("Changed", 2);
        AssertValue(optic, Row("REAB", 1, 0, 0, 1), 0.25);
    }

    [Fact]
    public void SelectedAndPrimaryWavelengthsUseActualDispersion()
    {
        var optic = Plate();
        optic.Fields[0].Y = 30;
        optic.Wavelengths[0].IsPrimary = false;
        optic.Wavelengths.Add(new Wavelength { Nanometers = 700, IsPrimary = true, Weight = 1 });
        optic.SurfaceGroup.Items[1].MaterialAfter = new CatalogGlassMaterial(
            "TEST:Dispersive", "TEST", "tabulated n", 400, 800,
            refractiveIndexWavelengthsNanometers: [400, 800], refractiveIndices: [1.5, 1.6]);
        AssertValue(optic, Row("REAB", 1, 1, 0, 1), 0.5 / 1.5469);
        AssertValue(optic, Row("REAB", 1, 0, 0, 1), 0.5 / 1.575);
        AssertValue(optic, Row("PLEN", 1, 2), 15.75);
    }

    [Fact]
    public void ImageHeightFieldsWorkWhenTheEntrancePupilCoincidesWithTheFirstSurface()
    {
        var optic = ThinLens(50);
        optic.FieldDefinition = FieldDefinitionKind.ParaxialImageHeight;
        optic.Fields[0].Y = 4;
        AssertValue(optic, Row("PIMH", 0), 4);
        AssertValue(optic, Row("LINV", 0), 0.4);
        optic.SurfaceGroup.Items[0].Thickness = 0;
        optic.SurfaceGroup.Renumber();
        AssertInvalid(optic, Row("OBSN", 0));
    }

    [Fact]
    public void RayOperandsHonorCancellation()
    {
        var optic = Plate();
        using var token = new CancellationTokenSource();
        token.Cancel();
        using var scope = ComputationCancellation.Push(token.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(optic, Row("REAC", 1)));
    }

    [Theory]
    [MemberData(nameof(NewCodes))]
    public void EveryNewOperandRejectsInvalidReferencesOrUnsupportedConjugate(string code)
    {
        var row = Row(code, 999, 999);
        AssertInvalid(Plate(), row);
    }

    [Fact]
    public void NewSurfaceOperandDrivesMarkedCurvatureOptimization()
    {
        var optic = ThinLens(50);
        var surface = optic.SurfaceGroup.Items[1];
        surface.InteractionModel = new RefractiveReflectiveInteractionModel();
        surface.Radius = 40;
        surface.SemiDiameter = 5;
        surface.RadiusVariable = true;
        var operand = Row("SAGY", 1);
        operand.Target = 20 - Math.Sqrt(375);
        optic.MeritFunctionOperands.Add(operand);
        var runtime = new WorkbenchRuntime(optic);

        var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 50);

        Assert.True(result.FinalMerit < result.InitialMerit);
        Assert.InRange(result.FinalMerit, 0, 1e-12);
        Assert.Equal(20, runtime.Surfaces[1].Radius, 5);
        AssertValue(runtime.CurrentOptic, operand, operand.Target);
    }

    private static Optic Plate(bool finite = false)
    {
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.Replace([
            Surface("Object", finite ? 10 : double.PositiveInfinity, new AirMaterial()),
            Surface("Front", 10, new ConstantIndexMaterial("Plate", 1.5)),
            Surface("Image", 0, new AirMaterial())
        ]);
        optic.SurfaceGroup.Items[1].IsStop = true;
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter;
        optic.Aperture.Value = 10;
        optic.RayAimingEnabled = false;
        return optic;
    }

    private static Optic ThinLens(double imageDistance)
    {
        var optic = Plate();
        var lens = optic.SurfaceGroup.Items[1];
        lens.MaterialAfter = new AirMaterial();
        lens.InteractionModel = new ThinLensInteractionModel(50);
        lens.Thickness = imageDistance;
        optic.SurfaceGroup.Renumber();
        optic.Fields[0].Y = 5;
        return optic;
    }

    private static OpticalSurface Surface(string label, double thickness, IMaterial after) => new()
    {
        Label = label,
        Thickness = thickness,
        Geometry = new PlaneGeometry(),
        SemiDiameter = 50,
        MaterialBefore = new AirMaterial(),
        MaterialAfter = after,
        InteractionModel = new RefractiveReflectiveInteractionModel(),
        CoatingModel = new NoneCoatingModel()
    };

    private static CatalogGlassMaterial CatalogGlass(double? alpha, double? deviation) => new(
        "TEST:Glass", "TEST", "tabulated n", 400, 800,
        refractiveIndexWavelengthsNanometers: [400, 800], refractiveIndices: [1.5, 1.5],
        zemaxData: new OpticalGlassDefinition { ThermalExpansionLow = alpha, RelativePartialDispersionDeviation = deviation });

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
        var restored = Optic.FromSnapshot(optic.ToSnapshot());
        var roundTrip = MeritFunctionCatalog.Evaluate(restored, row.Clone());
        Assert.True(string.IsNullOrEmpty(roundTrip.Error), $"{row.Type} round trip: {roundTrip.Error}");
        Assert.Equal(result.Value, roundTrip.Value, 9);
    }

    private static void AssertInvalid(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.NotEmpty(result.Error);
        Assert.True(double.IsNaN(result.Value));
        Assert.True(double.IsPositiveInfinity(result.Contribution));
    }
}
