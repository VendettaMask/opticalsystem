using OptilandWorkbench.Core.Backend;

namespace OptilandWorkbench.Core.Geometries;

public enum IntersectionStatus
{
    Success,
    NoRoot,
    DomainError,
    Tangent,
    MaxIterations,
    InvalidNormal,
    InvalidInput
}

public readonly record struct IntersectionResult(
    IntersectionStatus Status,
    double Distance,
    Vector3D Point,
    Vector3D Normal,
    double Residual,
    int Iterations,
    double ConditionEstimate)
{
    public bool IsHit => Status is IntersectionStatus.Success or IntersectionStatus.Tangent;

    public static IntersectionResult Failure(
        IntersectionStatus status,
        double residual = double.NaN,
        int iterations = 0,
        double conditionEstimate = double.PositiveInfinity) => new(
            status,
            double.NaN,
            new Vector3D(double.NaN, double.NaN, double.NaN),
            new Vector3D(double.NaN, double.NaN, double.NaN),
            residual,
            iterations,
            conditionEstimate);
}

public interface IGeometry
{
    string Kind { get; }

    double Sag(double x, double y);

    IntersectionResult DistanceToIntersection(Vector3D origin, Vector3D direction);

    Vector3D SurfaceNormal(Vector3D localPoint);

    IGeometry Clone();
}

public interface IGratingGeometry : IGeometry
{
    int GratingOrder { get; }

    double GratingPeriodMicrometers { get; }

    double GrooveOrientationAngleRadians { get; }

    double ParaxialRadius { get; }

    Vector3D GratingVector(Vector3D localPoint);
}
