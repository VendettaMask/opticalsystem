using System.Text.Json;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Propagation;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class GradientIndexParaxialTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RadialFirstOrderTransportMatchesIndependentHarmonicSolution(int kind)
    {
        var m = GradientIndexParaxialTransport.Between(Material(Profile(kind)), 2, 10, 550);
        var k = Math.Sqrt(.02); var phase = 8 * k;
        var expected = new GradientIndexParaxialMatrix(Math.Cos(phase), Math.Sin(phase) / k,
            -k * Math.Sin(phase), Math.Cos(phase));
        Near(expected, m); Near(1, m.Determinant);
        output.WriteLine(JsonSerializer.Serialize(new { grinParaxial = "harmonic", kind, matrixError = Error(expected, m) }));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void AxialGradientUsesIntegratedReciprocalIndexAndEndpointSlopeRatio(int sign)
    {
        var gradient = .03 * sign;
        var material = Material(new Gradient3IndexProfile(1.5, axial1: gradient));
        var n1 = 1.5 + gradient * 2; var n2 = 1.5 + gradient * 12;
        var m = GradientIndexParaxialTransport.Between(material, 2, 12, 550);
        var expected = new GradientIndexParaxialMatrix(1,
            sign == 0 ? 10 : n1 / gradient * Math.Log(n2 / n1), 0, n1 / n2);
        Near(expected, m); Near(n1 / n2, m.Determinant);
        output.WriteLine(JsonSerializer.Serialize(new { grinParaxial = "axial", sign, matrixError = Error(expected, m) }));
    }

    [Fact]
    public void DefocusingAndIndependentAxesHaveTheCorrectHyperbolicAndHarmonicPowers()
    {
        var material = Material(new Gradient4IndexProfile(1.5, x2: .015, y2: -.015));
        var x = GradientIndexParaxialTransport.Between(material, 0, 8, 550, useX: true);
        var y = GradientIndexParaxialTransport.Between(material, 0, 8, 550);
        var k = Math.Sqrt(.02); var a = 8 * k;
        Near(new(Math.Cosh(a), Math.Sinh(a) / k, k * Math.Sinh(a), Math.Cosh(a)), x);
        Near(new(Math.Cos(a), Math.Sin(a) / k, -k * Math.Sin(a), Math.Cos(a)), y);
        Assert.Throws<NotSupportedException>(() => Rod(material).Paraxial.EstimateEffectiveFocalLength());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MatricesComposeAndReverseWithoutChangingTheProfileOrigin(int kind)
    {
        var material = Material(Profile(kind, axial: true));
        var ab = GradientIndexParaxialTransport.Between(material, 1, 5, 550);
        var bc = GradientIndexParaxialTransport.Between(material, 5, 9, 550);
        var ac = GradientIndexParaxialTransport.Between(material, 1, 9, 550);
        var ca = GradientIndexParaxialTransport.Between(material, 9, 1, 550);
        Near(ac, Compose(bc, ab)); Near(new(1, 0, 0, 1), Compose(ca, ac));
        Near(new(1, 0, 0, 1), GradientIndexParaxialTransport.Between(material, 3, 3, 550));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ParaxialColumnsAreLimitsOfTheFormalContinuousRayEquation(int kind)
    {
        var material = Material(Profile(kind, axial: true));
        var m = GradientIndexParaxialTransport.Between(material, 0, 8, 550);
        const double epsilon = .0001;
        var height = Trace(epsilon, 0);
        var slope = Trace(0, epsilon);
        Assert.InRange(Math.Abs(height.Height / epsilon - m.A), 0, 2e-7);
        Assert.InRange(Math.Abs(height.Slope / epsilon - m.C), 0, 2e-7);
        Assert.InRange(Math.Abs(slope.Height / epsilon - m.B), 0, 2e-7);
        Assert.InRange(Math.Abs(slope.Slope / epsilon - m.D), 0, 2e-7);
        (double Height, double Slope) Trace(double h, double u)
        {
            var ray = GradientIndexRayIntegrator.TraceToSurface(material.Profile, CoordinateSystem.Global,
                new(0, h, 0), new(0, u, 1), 550, new PlaneGeometry(), new(new(0, 0, 8)), 20,
                new(MaximumStep: .02, PositionTolerance: 1e-13, DirectionTolerance: 1e-13, RelativeTolerance: 1e-13));
            Assert.Equal(GradientIndexTermination.SurfaceReached, ray.Termination);
            return (ray.End.Position.Y, ray.End.Direction.Y / ray.End.Direction.Z);
        }
    }

    [Fact]
    public void FullRodPowerPupilsAndMarginalRayMatchIndependentTransferMatrices()
    {
        var optic = Rod(); var k = Math.Sqrt(.02); var phase = k * 8;
        var c = Math.Cos(phase); var s = Math.Sin(phase); const double n = 1.5;
        Near(1 / (n * k * s), optic.Paraxial.EstimateEffectiveFocalLength());
        Near(n * k * s, optic.Paraxial.EstimateOpticalPower());
        Near(Math.Tan(phase) / (n * k), optic.Paraxial.EstimateEntrancePupilLocation());
        Near(2 / c, optic.Paraxial.EstimateEntrancePupilDiameter());
        Near(-5, optic.Paraxial.EstimateExitPupilLocation());
        Near(2, optic.Paraxial.EstimateExitPupilDiameter());
        var paraxial = optic.Paraxial.TraceGeneric([1.0, 0], [0.0, 1], 0, .55);
        Near(c - 5 * n * k * s, paraxial.Heights[^1][0]);
        Near(s / (n * k) + 5 * c, paraxial.Heights[^1][1]);
        Near(-n * k * s, paraxial.Slopes[^1][0]); Near(c, paraxial.Slopes[^1][1]);
        var clone = Optic.FromSnapshot(optic.ToSnapshot());
        Near(optic.Paraxial.EstimateEffectiveFocalLength(), clone.Paraxial.EstimateEffectiveFocalLength());
    }

    [Fact]
    public void CurvedInterfacesUseDifferentAxialEndpointIndices()
    {
        var optic = Rod(Material(new Gradient3IndexProfile(1.5, axial1: .02)));
        optic.SurfaceGroup.Items[1].Geometry = new StandardGeometry(30);
        optic.SurfaceGroup.Items[2].Geometry = new StandardGeometry(-40);
        const double front = 1.5, back = 1.66;
        var entry = new GradientIndexParaxialMatrix(1, 0, -(front - 1) / (front * 30), 1 / front);
        var volume = new GradientIndexParaxialMatrix(1, front / .02 * Math.Log(back / front), 0, front / back);
        var exit = new GradientIndexParaxialMatrix(1, 0, -(1 - back) / -40, back);
        var expected = Compose(new(1, 5, 0, 1), Compose(exit, Compose(volume, entry)));
        var actual = optic.Paraxial.TraceGeneric([1.0, 0], [0.0, 1], 0, .55);
        Near(expected.A, actual.Heights[^1][0]); Near(expected.B, actual.Heights[^1][1]);
        Near(expected.C, actual.Slopes[^1][0]); Near(expected.D, actual.Slopes[^1][1]);
        Near(-1 / expected.C, optic.Paraxial.EstimateEffectiveFocalLength());
    }

    [Fact]
    public async Task ExistingFirstOrderOperandsEvaluateAfterApplicationOpenEditAndSave()
    {
        var optic = Rod(); var k = Math.Sqrt(.02); var p = k * 8;
        var values = new Dictionary<string, double>
        {
            ["EFFL"] = 1 / (1.5 * k * Math.Sin(p)),
            ["ENPP"] = Math.Tan(p) / (1.5 * k),
            ["EFLX"] = 1 / (1.5 * k * Math.Sin(p)),
            ["EFLY"] = 1 / (1.5 * k * Math.Sin(p)),
            ["EPDI"] = 2 / Math.Cos(p),
            ["EXPP"] = -5,
            ["EXPD"] = 2
        };
        foreach (var pair in values)
            optic.MeritFunctionOperands.Add(new()
            {
                Type = pair.Key,
                Target = pair.Value,
                Weight = 2,
                ZemaxIntegerParameters = pair.Key is "EFLX" or "EFLY" ? [1, 2] : []
            });
        var path = Path.Combine(Path.GetTempPath(), $"grin-paraxial-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(path);
            var rows = app.Optimization.GetMeritFunction(); Assert.Equal(7, rows.Count);
            foreach (var row in rows) { Assert.Empty(row.Error); Near(values[row.Type], row.Value); }
            app.Optimization.SetMeritFunction(rows.Select(row => row with { Weight = 3 }).ToArray());
            await app.Documents.SaveAsync(path);
            var restored = (await StarOptProjectStore.LoadAsync(path)).Configurations[0];
            foreach (var row in restored.MeritFunctionOperands)
            {
                Assert.Equal(3, row.Weight);
                var result = MeritFunctionCatalog.Evaluate(restored, row); Assert.Empty(result.Error);
                Near(values[row.Type], result.Value);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ProductionDlsOptimizesGradientRodThicknessAgainstFocalLength()
    {
        var optic = Rod(); optic.SurfaceGroup.Items[1].ThicknessVariable = true;
        var k = Math.Sqrt(.02); var target = 1 / (1.5 * k * Math.Sin(k * 6));
        optic.MeritFunctionOperands.Add(new() { Type = "EFFL", Target = target, Weight = 1 });
        var runtime = new WorkbenchRuntime(optic);
        var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 40);
        Assert.True(result.FinalMerit < result.InitialMerit * 1e-8);
        Assert.InRange(Math.Abs(runtime.CurrentOptic.SurfaceGroup.Items[1].Thickness - 6), 0, 1e-4);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutomaticEnvelopePreservesTheStopThatDefinesTheAperture(bool gradient)
    {
        var optic = Rod();
        if (!gradient)
        {
            var glass = new ConstantIndexMaterial("Glass", 1.5);
            optic.SurfaceGroup.Items[1].MaterialAfter = glass;
            optic.SurfaceGroup.Items[2].MaterialBefore = glass;
        }
        var diameter = optic.Paraxial.EstimateEntrancePupilDiameter();
        var stop = optic.SurfaceGroup.Items[2]; Assert.False(stop.SemiDiameterFixed);
        AutomaticSemiDiameterSolver.Update(optic);
        Assert.Equal(1, stop.SemiDiameter); Near(diameter, optic.Paraxial.EstimateEntrancePupilDiameter());
        Assert.NotEqual(7, optic.SurfaceGroup.Items[1].SemiDiameter);
        AutomaticSemiDiameterSolver.Update(optic);
        Assert.Equal(1, stop.SemiDiameter); Near(diameter, optic.Paraxial.EstimateEntrancePupilDiameter());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReverseTraceReturnsToTheActualFiniteObjectPlane(bool axial)
    {
        var optic = Rod(Material(Profile(3, axial)), finiteObject: true);
        var forward = optic.Paraxial.TraceGeneric([.2], [.01], 0, .55);
        var reverse = optic.Paraxial.TraceGenericReverse([forward.Heights[^1][0]], [-forward.Slopes[^1][0]], 0, .55);
        Near(.2, reverse.Heights[^1][0]); Near(-.01, reverse.Slopes[^1][0]);
        var positions = optic.SurfaceGroup.Items;
        var stopBack = optic.Paraxial.TraceGenericReverse([forward.Heights[2][0]], [-forward.Slopes[2][0]],
            positions[^1].CoordinateSystem.Origin.Z - positions[2].CoordinateSystem.Origin.Z, .55, 1);
        Near(.2, stopBack.Heights[^1][0]); Near(-.01, stopBack.Slopes[^1][0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FormalGeneratorAimsBothPupilAxesThroughTheGradientVolume(bool finiteObject)
    {
        var optic = Rod(finiteObject: finiteObject);
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(.15, .3, .25, -.35, .55, aimAtStop: true);
        using var trace = optic.SequentialRayTracer.Trace(bundle, TraceRequest.FullHistory(maxDegreeOfParallelism: 1));
        Assert.True(trace.TryGetSample(0, 2, out var stop));
        Assert.False(stop.Vignetted); Assert.True(stop.Intensity > 0);
        Assert.InRange(Math.Abs(stop.Position.X - .25), 0, 2e-7);
        Assert.InRange(Math.Abs(stop.Position.Y + .35), 0, 2e-7);
        Assert.True(trace.TryGetSample(0, 3, out var image)); Assert.True(image.Intensity > 0);
    }

    [Fact]
    public void SpotMetricsConsumeTheSameFormalGrinRaysAndAiming()
    {
        var optic = Rod(); optic.RayAimingEnabled = true;
        var pupils = ApertureSampler.GenerateHexapolarRings(2);
        var metric = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, .25, pupils, includeSurfaceTransmission: false);
        var diagram = SpotAnalysisEngine.Generate(optic, [(0, .25)], optic.Wavelengths, 2, "hexapolar",
            aimAtStop: true, includeSurfaceTransmission: false);
        Assert.NotNull(metric.Metrics); Assert.True(metric.Metrics.RayCount > 1);
        Near(SpotMetricEvaluator.Summarize(diagram, "GRIN").RmsSpotRadius, metric.Metrics.RmsSpotRadius);
    }

    [Theory]
    [InlineData("cusp")]
    [InlineData("bent-axis")]
    [InlineData("negative-interior")]
    [InlineData("negative-cubic")]
    [InlineData("zero-interior")]
    public void NonDifferentiableAxesAndInvalidInteriorIndicesCannotProduceMatrices(string problem)
    {
        var profile = problem switch
        {
            "cusp" => (ISpatialRefractiveIndex)new Gradient1IndexProfile(1.5, radialLinear: .01),
            "bent-axis" => new Gradient4IndexProfile(1.5, x1: .001),
            "negative-interior" => new Gradient3IndexProfile(1.5, axial1: -1, axial2: .1),
            "negative-cubic" => new Gradient3IndexProfile(1.5, axial1: -.6, axial2: .04, axial3: .002),
            _ => new Gradient4IndexProfile(2.5, z1: -1, z2: .1)
        };
        if (problem is "cusp" or "bent-axis")
            Assert.Throws<NotSupportedException>(() => GradientIndexParaxialTransport.Between(Material(profile), 0, 10, 550));
        else
            Assert.Throws<SpatialIndexDomainException>(() => GradientIndexParaxialTransport.Between(Material(profile), 0, 10, 550));
    }

    [Theory]
    [InlineData("tilt")]
    [InlineData("decenter")]
    [InlineData("reflection")]
    [InlineData("asphere")]
    [InlineData("negative-distance")]
    [InlineData("unsynchronized")]
    [InlineData("unbounded-object")]
    public void UnsupportedScalarSystemsRemainExplicitlyRejected(string problem)
    {
        var optic = Rod(); var s = optic.SurfaceGroup.Items[1];
        switch (problem)
        {
            case "tilt": s.CoordinateSystem = new(Vector3D.Zero, 1); break;
            case "decenter": s.CoordinateSystem = new(new(1, 0, 0)); break;
            case "reflection": s.IsReflective = true; break;
            case "asphere": s.Geometry = new EvenAsphereGeometry(30, 0, [.001]); break;
            case "negative-distance": s.Thickness = -8; break;
            case "unsynchronized": optic.SurfaceGroup.Items[2].CoordinateSystem = new(new(0, 0, 9)); break;
            default: optic.SurfaceGroup.Items[0].MaterialAfter = s.MaterialAfter; break;
        }
        Assert.Throws<NotSupportedException>(() => optic.Paraxial.EstimateEffectiveFocalLength());
    }

    [Fact]
    public void WorkBudgetsCancellationAndInvalidInputsAreNotConvertedToHomogeneousPropagation()
    {
        var material = Material(Profile(1));
        Assert.Throws<InvalidOperationException>(() => GradientIndexParaxialTransport.Between(material, 0, 101, 550));
        var limited = new GradientIndexMaterial("limited", Profile(1), 100, new(MaximumAttempts: 1));
        Assert.Throws<InvalidOperationException>(() => GradientIndexParaxialTransport.Between(limited, 0, 10, 550));
        Assert.Throws<ArgumentOutOfRangeException>(() => GradientIndexParaxialTransport.Between(material, double.NaN, 10, 550));
        Assert.Throws<ArgumentOutOfRangeException>(() => GradientIndexParaxialTransport.Between(material, 0, 10, 0));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => GradientIndexParaxialTransport.Between(material, 0, 10, 550,
            cancellationToken: cancellation.Token));
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.Throws<OperationCanceledException>(() => Rod().Paraxial.EstimateEffectiveFocalLength());
    }

    private static Optic Rod(GradientIndexMaterial? material = null, bool finiteObject = false)
    {
        var optic = new Optic("GRIN paraxial test");
        optic.Fields.Add(new FieldPoint { Y = 1 });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.Aperture.Kind = ApertureKind.FloatByStopSize;
        material ??= Material(Profile(1));
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = finiteObject ? 20 : double.PositiveInfinity },
            new OpticalSurface { MaterialAfter = material, Thickness = 8, SemiDiameter = 7 },
            new OpticalSurface { MaterialBefore = material, Thickness = 5, SemiDiameter = 1, IsStop = true },
            new OpticalSurface { Thickness = 0 }
        ]);
        return optic;
    }
    private static GradientIndexMaterial Material(ISpatialRefractiveIndex profile) => new("GRIN", profile, 100);
    private static ISpatialRefractiveIndex Profile(int kind, bool axial = false) => kind switch
    {
        1 => new Gradient1IndexProfile(1.5, -.015),
        2 => new Gradient2IndexProfile(2.25, -.045, .001, .0002),
        3 => new Gradient3IndexProfile(1.5, -.015, .001, .0002, axial ? .01 : 0, axial ? .001 : 0),
        _ => new Gradient4IndexProfile(1.5, x2: -.015, y2: -.015, z1: axial ? .01 : 0, z2: axial ? .001 : 0)
    };
    private static GradientIndexParaxialMatrix Compose(GradientIndexParaxialMatrix a, GradientIndexParaxialMatrix b) =>
        new(a.A * b.A + a.B * b.C, a.A * b.B + a.B * b.D, a.C * b.A + a.D * b.C, a.C * b.B + a.D * b.D);
    private static double Error(GradientIndexParaxialMatrix a, GradientIndexParaxialMatrix b) =>
        new[] { Math.Abs(a.A - b.A), Math.Abs(a.B - b.B), Math.Abs(a.C - b.C), Math.Abs(a.D - b.D) }.Max();
    private static void Near(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0, 2e-8);
    private static void Near(GradientIndexParaxialMatrix expected, GradientIndexParaxialMatrix actual) => Assert.InRange(Error(expected, actual), 0, 2e-8);
}
