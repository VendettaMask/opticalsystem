using OptilandWorkbench.Core.Backend;

namespace OptilandWorkbench.Core.Coordinates;

public sealed record CoordinateSystem(
    Vector3D Origin,
    double RotationXDegrees = 0,
    double RotationYDegrees = 0,
    double RotationZDegrees = 0)
{
    public static CoordinateSystem Global { get; } = new(Vector3D.Zero);

    public Vector3D ToLocalPoint(Vector3D global)
    {
        return RotationMatrix().Transpose().Transform(global - Origin);
    }

    public Vector3D ToGlobalPoint(Vector3D local)
    {
        return Origin + RotationMatrix().Transform(local);
    }

    public Vector3D ToLocalDirection(Vector3D globalDirection)
    {
        return RotationMatrix().Transpose().Transform(globalDirection);
    }

    public Vector3D ToGlobalDirection(Vector3D localDirection)
    {
        return RotationMatrix().Transform(localDirection);
    }

    private Matrix3x3 RotationMatrix()
    {
        var rx = DegreesToRadians(RotationXDegrees);
        var ry = DegreesToRadians(RotationYDegrees);
        var rz = DegreesToRadians(RotationZDegrees);

        var cx = Math.Cos(rx);
        var sx = Math.Sin(rx);
        var cy = Math.Cos(ry);
        var sy = Math.Sin(ry);
        var cz = Math.Cos(rz);
        var sz = Math.Sin(rz);

        var x = new Matrix3x3(1, 0, 0, 0, cx, -sx, 0, sx, cx);
        var y = new Matrix3x3(cy, 0, sy, 0, 1, 0, -sy, 0, cy);
        var z = new Matrix3x3(cz, -sz, 0, sz, cz, 0, 0, 0, 1);

        return z * y * x;
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }
}
