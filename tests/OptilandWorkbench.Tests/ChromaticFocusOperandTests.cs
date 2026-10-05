using System.Text.Json;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
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

public sealed class ChromaticFocusOperandTests
{
    public static TheoryData<string> Codes => new("LONA", "AXCL", "LACL", "SPCH");

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task ImportAndNativeProjectKeepWavelengthSlotsAndState(string code)
    {
        var optic = OpticalFormatCatalog.Import($"""
            MODE SEQ
            ENPD 20
            WAVM 1 0.5 1
            WAVM 2 0.6 1
            SURF 0
              DISZ INFINITY
            SURF 1
              STOP
              CURV 0.01
              DISZ 250
            SURF 2
              DISZ 0
            {code} 1 2 0.7 0.25 0.375 0.5 0.625 2.5 0 0
            """, ".zmx");
        var path = Path.Combine(Path.GetTempPath(), $"chromatic-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var row = Assert.Single((await StarOptProjectStore.LoadAsync(path)).Configurations[0].MeritFunctionOperands);
            Assert.Equal(code, row.Type);
            Assert.Equal(new[] { 1, 2 }, row.ZemaxIntegerParameters);
            Assert.Equal(new[] { .7, .25, .375, .5 }, row.ZemaxDataParameters);
            Assert.Equal(.625, row.Target); Assert.Equal(2.5, row.Weight);
            Assert.True(row.Enabled); Assert.False(row.CompatibilityOnly);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void CompatibilityUpgradeKeepsDisabledState(string code)
    {
        var optic = Lens(); var row = Row(code, 1, 2);
        row.CompatibilityOnly = true; row.Enabled = false; optic.MeritFunctionOperands.Add(row);
        var restored = Assert.Single(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands);
        Assert.False(restored.CompatibilityOnly); Assert.False(restored.Enabled);
    }

    [Theory]
    [InlineData(1, 50)]
    [InlineData(2, 16.66666666666667)]
    [InlineData(0, 50)]
    public void ParaxialDefocusMatchesSingleRefractingSurface(int wave, double expected) => Value(Lens(), Row("LONA", 0, wave), expected);

    [Theory]
    [InlineData(1, .25)]
    [InlineData(1, .7)]
    [InlineData(1, 1)]
    [InlineData(2, .25)]
    [InlineData(2, .7)]
    [InlineData(2, 1)]
    public void RealFocusMatchesIndependentSphericalSnellIntersection(int wave, double zone) =>
        Value(Lens(), Row("LONA", 0, wave, zone), SnellFocus(wave == 1 ? 1.5 : 1.6, zone) - 250);

    [Theory]
    [InlineData(0)]
    [InlineData(.5)]
    [InlineData(1)]
    public void AxialColorHasSignedWavelengthOrderAndImageTranslationInvariance(double zone)
    {
        var optic = Lens();
        var expected = zone == 0 ? 100.0 / 3 : SnellFocus(1.5, zone) - SnellFocus(1.6, zone);
        Value(optic, Row("AXCL", 1, 2, zone), expected);
        Value(optic, Row("AXCL", 2, 1, zone), -expected);
        Value(optic, Row("AXCL", 0, 2, zone), expected);
        optic.SurfaceGroup.Items[1].Thickness += 20; optic.SurfaceGroup.Renumber();
        Value(optic, Row("AXCL", 1, 2, zone), expected);
        var focus = zone == 0 ? 300 : SnellFocus(1.5, zone);
        Value(optic, Row("LONA", 0, 1, zone), focus - 270);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(.5)]
    [InlineData(1)]
    public void SpherochromatismSubtractsParaxialColor(double zone)
    {
        var expected = zone == 0 ? 0 : SnellFocus(1.5, zone) - SnellFocus(1.6, zone) - 100.0 / 3;
        Value(Lens(), Row("SPCH", 1, 2, zone), expected);
        Value(Lens(), Row("SPCH", 2, 1, zone), -expected);
    }

    [Fact]
    public void LateralColorUsesCurrentImagePlaneAndPositiveRadialField()
    {
        var optic = Lens();
        optic.Fields.Clear(); optic.Fields.Add(new FieldPoint { X = -3, Y = -4 });
        var expected = 250 * Math.Tan(5 * Math.PI / 180) * (1 / 1.6 - 1 / 1.5);
        Value(optic, Row("LACL", 1, 2), expected);
        Value(optic, Row("LACL", 2, 1), -expected);
        Value(optic, Row("LACL", 0, 2), expected);
        optic.SurfaceGroup.Items[1].Thickness = 125; optic.SurfaceGroup.Renumber();
        Value(optic, Row("LACL", 1, 2), expected / 2);
        optic.Fields.Clear(); optic.Fields.Add(new FieldPoint());
        Value(optic, Row("LACL", 1, 2), 0);
    }

    [Fact]
    public void LongitudinalFocusIgnoresOffAxisEditorField()
    {
        var optic = Lens(); optic.Fields.Clear(); optic.Fields.Add(new FieldPoint { Y = 30 });
        Value(optic, Row("LONA", 0, 1, 1), SnellFocus(1.5, 1) - 250);
    }

    [Fact]
    public void PrimarySelectionAndWaveIndicesAreIndependentOfWavelengthSorting()
    {
        var optic = Lens(); optic.Wavelengths[0].IsPrimary = false; optic.Wavelengths[1].IsPrimary = true;
        Value(optic, Row("LONA", 987, 0), 100.0 / 6); // Unused Int1 is not a surface.
        Value(optic, Row("AXCL", 0, 1), -100.0 / 3);
        optic.Wavelengths[0].Nanometers = 600; optic.Wavelengths[1].Nanometers = 500;
        Value(optic, Row("AXCL", 1, 2), -100.0 / 3);
    }

    [Fact]
    public void FiniteConjugateUsesObjectDistanceAndCoincidentObjectIsNotInfinity()
    {
        var optic = Lens();
        optic.SurfaceGroup.Items[0].Thickness = 100;
        optic.SurfaceGroup.Items[1].Geometry = new PlaneGeometry();
        optic.SurfaceGroup.Items[1].InteractionModel = new ThinLensInteractionModel(50);
        optic.SurfaceGroup.Items[1].MaterialAfter = new AirMaterial();
        optic.SurfaceGroup.Items[2].MaterialAfter = new AirMaterial();
        optic.SurfaceGroup.Items[1].Thickness = 75; optic.SurfaceGroup.Renumber();
        Value(optic, Row("LONA", 0, 1), 25);
        optic.SurfaceGroup.Items[0].Thickness = 0; optic.SurfaceGroup.Renumber();
        Invalid(optic, Row("LONA", 0, 1));
        optic.SurfaceGroup.Items[0].Thickness = double.PositiveInfinity; optic.SurfaceGroup.Renumber();
        Value(optic, Row("LONA", 0, 1), -25);
    }

    [Theory]
    [InlineData("LONA", -1)]
    [InlineData("AXCL", -1)]
    [InlineData("SPCH", -1)]
    [InlineData("LONA", 1.01)]
    [InlineData("AXCL", 1.01)]
    [InlineData("SPCH", double.NaN)]
    [InlineData("LONA", double.PositiveInfinity)]
    public void InvalidZoneIsAnError(string code, double zone) => Invalid(Lens(), Row(code, 1, 2, zone));

    [Theory]
    [MemberData(nameof(Codes))]
    public void InvalidWavelengthIsAnError(string code)
    {
        Invalid(Lens(), Row(code, 1, 3)); Invalid(Lens(), Row(code, 1, -1));
        if (code != "LONA") { Invalid(Lens(), Row(code, -1, 2)); Invalid(Lens(), Row(code, 3, 2)); }
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void AfocalDecenteredRotatedAndAsphericModelsAreRejected(string code)
    {
        var optic = Lens(); optic.ImageSpaceAfocal = true; Invalid(optic, Row(code, 1, 2)); optic.ImageSpaceAfocal = false;
        var surface = optic.SurfaceGroup.Items[1];
        surface.CoordinateSystem = new(new(1, 0, 0)); Invalid(optic, Row(code, 1, 2));
        surface.CoordinateSystem = new(new(0, 0, 0), RotationXDegrees: 1); Invalid(optic, Row(code, 1, 2));
        surface.CoordinateSystem = new(new(0, 0, 0));
        surface.Geometry = new EvenAsphereGeometry(100, 0, [.01]); Invalid(optic, Row(code, 1, 2));
    }

    [Theory]
    [InlineData("LONA")]
    [InlineData("AXCL")]
    [InlineData("SPCH")]
    public void UndefinedFocusAndClippedRequiredRayCannotBecomeSuccessfulZero(string code)
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(.1);
        Invalid(optic, Row(code, 1, 2, 1));
        optic.SurfaceGroup.Items[1].PhysicalAperture = null;
        optic.SurfaceGroup.Items[1].Geometry = new PlaneGeometry();
        Invalid(optic, Row(code, 1, 2)); Invalid(optic, Row(code, 1, 2, 1));
    }

    [Fact]
    public void PoweredOrRefractingImageIsRejected()
    {
        var optic = Lens(); optic.SurfaceGroup.Items[2].Geometry = new StandardGeometry(100);
        Invalid(optic, Row("LONA", 0, 1));
        optic.SurfaceGroup.Items[2].Geometry = new PlaneGeometry(); optic.SurfaceGroup.Items[2].MaterialAfter = new AirMaterial();
        Invalid(optic, Row("LONA", 0, 1));
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void EmptyWavelengthTableIsNotReplacedWithDefaultWavelength(string code)
    {
        var optic = Lens(); optic.Wavelengths.Clear(); Invalid(optic, Row(code, 0, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NonPhysicalIndexIsRejected(double index)
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].MaterialAfter = new ConstantIndexMaterial("Invalid", index);
        Invalid(optic, Row("AXCL", 1, 2));
    }

    [Fact]
    public void ReflectiveAndInconsistentCoordinateModelsAreRejected()
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].IsReflective = true; Invalid(optic, Row("LONA", 0, 1));
        optic.SurfaceGroup.Items[1].IsReflective = false;
        optic.SurfaceGroup.Items[2].CoordinateSystem = new(new(0, 0, 240)); Invalid(optic, Row("LONA", 0, 1));
    }

    [Fact]
    public void InvalidLateralFieldCannotBeSilentlyClamped()
    {
        var optic = Lens(); optic.Fields[0].Y = 90; Invalid(optic, Row("LACL", 1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => optic.Fields[0].Y = double.NaN);
    }

    [Fact]
    public void FormalAxialAndParaxialLateralAnalysisAgreeForSameCookeSnapshot()
    {
        var optic = Optic.CreateCookeTriplet();
        var axial = new AxialAberrationAnalysis(optic, sampleCount: 21).GenerateData();
        for (var wave = 1; wave <= optic.Wavelengths.Count; wave++)
            foreach (var point in axial.PlotSeries[wave - 1].Points)
                Assert.Equal(point.X, Evaluate(optic, Row("LONA", 0, wave, point.Y)), 9);
        var lateral = new LateralColorAnalysis(optic, useRealRays: false, showAiryDisk: false).GenerateData();
        var first = optic.Wavelengths.ToList().FindIndex(w => w.Nanometers == optic.Wavelengths.Min(w => w.Nanometers)) + 1;
        var last = optic.Wavelengths.ToList().FindIndex(w => w.Nanometers == optic.Wavelengths.Max(w => w.Nanometers)) + 1;
        Assert.Equal(lateral.PlotSeries[0].Points[^1].X / 1000, Evaluate(optic, Row("LACL", first, last)), 9);
    }

    [Theory]
    [InlineData(420, 0)]
    [InlineData(440, 1)]
    [InlineData(460, 2)]
    public void LongitudinalOperandMatchesCaptured123456PupilCurve(double nanometers, int column)
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(fixtures, "zemax-123456.ZMX")), ".zmx");
        using var capture = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, "zemax-123456-longitudinal-aberration.json")));
        var series = capture.RootElement.GetProperty("dataSeries")[0];
        var pupil = series.GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        var expected = series.GetProperty("y").EnumerateArray().Select(v => v[column].GetDouble()).ToArray();
        var wave = optic.Wavelengths.ToList().FindIndex(w => Math.Abs(w.Nanometers - nanometers) < 1e-9) + 1;
        Assert.True(wave > 0); Assert.Equal(101, pupil.Length); Assert.Equal(pupil.Length, expected.Length);
        var actual = pupil.Select(p => Evaluate(optic, Row("LONA", 0, wave, p))).ToArray();
        var peak = expected.Max(Math.Abs);
        var nrmse = Math.Sqrt(actual.Zip(expected, (a, b) => Math.Pow((a - b) / peak, 2)).Average());
        // Existing fixed-file analysis acceptance: <=1% of the captured curve peak.
        // This is not pointwise equality or a native MFE parameter-slot capture.
        Assert.True(nrmse <= .01, $"Captured {nanometers} nm LONA curve NRMSE={nrmse:P6}");
    }

    [Fact]
    public void RealRayCacheReusesResultsAndDetachesAfterOpticalEdit()
    {
        var optic = Lens(); var cache = new RayTraceCache(32, 512); optic.ConfigureRayTraceCache(cache, opticRevision: 1);
        var row = Row("LONA", 0, 1, 1);
        Value(optic, row, SnellFocus(1.5, 1) - 250); Value(optic, row, SnellFocus(1.5, 1) - 250);
        Assert.True(cache.Statistics.Hits > 0);
        optic.Aperture.Value = 10; Value(optic, row, SnellFocus(1.5, .5) - 250);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void ApplicationEditingPreservesBothWavelengthParameters(string code)
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, true, code, 1, 2, 0, 0, 0, 0, 0, 0, 1, 0, 0, "",
            ZemaxInt1: 1, ZemaxInt2: 2, ZemaxData1: .7)]);
        var row = Assert.Single(app.Optimization.GetMeritFunction());
        Assert.Empty(row.Error); Assert.Equal(1, row.ZemaxInt1); Assert.Equal(2, row.ZemaxInt2);
        app.Optimization.SetMeritFunction([row]);
        Assert.Equal(row.Value, Assert.Single(app.Optimization.GetMeritFunction()).Value);
        Assert.False(Assert.Single(app.Optimization.GetMeritOperandTypes(), t => t.Code == code).CompatibilityOnly);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void CancellationPropagates(string code)
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(Lens(), Row(code, 1, 2)));
    }

    [Fact]
    public void LongitudinalFocusDrivesProductionDls()
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].ThicknessVariable = true;
        var row = Row("LONA", 0, 1); optic.MeritFunctionOperands.Add(row);
        var runtime = new WorkbenchRuntime(optic);
        var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 50);
        Assert.True(result.FinalMerit < result.InitialMerit);
        Assert.Equal(0, Evaluate(runtime.CurrentOptic, row), 5);
        Assert.Equal(300, runtime.CurrentOptic.SurfaceGroup.Items[1].Thickness, 5);
    }

    private static double SnellFocus(double index, double zone)
    {
        var height = 10 * zone;
        var sag = 100 - Math.Sqrt(10000 - height * height);
        var deviation = Math.Asin(height / 100) - Math.Asin(height / (100 * index));
        return sag + height / Math.Tan(deviation);
    }

    private static Optic Lens()
    {
        var optic = Optic.CreateBlank();
        var glass = new CatalogGlassMaterial("TEST:Dispersive", "TEST", "tabulated n", 400, 800,
            refractiveIndexWavelengthsNanometers: [500, 600], refractiveIndices: [1.5, 1.6]);
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity, Geometry = new PlaneGeometry(), MaterialAfter = new AirMaterial(), SemiDiameter = 100 },
            new OpticalSurface { Thickness = 250, Geometry = new StandardGeometry(100), MaterialAfter = glass, SemiDiameter = 50, IsStop = true },
            new OpticalSurface { Thickness = 0, Geometry = new PlaneGeometry(), MaterialAfter = glass, SemiDiameter = 100 }
        ]);
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter; optic.Aperture.Value = 20; optic.RayAimingEnabled = false;
        optic.Wavelengths.Clear();
        optic.Wavelengths.Add(new Wavelength { Nanometers = 500, IsPrimary = true });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 600, IsPrimary = false });
        optic.Fields.Clear(); optic.Fields.Add(new FieldPoint { Y = 5 });
        return optic;
    }

    private static MeritOperandDefinition Row(string code, int first, int second, double zone = 0) => new()
    {
        Type = code,
        Surface = first,
        Wavelength = second,
        Hx = zone,
        ZemaxIntegerParameters = [first, second],
        ZemaxDataParameters = [zone, 0, 0, 0],
        Weight = 2
    };
    private static double Evaluate(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.True(string.IsNullOrEmpty(result.Error), result.Error);
        return result.Value;
    }
    private static void Value(Optic optic, MeritOperandDefinition row, double expected)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.True(string.IsNullOrEmpty(result.Error), result.Error);
        Assert.Equal(expected, result.Value, 8); Assert.Equal(2 * Math.Pow(expected - row.Target, 2), result.Contribution, 6);
        Assert.Equal(result.Value, Evaluate(Optic.FromSnapshot(optic.ToSnapshot()), row.Clone()), 9);
    }
    private static void Invalid(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.NotEmpty(result.Error); Assert.True(double.IsNaN(result.Value)); Assert.True(double.IsPositiveInfinity(result.Contribution));
    }
}
