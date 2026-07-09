namespace OptilandWorkbench.Core.Backend;

public interface INumericBackend
{
    string Name { get; }

    double Pi { get; }

    double Epsilon { get; }

    double Abs(double value);

    double Acos(double value);

    double Asin(double value);

    double Atan2(double y, double x);

    double Cos(double value);

    double Exp(double value);

    double Log(double value);

    double Pow(double value, double power);

    double Sin(double value);

    double Sqrt(double value);

    double Tan(double value);

    double Clamp(double value, double min, double max);

    double Dot(Vector3D left, Vector3D right);

    Vector3D Cross(Vector3D left, Vector3D right);

    Vector3D Normalize(Vector3D vector);
}
