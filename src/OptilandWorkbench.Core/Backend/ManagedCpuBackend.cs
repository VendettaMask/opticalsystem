namespace OptilandWorkbench.Core.Backend;

public sealed partial class ManagedCpuBackend : INumericBackend, IBatchedNumericBackend
{
    public string Name => "managed-cpu";

    public double Pi => Math.PI;

    public double Epsilon => 1e-12;

    public double Abs(double value) => Math.Abs(value);

    public double Acos(double value) => Math.Acos(Clamp(value, -1, 1));

    public double Asin(double value) => Math.Asin(Clamp(value, -1, 1));

    public double Atan2(double y, double x) => Math.Atan2(y, x);

    public double Cos(double value) => Math.Cos(value);

    public double Exp(double value) => Math.Exp(value);

    public double Log(double value) => Math.Log(value);

    public double Pow(double value, double power) => Math.Pow(value, power);

    public double Sin(double value) => Math.Sin(value);

    public double Sqrt(double value) => Math.Sqrt(Math.Max(0, value));

    public double Tan(double value) => Math.Tan(value);

    public double Clamp(double value, double min, double max) => Math.Min(Math.Max(value, min), max);

    public double Dot(Vector3D left, Vector3D right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }

    public Vector3D Cross(Vector3D left, Vector3D right)
    {
        return new Vector3D(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));
    }

    public Vector3D Normalize(Vector3D vector)
    {
        var length = vector.Length;
        return length <= Epsilon ? new Vector3D(0, 0, 1) : vector / length;
    }
}
