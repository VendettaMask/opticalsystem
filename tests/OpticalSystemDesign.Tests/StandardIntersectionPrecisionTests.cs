using System.Numerics;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Tests;

public sealed class StandardIntersectionPrecisionTests
{
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

    [Fact]
    public void PetzvalGaussianScanRetainsEveryRayConfirmedByTheNativePupilAudit()
    {
        var root = ProductDependencyArchitectureTests.RepositoryRoot();
        var source = Path.Combine(root, "src/OpticalSystemDesign.App/Assets/SequentialExamples/Objectives/Petzval.zmx");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx");
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
