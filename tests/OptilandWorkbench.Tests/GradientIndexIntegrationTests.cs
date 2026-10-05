using System.Text.Json;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Propagation;
using Xunit.Abstractions;

namespace OptilandWorkbench.Tests;

public sealed class GradientIndexIntegrationTests(ITestOutputHelper output)
{
    private static readonly Vector3D Forward = new(0, 0, 1);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void PublishedProfilesReturnIndexAndAnalyticGradientInLocalLensUnits(int kind)
    {
        var profile = Profile(kind);
        var sample = profile.Evaluate(new Vector3D(0.6, 0.8, 2), 550);
        var expected = kind switch
        {
            1 => new SpatialIndexSample(1.55, new Vector3D(0.042, 0.056, 0)),
            2 => new SpatialIndexSample(Math.Sqrt(2.2623456),
                new Vector3D(0.6 * 0.0150886, 0.8 * 0.0150886, 0) / Math.Sqrt(2.2623456)),
            3 => new SpatialIndexSample(1.6162, new Vector3D(0.03264, 0.04352, 0.0532)),
            _ => new SpatialIndexSample(1.9028, new Vector3D(0.034, 0.094, 0.29))
        };
        Near(expected.Index, sample.Index, 2e-15);
        Assert.Equal(sample.Index, profile.RefractiveIndex(new(0.6, 0.8, 2), 550));
        Near(expected.Gradient, sample.Gradient, 2e-15);
        Assert.Equal(profile.Evaluate(new(0.6, 0.8, 2), 450), profile.Evaluate(new(0.6, 0.8, 2), 650));
        var point = new Vector3D(0.3, -0.7, -0.2);
        var actual = profile.Evaluate(point, 550);
        const double epsilon = 1e-5;
        foreach (var axis in new[] { new Vector3D(1, 0, 0), new Vector3D(0, 1, 0), Forward })
        {
            var difference = (profile.Evaluate(point + axis * epsilon, 550).Index
                - profile.Evaluate(point - axis * epsilon, 550).Index) / (2 * epsilon);
            Near(Dot(actual.Gradient, axis), difference, 3e-10);
        }
    }

