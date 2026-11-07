using OpticalSystemDesign.Validation;
using System.Numerics;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Tests;

public sealed class StandardIntersectionPrecisionTests
{
    [Fact]
    public void GrazingParabolicIntersectionPreservesTheFocusToPlaneReflection()
    {
        var geometry = new StandardGeometry(-200, -1);
        var focus = new Vector3D(0, 0, -100);
        var direction = new Vector3D(-.17453247854137008, -.9846510982134834, .0007929194077886404);
        var hit = geometry.DistanceToIntersection(focus, direction);
        Assert.True(hit.IsHit);
        Assert.InRange(Math.Abs(hit.Point.Z - geometry.Sag(hit.Point.X, hit.Point.Y)), 0, 1e-12);
        var dot = direction.X * hit.Normal.X + direction.Y * hit.Normal.Y + direction.Z * hit.Normal.Z;
        var reflected = direction - hit.Normal * (2 * dot);
        Assert.InRange((reflected - new Vector3D(0, 0, -1)).Length, 0, 1e-13);

        var count = 2 * Vector<double>.Count + 1;
        double[] Repeat(double value) => Enumerable.Repeat(value, count).ToArray();
        IBatchedNumericBackend backend = new ManagedCpuBackend();
        var distances = new double[count];
        var hits = new bool[count];
        backend.IntersectStandard(Repeat(focus.X), Repeat(focus.Y), Repeat(focus.Z),
            Repeat(direction.X), Repeat(direction.Y), Repeat(direction.Z), -200, -1, distances, hits);
        Assert.All(hits, value => Assert.True(value));
        Assert.All(distances, value => Assert.InRange(Math.Abs(value - hit.Distance), 0, 1e-12));
    }

    [Fact]
    public void TwoForwardConicHitsUseTheSequentialVertexBranchInEveryBatchLane()
    {
        const double radius = 1 / .021219123523;
        const double conic = -.568372493;
        var geometry = new StandardGeometry(radius, conic);
        var origin = new Vector3D(0, -150, 178.59 - 36.955461312647415);
        var direction = new Vector3D(0, .7927005470080939, -.6096112226436352);
        direction /= direction.Length;
        var scalar = geometry.DistanceToIntersection(origin, direction);
        Assert.True(scalar.IsHit);
        Assert.InRange(scalar.Point.Y, 25.15598680903, 25.15598680906);

        var count = 2 * Vector<double>.Count + 1;
        double[] Repeat(double value) => Enumerable.Repeat(value, count).ToArray();
        IBatchedNumericBackend[] backends = [new ManagedCpuBackend(), new ScalarBatchedNumericBackendAdapter(new ManagedCpuBackend())];
        foreach (var backend in backends)
        {
            var hits = new bool[count];
            var distances = new double[count];
            backend.IntersectStandard(Repeat(origin.X), Repeat(origin.Y), Repeat(origin.Z),
                Repeat(direction.X), Repeat(direction.Y), Repeat(direction.Z), radius, conic, distances, hits);
            Assert.All(hits, hit => Assert.True(hit));
            Assert.All(distances, distance => Assert.Equal(scalar.Distance, distance, 10));
        }
    }

    [Fact]
    public void ZeroAndRepeatedZeroRootsRemainValidAcrossScalarAndVectorPaths()
    {
        var geometry = new StandardGeometry(50);
        var count = 2 * Vector<double>.Count + 1;
        IBatchedNumericBackend[] backends = [new ManagedCpuBackend(), new ScalarBatchedNumericBackendAdapter(new ManagedCpuBackend())];
        foreach (var tangent in new[] { false, true })
        {
            var direction = tangent ? new Vector3D(1, 0, 0) : new Vector3D(0, 0, 1);
            var scalar = geometry.DistanceToIntersection(new Vector3D(0, 0, 0), direction);
            Assert.True(scalar.IsHit);
            Assert.Equal(0, scalar.Distance);
            var zero = new double[count];
            var dx = Enumerable.Repeat(direction.X, count).ToArray();
            var dz = Enumerable.Repeat(direction.Z, count).ToArray();
            foreach (var backend in backends)
            {
                var distances = new double[count];
                var hits = new bool[count];
                backend.IntersectStandard(zero, zero, zero, dx, zero, dz, 50, 0, distances, hits);
                Assert.All(hits, hit => Assert.True(hit));
                Assert.All(distances, distance => Assert.Equal(0, distance));
            }
        }
    }

