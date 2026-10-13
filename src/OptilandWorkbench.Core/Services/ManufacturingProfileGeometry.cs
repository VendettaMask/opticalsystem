using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Geometries;

namespace OptilandWorkbench.Core.Services;

/// <summary>Radial manufacturing profiles use the same geometry as optical tracing.
/// A single meridian is not a manufacturing certification of a freeform surface.</summary>
public static class ManufacturingProfileGeometry
{
    public static bool IsSupported(IGeometry geometry) => geometry is
        PlaneGeometry or PlaneGratingGeometry or StandardGeometry or StandardGratingGeometry
        or EvenAsphereGeometry or OddAsphereGeometry or ForbesQGeometry;

    public static double? Sag(IGeometry geometry, double height)
    {
        EnsureSupported(geometry);
        if (!double.IsFinite(height)) return null;
        var value = geometry.Sag(0, height);
        return double.IsFinite(value) ? value : null;
    }

    public static double? SlopeDegrees(IGeometry geometry, double height)
    {
        if (Sag(geometry, height) is not { } sag) return null;
        var normal = geometry.SurfaceNormal(new Vector3D(0, height, sag));
        if (!double.IsFinite(normal.Y) || !double.IsFinite(normal.Z)
            || normal.Y == 0 && normal.Z == 0) return null;
        return Math.Atan2(Math.Abs(normal.Y), Math.Abs(normal.Z)) * 180 / Math.PI;
    }

    public static double? MinimumThickness(IGeometry front, IGeometry back, double centerThickness,
        double semiDiameter, int intervals = 64)
    {
        EnsureSupported(front);
        EnsureSupported(back);
        if (!double.IsFinite(centerThickness) || !double.IsFinite(semiDiameter) || semiDiameter < 0)
            return null;
        if (intervals is < 1 or > 65536) throw new ArgumentOutOfRangeException(nameof(intervals));
        var minimum = double.PositiveInfinity;
        for (var index = 0; index <= intervals; index++)
        {
            var height = semiDiameter * index / intervals;
            if (Sag(front, height) is not { } a || Sag(back, height) is not { } b) return null;
            var thickness = centerThickness + b - a;
            if (!double.IsFinite(thickness)) return null;
            minimum = Math.Min(minimum, thickness);
        }
        return minimum;
    }

    public static double MaximumRadius(IGeometry geometry)
    {
        EnsureSupported(geometry);
        var standard = geometry switch
        {
            StandardGeometry value => value,
            StandardGratingGeometry value => value.Base,
            EvenAsphereGeometry value => value.Base,
            OddAsphereGeometry value => value.Base,
            ForbesQGeometry value => value.Base,
            _ => null
        };
        return standard is null || Math.Abs(standard.Radius) < 1e-12
            || double.IsInfinity(standard.Radius) || standard.Conic <= -1
            ? double.PositiveInfinity
            : Math.Abs(standard.Radius) / Math.Sqrt(1 + standard.Conic);
    }

    private static void EnsureSupported(IGeometry geometry)
    {
        if (!IsSupported(geometry))
            throw new NotSupportedException("当前径向制造评估/图纸仅支持旋转对称面，不能将自由曲面或未知面型退化为标准面。");
    }
}
