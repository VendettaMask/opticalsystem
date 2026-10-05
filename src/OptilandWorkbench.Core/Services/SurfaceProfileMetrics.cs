using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;

namespace OptilandWorkbench.Core.Services;

public enum SurfaceProfileQuantity { Sag, Slope, Curvature }

public readonly record struct SurfaceProfileStatistics(double Rms, double Minimum, double Maximum,
    double MinimumX, double MinimumY, double MaximumX, double MaximumY, int SampleCount)
{
    public double Value(int data) => data switch
    {
        1 => Rms,
        2 => Maximum - Minimum,
        3 => Minimum,
        4 => Maximum,
        5 => MinimumX,
        6 => MinimumY,
        7 => MaximumX,
        8 => MaximumY,
        _ => throw new NotSupportedException("当前支持 Data=1..8；拟合球面和离轴坐标数据尚未实现。")
    };
}

/// <summary>Uniform XY-grid statistics of the actual local surface profile inside its physical aperture.</summary>
public static class SurfaceProfileMetrics
{
    /// <summary>Local coordinates in mm; the evaluation point is not clipped by the aperture.</summary>
    public static double AtPoint(OpticalSurface surface, SurfaceProfileQuantity quantity, double x, double y,
        int remove = 0, int orientation = 0)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        OpticCapabilityPreflight.EnsureSurfaceSupported(surface, OpticCapabilityOperation.Optimization);
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(x));
        if (!Enum.IsDefined(quantity)) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (remove is not (0 or 1)) throw new NotSupportedException("当前支持 Remove=0/1；最佳拟合球面移除尚未实现。");
        if (orientation < 0 || orientation > (quantity == SurfaceProfileQuantity.Slope ? 4 : 3)) throw new NotSupportedException("斜率支持 Orientation=0..4，曲率支持 0..3。");
        var value = At(surface, remove == 1 ? BaseSphere(surface) : null, quantity, x, y, orientation);
        return double.IsFinite(value) ? value : throw new InvalidOperationException("指定点没有有限面形值。");
    }

    public static SurfaceProfileStatistics Evaluate(OpticalSurface surface, SurfaceProfileQuantity quantity,
        int sampling, int remove, int orientation)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        OpticCapabilityPreflight.EnsureSurfaceSupported(surface, OpticCapabilityOperation.Optimization);
        if (sampling is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(sampling), "面形统计当前支持 Samp=1..5（33..513）。");
        if (remove is not (0 or 1)) throw new NotSupportedException("当前支持 Remove=0 原面形或 1 减去基准球面；最佳拟合球面尚未实现。");
        if (orientation < 0 || orientation > (quantity == SurfaceProfileQuantity.Slope ? 4 : 3)) throw new NotSupportedException("斜率支持 Orientation=0..4，曲率支持 0..3。");
        if (!Enum.IsDefined(quantity)) throw new ArgumentOutOfRangeException(nameof(quantity));
        var baseSurface = remove == 1 ? BaseSphere(surface) : null;
        return SurfaceMetricSampling.Evaluate(surface, sampling,
            (x, y) => At(surface, baseSurface, quantity, x, y, orientation));
    }

    private static double At(OpticalSurface surface, OpticalSurface? baseSurface, SurfaceProfileQuantity quantity, double x, double y, int orientation)
    {
        if (quantity == SurfaceProfileQuantity.Sag) return surface.Geometry.Sag(x, y) - (baseSurface?.Geometry.Sag(x, y) ?? 0);
        var differential = SurfaceDifferentialMetrics.At(surface, x, y);
        if (baseSurface is not null)
        {
            var basis = SurfaceDifferentialMetrics.At(baseSurface, x, y);
            differential = new(differential.Dx - basis.Dx, differential.Dy - basis.Dy,
                differential.Dxx - basis.Dxx, differential.Dxy - basis.Dxy, differential.Dyy - basis.Dyy);
        }
        if (quantity == SurfaceProfileQuantity.Slope && orientation == 4)
            return double.Hypot(differential.Dx, differential.Dy);
        var radius = Math.Sqrt(x * x + y * y);
        var radialX = radius == 0 ? 0 : x / radius; var radialY = radius == 0 ? 1 : y / radius;
        var (dx, dy) = orientation switch
        { 0 => (radialX, radialY), 1 => (-radialY, radialX), 2 => (1.0, 0.0), _ => (0.0, 1.0) };
        return quantity == SurfaceProfileQuantity.Slope ? differential.Slope(dx, dy) : differential.NormalCurvature(dx, dy);
    }

    private static OpticalSurface BaseSphere(OpticalSurface source)
    {
        var radius = source.Geometry switch
        {
            PlaneGeometry => 0,
            StandardGeometry standard => standard.Radius,
            EvenAsphereGeometry even => even.Base.Radius,
            OddAsphereGeometry odd => odd.Base.Radius,
            _ => throw new NotSupportedException("该面形未定义可减去的基准球面。")
        };
        return new OpticalSurface { Geometry = new StandardGeometry(radius, 0) };
    }

}
