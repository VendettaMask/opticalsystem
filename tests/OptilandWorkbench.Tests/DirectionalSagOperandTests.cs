using System.Globalization;
using System.Text.RegularExpressions;
using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;
using Xunit.Abstractions;

namespace OptilandWorkbench.Tests;

public sealed class DirectionalSagOperandTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0, 0, 0, 2)]
    [InlineData(30, 0, 0, -2)]
    [InlineData(0, -40, 0, 2)]
    [InlineData(20, -30, 40, -2)]
    [InlineData(200, 30, -40, 2)]
    public void PlaneDistanceIsSignedAndUsesFullTiltedMeasurementAxis(double tx, double ty, double tz, double z)
    {
        var hit = SurfaceDirectionalSag.Evaluate(new OpticalSurface(), 1, new(3, -4, z), tx, ty, tz);
        var expected = -z / (Math.Cos(tx * Math.PI / 180) * Math.Cos(ty * Math.PI / 180));
        Assert.Equal(expected, hit.Distance, 11); Assert.InRange(Math.Abs(hit.Point.Z), 0, 1e-11);
        Assert.Equal(1, hit.Direction.Length, 12);
    }

    [Theory]
    [InlineData(40, 0)]
    [InlineData(-40, 0)]
    [InlineData(40, 1)]
    [InlineData(-40, 1)]
    public void AxisAlignedSphereSagRetainsSignAndReferenceOffset(double radius, int mode)
    {
        var optic = Lens(new StandardGeometry(radius));
        var expected = Math.CopySign(25 / (40 + Math.Sqrt(1600 - 25)), radius) - 2;
        Assert.Equal(expected, Value(optic, Row(mode, 3, 4, 2)), 11);
        Assert.Equal(0, Value(optic, Row(mode, 0, 0, 0)), 12);
    }

    [Fact]
    public void CompoundTiltHasIndependentSlopingPlaneReference()
    {
        // z = 0.2 x - 0.3 y + 1.7, with independent elementary rotations of +Z.
        var surface = new OpticalSurface
        {
            Geometry = new PolynomialGeometry(new Dictionary<(int, int), double>
            { [(1, 0)] = .2, [(0, 1)] = -.3, [(0, 0)] = 1.7 })
        };
        var ax = 20 * Math.PI / 180; var ay = -30 * Math.PI / 180; var az = 40 * Math.PI / 180;
        var dx = Math.Cos(az) * Math.Sin(ay) * Math.Cos(ax) + Math.Sin(az) * Math.Sin(ax);
        var dy = Math.Sin(az) * Math.Sin(ay) * Math.Cos(ax) - Math.Cos(az) * Math.Sin(ax);
        var dz = Math.Cos(ay) * Math.Cos(ax);
        var reference = new Vector3D(2, -3, 4);
        var expected = (.2 * reference.X - .3 * reference.Y + 1.7 - reference.Z) / (dz - .2 * dx + .3 * dy);
        var hit = SurfaceDirectionalSag.Evaluate(surface, 1, reference, 20, -30, 40);
        // Use an explicit 1e-9 mm bound consistent with the shared iterative solver at this coordinate scale.
        Assert.InRange(Math.Abs(expected - hit.Distance), 0, 1e-9);
        Assert.InRange(Math.Abs(reference.X + expected * dx - hit.Point.X), 0, 1e-9);
        Assert.InRange(Math.Abs(reference.Y + expected * dy - hit.Point.Y), 0, 1e-9);
        Assert.InRange(Math.Abs(.2 * hit.Point.X - .3 * hit.Point.Y + 1.7 - hit.Point.Z), 0, 1e-9);
        Assert.NotEqual(hit.Distance, SurfaceDirectionalSag.Evaluate(surface, 1, reference, 20, -30, 0).Distance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TiltedRayHitsActualSphereAndShiftingAlongAxisChangesOnlyDistance(int mode)
    {
        var surface = new OpticalSurface { Geometry = new StandardGeometry(40), SemiDiameter = 10 };
        var hit = SurfaceDirectionalSag.Evaluate(surface, mode, new(2, 3, -1), 12, -8, 17);
        Assert.Equal(1600, hit.Point.X * hit.Point.X + hit.Point.Y * hit.Point.Y + Math.Pow(hit.Point.Z - 40, 2), 8);
        var next = SurfaceDirectionalSag.Evaluate(surface, mode, new Vector3D(2, 3, -1) + hit.Direction * 3, 12, -8, 17);
        Assert.Equal(hit.Distance - 3, next.Distance, 9); Assert.Equal(hit.Point.X, next.Point.X, 9);
        Assert.Equal(hit.Point.Y, next.Point.Y, 9); Assert.Equal(hit.Point.Z, next.Point.Z, 9);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AsphereFlatEdgeStartsAtClearRadiusPlusChipZone(bool odd)
    {
        var optic = Lens(odd ? new OddAsphereGeometry(0, 0, [0, .02, .001]) : new EvenAsphereGeometry(0, 0, [.02, .001]));
        var surface = optic.SurfaceGroup.Items[1]; surface.SemiDiameter = 3; surface.ChipZone = 1;
        surface.MechanicalSemiDiameter = 8;
        double Sag(double r) => .02 * r * r + .001 * Math.Pow(r, odd ? 3 : 4);
        Assert.Equal(Sag(3.5), Value(optic, Row(0, 0, 3.5)), 11);
        Assert.Equal(Sag(4), Value(optic, Row(0, 0, 7)), 11);
        Assert.Equal(Sag(7), Value(optic, Row(1, 0, 7)), 11);
        surface.ChipZone = 2; Assert.Equal(Sag(5), Value(optic, Row(0, 0, 7)), 11);
    }

    [Fact]
    public void FlatEdgeIsChosenAtTheIntersectionRatherThanTheReferencePoint()
    {
        var surface = new OpticalSurface { Geometry = new StandardGeometry(40), SemiDiameter = 3, ChipZone = 1 };
        var edge = 40 - Math.Sqrt(1600 - 16);
        var outside = SurfaceDirectionalSag.Evaluate(surface, 0, new(3, 0, -5), 0, 30);
        Assert.True(outside.Point.X > 4); Assert.Equal(edge, outside.Point.Z, 10);
        Assert.Equal((edge + 5) / Math.Cos(Math.PI / 6), outside.Distance, 10);
        var inside = SurfaceDirectionalSag.Evaluate(surface, 0, new(5, 0, -5), 0, -30);
        Assert.True(inside.Point.X < 4); Assert.True(inside.Point.Z < edge);
        Assert.Equal(1600, inside.Point.X * inside.Point.X + Math.Pow(inside.Point.Z - 40, 2), 8);
    }

    [Fact]
    public void LocalMeasurementIgnoresGlobalPoseAndTraceAperture()
    {
        var optic = Lens(new StandardGeometry(40)); var row = Row(1, 3, 4, 2, 10, 15, 20);
        var expected = Value(optic, row); var surface = optic.SurfaceGroup.Items[1];
        surface.CoordinateSystem = new CoordinateSystem(new(30, -20, 10), 10, 20, 30);
        surface.PhysicalAperture = new RectangularAperture(.1, .1);
        row.Surface = 999; row.Hx = 111; row.Hy = 222; row.Px = 333; row.Py = 444;
        Assert.Equal(expected, Value(optic, row), 12);
        surface.Radius = 50; Assert.NotEqual(expected, Value(optic, row));
        row.Target = Value(optic, row) + .25; row.Weight = 2;
        Assert.Equal(.125, MeritFunctionCatalog.Evaluate(optic, row).Contribution, 12);
    }

    [Theory]
    [InlineData("surface")]
    [InlineData("mode")]
    [InlineData("nan")]
    [InlineData("tilt")]
    [InlineData("miss")]
    [InlineData("parallel")]
    [InlineData("edge-domain")]
    [InlineData("non-radial-flat")]
    public void InvalidOrAmbiguousMeasurementsRemainErrors(string fault)
    {
        var optic = Lens(new StandardGeometry(40)); var row = Row(1, 1, 2, 3);
        if (fault == "surface") row.ZemaxIntegerParameters[0] = 99;
        if (fault == "mode") row.ZemaxIntegerParameters[1] = 2;
        if (fault == "nan") row.ZemaxDataParameters[1] = double.NaN;
        if (fault == "tilt") row.ZemaxDataParameters[5] = double.PositiveInfinity;
        if (fault == "miss") row.ZemaxDataParameters[0] = 50;
        if (fault == "parallel") { optic.SurfaceGroup.Items[1].Geometry = new PlaneGeometry(); row.ZemaxDataParameters[4] = 90; }
        if (fault == "edge-domain") { row.ZemaxIntegerParameters[1] = 0; optic.SurfaceGroup.Items[1].ChipZone = 40; }
        if (fault == "non-radial-flat") { row.ZemaxIntegerParameters[1] = 0; optic.SurfaceGroup.Items[1].Geometry = new PolynomialGeometry(new Dictionary<(int, int), double>()); }
        Invalid(optic, row);
    }

    [Fact]
    public void CancellationIsNotTurnedIntoAZeroOrMeritError()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => Value(Lens(new PlaneGeometry()), Row(1)));
    }

    [Fact]
    public async Task EightSlotsAndChipZoneSurviveEditingAndProjectSaveWhileNativeRowsRemainPreserved()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var type = application.Optimization.GetMeritOperandTypes().Single(t => t.Code == "TSAG");
        Assert.False(type.CompatibilityOnly); Assert.Equal(8, type.Parameters!.Count);
        application.Optimization.SetMeritFunction([new(1, true, "TSAG", 1, 0, 0, 0, 0, 0, 0, .25, 2, 0, 0, "local",
            ZemaxInt1: 1, ZemaxInt2: 1, ZemaxData1: 1, ZemaxData2: 2, ZemaxData3: 3, ZemaxData4: 4, ZemaxData5: 5, ZemaxData6: 6)]);
        var editor = new MeritOperandEditorRow(Assert.Single(application.Optimization.GetMeritFunction()), type);
        Assert.True(editor.IsParameterEditable(7)); editor.Parameter8 = 16; editor.Parameter5 = -3;
        application.Optimization.SetMeritFunction([editor.ToDto()]);
        var dto = Assert.Single(application.Optimization.GetMeritFunction());
        Assert.Equal(16, dto.ZemaxData6); Assert.Equal(-3, dto.ZemaxData3); Assert.Equal(.25, dto.Target); Assert.Equal(2, dto.Weight);

        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX"));
        var optic = OpticalFormatCatalog.Import(source + "\nTSAG 1 0 1 2 3 4 .25 2 0 0\n", ".zmx");
        var imported = optic.MeritFunctionOperands.Last(); Assert.True(imported.CompatibilityOnly); Assert.False(imported.Enabled);
        Assert.Equal(4, imported.ZemaxDataParameters.Length);
        optic.SurfaceGroup.Items[1].ChipZone = 1.25; optic.MeritFunctionOperands.Add(Row(0, 1, 2, -3, 4, 5, 16));
        var expected = Value(optic, optic.MeritFunctionOperands.Last());
        var path = Path.Combine(Path.GetTempPath(), $"directional-sag-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var copy = (await StarOptProjectStore.LoadAsync(path)).Configurations[0];
            Assert.Equal(1.25, copy.SurfaceGroup.Items[1].ChipZone);
            Assert.Equal(expected, Value(copy, copy.MeritFunctionOperands.Last()), 11);
            var preserved = copy.MeritFunctionOperands.Single(r => r.Type == "TSAG" && r.CompatibilityOnly);
            Assert.False(preserved.Enabled); Assert.Equal(imported.ZemaxDataParameters, preserved.ZemaxDataParameters);
        }
        finally { File.Delete(path); }
        var missing = Lens(new PlaneGeometry()); var incomplete = Row(1); incomplete.ZemaxDataParameters = [0, 0, 0, 0];
        missing.MeritFunctionOperands.Add(incomplete); Assert.Throws<InvalidDataException>(() => Optic.FromSnapshot(missing.ToSnapshot()));
    }

    [Fact]
    public void ChipZoneEditPublishesOneRevisionAndPreservesUndoAndUnrelatedDtoEdits()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var editor = new SurfaceEditorRow(app.Prescription.GetSurfaces()[1]); var old = editor.ToDto();
        var revision = app.Events.Revision; editor.ExtensionZoneDisplay = "1.25";
        app.Prescription.UpdateSurface(editor.ToDto()); Assert.Equal(revision + 1, app.Events.Revision);
        Assert.Equal(1.25, app.Prescription.GetSurfaces()[1].ChipZone);
        Assert.True(app.Documents.Undo()); Assert.Equal(old.ChipZone, app.Prescription.GetSurfaces()[1].ChipZone);
        Assert.True(app.Documents.Redo()); Assert.Equal(1.25, app.Prescription.GetSurfaces()[1].ChipZone);
        app.Prescription.UpdateSurface(old with { Label = "Changed", ChipZone = null });
        Assert.Equal(1.25, app.Prescription.GetSurfaces()[1].ChipZone);
        Assert.Throws<ArgumentOutOfRangeException>(() => app.Prescription.UpdateSurface(old with { ChipZone = -1 }));
        Assert.Equal(1.25, app.Prescription.GetSurfaces()[1].ChipZone);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("Infinity")]
    [InlineData("NaN")]
    public void InvalidChipZoneInputCannotSilentlyReplaceSavedValue(string text)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var row = new SurfaceEditorRow(app.Prescription.GetSurfaces()[1] with { ChipZone = 1.25 });
        Assert.Throws<FormatException>(() => row.ExtensionZoneDisplay = text); Assert.Equal(1.25, row.ToDto().ChipZone);
    }

    [Fact]
    public void ChipZoneIsClonedValidatedAndNeverSilentlyLostInPrescriptionExports()
    {
        var optic = Lens(new StandardGeometry(40)); var surface = optic.SurfaceGroup.Items[1]; surface.ChipZone = 2;
        Assert.Equal(7, surface.MechanicalSemiDiameter); Assert.Equal(2, surface.Clone().ChipZone);
        Assert.Equal(2, Optic.FromSnapshot(optic.ToSnapshot()).SurfaceGroup.Items[1].ChipZone);
        surface.MechanicalSemiDiameter = 9; surface.ChipZone = 3; Assert.Equal(9, surface.MechanicalSemiDiameter);
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.ChipZone = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.ChipZone = double.NaN);
        var snapshot = optic.ToSnapshot(); snapshot.Surfaces[1] = snapshot.Surfaces[1] with { ChipZone = -1 };
        Assert.Throws<InvalidDataException>(() => Optic.FromSnapshot(snapshot));
        foreach (var exporter in OpticalFormatCatalog.Exporters) Assert.Throws<NotSupportedException>(() => exporter.Export(optic));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1.5)]
    public void MechanicalVolumeAndBlankThicknessIncludeChipZoneThenAFlatAnnulus(double chip)
    {
        var front = new OpticalSurface
        {
            Geometry = new StandardGeometry(-40),
            SemiDiameter = 3,
            ChipZone = chip,
            MechanicalSemiDiameter = 7,
            Thickness = 5
        };
        var back = new OpticalSurface
        {
            Geometry = new PlaneGeometry(),
            SemiDiameter = 5,
            ChipZone = chip,
            MechanicalSemiDiameter = 7,
            CoordinateSystem = new CoordinateSystem(new(0, 0, 5))
        };
        var r = 3 + chip; var h = 40 - Math.Sqrt(1600 - r * r);
        var cap = Math.PI * h * h * (40 - h / 3);
        var expected = (Math.PI * 49 * (5 + h) - cap) / 1000;
        Assert.Equal(expected, ElementVolumeMetrics.VolumeCubicCentimeters(front, back, SurfaceDiameterMode.Mechanical), 10);
        Assert.Equal(5 + h, SurfaceManufacturingMetrics.BlankThickness(front, back, 4, SurfaceDiameterMode.Mechanical), 10);
    }

    [Fact]
    public void CadMeshExtendsTheCurvedFaceThroughChipZoneBeforeItsFlatRim()
    {
        var optic = Lens(new StandardGeometry(40)); var front = optic.SurfaceGroup.Items[1];
        front.SemiDiameter = 3; front.ChipZone = 1; front.MechanicalSemiDiameter = 6;
        front.MaterialAfter = new ConstantIndexMaterial("Glass", 1.5);
        var back = optic.SurfaceGroup.Items[2]; back.SemiDiameter = 3; back.ChipZone = 1; back.MechanicalSemiDiameter = 6;
        back.CoordinateSystem = new CoordinateSystem(new(0, 0, 10));
        var mesh = Assert.Single(CadLensMeshBuilder.Build(optic, new StepCadExportOptions(), CancellationToken.None).Parts);
        var frontRim = mesh.Vertices.Where(p => double.Hypot(p.X, p.Y) > 5.99 && p.Z < 5).ToArray();
        Assert.NotEmpty(frontRim);
        Assert.All(frontRim, p => Assert.Equal(40 - Math.Sqrt(1600 - 16), p.Z, 8));
        Assert.InRange(mesh.MaximumChordErrorMillimeters, 0, .005);
    }

    [Fact]
    public void DirectionalSagDrivesProductionDlsRadiusOptimization()
    {
        var optic = Lens(new StandardGeometry(40)); optic.SurfaceGroup.Items[1].RadiusVariable = true;
        var row = Row(1, 3, 4); row.Target = 25 / (50 + Math.Sqrt(2500 - 25)); row.Weight = 10;
        optic.MeritFunctionOperands.Add(row); var runtime = new WorkbenchRuntime(optic);
        var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 40);
        Assert.True(result.FinalMerit < result.InitialMerit * 1e-6);
        Assert.Equal(50, runtime.CurrentOptic.SurfaceGroup.Items[1].Radius, 4);
    }

    [Fact]
    public void CapturedSagTableValidatesOnlyItsZeroTiltSphericalCase()
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(fixtures, "zemax-123456.ZMX")), ".zmx");
        var lines = File.ReadAllLines(Path.Combine(fixtures, "zemax-123456-sag-table.txt"));
        var count = 0; var maximum = 0.0;
        foreach (var line in lines)
        {
            var values = Regex.Matches(line, @"[-+]?\d+\.\d+E[-+]\d+");
            if (values.Count != 8) continue;
            var y = double.Parse(values[0].Value, CultureInfo.InvariantCulture);
            if (y > 7.8) continue; // Last printed coordinate is rounded and lies beyond the captured clear radius.
            var native = double.Parse(values[1].Value, CultureInfo.InvariantCulture);
            foreach (var mode in new[] { 0, 1 })
            {
                var error = Math.Abs(Value(optic, Row(mode, 0, y)) - native);
                maximum = Math.Max(maximum, error); Assert.InRange(error, 0, 5e-8);
            }
            count++;
        }
        Assert.Equal(40, count);
        output.WriteLine($"TSAG zero-tilt captured sag table: {count} rows x 2 modes, max absolute difference={maximum:R} mm; text precision gate=5E-8 mm. Not a native TSAG MFE, tilted or chip-zone capture.");
    }

    private static Optic Lens(IGeometry geometry)
    {
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.Replace([new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { Geometry = geometry, SemiDiameter = 5, Thickness = 10, IsStop = true }, new OpticalSurface { Thickness = 0 }]);
        return optic;
    }
    private static MeritOperandDefinition Row(int mode, double x = 0, double y = 0, double z = 0, double tx = 0, double ty = 0, double tz = 0) => new()
    { Type = "TSAG", ZemaxIntegerParameters = [1, mode], ZemaxDataParameters = [x, y, z, tx, ty, tz] };
    private static double Value(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.True(string.IsNullOrEmpty(result.Error), result.Error); return result.Value;
    }
    private static void Invalid(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.NotEmpty(result.Error);
        Assert.True(double.IsNaN(result.Value)); Assert.True(double.IsPositiveInfinity(result.Contribution));
        Assert.Throws<OptimizationEvaluationException>(() => MeritFunctionCatalog.EvaluateOptimizationValues(optic, [row]));
    }
}
