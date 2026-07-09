namespace OptilandWorkbench.Core.Backend;

public readonly record struct Matrix3x3(
    double M11,
    double M12,
    double M13,
    double M21,
    double M22,
    double M23,
    double M31,
    double M32,
    double M33)
{
    public static Matrix3x3 Identity => new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    public Vector3D Transform(Vector3D vector)
    {
        return new Vector3D(
            (M11 * vector.X) + (M12 * vector.Y) + (M13 * vector.Z),
            (M21 * vector.X) + (M22 * vector.Y) + (M23 * vector.Z),
            (M31 * vector.X) + (M32 * vector.Y) + (M33 * vector.Z));
    }

    public Matrix3x3 Transpose()
    {
        return new Matrix3x3(M11, M21, M31, M12, M22, M32, M13, M23, M33);
    }

    public static Matrix3x3 operator *(Matrix3x3 left, Matrix3x3 right)
    {
        return new Matrix3x3(
            (left.M11 * right.M11) + (left.M12 * right.M21) + (left.M13 * right.M31),
            (left.M11 * right.M12) + (left.M12 * right.M22) + (left.M13 * right.M32),
            (left.M11 * right.M13) + (left.M12 * right.M23) + (left.M13 * right.M33),
            (left.M21 * right.M11) + (left.M22 * right.M21) + (left.M23 * right.M31),
            (left.M21 * right.M12) + (left.M22 * right.M22) + (left.M23 * right.M32),
            (left.M21 * right.M13) + (left.M22 * right.M23) + (left.M23 * right.M33),
            (left.M31 * right.M11) + (left.M32 * right.M21) + (left.M33 * right.M31),
            (left.M31 * right.M12) + (left.M32 * right.M22) + (left.M33 * right.M32),
            (left.M31 * right.M13) + (left.M32 * right.M23) + (left.M33 * right.M33));
    }
}