    [Theory]
    [InlineData(50)]
    [InlineData(-50)]
    public void FiniteFarObjectRetainsSphericalHitsInEveryBatchLane(double radius)
    {
        var count = 2 * Vector<double>.Count + 1;
        var geometry = new StandardGeometry(radius);
        const double expectedDistance = 100_000;
        var targets = Enumerable.Range(0, count).Select(i =>
        {
            var x = 3d + i;
            return new Vector3D(x, -2, geometry.Sag(x, -2));
        }).ToArray();
        var direction = new Vector3D(.001, -.002, 1);
        direction /= direction.Length;
        var origins = targets.Select(p => p - direction * expectedDistance).ToArray();
        IBatchedNumericBackend[] backends = [new ManagedCpuBackend(), new ScalarBatchedNumericBackendAdapter(new ManagedCpuBackend())];
        foreach (var backend in backends)
        {
            var distances = new double[count];
            var hits = new bool[count];
            backend.IntersectStandard(origins.Select(p => p.X).ToArray(), origins.Select(p => p.Y).ToArray(),
                origins.Select(p => p.Z).ToArray(), Enumerable.Repeat(direction.X, count).ToArray(),
                Enumerable.Repeat(direction.Y, count).ToArray(), Enumerable.Repeat(direction.Z, count).ToArray(),
                radius, 0, distances, hits);
            Assert.All(hits, hit => Assert.True(hit));
            Assert.All(distances, value => Assert.InRange(Math.Abs(value - expectedDistance), 0, 1e-9));
        }
    }

