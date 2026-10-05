using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class ManufacturingProfileOperandTests
{
    public static TheoryData<string> Codes => new("VOLU", "TMAS", "DSAG", "DSLP", "DCRV");
    public static TheoryData<string> ProfileCodes => new("DSAG", "DSLP", "DCRV");
    private static Optic Lens()
    {
        var optic = new Optic("Manufacturing reference");
        optic.Fields.Add(new FieldPoint()); optic.Wavelengths.Add(new Wavelength { Nanometers = 550, Weight = 1, IsPrimary = true });
        optic.SurfaceGroup.ImportLegacySurfaces([
            new OpticalSurface { Thickness = double.PositiveInfinity, SemiDiameter = 5 },
            new OpticalSurface { Thickness = 4, SemiDiameter = 3, IsStop = true },
            new OpticalSurface { Thickness = 10, SemiDiameter = 5 },
            new OpticalSurface { Thickness = 0, SemiDiameter = 5 }
        ]);
        optic.SurfaceGroup.Items[1].MaterialAfter = Glass(2.5);
        return optic;
    }
    private static CatalogGlassMaterial Glass(double? density) => new("TEST:Dense", "TEST", "tabulated n", 400, 800,
        refractiveIndexWavelengthsNanometers: [400, 800], refractiveIndices: [1.5, 1.5],
        zemaxData: new OpticalGlassDefinition { Density = density });
    private static MeritOperandDefinition Row(string code, int data = 1, int sample = 1, int remove = 0, int orientation = 0) => new()
    {
        Type = code,
        Surface = 1,
        Wavelength = data,
        ZemaxIntegerParameters = [1, data],
        ZemaxDataParameters = code is "VOLU" or "TMAS" ? [0, 0, 0, 0]
            : code == "DSAG" ? [sample, 0, remove, 0] : [sample, 0, remove, 0, orientation]
    };
    private static double Value(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.True(string.IsNullOrEmpty(result.Error), result.Error); return result.Value;
    }
    private static void Invalid(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.NotEmpty(result.Error); Assert.True(double.IsNaN(result.Value)); Assert.True(double.IsPositiveInfinity(result.Contribution));
        Assert.Throws<OptimizationEvaluationException>(() => MeritFunctionCatalog.EvaluateOptimizationValues(optic, [row]));
    }

    [Theory]
    [InlineData("VOLU", 0)]
    [InlineData("TMAS", 0)]
    [InlineData("VOLU", 1)]
    [InlineData("TMAS", 1)]
    public void PlaneElementUsesLargerSelectedDiameterAndCorrectUnits(string code, int mode)
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].MechanicalSemiDiameter = 6;
        optic.SurfaceGroup.Items[2].MechanicalSemiDiameter = 7;
        var row = Row(code); row.ZemaxDataParameters[1] = mode;
        var radius = mode == 0 ? 7 : 5; var expected = Math.PI * radius * radius * 4 / 1000;
        if (code == "TMAS") expected *= 2.5;
        Assert.Equal(expected, Value(optic, row), 12);
        row.Target = expected + .1; row.Weight = 3;
        Assert.Equal(.03, MeritFunctionCatalog.Evaluate(optic, row).Contribution, 12);
    }

    [Theory]
    [InlineData(50, -60)]
    [InlineData(-50, 60)]
    [InlineData(0, -50)]
    [InlineData(1e12, -1e12)]
    public void SphericalVolumeMatchesIndependentCapConstruction(double frontRadius, double backRadius)
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].Radius = frontRadius; optic.SurfaceGroup.Items[2].Radius = backRadius;
        static double UnderFace(double r, double aperture, double outside)
        {
            if (r == 0) return 0;
            var h = aperture * aperture / (Math.Abs(r) + Math.Sqrt(r * r - aperture * aperture));
            var cap = Math.PI * h * h * (Math.Abs(r) - h / 3);
            return Math.Sign(r) * (Math.PI * outside * outside * h - cap);
        }
        var expected = (Math.PI * 25 * 4 + UnderFace(backRadius, 5, 5) - UnderFace(frontRadius, 3, 5)) / 1000;
        Assert.Equal(expected, Value(optic, Row("VOLU")), 12);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AsphericVolumeMatchesAnalyticalPolynomialIntegral(bool odd)
    {
        var optic = Lens(); var front = optic.SurfaceGroup.Items[1];
        front.Geometry = odd ? new OddAsphereGeometry(0, 0, [.01, .02]) : new EvenAsphereGeometry(0, 0, [.01, .002]);
        var r = 3.0; var outside = 5.0;
        var edge = odd ? .01 * r + .02 * r * r : .01 * r * r + .002 * Math.Pow(r, 4);
        var integral = odd ? 2 * Math.PI * (.01 * Math.Pow(r, 3) / 3 + .02 * Math.Pow(r, 4) / 4)
            : 2 * Math.PI * (.01 * Math.Pow(r, 4) / 4 + .002 * Math.Pow(r, 6) / 6);
        integral += Math.PI * (outside * outside - r * r) * edge;
        Assert.Equal((Math.PI * 25 * 4 - integral) / 1000, Value(optic, Row("VOLU")), 11);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(-50)]
    public void ConicVolumeMatchesParaboloidIntegral(double radius)
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].Geometry = new StandardGeometry(radius, -1);
        var integral = Math.PI * Math.Pow(3, 4) / (4 * radius) + Math.PI * (25 - 9) * 9 / (2 * radius);
        Assert.Equal((Math.PI * 25 * 4 - integral) / 1000, Value(optic, Row("VOLU")), 11);
    }

    [Fact]
    public void VeryLargeSagUsesScaledRmsWithoutSquareOverflow()
    {
        var optic = Lens(); var surface = optic.SurfaceGroup.Items[1];
        surface.Geometry = new PolynomialGeometry(new Dictionary<(int, int), double> { [(0, 0)] = 1e200 });
        var row = Row("DSAG"); row.Target = 1e200; // Keep the separate merit contribution finite.
        Assert.Equal(1, Value(optic, row) / 1e200, 12);
    }

    [Fact]
    public void VolumeAndMassOverflowAreErrors()
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].Geometry = new EvenAsphereGeometry(0, 0, [1e308]);
        Invalid(optic, Row("VOLU"));
        optic = Lens(); optic.SurfaceGroup.Items[1].MaterialAfter = Glass(double.MaxValue);
        optic.SurfaceGroup.Items[1].Thickness = 100; optic.SurfaceGroup.Renumber();
        Invalid(optic, Row("TMAS"));
    }

    [Fact]
    public void ClosedRangeIncludesFollowingSpacesAndMassOmitsAir()
    {
        var optic = Lens(); var row = Row("VOLU", data: 2);
        Assert.Equal(Math.PI * 25 * 14 / 1000, Value(optic, row), 12);
        row.Type = "TMAS"; Assert.Equal(Math.PI * 25 * 4 / 1000 * 2.5, Value(optic, row), 12);
        row.ZemaxIntegerParameters = [2, 2]; Assert.Equal(0, Value(optic, row));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void MissingOrInvalidCatalogDensityNeverBecomesZeroMass(double? density)
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].MaterialAfter = Glass(density);
        Invalid(optic, Row("TMAS")); Assert.True(Value(optic, Row("VOLU")) > 0);
    }

    [Theory]
    [InlineData("range")]
    [InlineData("image")]
    [InlineData("object")]
    [InlineData("mode")]
    [InlineData("tilt")]
    [InlineData("decenter")]
    [InlineData("spacing")]
    [InlineData("negative")]
    [InlineData("crossing")]
    [InlineData("aperture")]
    [InlineData("cutout")]
    [InlineData("domain")]
    [InlineData("mirror")]
    public void InvalidVolumeRequestsFailWithoutPretendGeometry(string fault)
    {
        var optic = Lens(); var row = Row("VOLU"); var front = optic.SurfaceGroup.Items[1]; var back = optic.SurfaceGroup.Items[2];
        if (fault == "range") row.ZemaxIntegerParameters = [2, 1];
        if (fault == "image") row.ZemaxIntegerParameters = [3, 3];
        if (fault == "object") row.ZemaxIntegerParameters = [0, 1];
        if (fault == "mode") row.ZemaxDataParameters[1] = 2;
        if (fault == "tilt") front.CoordinateSystem = new CoordinateSystem(front.CoordinateSystem.Origin, 1, 0, 0);
        if (fault == "decenter") back.CoordinateSystem = new CoordinateSystem(back.CoordinateSystem.Origin + new Vector3D(1, 0, 0));
        if (fault == "spacing") back.CoordinateSystem = new CoordinateSystem(back.CoordinateSystem.Origin + new Vector3D(0, 0, 1));
        if (fault == "negative") front.Thickness = -4;
        if (fault == "crossing") front.Geometry = new EvenAsphereGeometry(0, 0, [1]);
        if (fault == "aperture") front.PhysicalAperture = new RectangularAperture(2, 2);
        if (fault == "cutout") front.PhysicalAperture = new CircularAperture(2);
        if (fault == "domain") front.Radius = 2;
        if (fault == "mirror") front.IsReflective = true;
        Invalid(optic, row);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void SagStatisticsMatchIndependentUniformGridOnAsymmetricPolynomial(int data)
    {
        var optic = Lens(); var surface = optic.SurfaceGroup.Items[1];
        surface.Geometry = new PolynomialGeometry(new Dictionary<(int, int), double> { [(1, 0)] = .2, [(0, 1)] = -.3, [(2, 0)] = .01 });
        surface.PhysicalAperture = new RectangularAperture(2, 1, .5, -.25);
        var points = new List<(double X, double Y, double Value)>();
        for (var iy = 0; iy < 33; iy++) for (var ix = 0; ix < 33; ix++)
        {
            var x = -1.5 + ix / 8.0; var y = -1.25 + iy / 16.0;
            points.Add((x, y, .2 * x - .3 * y + .01 * x * x));
        }
        var minimum = points.MinBy(p => p.Value); var maximum = points.MaxBy(p => p.Value);
        var expected = data switch
        {
            1 => Math.Sqrt(points.Average(p => p.Value * p.Value)),
            2 => maximum.Value - minimum.Value,
            3 => minimum.Value,
            4 => maximum.Value,
            5 => minimum.X,
            6 => minimum.Y,
            7 => maximum.X,
            _ => maximum.Y
        };
        Assert.Equal(expected, Value(optic, Row("DSAG", data)), 11);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PlaneTiltSlopeStatisticsUseRequestedDirection(int orientation)
    {
        var optic = Lens(); var surface = optic.SurfaceGroup.Items[1];
        surface.Geometry = new PolynomialGeometry(new Dictionary<(int, int), double> { [(1, 0)] = .2, [(0, 1)] = -.3 });
        surface.PhysicalAperture = new RectangularAperture(2, 1, .5, -.25);
        var values = new List<double>();
        for (var iy = 0; iy < 33; iy++) for (var ix = 0; ix < 33; ix++)
        {
            var x = -1.5 + ix / 8.0; var y = -1.25 + iy / 16.0; var r = Math.Sqrt(x * x + y * y);
            var tx = r == 0 ? 0 : x / r; var ty = r == 0 ? 1 : y / r;
            values.Add(orientation switch { 0 => .2 * tx - .3 * ty, 1 => -.2 * ty - .3 * tx, 2 => .2, _ => -.3 });
        }
        Assert.Equal(Math.Sqrt(values.Average(v => v * v)), Value(optic, Row("DSLP", orientation: orientation)), 11);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SphereCurvatureIsConstantInEveryDirection(int orientation)
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].Radius = -50;
        Assert.Equal(.02, Value(optic, Row("DCRV", orientation: orientation)), 10);
        Assert.Equal(-.02, Value(optic, Row("DCRV", data: 3, orientation: orientation)), 10);
    }

    [Theory]
    [MemberData(nameof(ProfileCodes))]
    public void BaseSphereRemovalOperatesOnProfileBeforeDifferentiation(string code)
    {
        var optic = Lens(); var surface = optic.SurfaceGroup.Items[1];
        surface.Geometry = new EvenAsphereGeometry(50, 0, [.1]);
        var removed = Value(optic, Row(code, remove: 1));
        surface.Geometry = new EvenAsphereGeometry(0, 0, [.1]);
        Assert.Equal(Value(optic, Row(code)), removed, 11);
        surface.Radius = 50; surface.Geometry = new StandardGeometry(50);
        Assert.Equal(0, Value(optic, Row(code, remove: 1)), 12);
    }

    [Theory]
    [InlineData("circle")]
    [InlineData("annulus")]
    [InlineData("offset")]
    [InlineData("ellipse")]
    public void StatisticsRespectPhysicalApertureAndItsOffset(string aperture)
    {
        var optic = Lens(); var surface = optic.SurfaceGroup.Items[1];
        surface.Geometry = new PolynomialGeometry(new Dictionary<(int, int), double> { [(1, 0)] = 1 });
        surface.PhysicalAperture = aperture switch
        { "circle" => new CircularAperture(2), "annulus" => new AnnularAperture(2, 1), "offset" => new OffsetRadialAperture(2, 0, 3, 1), _ => new EllipticalAperture(2, 1, 3, 1) };
        var shift = aperture is "offset" or "ellipse" ? 3 : 0;
        Assert.Equal(shift - 2, Value(optic, Row("DSAG", data: 3)), 12);
        Assert.Equal(shift + 2, Value(optic, Row("DSAG", data: 4)), 12);
        var result = SurfaceProfileMetrics.Evaluate(surface, SurfaceProfileQuantity.Sag, 1, 0, 0);
        Assert.InRange(result.SampleCount, 1, 33 * 33 - 1);
    }

    [Theory]
    [InlineData("DSAG", "reference")]
    [InlineData("DSLP", "data")]
    [InlineData("DCRV", "sampling")]
    [InlineData("DSAG", "offAxis")]
    [InlineData("DSLP", "remove")]
    [InlineData("DCRV", "bfs")]
    [InlineData("DSLP", "orientation")]
    [InlineData("DSAG", "domain")]
    public void UnsupportedProfileModesAreExplicitErrors(string code, string fault)
    {
        var optic = Lens(); var row = Row(code);
        if (fault == "reference") row.ZemaxIntegerParameters[0] = 99;
        if (fault == "data") row.ZemaxIntegerParameters[1] = 9;
        if (fault == "sampling") row.ZemaxDataParameters[0] = 6;
        if (fault == "offAxis") row.ZemaxDataParameters[1] = 1;
        if (fault == "remove") row.ZemaxDataParameters[2] = 2;
        if (fault == "bfs") row.ZemaxDataParameters[3] = 3;
        if (fault == "orientation") row.ZemaxDataParameters[4] = 5;
        if (fault == "domain") optic.SurfaceGroup.Items[1].Radius = 2;
        Invalid(optic, row);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void CancellationAndSourceEditsAreObserved(string code)
    {
        var optic = Lens(); var row = Row(code);
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel(); using var scope = ComputationCancellation.Push(cancellation.Token);
            Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(optic, row));
        }
        var before = Value(optic, row);
        optic.SurfaceGroup.Items[1].Geometry = new EvenAsphereGeometry(0, 0, [.02]);
        Assert.NotEqual(before, Value(optic, row));
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task ProjectAndEditorPreserveLocalParametersAndNativeImportStaysReadOnly(string code)
    {
        var optic = Lens(); var original = Row(code); optic.MeritFunctionOperands.Add(original);
        var expected = Value(optic, original);
        var path = Path.Combine(Path.GetTempPath(), $"manufacturing-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var restored = (await StarOptProjectStore.LoadAsync(path)).Configurations[0];
            var row = Assert.Single(restored.MeritFunctionOperands); Assert.False(row.CompatibilityOnly);
            Assert.Equal(original.ZemaxDataParameters, row.ZemaxDataParameters);
            Assert.Equal(expected, Value(restored, row), 11);
        }
        finally { File.Delete(path); }
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, false, code, 1, 0, 1, 0, 0, 0, 0, .25, 2, 0, 0, "manufacturing",
            ZemaxInt1: 1, ZemaxInt2: 1, ZemaxData1: original.ZemaxDataParameters[0], ZemaxData2: 0, ZemaxData3: 0, ZemaxData4: 0,
            ZemaxData5: code is "DSLP" or "DCRV" ? 2 : null)]);
        var type = app.Optimization.GetMeritOperandTypes().Single(t => t.Code == code);
        var editor = new MeritOperandEditorRow(Assert.Single(app.Optimization.GetMeritFunction()), type);
        if (code is "DSLP" or "DCRV") editor.Parameter7 = 3;
        app.Optimization.SetMeritFunction([editor.ToDto()]); var saved = Assert.Single(app.Optimization.GetMeritFunction());
        Assert.False(saved.CompatibilityOnly); Assert.Equal(.25, saved.Target); Assert.Equal(2, saved.Weight);
        if (code is "DSLP" or "DCRV") Assert.Equal(3, saved.ZemaxData5);
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX"));
        var imported = OpticalFormatCatalog.Import(text + $"\n{code} 1 1 1 0 0 0 .25 2 0 0\n", ".zmx");
        var native = imported.MeritFunctionOperands.Last(); Assert.True(native.CompatibilityOnly); Assert.False(native.Enabled);
        var again = Optic.FromSnapshot(imported.ToSnapshot()).MeritFunctionOperands.Last(); Assert.True(again.CompatibilityOnly);
        Assert.Equal(native.ZemaxDataParameters, again.ZemaxDataParameters);
    }
}