    [Fact]
    public void Gradient1CuspIsRejectedRatherThanInventingAnAxisGradient()
    {
        Assert.Equal(1.5, new Gradient1IndexProfile(1.5, radialLinear: 0.1).RefractiveIndex(Vector3D.Zero, 550));
        Assert.Throws<NotSupportedException>(() => new Gradient1IndexProfile(1.5, radialLinear: 0.1).Evaluate(Vector3D.Zero, 550));
        Assert.Equal(new SpatialIndexSample(1.5, Vector3D.Zero), new Gradient1IndexProfile(1.5, 0.1).Evaluate(Vector3D.Zero, 550));
        Assert.Equal(new SpatialIndexSample(1.5, Vector3D.Zero), new Gradient2IndexProfile(2.25, -0.01).Evaluate(Vector3D.Zero, 550));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ProfilesRejectInvalidConstructionAndSampling(int kind)
    {
        foreach (var invalid in new[] { 0.0, -1, double.NaN, double.PositiveInfinity })
            Assert.Throws<ArgumentOutOfRangeException>(() => Profile(kind, invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => Profile(kind, badCoefficient: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => Profile(kind).Evaluate(new(double.NaN, 0, 0), 550));
        Assert.Throws<ArgumentOutOfRangeException>(() => Profile(kind).Evaluate(new(1, 0, 0), 0));
    }

    [Fact]
    public void NonpositiveIndexAndSquaredIndexAreRejected()
    {
        Assert.Throws<SpatialIndexDomainException>(() => new Gradient1IndexProfile(1, -1).Evaluate(new(2, 0, 0), 550));
        Assert.Throws<SpatialIndexDomainException>(() => new Gradient2IndexProfile(1, -1).Evaluate(new(2, 0, 0), 550));
        Assert.Throws<SpatialIndexDomainException>(() => new Gradient3IndexProfile(1, axial1: -1).Evaluate(new(0, 0, 2), 550));
        Assert.Throws<SpatialIndexDomainException>(() => new Gradient4IndexProfile(1, x1: -1).Evaluate(new(2, 0, 0), 550));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void HomogeneousLimitPreservesDirectionAndIntegratesOpticalPath(int sign)
    {
        var coordinates = new CoordinateSystem(new(40, -2, 7), 15, -22, 48);
        var origin = coordinates.ToGlobalPoint(new(2, -3, 1));
        var direction = coordinates.ToGlobalDirection(new Vector3D(0.2, -0.3, sign));
        var expectedDirection = direction / direction.Length;
        var result = GradientIndexRayIntegrator.TraceDistance(new Gradient3IndexProfile(1.7), coordinates,
            origin, direction * 3, 550, 12, new(MaximumStep: 1.3, RetainPath: true));
        Assert.Equal(GradientIndexTermination.DistanceReached, result.Termination);
        Near(origin + expectedDirection * 12, result.End.Position, 2e-12);
        Near(expectedDirection, result.End.Direction, 2e-15);
        Near(20.4, result.End.OpticalPathLength, 2e-13);
        Near(12, result.End.PathLength, 1e-14);
        Assert.Null(result.SurfaceResidual);
        Assert.Equal(result.End, result.Path[^1]);
        Assert.Equal(0, result.Path[0].PathLength);
    }

    [Theory]
    [InlineData(0.02, 0.3)]
    [InlineData(0.02, -0.3)]
    [InlineData(-0.02, 0.3)]
    [InlineData(-0.02, -0.3)]
    public void AxialLinearGradientMatchesExactNonparaxialPositionDirectionAndOpl(double gradient, double angle)
    {
        const double n0 = 1.5, z = 10;
        var profile = new Gradient3IndexProfile(n0, axial1: gradient);
        var result = ToPlane(profile, new(Math.Sin(angle), 0, Math.Cos(angle)), z);
        var k = n0 * Math.Abs(Math.Sin(angle));
        var n1 = n0 + gradient * z;
        var q0 = Math.Sqrt(n0 * n0 - k * k);
        var q1 = Math.Sqrt(n1 * n1 - k * k);
        var logarithm = Math.Log((n1 + q1) / (n0 + q0));
        var expected = new GradientIndexPathPoint(new(Math.Sign(angle) * k * logarithm / gradient, 0, z),
            new(Math.Sign(angle) * k / n1, 0, q1 / n1), (q1 - q0) / gradient,
            (n1 * q1 - n0 * q0 + k * k * logarithm) / (2 * gradient), n1);
        VerifyExact($"axial-{gradient}-{angle}", expected, result, 2e-9);
    }

    [Theory]
    [InlineData(0.08, false)]
    [InlineData(-0.08, false)]
    [InlineData(0.08, true)]
    [InlineData(-0.08, true)]
    public void TransverseLinearGradientMatchesExactCatenaryWithRotatedAndTranslatedVolume(double gradient, bool transformed)
    {
        var coordinates = transformed ? new CoordinateSystem(new(12, -4, 8), 23, -31, 17) : CoordinateSystem.Global;
        var expected = Catenary(1.5, gradient, 10, coordinates);
        var profile = new Gradient4IndexProfile(1.5, x1: gradient);
        var result = GradientIndexRayIntegrator.TraceToSurface(profile, coordinates, coordinates.Origin,
            coordinates.ToGlobalDirection(Forward), 550, new PlaneGeometry(),
            coordinates with { Origin = coordinates.ToGlobalPoint(new(0, 0, 10)) }, 30, new(MaximumStep: 1, RetainPath: true));
        VerifyExact($"catenary-{gradient}-{transformed}", expected, result, 3e-9);
        var byLength = GradientIndexRayIntegrator.TraceDistance(profile, coordinates, coordinates.Origin,
            coordinates.ToGlobalDirection(Forward), 550, expected.PathLength, new(MaximumStep: 1));
        Near(expected.Position, byLength.End.Position, 3e-9);
        Near(expected.OpticalPathLength, byLength.End.OpticalPathLength, 3e-9);
        Assert.Empty(byLength.Path);
        Assert.All(result.Path, point => Near(1, point.Direction.Length, 2e-14));
    }

    [Fact]
    public void StrongGradientAdaptsAndConvergesTowardExactSolution()
    {
        var profile = new Gradient4IndexProfile(1.5, x1: 0.8);
        var expected = Catenary(1.5, 0.8, 5, CoordinateSystem.Global);
        var loose = ToPlane(profile, Forward, 5, new(MaximumStep: 8, PositionTolerance: 1e-4,
            DirectionTolerance: 1e-4, OpticalPathTolerance: 1e-4, RelativeTolerance: 1e-4));
        var tight = ToPlane(profile, Forward, 5, new(MaximumStep: 8));
        var fine = ToPlane(profile, Forward, 5, new(MaximumStep: 0.02));
        var looseError = Math.Abs(expected.OpticalPathLength - loose.End.OpticalPathLength);
        var tightError = Math.Abs(expected.OpticalPathLength - tight.End.OpticalPathLength);
        Assert.True(tightError < looseError / 100);
        VerifyExact("strong-catenary-adaptive", expected, tight, 5e-8);
        VerifyExact("strong-catenary-fine", expected, fine, 5e-8);
    }

    [Theory]
    [InlineData(1e12)]
    [InlineData(-1e12)]
    public void LargeAbsoluteCoordinatesDoNotQuantizeIntersectionDistanceOrOpticalPath(double offset)
    {
        var coordinates = new CoordinateSystem(new(offset, -offset, offset));
        var expected = Catenary(1.5, 0.08, 10, CoordinateSystem.Global);
        var result = GradientIndexRayIntegrator.TraceToSurface(new Gradient4IndexProfile(1.5, x1: 0.08), coordinates,
            coordinates.Origin, Forward, 550, new PlaneGeometry(), coordinates with { Origin = coordinates.Origin + new Vector3D(0, 0, 10) },
            30, new(MaximumStep: 1));
        Assert.Equal(GradientIndexTermination.SurfaceReached, result.Termination);
        Near(expected.PathLength, result.End.PathLength, 3e-9);
        Near(expected.OpticalPathLength, result.End.OpticalPathLength, 3e-9);
        Near(expected.Direction, result.End.Direction, 3e-9);
        // Global doubles have ~1e-4 spacing here; the integrated local path and
        // OPL must not inherit that unavoidable output-position quantization.
        Near(coordinates.Origin + expected.Position, result.End.Position, 3e-4);
    }

    [Fact]
    public void RadialProfileConservesAxialMomentumAndAngularMomentumAndIsReversible()
    {
        var profile = new Gradient2IndexProfile(2.25, -0.04, -0.00001);
        var start = new Vector3D(1, 0.2, 0);
        var direction = new Vector3D(0.08, 0.12, 1);
        direction /= direction.Length;
        var result = GradientIndexRayIntegrator.TraceDistance(profile, CoordinateSystem.Global, start, direction,
            550, 25, new(MaximumStep: 0.7, RetainPath: true));
        var n = profile.Evaluate(start, 550).Index;
        var pz = n * direction.Z;
        var lz = n * (start.X * direction.Y - start.Y * direction.X);
        foreach (var point in result.Path)
        {
            Near(pz, point.RefractiveIndex * point.Direction.Z, 2e-9);
            Near(lz, point.RefractiveIndex * (point.Position.X * point.Direction.Y - point.Position.Y * point.Direction.X), 2e-9);
        }
        var reverse = GradientIndexRayIntegrator.TraceDistance(profile, CoordinateSystem.Global, result.End.Position,
            -result.End.Direction, 550, result.End.PathLength, new(MaximumStep: 0.7));
        Near(start, reverse.End.Position, 3e-8);
        Near(-direction, reverse.End.Direction, 3e-9);
        Near(result.End.OpticalPathLength, reverse.End.OpticalPathLength, 3e-8);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(-20)]
    public void HomogeneousCurvedExitAgreesWithFormalSphereIntersection(double radius)
    {
        var geometry = new StandardGeometry(radius);
        var coordinates = new CoordinateSystem(new(0, 0, 5));
        var origin = new Vector3D(1, 0.5, 0);
        var direction = new Vector3D(0.03, 0.01, 1);
        direction /= direction.Length;
        var expected = geometry.DistanceToIntersection(coordinates.ToLocalPoint(origin), direction);
        Assert.True(expected.IsHit);
        var result = GradientIndexRayIntegrator.TraceToSurface(new Gradient3IndexProfile(1.5), CoordinateSystem.Global,
            origin, direction, 550, geometry, coordinates, 10, new(MaximumStep: 0.9));
        Assert.Equal(GradientIndexTermination.SurfaceReached, result.Termination);
        Near(coordinates.ToGlobalPoint(expected.Point), result.End.Position, 2e-9);
        Near(expected.Distance * 1.5, result.End.OpticalPathLength, 3e-9);
    }

    [Fact]
    public void CurvedRayHitsTiltedExitAtAnalyticCatenaryIntersection()
    {
        const double g = 0.08, angle = 15 * Math.PI / 180;
        var lower = 0.0;
        var upper = 10.0;
        for (var i = 0; i < 70; i++)
        {
            var middle = (lower + upper) / 2;
            var point = Catenary(1.5, g, middle, CoordinateSystem.Global).Position;
            if (Math.Sin(angle) * point.X + Math.Cos(angle) * (point.Z - 5) > 0) upper = middle;
            else lower = middle;
        }
        var expected = Catenary(1.5, g, (lower + upper) / 2, CoordinateSystem.Global);
        var result = GradientIndexRayIntegrator.TraceToSurface(new Gradient4IndexProfile(1.5, x1: g), CoordinateSystem.Global,
            Vector3D.Zero, Forward, 550, new PlaneGeometry(), new(new(0, 0, 5), RotationYDegrees: 15), 10);
        VerifyExact("tilted-exit", expected, result, 3e-9);
    }

    [Fact]
    public void AxialTurningRayDoesNotBecomeAStraightRaySurfaceHit()
    {
        var result = GradientIndexRayIntegrator.TraceToSurface(new Gradient3IndexProfile(1.5, axial1: -0.2),
            CoordinateSystem.Global, Vector3D.Zero, new(0.8, 0, 0.6), 550, new PlaneGeometry(), new(new(0, 0, 4)),
            8, new(RetainPath: true));
        Assert.Equal(GradientIndexTermination.PathLimitReached, result.Termination);
        Assert.True(result.End.Direction.Z < 0);
        Assert.All(result.Path, point => Assert.InRange(point.Position.Z, -1, 1.500000001));
        Assert.Equal(8, result.End.PathLength);
    }

    [Fact]
    public void ZeroLengthInitialSurfaceAndTangencyAreDistinct()
    {
        var profile = new Gradient3IndexProfile(1.5);
        var zero = GradientIndexRayIntegrator.TraceDistance(profile, CoordinateSystem.Global, Vector3D.Zero, Forward, 550, 0);
        Assert.Equal(GradientIndexTermination.DistanceReached, zero.Termination);
        Assert.Equal(0, zero.End.OpticalPathLength);
        Assert.Equal(0, zero.IntegrationAttempts);
        var onSurface = GradientIndexRayIntegrator.TraceToSurface(profile, CoordinateSystem.Global, Vector3D.Zero, Forward,
            550, new PlaneGeometry(), CoordinateSystem.Global, 10);
        Assert.Equal(GradientIndexTermination.SurfaceReached, onSurface.Termination);
        var tangent = GradientIndexRayIntegrator.TraceToSurface(profile, CoordinateSystem.Global, Vector3D.Zero, new(1, 0, 0),
            550, new PlaneGeometry(), CoordinateSystem.Global, 10);
        Assert.Equal(GradientIndexTermination.SurfaceTangent, tangent.Termination);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void ExitExactlyAtThePathBudgetIsNotReportedAsMissedDueToRounding(int sign)
    {
        var result = GradientIndexRayIntegrator.TraceToSurface(new Gradient3IndexProfile(1.5), CoordinateSystem.Global,
            Vector3D.Zero, new(0.6, 0, sign * 0.8), 550, new PlaneGeometry(), new(new(0, 0, sign * 8)),
            10, new(MaximumStep: 0.3));
        Assert.Equal(GradientIndexTermination.SurfaceReached, result.Termination);
        Near(new Vector3D(6, 0, sign * 8), result.End.Position, 1e-10);
        Near(15, result.End.OpticalPathLength, 1e-10);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InvalidCustomIndexFieldsCannotYieldSuccessfulTraces(int kind)
    {
        var sample = kind switch
        {
            0 => new SpatialIndexSample(0, Vector3D.Zero),
            1 => new SpatialIndexSample(double.NaN, Vector3D.Zero),
            _ => new SpatialIndexSample(1.5, new(double.PositiveInfinity, 0, 0))
        };
        Assert.Throws<SpatialIndexDomainException>(() => GradientIndexRayIntegrator.TraceDistance(new CallbackProfile(() => sample),
            CoordinateSystem.Global, Vector3D.Zero, Forward, 550, 1));
    }

    [Fact]
    public void CancellationAndWorkLimitsFailExplicitly()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => ToPlane(new Gradient3IndexProfile(1.5), Forward, 1, token: cancelled.Token));
        using var during = new CancellationTokenSource();
        var calls = 0;
        var profile = new CallbackProfile(() =>
        {
            if (++calls == 5) during.Cancel();
            return new SpatialIndexSample(1.5, Vector3D.Zero);
        });
        Assert.Throws<OperationCanceledException>(() => ToPlane(profile, Forward, 10, token: during.Token));
        Assert.Throws<InvalidOperationException>(() => ToPlane(new Gradient3IndexProfile(1.5), Forward, 10,
            new(MaximumStep: 0.01, MaximumAttempts: 3)));
        Assert.Throws<InvalidOperationException>(() => ToPlane(new Gradient4IndexProfile(1.5, x1: 0.8), Forward, 5,
            new(MaximumStep: 20, MinimumStep: 20)));
    }

    [Fact]
    public void InvalidInputsOptionsAndExitDomainAreRejected()
    {
        var profile = new Gradient3IndexProfile(1.5);
        foreach (var direction in new[] { Vector3D.Zero, new Vector3D(double.NaN, 0, 1), new Vector3D(double.MaxValue, 0, 1) })
            Assert.Throws<ArgumentOutOfRangeException>(() => ToPlane(profile, direction, 1));
        foreach (var options in new[] { new GradientIndexIntegrationOptions(MaximumStep: 0), new(MinimumStep: 1),
            new(PositionTolerance: double.NaN), new(RelativeTolerance: -1), new(MaximumAttempts: 1_000_001) })
            Assert.Throws<ArgumentOutOfRangeException>(() => ToPlane(profile, Forward, 1, options));
        Assert.Throws<ArgumentOutOfRangeException>(() => GradientIndexRayIntegrator.TraceDistance(profile,
            new(Vector3D.Zero, double.NaN), Vector3D.Zero, Forward, 550, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GradientIndexRayIntegrator.TraceDistance(profile,
            CoordinateSystem.Global, Vector3D.Zero, Forward, 550, -1));
        Assert.Throws<InvalidOperationException>(() => GradientIndexRayIntegrator.TraceToSurface(profile,
            CoordinateSystem.Global, new(40, 0, 0), Forward, 550, new StandardGeometry(20), new(new(0, 0, 5)), 10));
    }

    private void VerifyExact(string name, GradientIndexPathPoint expected, GradientIndexTraceResult actual, double tolerance)
    {
        var evidence = new
        {
            name,
            expected,
            actual,
            positionError = (expected.Position - actual.End.Position).Length,
            directionError = (expected.Direction - actual.End.Direction).Length,
            pathError = Math.Abs(expected.PathLength - actual.End.PathLength),
            opticalPathError = Math.Abs(expected.OpticalPathLength - actual.End.OpticalPathLength)
        };
        output.WriteLine(JsonSerializer.Serialize(evidence));
        Assert.Equal(GradientIndexTermination.SurfaceReached, actual.Termination);
        Assert.InRange(evidence.positionError, 0, tolerance);
        Assert.InRange(evidence.directionError, 0, tolerance);
        Assert.InRange(evidence.pathError, 0, tolerance);
        Assert.InRange(evidence.opticalPathError, 0, tolerance);
        Near(expected.RefractiveIndex, actual.End.RefractiveIndex, tolerance);
    }

    private static GradientIndexTraceResult ToPlane(ISpatialRefractiveIndex profile, Vector3D direction, double z,
        GradientIndexIntegrationOptions? options = null, CancellationToken token = default) =>
        GradientIndexRayIntegrator.TraceToSurface(profile, CoordinateSystem.Global, Vector3D.Zero, direction, 550,
            new PlaneGeometry(), new(new(0, 0, z)), 40, options, token);

    // Independent exact solution for n(x) = n0 + g*x, initial ray along +Z.
    private static GradientIndexPathPoint Catenary(double n0, double g, double z, CoordinateSystem coordinates)
    {
        var a = g * z / n0;
        var sinh = Math.Sinh(a);
        var cosh = Math.Cosh(a);
        return new(coordinates.ToGlobalPoint(new(n0 / g * (cosh - 1), 0, z)),
            coordinates.ToGlobalDirection(new(Math.Tanh(a), 0, 1 / cosh)),
            n0 / g * sinh, n0 * n0 / (2 * g) * (a + sinh * cosh), n0 * cosh);
    }

    private static ISpatialRefractiveIndex Profile(int kind, double baseIndex = 1.5, bool badCoefficient = false) => kind switch
    {
        1 => new Gradient1IndexProfile(baseIndex, badCoefficient ? double.NaN : 0.02, 0.03),
        2 => new Gradient2IndexProfile(baseIndex > 0 ? baseIndex * baseIndex : baseIndex,
            badCoefficient ? double.NaN : 0.01, 0.002, 0.0003, 0.00004, 0.000005, 0.0000006),
        3 => new Gradient3IndexProfile(baseIndex, badCoefficient ? double.NaN : 0.02, 0.003, 0.0004, 0.04, 0.003, 0.0001),
        _ => new Gradient4IndexProfile(baseIndex, badCoefficient ? double.NaN : 0.01, 0.02, 0.03, 0.04, 0.05, 0.06)
    };

    private static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static void Near(double expected, double actual, double tolerance) => Assert.InRange(Math.Abs(expected - actual), 0, tolerance);
    private static void Near(Vector3D expected, Vector3D actual, double tolerance) => Assert.InRange((expected - actual).Length, 0, tolerance);
    private sealed class CallbackProfile(Func<SpatialIndexSample> evaluate) : ISpatialRefractiveIndex
    {
        public double RefractiveIndex(Vector3D localPosition, double wavelengthNanometers) => evaluate().Index;
        public SpatialIndexSample Evaluate(Vector3D localPosition, double wavelengthNanometers) => evaluate();
    }
}
