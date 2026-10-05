using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Materials;

namespace OptilandWorkbench.Core.Services;

public readonly record struct GradientIndexControlPoint(int Number, Vector3D LocalPosition, double Index);
public readonly record struct GradientIndexBlankRange(double MinimumZ, double MaximumZ, double FrontIndex, double RearIndex)
{
    public double Delta => Math.Abs(RearIndex - FrontIndex);
}

/// <summary>Material constraints use the same entrance-local index field as continuous ray tracing.</summary>
public static class GradientIndexControlMetrics
{
    public static GradientIndexControlPoint AtPoint(OpticalSurface front, OpticalSurface back, int point, double wavelengthNanometers)
    {
        var material = Validate(front, back, wavelengthNanometers);
        if (point is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(point), "GRIN 取样点编号必须为 1..6。");
        var radius = Math.Max(front.SemiDiameter, back.SemiDiameter);
        var x = point is 3 or 6 ? radius : 0;
        var y = point is 2 or 5 ? radius : 0;
        var rear = point >= 4;
        // Curved control points follow the local optical sag at the common clear
        // radius. Native curved-point coordinates still require captured validation.
        var z = FiniteSag(rear ? back : front, x, y) + (rear ? front.Thickness : 0);
        var position = new Vector3D(x, y, z);
        return new(point, position, material.RefractiveIndex(position, wavelengthNanometers));
    }

    public static IReadOnlyList<GradientIndexControlPoint> SixPoints(OpticalSurface front, OpticalSurface back, double wavelengthNanometers) =>
        Enumerable.Range(1, 6).Select(point => AtPoint(front, back, point, wavelengthNanometers)).ToArray();

    public static GradientIndexBlankRange AxialBlank(OpticalSurface front, OpticalSurface back, double wavelengthNanometers)
    {
        var material = Validate(front, back, wavelengthNanometers);
        // Standard conics are monotonic in radius on their finite sag branch.
        // These are blank endpoints, not interior extrema of a nonlinear n(z).
        var minimumZ = Math.Min(0, FiniteSag(front, front.SemiDiameter, 0));
        var maximumZ = front.Thickness + Math.Max(0, FiniteSag(back, back.SemiDiameter, 0));
        if (!double.IsFinite(maximumZ) || maximumZ < minimumZ)
            throw new InvalidOperationException("GRIN 毛坯轴向范围无效。");
        return new(minimumZ, maximumZ,
            material.RefractiveIndex(new(0, 0, minimumZ), wavelengthNanometers),
            material.RefractiveIndex(new(0, 0, maximumZ), wavelengthNanometers));
    }

    private static GradientIndexMaterial Validate(OpticalSurface front, OpticalSurface back, double wavelength)
    {
        ArgumentNullException.ThrowIfNull(front);
        ArgumentNullException.ThrowIfNull(back);
        ComputationCancellation.ThrowIfCancellationRequested();
        if (front.MaterialAfter is not GradientIndexMaterial material)
            throw new NotSupportedException("GRIN 材料约束需要指定表面后的空间梯度材料，不能用普通玻璃替代。");
        if (!double.IsFinite(wavelength) || wavelength <= 0)
            throw new ArgumentOutOfRangeException(nameof(wavelength), "波长必须为有限正数。");
        if (!double.IsFinite(front.Thickness) || front.Thickness < 0
            || !double.IsFinite(front.SemiDiameter) || front.SemiDiameter < 0
            || !double.IsFinite(back.SemiDiameter) || back.SemiDiameter < 0)
            throw new InvalidOperationException("GRIN 材料约束需要有限非负厚度和净半口径。");
        foreach (var surface in new[] { front, back })
        {
            if (surface.Geometry is not (PlaneGeometry or StandardGeometry))
                throw new NotSupportedException("GRIN 六点/毛坯约束当前支持标准球面、圆锥和平面边界。");
            var frame = surface.CoordinateSystem;
            if (!Finite(frame.Origin) || !double.IsFinite(frame.RotationXDegrees)
                || !double.IsFinite(frame.RotationYDegrees) || !double.IsFinite(frame.RotationZDegrees))
                throw new InvalidOperationException("GRIN 边界坐标必须有限。");
        }
        var a = front.CoordinateSystem;
        var b = back.CoordinateSystem;
        var offset = a.ToLocalPoint(b.Origin);
        var tolerance = 1e-10 * Math.Max(1, front.Thickness);
        if (!Finite(offset) || Math.Abs(offset.X) > tolerance || Math.Abs(offset.Y) > tolerance
            || Math.Abs(offset.Z - front.Thickness) > tolerance
            || !Aligned(a, b, new(1, 0, 0)) || !Aligned(a, b, new(0, 1, 0)))
            throw new NotSupportedException("GRIN 材料约束要求相邻边界共轴、局部横轴对齐且顶点间距与厚度一致。");
        return material;
    }

    private static bool Aligned(CoordinateSystem a, CoordinateSystem b, Vector3D direction) =>
        (a.ToLocalDirection(b.ToGlobalDirection(direction)) - direction).Length <= 1e-12;

    private static bool Finite(Vector3D value) => double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);

    private static double FiniteSag(OpticalSurface surface, double x, double y)
    {
        var sag = surface.Geometry.Sag(x, y);
        return double.IsFinite(sag) ? sag : throw new InvalidOperationException("GRIN 控制点超出边界的有限矢高定义域。");
    }
}
