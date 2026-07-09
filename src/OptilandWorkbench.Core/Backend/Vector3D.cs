namespace OptilandWorkbench.Core.Backend;

public readonly record struct Vector3D(double X, double Y, double Z)
{
    public static Vector3D Zero => new(0, 0, 0);

    public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

    public static Vector3D operator +(Vector3D left, Vector3D right)
    {
        return new Vector3D(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    }

    public static Vector3D operator -(Vector3D left, Vector3D right)
    {
        return new Vector3D(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    }

    public static Vector3D operator -(Vector3D vector)
    {
        return new Vector3D(-vector.X, -vector.Y, -vector.Z);
    }

    public static Vector3D operator *(Vector3D vector, double scalar)
    {
        return new Vector3D(vector.X * scalar, vector.Y * scalar, vector.Z * scalar);
    }

    public static Vector3D operator *(double scalar, Vector3D vector)
    {
        return vector * scalar;
    }

    public static Vector3D operator /(Vector3D vector, double scalar)
    {
        return new Vector3D(vector.X / scalar, vector.Y / scalar, vector.Z / scalar);
    }
}
