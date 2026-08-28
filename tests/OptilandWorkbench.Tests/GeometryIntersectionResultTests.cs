using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Geometries;

namespace OptilandWorkbench.Tests;

public sealed class GeometryIntersectionResultTests
{
    [Fact]
    public void NonlinearIntersectionReturnsVerifiedPointNormalResidualAndCondition()
    {
        var geometry = new EvenAsphereGeometry(40, -0.5, new[] { 2e-5, -3e-8 });
        var origin = new Vector3D(3, -2, -12);
        var direction = new Vector3D(0.01, -0.005, 1);

        var result = geometry.DistanceToIntersection(origin, direction);

        Assert.Equal(IntersectionStatus.Success, result.Status);
        Assert.True(result.IsHit);
        Assert.True(result.Iterations > 0);
        Assert.True(double.IsFinite(result.ConditionEstimate));
        Assert.True(Math.Abs(result.Residual) <= 1e-8);
        Assert.Equal(origin + (direction * result.Distance), result.Point);
        Assert.Equal(geometry.Sag(result.Point.X, result.Point.Y), result.Point.Z, precision: 8);
        Assert.Equal(1, result.Normal.Length, precision: 10);
    }

    [Fact]
    public void UndefinedSagDomainNeverReturnsSuccessfulIntersection()
    {
        var geometry = new EvenAsphereGeometry(1, 0, new[] { 1e-6 });

        var result = geometry.DistanceToIntersection(
            new Vector3D(1.01, 0, -5),
            new Vector3D(0, 0, 1));

        Assert.Equal(IntersectionStatus.DomainError, result.Status);
        Assert.False(result.IsHit);
        Assert.True(double.IsNaN(result.Distance));
    }

    [Fact]
    public void ParallelRayWithoutRootReturnsNoRoot()
    {
        var geometry = new PolynomialGeometry(
            new Dictionary<(int X, int Y), double> { [(0, 0)] = 0 });

        var result = geometry.DistanceToIntersection(
            new Vector3D(0, 0, -1),
            new Vector3D(1, 0, 0));

        Assert.Equal(IntersectionStatus.NoRoot, result.Status);
        Assert.False(result.IsHit);
    }

    [Fact]
    public void GrazingDoubleRootIsReportedAsTangent()
    {
        var geometry = new PolynomialGeometry(
            new Dictionary<(int X, int Y), double> { [(2, 0)] = 1 });

        var result = geometry.DistanceToIntersection(
            new Vector3D(-1, 0, 0),
            new Vector3D(1, 0, 0));

        Assert.Equal(IntersectionStatus.Tangent, result.Status);
        Assert.True(result.IsHit);
        Assert.True(Math.Abs(result.Residual) <= 1e-10);
        Assert.True(result.ConditionEstimate >= 10_000);
    }

    [Fact]
    public void DiscontinuousSignChangeCannotBecomeSuccessWithoutFinalResidual()
    {
        static double Sag(double x, double y) => x < 0.5 ? -1 : 1;
        static Vector3D Normal(Vector3D point) => new(0, 0, 1);

        var result = StandardGeometry.NewtonSolveDistance(
            new Vector3D(0, 0, 0),
            new Vector3D(1, 0, 0),
            Sag,
            Normal);

        Assert.Equal(IntersectionStatus.MaxIterations, result.Status);
        Assert.False(result.IsHit);
        Assert.True(Math.Abs(result.Residual) >= 1);
        Assert.True(double.IsFinite(result.Distance));
        Assert.True(double.IsFinite(result.Point.X));
    }

    [Fact]
    public void InvalidSurfaceNormalCannotBecomeSuccessfulIntersection()
    {
        var result = StandardGeometry.NewtonSolveDistance(
            new Vector3D(0, 0, -1),
            new Vector3D(0, 0, 1),
            static (x, y) => 0,
            static point => new Vector3D(double.NaN, 0, 1));

        Assert.Equal(IntersectionStatus.InvalidNormal, result.Status);
        Assert.False(result.IsHit);
    }

    [Fact]
    public void NonFiniteRayInputReturnsInvalidInput()
    {
        var result = new PlaneGeometry().DistanceToIntersection(
            new Vector3D(double.NaN, 0, -1),
            new Vector3D(0, 0, 1));

        Assert.Equal(IntersectionStatus.InvalidInput, result.Status);
        Assert.False(result.IsHit);
    }
}
