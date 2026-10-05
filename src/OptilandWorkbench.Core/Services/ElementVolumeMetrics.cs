using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Propagation;

namespace OptilandWorkbench.Core.Services;

/// <summary>Manufacturing volume in cm³ and mass in grams. Core prescription lengths are millimeters.</summary>
public static class ElementVolumeMetrics
{
    internal static double RadialSagIntegral(IGeometry geometry, double minimumRadius, double maximumRadius) =>
        FaceIntegral(geometry, maximumRadius, maximumRadius) - FaceIntegral(geometry, minimumRadius, minimumRadius);

    public static double VolumeCubicCentimeters(OpticalSurface front, OpticalSurface back, SurfaceDiameterMode mode)
    {
        ArgumentNullException.ThrowIfNull(front); ArgumentNullException.ThrowIfNull(back);
        ComputationCancellation.ThrowIfCancellationRequested();
        Validate(front); Validate(back);
        var a = front.CoordinateSystem; var b = back.CoordinateSystem;
        if (Math.Abs(a.Origin.X - b.Origin.X) > 1e-12 || Math.Abs(a.Origin.Y - b.Origin.Y) > 1e-12
            || Math.Abs(a.RotationXDegrees) > 1e-12 || Math.Abs(a.RotationYDegrees) > 1e-12
            || Math.Abs(b.RotationXDegrees) > 1e-12 || Math.Abs(b.RotationYDegrees) > 1e-12)
            throw new NotSupportedException("元件体积当前要求相邻面共轴且没有倾斜。");
        if (!double.IsFinite(front.Thickness) || front.Thickness <= 0
            || Math.Abs((b.Origin.Z - a.Origin.Z) - front.Thickness) > 1e-9 * Math.Max(1, front.Thickness))
            throw new InvalidOperationException("元件体积要求有限正厚度，并与两个面的位置一致。");
        var ra = SurfaceManufacturingMetrics.Diameter(front, mode) / 2;
        var rb = SurfaceManufacturingMetrics.Diameter(back, mode) / 2;
        var radius = Math.Max(ra, rb);
        if (Math.Min(ra, rb) <= 0) throw new InvalidOperationException("元件半口径必须为正。");
        // The smaller circular face extends as a flat annulus to the larger face's edge.
        var frontProfileRadius = Math.Min(ra, front.SemiDiameter + front.ChipZone);
        var backProfileRadius = Math.Min(rb, back.SemiDiameter + back.ChipZone);
        var frontIntegral = FaceIntegral(front.Geometry, frontProfileRadius, radius);
        var backIntegral = FaceIntegral(back.Geometry, backProfileRadius, radius);
        // Reject crossing surfaces rather than allowing a positive signed integral to conceal them.
        for (var i = 0; i <= 1024; i++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var r = radius * i / 1024.0;
            var thickness = front.Thickness + Sag(back.Geometry, Math.Min(r, backProfileRadius))
                - Sag(front.Geometry, Math.Min(r, frontProfileRadius));
            if (thickness < -1e-10) throw new InvalidOperationException("元件表面相交，不能返回实体体积。");
        }
        var volume = (Math.PI * radius * radius * front.Thickness + backIntegral - frontIntegral) / 1000;
        if (!double.IsFinite(volume) || volume <= 0) throw new InvalidOperationException("元件体积不是有限正数。");
        return volume;
    }

    public static double MassGrams(OpticalSurface front, OpticalSurface back, SurfaceDiameterMode mode)
    {
        ArgumentNullException.ThrowIfNull(front); ArgumentNullException.ThrowIfNull(back);
        ComputationCancellation.ThrowIfCancellationRequested();
        // Empty air space contributes no material mass, independent of its refractive-index approximation.
        if (front.MaterialAfter is AirMaterial) return 0;
        if (front.MaterialAfter.PropagationModel is not HomogeneousPropagationModel)
            throw new NotSupportedException("GRIN 材料密度没有正式定义，不采用固定密度近似。");
        var density = front.MaterialAfter is CatalogGlassMaterial catalog ? catalog.ZemaxData?.Density : null;
        if (density is not { } value || !double.IsFinite(value) || value <= 0)
            throw new InvalidOperationException("玻璃目录缺少有效密度（g/cm³），不能计算质量。");
        var mass = VolumeCubicCentimeters(front, back, mode) * value;
        return double.IsFinite(mass) ? mass : throw new InvalidOperationException("元件质量溢出。");
    }

