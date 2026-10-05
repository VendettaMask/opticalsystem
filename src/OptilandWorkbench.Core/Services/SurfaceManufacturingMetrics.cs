using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;

namespace OptilandWorkbench.Core.Services;

public enum SurfaceDiameterMode { Mechanical, Clear }

public static class SurfaceManufacturingMetrics
{
    /// <summary>Rotational optical profile plus its flat annulus, in local mm.</summary>
    public static double MechanicalSag(OpticalSurface surface, double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(x));
        if (surface.Geometry is PlaneGeometry or StandardGeometry or EvenAsphereGeometry or OddAsphereGeometry)
        {
            var radius = double.Hypot(x, y);
            var boundary = surface.SemiDiameter + surface.ChipZone;
            if (radius > boundary) return surface.Geometry.Sag(boundary, 0);
        }
        else if (surface.ChipZone != 0)
            throw new NotSupportedException("非旋转对称面的延伸区和平边尚未定义。");
        return surface.Geometry.Sag(x, y);
    }

    public static double BoundingCylinderVolume(IReadOnlyList<OpticalSurface> surfaces, SurfaceDiameterMode mode)
    {
        if (surfaces.Count == 0) throw new ArgumentException("包围圆柱需要非空表面范围。", nameof(surfaces));
        var low = double.PositiveInfinity;
        var high = double.NegativeInfinity;
        var radius = 0.0;
        foreach (var surface in surfaces)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var frame = surface.CoordinateSystem;
            if (surface.Number == 0 && ObjectConjugate.IsInfinite(surface))
                throw new InvalidOperationException("无穷远物面不能包含在有限圆柱中。");
            if (Math.Abs(frame.Origin.X) > 1e-12 || Math.Abs(frame.Origin.Y) > 1e-12
                || Math.Abs(frame.RotationXDegrees) > 1e-12 || Math.Abs(frame.RotationYDegrees) > 1e-12
                || surface.IsReflective)
                throw new NotSupportedException("CVOL 当前支持共轴、无倾斜的透射表面范围。");
            if (!double.IsFinite(frame.Origin.Z)) throw new InvalidOperationException("表面顶点不是有限位置。");
            low = Math.Min(low, frame.Origin.Z);
            high = Math.Max(high, frame.Origin.Z);
            radius = Math.Max(radius, Diameter(surface, mode) / 2);
        }
        return Math.PI * radius * radius * (high - low);
    }

    public static double Diameter(OpticalSurface surface, SurfaceDiameterMode mode)
    {
        var radius = mode switch
        {
            SurfaceDiameterMode.Mechanical => surface.MechanicalSemiDiameter,
            SurfaceDiameterMode.Clear => surface.SemiDiameter,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        if (!double.IsFinite(radius) || radius < 0)
            throw new InvalidOperationException("所选半口径不是有限非负数值。");
        return 2 * radius;
    }

    public static double DiameterToThickness(OpticalSurface surface, SurfaceDiameterMode mode)
    {
        if (!double.IsFinite(surface.Thickness) || surface.Thickness <= 0)
            throw new InvalidOperationException("径厚比需要有限正的玻璃中心厚度。");
        return Diameter(surface, mode) / surface.Thickness;
    }

    public static double BlankThickness(OpticalSurface front, OpticalSurface back, int axisCode, SurfaceDiameterMode mode)
    {
        if (axisCode is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(axisCode), "毛坯轴向代码必须为 0..4。");
        OpticCapabilityPreflight.EnsureSurfaceSupported(front, OpticCapabilityOperation.Optimization);
        OpticCapabilityPreflight.EnsureSurfaceSupported(back, OpticCapabilityOperation.Optimization);
        if (!double.IsFinite(front.Thickness) || front.Thickness <= 0)
            throw new InvalidOperationException("毛坯厚度需要有限正中心厚度。");
        var a = front.CoordinateSystem;
        var b = back.CoordinateSystem;
        if (Math.Abs(a.Origin.X - b.Origin.X) > 1e-12 || Math.Abs(a.Origin.Y - b.Origin.Y) > 1e-12
            || Math.Abs(a.RotationXDegrees) > 1e-12 || Math.Abs(a.RotationYDegrees) > 1e-12
            || Math.Abs(b.RotationXDegrees) > 1e-12 || Math.Abs(b.RotationYDegrees) > 1e-12
            || Math.Abs(a.RotationZDegrees - b.RotationZDegrees) > 1e-12)
            throw new NotSupportedException("毛坯厚度当前要求两个表面共轴、局部横轴对齐。");

        var frontRadius = Diameter(front, mode) / 2;
        var backRadius = Diameter(back, mode) / 2;
        var low = double.PositiveInfinity;
        var high = double.NegativeInfinity;
        var axes = axisCode == 4 ? new[] { 0, 1, 2, 3 } : new[] { axisCode };
        foreach (var axis in axes)
        {
            var (x, y) = axis switch { 0 => (0.0, 1.0), 1 => (1.0, 0.0), 2 => (0.0, -1.0), _ => (-1.0, 0.0) };
            // 200 points per radial axis, including the vertex and selected edge.
            for (var point = 0; point < 200; point++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                var zone = point / 199.0;
                var zFront = mode == SurfaceDiameterMode.Mechanical
                    ? MechanicalSag(front, x * frontRadius * zone, y * frontRadius * zone)
                    : front.Geometry.Sag(x * frontRadius * zone, y * frontRadius * zone);
                var zBack = front.Thickness + (mode == SurfaceDiameterMode.Mechanical
                    ? MechanicalSag(back, x * backRadius * zone, y * backRadius * zone)
                    : back.Geometry.Sag(x * backRadius * zone, y * backRadius * zone));
                if (!double.IsFinite(zFront) || !double.IsFinite(zBack))
                    throw new InvalidOperationException("毛坯采样点超出表面有限矢高定义域。");
                low = Math.Min(low, Math.Min(zFront, zBack));
                high = Math.Max(high, Math.Max(zFront, zBack));
            }
        }
        return high - low;
    }
}
