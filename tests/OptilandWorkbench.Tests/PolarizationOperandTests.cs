using System.Numerics;
using OptilandWorkbench.App.ViewModels;
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
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class PolarizationOperandTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(60)]
    public void ComplexPlateFieldMatchesIndependentSnellFresnelAndUnpolarizedTrace(double degrees)
    {
        var optic = Plate(); var source = Source(degrees) with { Intensity = .7 };
        var result = optic.SequentialRayTracer.TracePolarized(source, new(1, 2, 20, -40), includePropagationPhase: false);
        var (ts, tp) = BarePower(degrees);
        var ex = ts * Math.Sqrt(.7 / 5) * Phase(20);
        var ep = tp * Math.Sqrt(.7 * 4 / 5) * Phase(-40);
        Close(ex, result.LocalField.X);
        Close(ep * Math.Cos(Rad(degrees)), result.LocalField.Y);
        Close(-ep * Math.Sin(Rad(degrees)), result.LocalField.Z);
        Near(.7 * (ts * ts + 4 * tp * tp) / 5, result.PolarizedIntensity);
        Near(.7 * (ts * ts + tp * tp) / 2, result.UnpolarizedIntensity);
        var diagnostic = optic.SequentialRayTracer.DiagnoseApertures(source, stopAtIncidentImage: true, usePolarization: true);
        Near(diagnostic.UnpolarizedIntensity!.Value, result.UnpolarizedIntensity);
        Near(Rad(60), PolarizationMetrics.PhaseDifference(result.LocalField));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(60)]
    public void CoherentFilmUsesComplexAiryAmplitudeAndPositiveTimeConvention(double degrees)
    {
        var optic = Plate(coated: true);
        var result = optic.SequentialRayTracer.TracePolarized(Source(degrees), new(1, 1, 45, 0), includePropagationPhase: false);
        var (ts, tp) = BarePower(degrees);
        var s = Complex.Conjugate(FilmTransmission(550, degrees, false)) * Math.Sqrt(ts / 2) * Phase(45);
        var p = Complex.Conjugate(FilmTransmission(550, degrees, true)) * Math.Sqrt(tp / 2);
        Close(s, result.LocalField.X); Close(p * Math.Cos(Rad(degrees)), result.LocalField.Y);
        Close(-p * Math.Sin(Rad(degrees)), result.LocalField.Z);
        Near(s.Magnitude * s.Magnitude + p.Magnitude * p.Magnitude, result.PolarizedIntensity);
        var negative = optic.SequentialRayTracer.TracePolarized(Source(degrees), new(1, 1, -45, 0),
            timeConvention: HarmonicTimeConvention.Negative, includePropagationPhase: false);
        Close(Complex.Conjugate(result.LocalField.X), negative.LocalField.X);
        Close(Complex.Conjugate(result.LocalField.Y), negative.LocalField.Y);
        Near(result.UnpolarizedIntensity, negative.UnpolarizedIntensity);
    }

    [Fact]
    public void CommonPropagationPhaseUsesOpticalPathAndCanBeOmittedWithoutLosingInterfacePhase()
    {
        var optic = Plate(coated: true); var source = Source(30);
        var full = optic.SequentialRayTracer.TracePolarized(source, new(1, 1, 15, -10));
        var reduced = optic.SequentialRayTracer.TracePolarized(source, new(1, 1, 15, -10), includePropagationPhase: false);
        var angle = Rad(30); var glassCos = Math.Sqrt(1 - Math.Pow(Math.Sin(angle) / 1.5, 2));
        var opl = 6 / Math.Cos(angle) + 1.5 * 2 / glassCos;
        var propagation = Complex.FromPolarCoordinates(1, -2 * Math.PI * Math.IEEERemainder(opl, .00055) / .00055);
        Close(reduced.LocalField.X * propagation, full.LocalField.X, 2e-10);
        Close(reduced.LocalField.Y * propagation, full.LocalField.Y, 2e-10);
        Near(PolarizationMetrics.PhaseDifference(reduced.LocalField), PolarizationMetrics.PhaseDifference(full.LocalField));
        Assert.True(full.IncludesPropagationPhase); Assert.False(reduced.IncludesPropagationPhase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TotalInternalReflectionAndCommandedMirrorKeepTheirDistinctComplexFields(bool mirror)
    {
        var optic = new Optic();
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity, MaterialAfter = mirror ? new AirMaterial() : new ConstantIndexMaterial("glass", 1.5) },
            new OpticalSurface { Thickness = -2, IsReflective = mirror }, new OpticalSurface()]);
        var result = optic.SequentialRayTracer.TracePolarized(Source(60), new(1, 1), targetSurfaceIndex: 1, includePropagationPhase: false);
        var q = Math.Sqrt(.75 - 1 / 2.25);
        var rs = mirror ? new Complex(-1, 0) : Complex.FromPolarCoordinates(1, 2 * Math.Atan(q / .5));
        var rp = mirror ? Complex.One : Complex.FromPolarCoordinates(1, 2 * Math.Atan(2.25 * q / .5));
        Close(rs / Math.Sqrt(2), result.LocalField.X);
        Close(-rp * .5 / Math.Sqrt(2), result.LocalField.Y);
        Close(-rp * Math.Sqrt(.75 / 2), result.LocalField.Z);
        Near(1, result.PolarizedIntensity);
        Assert.Equal(mirror ? RayInteractionKind.Reflected : RayInteractionKind.TotalInternalReflection, result.Sample.InteractionKind);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(.25)]
    [InlineData(1)]
    public void ScalarCoatingIsAppliedOnlyOnce(double transmission)
    {
        var optic = Plate(); optic.SurfaceGroup.Items[1].CoatingModel = new SimpleCoatingModel(transmission);
        var result = optic.SequentialRayTracer.TracePolarized(Source(30), new(1, 2), includePropagationPhase: false);
        var (ts, tp) = BarePower(30);
        Near(transmission * (ts + 4 * tp) / 5, result.PolarizedIntensity);
        Near(transmission * (ts + tp) / 2, result.UnpolarizedIntensity);
    }

    [Theory]
    [InlineData(PolarizationReferenceAxis.X)]
    [InlineData(PolarizationReferenceAxis.Y)]
    [InlineData(PolarizationReferenceAxis.Z)]
    public void ObjectFrameReferenceAndTargetProjectionAreRigidTransformInvariant(PolarizationReferenceAxis axis)
    {
        var optic = Plate(coated: true); var source = Source(35); var input = new JonesInputState(1, 2, 15, -20, axis);
        var before = optic.SequentialRayTracer.TracePolarized(source, input, includePropagationPhase: false);
        var frame = new CoordinateSystem(new(4, -3, 9), 13, 21, -7);
        foreach (var surface in optic.SurfaceGroup.Items)
            surface.CoordinateSystem = new(frame.ToGlobalPoint(surface.CoordinateSystem.Origin), 13, 21, -7);
        var after = optic.SequentialRayTracer.TracePolarized(source with
        { Origin = frame.ToGlobalPoint(source.Origin), Direction = frame.ToGlobalDirection(source.Direction) },
            input, inputCoordinates: frame, includePropagationPhase: false);
        Close(before.LocalField.X, after.LocalField.X); Close(before.LocalField.Y, after.LocalField.Y);
        Close(before.LocalField.Z, after.LocalField.Z);
        Near(before.PolarizedIntensity, after.PolarizedIntensity);
    }

    [Fact]
    public void InputJonesNormalizationHandlesHugeMagnitudesAndReferenceAxisDegeneracyIsExplicit()
    {
        var optic = Plate(); var ray = Source(30);
        var expected = optic.SequentialRayTracer.TracePolarized(ray, new(1, 2, 90, 0), includePropagationPhase: false);
        var actual = optic.SequentialRayTracer.TracePolarized(ray, new(1e300, 2e300, 450, 0), includePropagationPhase: false);
        Assert.Equal(expected.LocalField, actual.LocalField);
        Assert.Throws<NotSupportedException>(() => optic.SequentialRayTracer.TracePolarized(Source(0), new(1, 1, ReferenceAxis: PolarizationReferenceAxis.Z)));
    }

    [Fact]
    public void RotatingIncidencePlanesPreserveCoherentCrossTerms()
    {
        var k = new Vector3D(0, 0, 1); var transport = new UnpolarizedPowerTransport(new(1, 0, 0), new(0, 1, 0));
        transport.Apply(k, k, new(0, 1, -1), 1, Complex.ImaginaryOne);
        transport.Apply(k, k, new(1, 1, -1), .2, .8);
        var input = 1 / Math.Sqrt(2.0);
        var field = transport.Combine(input, -Complex.ImaginaryOne * input);
        // Quarter-wave plate converts circular input to the +45 degree linear
        // state; the second element transmits only its 0.8 P amplitude.
        Close(.8 * input, field.X); Close(.8 * input, field.Y); Near(.64, field.SquaredNorm);
        Near((.04 + .64) / 2, transport.Power);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(90, 0, 90)]
    [InlineData(170, -170, 20)]
    [InlineData(-30, 15, 45)]
    public void RmsUsesPrincipalLocalExEyPhaseWithoutSubtractingItsMean(double xp, double yp, double expected)
    {
        var optic = Plate(); var row = Row(); row.ZemaxDataParameters[4] = xp; row.ZemaxDataParameters[5] = yp;
        Near(Rad(expected), Value(optic, row));
        row.ZemaxIntegerParameters[0] = 32;
        Near(Rad(expected), Value(optic, row));
    }

    [Fact]
    public void AllWavelengthRmsUsesNormalizedSpectralWeightsAndIndependentFilmPhase()
    {
        var optic = Plate(coated: true);
        optic.Wavelengths[0].Weight = 1;
        optic.Wavelengths.Add(new Wavelength { Nanometers = 650, Weight = 3 });
        var row = Row(); row.ZemaxIntegerParameters[1] = 0;
        var a = IndependentRetardance(550); var b = IndependentRetardance(650);
        Near(Math.Sqrt((a * a + 3 * b * b) / 4), Value(optic, row));
        row.ZemaxIntegerParameters[1] = 2; Near(Math.Abs(b), Value(optic, row));
        optic.Wavelengths[1].Weight = 0; Near(Math.Abs(b), Value(optic, row));
        row.ZemaxIntegerParameters[1] = 0; Near(Math.Abs(a), Value(optic, row));
        optic.Wavelengths[0].Weight = 0;
        Assert.NotEmpty(MeritFunctionCatalog.Evaluate(optic, row).Error);
    }

    [Fact]
    public void ChangingCoatingAndImageGeometryRecalculatesRatherThanReusingAnOldJonesResult()
    {
        var optic = Plate(coated: true); var row = Row();
        var before = Value(optic, row);
        optic.SurfaceGroup.Items[1].CoatingModel = new CoherentMultilayerCoating([new(new ConstantIndexMaterial("film", 2.1, .15), 130)]);
        Assert.True(Math.Abs(Value(optic, row) - before) > .005);
        optic.SurfaceGroup.Items[^1].CoatingModel = new SimpleCoatingModel(0);
        Assert.NotEmpty(MeritFunctionCatalog.Evaluate(optic, row).Error);
    }

    [Fact]
    public void ProductionDlsCanOptimizeRadiusAgainstARealRetardanceResidual()
    {
        var optic = Plate(coated: true); var front = optic.SurfaceGroup.Items[1]; var row = Row();
        optic.Aperture.Value = 10;
        front.Geometry = new StandardGeometry(40); row.Target = Value(optic, row);
        front.Geometry = new StandardGeometry(55); front.RadiusVariable = true;
        optic.MeritFunctionOperands.Add(row);
        var runtime = new WorkbenchRuntime(optic);
        var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 60);
        Assert.True(result.InitialMerit > 1e-8, $"Initial merit {result.InitialMerit:R}, final {result.FinalMerit:R}");
        Assert.True(result.FinalMerit < result.InitialMerit * 1e-5, $"Initial merit {result.InitialMerit:R}, final {result.FinalMerit:R}");
        Assert.InRange(Math.Abs(((StandardGeometry)runtime.CurrentOptic.SurfaceGroup.Items[1].Geometry).Radius - 40), 0, .03);
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
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public void InvalidSlotsOrUnsupportedOpticsReturnErrorsInsteadOfSuccessfulZero(int problem)
    {
        var optic = Plate(); var row = Row();
        switch (problem)
        {
            case 0: row.ZemaxIntegerParameters[0] = 0; break;
            case 1: row.ZemaxIntegerParameters[0] = 33; break;
            case 2: row.ZemaxIntegerParameters[1] = -1; break;
            case 3: row.ZemaxIntegerParameters[1] = 99; break;
            case 4: row.ZemaxDataParameters[0] = double.NaN; break;
            case 5: row.ZemaxDataParameters[1] = 1.1; break;
            case 6: row.ZemaxDataParameters[2] = -1; break;
            case 7: row.ZemaxDataParameters[2] = row.ZemaxDataParameters[3] = 0; break;
            case 8: row.ZemaxDataParameters[4] = double.PositiveInfinity; break;
            case 9: row.ZemaxDataParameters = [0, 1, 1, 1]; break;
            case 10: optic.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(.01); break;
            case 11: optic.SurfaceGroup.Items[1].PhysicalAperture = new AnnularAperture(.1, 30); break;
            case 12: optic.SurfaceGroup.Items[1].MaterialAfter = new GradientIndexMaterial("grin", new Gradient3IndexProfile(1.5, axial1: .01), 100); break;
            case 13: optic.SurfaceGroup.Items[1].InteractionModel = new ThinLensInteractionModel(50); break;
            case 14: optic.Fields[0].VignetteFactorY = .2; break;
        }
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.NotEmpty(result.Error); Assert.True(double.IsPositiveInfinity(result.Contribution));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void InvalidRayAndUndefinedPhaseAreExplicit(int problem)
    {
        var optic = Plate(); var ray = Source(0); var input = new JonesInputState(1, 1);
        if (problem == 0) ray = ray with { Intensity = -1 };
        if (problem == 1) ray = ray with { WavelengthNanometers = 0 };
        if (problem == 2) ray = ray with { Direction = Vector3D.Zero };
        if (problem == 3) input = new(double.NaN, 1);
        if (problem == 4) input = new(0, 1);
        Assert.ThrowsAny<Exception>(() => PolarizationMetrics.PhaseDifference(
            optic.SequentialRayTracer.TracePolarized(ray, input).LocalField));
    }

    [Fact]
    public void CancellationReachesDirectTraceAndOperand()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Plate().SequentialRayTracer.TracePolarized(Source(0), new(1, 1), cancellationToken: cancellation.Token));
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(Plate(), Row()));
    }

    [Fact]
    public async Task ApplicationEditorAndStaroptPreserveAllEightSlotsAndRecalculate()
    {
        var optic = Plate(coated: true); optic.MeritFunctionOperands.Add(Row());
        var path = Path.Combine(Path.GetTempPath(), $"rret-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new([optic], 0), path);
            using var app = WorkbenchApplication.Create(); await app.Documents.OpenAsync(path);
            var dto = Assert.Single(app.Optimization.GetMeritFunction());
            var type = app.Optimization.GetMeritOperandTypes().Single(t => t.Code == "RRET");
            Assert.False(type.CompatibilityOnly); Assert.Equal(8, type.Parameters!.Count);
            Assert.Contains("原生", type.Calculation);
            var editor = new MeritOperandEditorRow(dto, type);
            for (var i = 0; i < 8; i++) Assert.True(editor.IsParameterEditable(i));
            editor.Parameter1 = 32; editor.Parameter5 = 2; editor.Parameter7 = 60; editor.Parameter8 = -10;
            app.Optimization.SetMeritFunction([editor.ToDto()]); await app.Documents.SaveAsync(path);
            var copy = (await StarOptProjectStore.LoadAsync(path)).Configurations[0];
            var row = Assert.Single(copy.MeritFunctionOperands);
            Assert.False(row.CompatibilityOnly); Assert.Equal(32, row.ZemaxIntegerParameters[0]);
            Assert.Equal(new[] { 0.0, 1, 2, 1, 60, -10 }, row.ZemaxDataParameters);
            Near(Math.Abs(IndependentRetardance(550) + Rad(25)), Value(copy, row));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NativeUnverifiedRowsRemainReadOnlyWithoutInventingMissingPhaseSlots()
    {
        const string line = "RRET 3 1 .1 .2 .3 .4 .05 7 91 92";
        var optic = OpticalFormatCatalog.Import($"""
            MODE SEQ
            ENPD 10
            WAVM 1 .55 1
            SURF 0
              DISZ INFINITY
            SURF 1
              STOP
              DISZ 10
            SURF 2
              DISZ 0
            {line}
            """, ".zmx");
        foreach (var copy in new[] { optic, Optic.FromSnapshot(optic.ToSnapshot()) })
        {
            var row = Assert.Single(copy.MeritFunctionOperands);
            Assert.True(row.CompatibilityOnly); Assert.False(row.Enabled);
            Assert.Equal(new[] { 3, 1 }, row.ZemaxIntegerParameters);
            Assert.Equal(new[] { .1, .2, .3, .4 }, row.ZemaxDataParameters);
            Assert.Contains("91 92", row.Comment);
            row.Enabled = true; Assert.NotEmpty(MeritFunctionCatalog.Evaluate(copy, row).Error);
        }
    }

    private static Optic Plate(bool coated = false)
    {
        var optic = new Optic("Jones reference plate"); var glass = new ConstantIndexMaterial("glass", 1.5);
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { Thickness = 2, MaterialAfter = glass, IsStop = true, SemiDiameter = 30, SemiDiameterFixed = true },
            new OpticalSurface { Thickness = 5, SemiDiameter = 30, SemiDiameterFixed = true },
            new OpticalSurface { SemiDiameter = 30, SemiDiameterFixed = true }]);
        if (coated) optic.SurfaceGroup.Items[1].CoatingModel = new CoherentMultilayerCoating([new(new ConstantIndexMaterial("film", 2.1, .15), 87)]);
        optic.Fields.Add(new FieldPoint { Y = 30 });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter; optic.Aperture.Value = 2;
        return optic;
    }
    private static MeritOperandDefinition Row() => new()
    { Type = "RRET", ZemaxIntegerParameters = [3, 1], ZemaxDataParameters = [0, 1, 1, 1, 45, 0], Weight = 1 };
    private static RealRay Source(double degrees) => new(new(0, 0, -1), new(0, Math.Sin(Rad(degrees)), Math.Cos(Rad(degrees))), 550);
    private static double Rad(double degrees) => degrees * Math.PI / 180;
    private static Complex Phase(double degrees) => Complex.FromPolarCoordinates(1, Rad(degrees));
    private static void Near(double expected, double actual, double tolerance = 2e-11) => Assert.InRange(Math.Abs(expected - actual), 0, tolerance);
    private static void Close(Complex expected, Complex actual, double tolerance = 2e-11) => Assert.InRange((expected - actual).Magnitude, 0, tolerance);
    private static double Value(Optic optic, MeritOperandDefinition row)
    { var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.Empty(result.Error); return result.Value; }

    // Independent analytical references belong only in tests, never the runtime.
    private static (double S, double P) BarePower(double degrees)
    {
        var c = Math.Cos(Rad(degrees)); var t = Math.Sqrt(1 - Math.Pow(Math.Sin(Rad(degrees)) / 1.5, 2));
        return (1 - Math.Pow((c - 1.5 * t) / (c + 1.5 * t), 2), 1 - Math.Pow((1.5 * c - t) / (1.5 * c + t), 2));
    }
    private static Complex FilmTransmission(double wavelength, double degrees, bool p)
    {
        Complex n0 = 1, film = new(2.1, .15), ns = 1.5;
        var sine = Math.Sin(Rad(degrees)); Complex c0 = Math.Cos(Rad(degrees));
        var cf = Complex.Sqrt(1 - sine * sine / (film * film)); var cs = Complex.Sqrt(1 - sine * sine / (ns * ns));
        var a = Interface(n0, film, c0, cf); var b = Interface(film, ns, cf, cs);
        var e = Complex.Exp(Complex.ImaginaryOne * 2 * Math.PI * film * cf * 87 / wavelength);
        return a.T * b.T * e / (1 + a.R * b.R * e * e) * Math.Sqrt((ns * cs).Real / c0.Real);
        (Complex R, Complex T) Interface(Complex n1, Complex n2, Complex c1, Complex c2) => p
            ? ((n2 * c1 - n1 * c2) / (n2 * c1 + n1 * c2), 2 * n1 * c1 / (n2 * c1 + n1 * c2))
            : ((n1 * c1 - n2 * c2) / (n1 * c1 + n2 * c2), 2 * n1 * c1 / (n1 * c1 + n2 * c2));
    }
    private static double IndependentRetardance(double wavelength) =>
        (Complex.Conjugate(FilmTransmission(wavelength, 30, false) / FilmTransmission(wavelength, 30, true)) * Phase(45)).Phase;
}