    [Theory]
    [InlineData(1e-8)]
    [InlineData(-1e-8)]
    public void TinyConicVertexDoesNotLoseAxialOrNearAxialBatchRays(double radius)
    {
        const double conic = -33.163437;
        const double launchDistance = 20;
        var geometry = new StandardGeometry(radius, conic);
        var count = 2 * Vector<double>.Count + 1;
        var x = Enumerable.Range(0, count).Select(i => i * 1e-9).ToArray();
        var zero = new double[count];
        var z = Enumerable.Repeat(-launchDistance, count).ToArray();
        var dz = Enumerable.Repeat(1d, count).ToArray();
        IBatchedNumericBackend[] backends = [new ManagedCpuBackend(), new ScalarBatchedNumericBackendAdapter(new ManagedCpuBackend())];
        foreach (var backend in backends)
        {
            var distances = new double[count];
            var hits = new bool[count];
            backend.IntersectStandard(x, zero, z, zero, zero, dz, radius, conic, distances, hits);
            for (var i = 0; i < count; i++)
            {
                Assert.True(hits[i]);
                Assert.Equal(launchDistance + geometry.Sag(x[i], 0), distances[i], 13);
            }
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1e-12, -1.7010451109524027)]
    [InlineData(1e-10, -41.04928370264171)]
    public void AxiconNearAxisTraceRetainsThePreciseVertexHit(double pupilX, double nativeImageX)
    {
        var root = ProductDependencyArchitectureTests.RepositoryRoot();
        var source = Path.Combine(root, "src/OpticalSystemDesign.App/Assets/SequentialExamples/Miscellaneous/Axicon model.zmx");
        var optic = OpticalFormatCatalog.Import(EvidenceFiles.ReadAllText(source), ".zmx");
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, pupilX, 0,
            optic.Wavelengths[0].Micrometers, aimAtStop: false);
        var image = optic.SequentialRayTracer.TraceFinalSamples(bundle).Single();
        Assert.NotNull(image);
        Assert.False(image.Vignetted);
        Assert.InRange(Math.Abs(image.Position.X - nativeImageX), 0, 1e-7);
    }

    [Fact]
    public void PetzvalGaussianScanRetainsEveryRayConfirmedByTheNativePupilAudit()
    {
        var root = ProductDependencyArchitectureTests.RepositoryRoot();
        var source = Path.Combine(root, "src/OpticalSystemDesign.App/Assets/SequentialExamples/Objectives/Petzval.zmx");
        var optic = OpticalFormatCatalog.Import(EvidenceFiles.ReadAllText(source), ".zmx");
        var pupil = ApertureSampler.GenerateGaussianQuadrature(6, 12);
        for (var field = 0; field <= 15; field++)
        {
            var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, field / 15d,
                optic.Wavelengths[1].Micrometers, pupil, aimAtStop: false);
            var histories = optic.SequentialRayTracer.TraceGaussianPupil(bundle);
            Assert.Equal(72, histories.Count);
            Assert.All(histories, history =>
            {
                Assert.Equal(9, history[^1].SurfaceNumber);
                Assert.False(history[^1].Vignetted);
                Assert.True(history[^1].Intensity > 0);
            });
        }
    }

    [Theory]
    [InlineData(1e6, 0)]
    [InlineData(-1e6, 0)]
    [InlineData(1e12, 0)]
    [InlineData(-1e12, 0)]
    [InlineData(1e12, -1)]
    [InlineData(-1e12, 0.75)]
    public void NearlyPlanarConicRecoversRejectedRootsAndPreservesAcceptedOnes(double radius, double conic)
    {
        var geometry = new StandardGeometry(radius, conic);
        var target = new Vector3D(3, -2, geometry.Sag(3, -2));
        var direction = new Vector3D(0.1, 0.05, 1);
        const double expectedDistance = 2.48;
        var origin = target - direction * expectedDistance;

        var actual = geometry.DistanceToIntersection(origin, direction);
        var (first, second) = OrdinaryRoots(origin, direction, radius, conic);
        var ordinaryFirst = Verify(first);
        var ordinarySecond = Verify(second);
        var ordinary = !ordinaryFirst.IsHit ? ordinarySecond
            : !ordinarySecond.IsHit || ordinaryFirst.Distance <= ordinarySecond.Distance ? ordinaryFirst : ordinarySecond;

        Assert.True(actual.IsHit, $"{actual.Status}: residual={actual.Residual:R}");
        if (ordinary.IsHit)
        {
            // Optimization and discrete pupil boundaries depend on preserving
            // already valid intersections, including their last bits.
            Assert.Equal(ordinary, actual);
        }
        else
        {
            Assert.InRange(Math.Abs(actual.Distance - expectedDistance), 0, 2e-12);
            Assert.InRange((actual.Point - target).Length, 0, 2e-12);
            Assert.InRange(Math.Abs(actual.Residual), 0, 2e-12);
        }

        IntersectionResult Verify(double distance) => !double.IsFinite(distance) || distance < -StandardGeometry.ConicDomainTolerance
            ? IntersectionResult.Failure(IntersectionStatus.NoRoot)
            : GeometryIntersectionSolver.CreateVerifiedResult(origin, direction, Math.Max(0, distance), geometry.Sag, geometry.SurfaceNormal, 0);
    }

    [Theory]
    [InlineData(1e6, 0)]
    [InlineData(-1e6, 0)]
    [InlineData(1e12, 0)]
    [InlineData(-1e12, 0)]
    [InlineData(1e12, -1)]
    [InlineData(-1e12, 0.75)]
    public void ScalarAndVectorBatchLanesRecoverRejectedRootsWithoutChangingAcceptedOnes(double radius, double conic)
    {
        var geometry = new StandardGeometry(radius, conic);
        var count = 2 * Vector<double>.Count + 1;
        var x = new double[count];
        var y = new double[count];
        var z = new double[count];
        var dx = Enumerable.Repeat(0.1, count).ToArray();
        var dy = Enumerable.Repeat(0.05, count).ToArray();
        var dz = Enumerable.Repeat(1d, count).ToArray();
        for (var i = 0; i < count; i++)
        {
            var targetX = 3 + i / 10d;
            var targetY = -2 + i / 20d;
            x[i] = targetX - dx[i] * 2.48;
            y[i] = targetY - dy[i] * 2.48;
            z[i] = geometry.Sag(targetX, targetY) - dz[i] * 2.48;
        }

        IBatchedNumericBackend[] backends = [new ManagedCpuBackend(), new ScalarBatchedNumericBackendAdapter(new ManagedCpuBackend())];
        foreach (var backend in backends)
        {
            var distances = new double[count];
            var hits = new bool[count];
            backend.IntersectStandard(x, y, z, dx, dy, dz, radius, conic, distances, hits);
            for (var i = 0; i < count; i++)
            {
                Assert.True(hits[i]);
                var (first, second) = OrdinaryRoots(new Vector3D(x[i], y[i], z[i]),
                    new Vector3D(dx[i], dy[i], dz[i]), radius, conic);
                var ordinary = StandardIntersectionValidation.SelectStandardExplicitSagDistance(first, second,
                    x[i], y[i], z[i], dx[i], dy[i], dz[i], radius, conic);
                if (ordinary.HasValue) Assert.Equal(ordinary.Value, distances[i]);
                else Assert.InRange(Math.Abs(distances[i] - 2.48), 0, 2e-12);
            }
        }
    }

    // Independent direct quadratic roots reproduce the accepted pre-repair
    // branch, while the manufactured target supplies the recovered-root oracle.
    private static (double First, double Second) OrdinaryRoots(Vector3D origin, Vector3D direction, double radius, double conic)
    {
        var k = 1 + conic;
        var a = direction.X * direction.X + direction.Y * direction.Y + k * direction.Z * direction.Z;
        var b = 2 * (origin.X * direction.X + origin.Y * direction.Y - radius * direction.Z + k * origin.Z * direction.Z);
        var c = origin.X * origin.X + origin.Y * origin.Y - 2 * radius * origin.Z + k * origin.Z * origin.Z;
        var root = Math.Sqrt(b * b - 4 * a * c);
        return ((-b - root) / (2 * a), (-b + root) / (2 * a));
    }
}
