using System.Numerics;
using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class CoatingDataOperandTests
{
    public static IEnumerable<object[]> CoefficientCodes => Enumerable.Range(1, 7).SelectMany(i => new[] { new object[] { i }, new object[] { -i } });

    [Theory]
    [MemberData(nameof(CoefficientCodes))]
    public void BareInterfaceDataMatchesIndependentPowerFresnel(int data)
    {
        var optic = Plate();
        var c0 = Math.Cos(Math.PI / 6); var c1 = Math.Sqrt(1 - .25 / 2.25);
        var rs = (c0 - 1.5 * c1) / (c0 + 1.5 * c1);
        var rp = (1.5 * c0 - c1) / (1.5 * c0 + c1);
        var r = data < 0 ? rs : rp; var t = Math.Sqrt(1 - r * r);
        var expected = Math.Abs(data) switch { 1 => r * r, 2 => t * t, 3 => 0, 4 => t, 5 => 0, 6 => r, _ => 0 };
        Near(expected, Value(optic, Row(data, 1)));
    }

    [Theory]
    [MemberData(nameof(CoefficientCodes))]
    public void AbsorbingFilmDataMatchesIndependentComplexAiry(int data)
    {
        var optic = Plate(); optic.SurfaceGroup.Items[1].CoatingModel = new CoherentMultilayerCoating([new(new ConstantIndexMaterial("film", 2.1, .15), 87)]);
        var response = Airy(data > 0);
        var r = Complex.Conjugate(response.R); var t = Complex.Conjugate(response.T);
        var expected = Math.Abs(data) switch
        { 1 => Squared(r), 2 => Squared(t), 3 => 1 - Squared(r) - Squared(t), 4 => t.Real, 5 => t.Imaginary, 6 => r.Real, _ => r.Imaginary };
        Near(expected, Value(optic, Row(data, 1)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CodaAlwaysUsesSpecifiedJonesButDataEightUsesTheOrthogonalAverage(bool unpolarized)
    {
        var optic = Plate(); optic.Polarization = new(unpolarized, 1, 0);
        var s = Value(optic, Row(-2, 1)); var p = Value(optic, Row(2, 1));
        Near(s * s, Value(optic, Row(0)));
        Near((s * s + p * p) / 2, Value(optic, Row(8)));
        Near(Value(optic, Row(8)), Value(optic, Row(-8)));
        optic.Polarization = optic.Polarization with { Jx = 0, Jy = 1 };
        Near(p * p, Value(optic, Row(0)));
        Near((s * s + p * p) / 2, Value(optic, Row(8)));
    }

    [Theory]
    [InlineData(101)]
    [InlineData(102)]
    [InlineData(103)]
    [InlineData(104)]
    [InlineData(105)]
    [InlineData(106)]
    [InlineData(110)]
    [InlineData(111)]
    [InlineData(112)]
    [InlineData(113)]
    public void LocalComplexComponentsAndRadiansReachOperandWithoutDroppingCommonPhase(int data)
    {
        var optic = Plate(); optic.Polarization = new(true, 1, 2, 20, -40);
        var source = optic.SequentialRayTracer.RayGenerator.CreatePupilRaySampler(0, 1, .55, aimAtStop: false)(0, 0);
        var s = Value(optic, Row(-2, 1)); var p = Value(optic, Row(2, 1));
        var glassCos = Math.Sqrt(1 - .25 / 2.25);
        var opl = (5 - source.Origin.Z) / Math.Cos(Math.PI / 6) + 3 / glassCos;
        var common = Complex.FromPolarCoordinates(1, -2 * Math.PI * Math.IEEERemainder(opl, .00055) / .00055);
        var x = common * Complex.FromPolarCoordinates(s / Math.Sqrt(5), 20 * Math.PI / 180);
        var ep = common * Complex.FromPolarCoordinates(2 * p / Math.Sqrt(5), -40 * Math.PI / 180);
        var y = ep * Math.Cos(Math.PI / 6); var z = -ep / 2;
        var expected = data switch
        {
            101 => x.Real,
            102 => x.Imaginary,
            103 => y.Real,
            104 => y.Imaginary,
            105 => z.Real,
            106 => z.Imaginary,
            110 => Math.PI / 3,
            111 => x.Phase,
            112 => y.Phase,
            _ => z.Phase
        };
        Near(expected, Value(optic, Row(data)), 5e-9);
        Near(expected, Value(optic, Row(-data)), 5e-9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(-70)]
    public void EllipseHasIndependentRotatedAxesAndExcludesZ(double angle)
    {
        var a = angle * Math.PI / 180; var c = Math.Cos(a); var s = Math.Sin(a);
        var e = new ComplexElectricField(new(2 * c, -.5 * s), new(2 * s, .5 * c), new(20, 30));
        var ellipse = CoatingRayMetrics.Ellipse(e);
        Near(2, ellipse.Major); Near(.5, ellipse.Minor); Near(angle, ellipse.AngleDegrees!.Value);
        var common = Complex.FromPolarCoordinates(3, 1.7);
        var changed = CoatingRayMetrics.Ellipse(e * common);
        Near(6, changed.Major); Near(1.5, changed.Minor); Near(angle, changed.AngleDegrees!.Value);
    }

    [Fact]
    public void CodaEllipseMatchesLinearPlateFieldAndCircularAngleIsUndefined()
    {
        var optic = Plate(); optic.Polarization = new(true, 1, 2);
        var s = Value(optic, Row(-2, 1)); var p = Value(optic, Row(2, 1));
        var x = s / Math.Sqrt(5); var y = 2 * p * Math.Cos(Math.PI / 6) / Math.Sqrt(5);
        Near(Math.Sqrt(x * x + y * y), Value(optic, Row(121)));
        Near(0, Value(optic, Row(122)));
        Near(Math.Atan2(y, x) * 180 / Math.PI, Value(optic, Row(123)));
        var circular = CoatingRayMetrics.Ellipse(new(1, Complex.ImaginaryOne, 0));
        Near(1, circular.Major); Near(1, circular.Minor); Assert.Null(circular.AngleDegrees);
        var nearlyLinear = CoatingRayMetrics.Ellipse(new(1, new Complex(1, 1e-12), 0));
        Assert.InRange(nearlyLinear.Minor, 7e-13, 7.2e-13);
    }

    [Fact]
    public void IlluminationHonorsGlobalJonesWhileUnpolarizedAverageRemainsAvailable()
    {
        var optic = Plate(); var source = new RealRay(new(0, 0, -1), new(0, .5, Math.Sqrt(.75)), 550);
        var average = optic.SequentialRayTracer.DiagnoseApertures(source, usePolarization: true, stopAtIncidentImage: true);
        optic.Polarization = new(false, 1, 0);
        var s = optic.SequentialRayTracer.DiagnoseApertures(source, usePolarization: true, stopAtIncidentImage: true);
        optic.Polarization = new(false, 0, 1);
        var p = optic.SequentialRayTracer.DiagnoseApertures(source, usePolarization: true, stopAtIncidentImage: true);
        Near(average.UnpolarizedIntensity!.Value, (s.PolarizedIntensity!.Value + p.PolarizedIntensity!.Value) / 2);
        Assert.True(p.PolarizationWeightedIntensity > s.PolarizationWeightedIntensity);
        Near(average.UnpolarizedIntensity.Value, p.UnpolarizedIntensity!.Value);
        // A collimated plane plate subtends zero image solid angle. Use a finite
        // off-axis object for an actual illumination integral.
        optic.SurfaceGroup.Items[0].Thickness = 10; optic.SurfaceGroup.Renumber();
        optic.FieldDefinition = FieldDefinitionKind.ObjectHeight; optic.Fields[0].Y = 3;
        optic.Polarization = new(false, 1, 0);
        var si = IlluminationMetrics.Evaluate(optic, (0, 1), .55, usePolarization: true);
        optic.Polarization = new(false, 0, 1);
        var pi = IlluminationMetrics.Evaluate(optic, (0, 1), .55, usePolarization: true);
        Assert.True(pi.ProjectedCosineArea > si.ProjectedCosineArea);
    }

    [Fact]
    public void RretUsesGlobalReferenceAxisButKeepsItsOwnExplicitJonesInputs()
    {
        var optic = Plate(); optic.Fields[0].X = 20;
        var row = new MeritOperandDefinition { Type = "RRET", ZemaxIntegerParameters = [2, 1], ZemaxDataParameters = [1, 1, 1, 2, 45, 0] };
        var x = Value(optic, row);
        optic.Polarization = new(false, 0, 1, 15, 18, PolarizationReferenceAxis.Y);
        var y = Value(optic, row); Assert.True(Math.Abs(x - y) > 1e-3);
        optic.Polarization = optic.Polarization with { Jx = 100, Jy = 30, XPhaseDegrees = 37, Unpolarized = true };
        Near(y, Value(optic, row));
    }

    [Fact]
    public void ScalarCoatingReportsDeclaredPowerAndOnlyAttenuatesOnce()
    {
        var optic = Plate(); optic.Polarization = new(false, 1, 0);
        optic.SurfaceGroup.Items[1].CoatingModel = new SimpleCoatingModel(.25, .5);
        Near(.5, Value(optic, Row(1, 1))); Near(.25, Value(optic, Row(2, 1))); Near(.25, Value(optic, Row(3, 1)));
        Near(.5, Value(optic, Row(4, 1))); Near(-Math.Sqrt(.5), Value(optic, Row(-6, 1)));
        Near(.25, Value(optic, Row(0, 1)));
        optic.SurfaceGroup.Items[1].CoatingModel = new SimpleCoatingModel(.8, .5);
        Assert.NotEmpty(MeritFunctionCatalog.Evaluate(optic, Row(0, 1)).Error);
    }

    [Fact]
    public void ReflectionUsesTypedBranchAndRetainsBothCoatingCoefficients()
    {
        var optic = Plate(); optic.SurfaceGroup.Items[0].MaterialAfter = new ConstantIndexMaterial("glass", 1.5);
        optic.SurfaceGroup.Items[1].MaterialAfter = new AirMaterial();
        optic.Fields[0].Y = 60;
        Near(1, Value(optic, Row(1, 1))); Near(0, Value(optic, Row(2, 1)));
        var q = Math.Sqrt(.75 - 1 / 2.25); var phase = 2 * Math.Atan(q / .5);
        Near(Math.Cos(phase), Value(optic, Row(-6, 1))); Near(Math.Sin(phase), Value(optic, Row(-7, 1)));
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
    public void InvalidReferencesAndModesAreExplicitErrors(int problem)
    {
        var optic = Plate(); var row = Row(0);
        switch (problem)
        {
            case 0: row.ZemaxIntegerParameters[0] = 999; break;
            case 1: row.ZemaxIntegerParameters[1] = 0; break;
            case 2: row.ZemaxDataParameters[0] = 0; break;
            case 3: row.ZemaxDataParameters[0] = 1.5; break;
            case 4: row.ZemaxDataParameters[1] = row.ZemaxDataParameters[2] = .8; break;
            case 5: row.ZemaxDataParameters[3] = 9; break;
            case 6: row.ZemaxDataParameters[3] = int.MinValue; break;
            case 7: optic.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(.01); row.ZemaxDataParameters[1] = 1; break;
            case 8: optic.SurfaceGroup.Items[1].MaterialAfter = new GradientIndexMaterial("grin", new Gradient3IndexProfile(1.5, axial1: .01), 100); break;
        }
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.NotEmpty(result.Error); Assert.True(double.IsPositiveInfinity(result.Contribution));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void InvalidSnapshotPolarizationNeverReplacesThePreviousState(int problem)
    {
        var optic = Plate(); var before = optic.ToSnapshot(); var p = before.Polarization!;
        p = problem switch
        {
            0 => p with { Jx = -1 },
            1 => p with { Jx = 0, Jy = 0 },
            2 => p with { XPhaseDegrees = double.NaN },
            3 => p with { ReferenceAxis = "1" },
            _ => p with { ReferenceAxis = "invalid" }
        };
        Assert.Throws<InvalidDataException>(() => Optic.FromSnapshot(before with { Polarization = p }));
        Assert.Equal(new SystemPolarization(), optic.Polarization);
        var copy = Optic.FromSnapshot(before with { Polarization = null }); Assert.Equal(new SystemPolarization(), copy.Polarization);
    }

    [Theory]
    [InlineData(".zmx")]
    [InlineData(".seq")]
    [InlineData(".len")]
    [InlineData(".txt")]
    public void TextExportRefusesToSilentlyDropUnmappedPolarization(string extension)
    {
        var optic = Plate(); optic.Polarization = new(true, 1, 1, 30, 0);
        Assert.Contains("STAROPT", Assert.Throws<NotSupportedException>(() => OpticalFormatCatalog.Export(optic, extension)).Message);
    }

    [Fact]
    public async Task ApplicationEditorStaroptAndUndoRedoPreserveSystemStateAndOperandSlots()
    {
        var optic = Plate(); optic.MeritFunctionOperands.Add(Row(-2, 1));
        var path = Path.Combine(Path.GetTempPath(), $"coda-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new([optic], 0), path);
            using var app = WorkbenchApplication.Create(); await app.Documents.OpenAsync(path);
            var state = new PolarizationSettingsDto(false, 1, 2, 30, -40, "Y"); var revision = app.Events.Revision;
            app.Prescription.UpdatePolarizationSettings(state); Assert.Equal(revision + 1, app.Events.Revision);
            Assert.Equal(state, app.Prescription.GetPolarizationSettings());
            Assert.True(app.Documents.Undo()); Assert.True(app.Prescription.GetPolarizationSettings().Unpolarized);
            Assert.True(app.Documents.Redo()); Assert.Equal(state, app.Prescription.GetPolarizationSettings());
            revision = app.Events.Revision;
            Assert.Throws<ArgumentException>(() => app.Prescription.UpdatePolarizationSettings(state with { Jx = -1 }));
            Assert.Equal(revision, app.Events.Revision); Assert.Equal(state, app.Prescription.GetPolarizationSettings());
            var type = app.Optimization.GetMeritOperandTypes().Single(t => t.Code == "CODA");
            Assert.False(type.CompatibilityOnly); Assert.Contains("原生", type.Calculation);
            var editor = new MeritOperandEditorRow(Assert.Single(app.Optimization.GetMeritFunction()), type);
            editor.Parameter1 = 0; editor.Parameter3 = 1; editor.Parameter4 = .3; editor.Parameter5 = -.2; editor.Parameter6 = 8;
            app.Optimization.SetMeritFunction([editor.ToDto()]); await app.Documents.SaveAsync(path);
            var copy = (await StarOptProjectStore.LoadAsync(path)).Configurations[0]; var row = Assert.Single(copy.MeritFunctionOperands);
            Assert.Equal(new SystemPolarization(false, 1, 2, 30, -40, PolarizationReferenceAxis.Y), copy.Polarization);
            Assert.Equal(new[] { 0, 1 }, row.ZemaxIntegerParameters); Assert.Equal(new[] { 1, .3, -.2, 8 }, row.ZemaxDataParameters);
            Assert.InRange(Value(copy, row), 0, 1);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ProductionDlsCanOptimizeCodaTransmissionUsingRadius()
    {
        var optic = Plate(); var front = optic.SurfaceGroup.Items[1]; var row = Row(-2, 1); row.ZemaxDataParameters[2] = .7;
        front.Geometry = new StandardGeometry(20); row.Target = Value(optic, row);
        front.Geometry = new StandardGeometry(35); front.RadiusVariable = true; optic.MeritFunctionOperands.Add(row);
        var runtime = new WorkbenchRuntime(optic); var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 60);
        Assert.True(result.InitialMerit > 1e-8); Assert.True(result.FinalMerit < result.InitialMerit * 1e-5);
        Assert.InRange(Math.Abs(((StandardGeometry)runtime.CurrentOptic.SurfaceGroup.Items[1].Geometry).Radius - 20), 0, .02);
    }

    [Fact]
    public void NativeCodaRowsRemainReadOnlyAndDoNotAcquireDefaultSystemSettingsAsVerifiedInput()
    {
        var optic = OpticalFormatCatalog.Import("MODE SEQ\nENPD 2\nWAVM 1 .55 1\nSURF 0\n DISZ INFINITY\nSURF 1\n STOP\n DISZ 5\nSURF 2\n DISZ 0\nCODA 1 1 1 .2 .3 110 0 1 91 92\n", ".zmx");
        foreach (var copy in new[] { optic, Optic.FromSnapshot(optic.ToSnapshot()) })
        {
            var row = Assert.Single(copy.MeritFunctionOperands); Assert.True(row.CompatibilityOnly); Assert.False(row.Enabled);
            Assert.Equal(new[] { 1, .2, .3, 110 }, row.ZemaxDataParameters); Assert.Contains("91 92", row.Comment);
            row.Enabled = true; Assert.NotEmpty(MeritFunctionCatalog.Evaluate(copy, row).Error);
        }
    }

    [Fact]
    public void CancellationIsNotConvertedToAnOperandFailureOrZero()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(Plate(), Row(0)));
    }

    private static Optic Plate()
    {
        var optic = new Optic("CODA reference plate");
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { Thickness = 2, MaterialAfter = new ConstantIndexMaterial("glass", 1.5), IsStop = true, SemiDiameter = 30, SemiDiameterFixed = true },
            new OpticalSurface { Thickness = 5, SemiDiameter = 30, SemiDiameterFixed = true },
            new OpticalSurface { SemiDiameter = 30, SemiDiameterFixed = true }]);
        optic.Fields.Add(new FieldPoint { Y = 30 }); optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.Aperture.Value = 2; return optic;
    }
    private static MeritOperandDefinition Row(int data, int surface = 0) => new()
    { Type = "CODA", ZemaxIntegerParameters = [surface, 1], ZemaxDataParameters = [1, 0, 0, data], Weight = 1 };
    private static double Value(Optic optic, MeritOperandDefinition row)
    { var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.Empty(result.Error); return result.Value; }
    private static void Near(double expected, double actual, double tolerance = 2e-11) => Assert.InRange(Math.Abs(expected - actual), 0, tolerance);
    private static double Squared(Complex value) => value.Magnitude * value.Magnitude;
    private static (Complex R, Complex T) Airy(bool p)
    {
        Complex a = 1, b = new(2.1, .15), c = 1.5, ca = Math.Sqrt(.75);
        var cb = Complex.Sqrt(1 - .25 / (b * b)); var cc = Complex.Sqrt(1 - .25 / (c * c));
        var f = Interface(a, b, ca, cb); var g = Interface(b, c, cb, cc);
        var e = Complex.Exp(Complex.ImaginaryOne * 2 * Math.PI * b * cb * 87 / 550);
        var d = 1 + f.R * g.R * e * e;
        return ((f.R + g.R * e * e) / d, f.T * g.T * e / d * Math.Sqrt((c * cc).Real / ca.Real));
        (Complex R, Complex T) Interface(Complex n1, Complex n2, Complex c1, Complex c2) => p
            ? ((n2 * c1 - n1 * c2) / (n2 * c1 + n1 * c2), 2 * n1 * c1 / (n2 * c1 + n1 * c2))
            : ((n1 * c1 - n2 * c2) / (n1 * c1 + n2 * c2), 2 * n1 * c1 / (n1 * c1 + n2 * c2));
    }
}
