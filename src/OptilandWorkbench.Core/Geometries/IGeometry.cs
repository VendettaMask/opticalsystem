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
