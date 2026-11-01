using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Geometries;

namespace OpticalSystemDesign.Core.NonSequential;

/// <summary>A rigid surface-to-assembly transform; explicit bases avoid Euler decomposition at gimbal lock.</summary>
public sealed class OpticalAssemblySurfaceFrame
{
    public Vector3D Origin { get; }
    public Vector3D AxisX { get; }
    public Vector3D AxisY { get; }
    public Vector3D AxisZ { get; }

    [JsonConstructor]
    public OpticalAssemblySurfaceFrame(Vector3D origin, Vector3D axisX, Vector3D axisY, Vector3D axisZ)
    {
        if (!Finite(origin) || !Finite(axisX) || !Finite(axisY) || !Finite(axisZ)
            || origin.Length > 1e9 || Math.Abs(axisX.Length - 1) > 1e-12
            || Math.Abs(axisY.Length - 1) > 1e-12 || Math.Abs(axisZ.Length - 1) > 1e-12
            || Math.Abs(Dot(axisX, axisY)) > 1e-12 || (Cross(axisX, axisY) - axisZ).Length > 1e-12)
            throw new InvalidDataException("逐面坐标架必须是有限、右手、正交的刚体变换。");
        Origin = origin; AxisX = axisX; AxisY = axisY; AxisZ = axisZ;
    }

    public static OpticalAssemblySurfaceFrame RelativeTo(CoordinateSystem assembly, CoordinateSystem surface) => new(
        assembly.ToLocalPoint(surface.Origin), assembly.ToLocalDirection(surface.ToGlobalDirection(new(1, 0, 0))),
        assembly.ToLocalDirection(surface.ToGlobalDirection(new(0, 1, 0))), assembly.ToLocalDirection(surface.ToGlobalDirection(new(0, 0, 1))));
    public Vector3D ToAssemblyPoint(Vector3D point) => Origin + ToAssemblyDirection(point);
    public Vector3D ToAssemblyDirection(Vector3D direction) => AxisX * direction.X + AxisY * direction.Y + AxisZ * direction.Z;
    public Vector3D ToSurfacePoint(Vector3D point) => ToSurfaceDirection(point - Origin);
    public Vector3D ToSurfaceDirection(Vector3D direction) => new(Dot(direction, AxisX), Dot(direction, AxisY), Dot(direction, AxisZ));
    internal static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    internal static Vector3D Cross(Vector3D a, Vector3D b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    internal static bool Finite(Vector3D p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);
}

/// <summary>Lossless, bounded definition of the supported shared-Core rotational surfaces.</summary>
public sealed class OpticalAssemblySurfaceGeometry
{
    public string Kind { get; }
    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public double Radius { get; }
    public double Conic { get; }
    public IReadOnlyList<double> Coefficients { get; }

    [JsonConstructor]
    public OpticalAssemblySurfaceGeometry(string kind, double radius, double conic, IReadOnlyList<double> coefficients)
    {
        if (kind is not ("plane" or "standard" or "even_asphere" or "odd_asphere"))
            throw new NotSupportedException("复合组件当前支持平面、Standard、偶次和奇次旋转非球面。");
        if (double.IsNaN(radius) || !double.IsFinite(conic) || coefficients is null
            || coefficients.Count > 32 || coefficients.Any(c => !double.IsFinite(c))
            || kind is "plane" or "standard" && coefficients.Count != 0
            || kind == "odd_asphere" && coefficients.Count > 0 && coefficients[0] != 0)
            throw new InvalidDataException("旋转曲面参数无效、超过32项，或奇次面含轴上不可微的r一次尖点。");
        if(kind=="plane"&&(!double.IsInfinity(radius)&&Math.Abs(radius)>=1e-12||conic!=0))
            throw new InvalidDataException("平面几何不能夹带有曲率的声明。");
        Kind = kind; Radius = radius; Conic = conic; Coefficients = Array.AsReadOnly(coefficients.ToArray());
    }

    public static OpticalAssemblySurfaceGeometry FromGeometry(IGeometry geometry, double planeRadius = 0) => geometry switch
    {
        PlaneGeometry => new("plane", planeRadius, 0, []),
        StandardGeometry g => new(g.Kind, g.Radius, g.Conic, []),
        EvenAsphereGeometry g => new(g.Kind, g.Base.Radius, g.Base.Conic, g.Coefficients),
        OddAsphereGeometry g => new(g.Kind, g.Base.Radius, g.Base.Conic, g.Coefficients),
        _ => throw new NotSupportedException($"不能将 {geometry.Kind} 无损转换为旋转光学组件。")
    };
    internal IGeometry Restore() => Kind switch
    {
        "plane" => new PlaneGeometry(), "standard" => new StandardGeometry(Radius, Conic),
        "even_asphere" => new EvenAsphereGeometry(Radius, Conic, Coefficients),
        "odd_asphere" => new OddAsphereGeometry(Radius, Conic, Coefficients),
        _ => throw new InvalidDataException("Unknown rotational surface.")
    };
    internal int Power(int index) => Kind == "even_asphere" ? 2 * (index + 1) : index + 1;
}