    private static void Validate(OpticalSurface surface)
    {
        OpticCapabilityPreflight.EnsureSurfaceSupported(surface, OpticCapabilityOperation.Optimization);
        if (surface.IsReflective) throw new NotSupportedException("反射空间的实体方向尚未接入体积积分。");
        if (surface.Geometry is not (PlaneGeometry or StandardGeometry or EvenAsphereGeometry or OddAsphereGeometry))
            throw new NotSupportedException("元件体积当前支持旋转对称标准面及偶/奇次非球面。");
        if (surface.PhysicalAperture is not (null or CircularAperture))
            throw new NotSupportedException("非圆或偏心孔径的实体体积积分尚未实现。");
        if (surface.PhysicalAperture is CircularAperture circular
            && Math.Abs(circular.Radius - surface.SemiDiameter) > 1e-12)
            throw new NotSupportedException("独立圆形裁切孔径与净半口径不一致，实体边界尚未定义。");
    }

    private static double FaceIntegral(IGeometry geometry, double faceRadius, double outerRadius)
    {
        var edge = Sag(geometry, faceRadius);
        double integral;
        if (geometry is PlaneGeometry) integral = 0;
        else if (geometry is StandardGeometry { Conic: 0 } sphere)
        {
            // Stable spherical-cap integral: avoids subtracting nearly equal R³ terms.
            integral = edge == 0 ? 0 : Math.PI * edge * edge * (sphere.Radius - 2 * edge / 3);
        }
        else
        {
            var evaluations = 0;
            double Function(double r)
            {
                if (++evaluations > 65_536) throw new InvalidOperationException("元件体积积分超出工作预算。");
                var value = 2 * Math.PI * r * Sag(geometry, r);
                return double.IsFinite(value) ? value : throw new InvalidOperationException("元件体积被积函数溢出。");
            }
            var left = Function(0); var middle = Function(faceRadius / 2); var right = Function(faceRadius);
            var whole = faceRadius * (left + 4 * middle + right) / 6;
            if (!double.IsFinite(whole)) throw new InvalidOperationException("元件体积积分溢出。");
            integral = Integrate(Function, 0, faceRadius, left, middle, right, whole,
                1e-10 * Math.Max(1, Math.Abs(whole)), 24);
        }
        return integral + Math.PI * (outerRadius * outerRadius - faceRadius * faceRadius) * edge;
    }

    private static double Sag(IGeometry geometry, double radius)
    {
        var sag = geometry.Sag(radius, 0);
        if (!double.IsFinite(sag)) throw new InvalidOperationException("元件口径超出有限面形定义域。");
        return sag;
    }

    private static double Integrate(Func<double, double> function, double a, double b, double fa, double fm, double fb,
        double whole, double tolerance, int remaining)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        var m = (a + b) / 2; var fl = function((a + m) / 2); var fr = function((m + b) / 2);
        var left = (m - a) * (fa + 4 * fl + fm) / 6; var right = (b - m) * (fm + 4 * fr + fb) / 6;
        var delta = left + right - whole;
        if (Math.Abs(delta) <= 15 * tolerance) return left + right + delta / 15;
        if (remaining == 0) throw new InvalidOperationException("元件体积积分未达到收敛要求。");
        return Integrate(function, a, m, fa, fl, fm, left, tolerance / 2, remaining - 1)
            + Integrate(function, m, b, fm, fr, fb, right, tolerance / 2, remaining - 1);
    }
}
