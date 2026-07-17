using OptilandWorkbench.Core.Backend;

namespace OptilandWorkbench.Core.Geometries;

public interface IGeometry
{
    string Kind { get; }

    double Sag(double x, double y);

    double? DistanceToIntersection(Vector3D origin, Vector3D direction);

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
