using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Services;

/// <summary>Shared uniform local XY aperture sampling for surface quantities.</summary>
internal static class SurfaceMetricSampling
{
    internal static int GridSize(int sampling)
    {
        if (sampling is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(sampling), "面数据当前支持 Samp=1..5（33..513）。");
        return (1 << (sampling + 4)) + 1;
    }

    internal static SurfaceProfileStatistics Evaluate(OpticalSurface surface, int sampling, Func<double, double, double> evaluate)
    {
        var aperture = surface.PhysicalAperture ?? new CircularAperture(surface.SemiDiameter);
        var (cx, cy, rx, ry) = Bounds(aperture);
        if (new[] { cx, cy, rx, ry, cx - rx, cx + rx, cy - ry, cy + ry }.Any(v => !double.IsFinite(v)) || Math.Min(rx, ry) <= 0)
            throw new InvalidOperationException("统计孔径没有有限非零范围。");
        var count = GridSize(sampling);
        var minimum = double.PositiveInfinity; var maximum = double.NegativeInfinity;
        var minX = 0.0; var minY = 0.0; var maxX = 0.0; var maxY = 0.0;
        var scale = 0.0; var scaledSquares = 0.0; var samples = 0;
        for (var iy = 0; iy < count; iy++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var y = cy + ry * (2.0 * iy / (count - 1) - 1);
            for (var ix = 0; ix < count; ix++)
            {
                var x = cx + rx * (2.0 * ix / (count - 1) - 1);
                if (!aperture.Contains(new Vector3D(x, y, 0))) continue;
                var value = evaluate(x, y);
                if (!double.IsFinite(value)) throw new InvalidOperationException("孔径内面形统计遇到非有限值。");
                if (value < minimum) { minimum = value; minX = x; minY = y; }
                if (value > maximum) { maximum = value; maxX = x; maxY = y; }
                var absolute = Math.Abs(value);
                if (absolute > scale)
                { scaledSquares = scaledSquares * Math.Pow(scale / absolute, 2) + 1; scale = absolute; }
                else if (scale > 0) scaledSquares += Math.Pow(absolute / scale, 2);
                samples++;
            }
        }
        if (samples == 0) throw new InvalidOperationException("孔径内没有可用面形采样点。");
        return new(scale * Math.Sqrt(scaledSquares / samples), minimum, maximum, minX, minY, maxX, maxY, samples);
    }
    private static (double X, double Y, double RadiusX, double RadiusY) Bounds(IPhysicalAperture aperture) => aperture switch
    {
        CircularAperture c => (0, 0, c.Radius, c.Radius),
        AnnularAperture a => (0, 0, a.OuterRadius, a.OuterRadius),
        OffsetRadialAperture a => (a.OffsetX, a.OffsetY, a.OuterRadius, a.OuterRadius),
        RectangularAperture r => (r.CenterX, r.CenterY, r.HalfWidth, r.HalfHeight),
        EllipticalAperture e => (e.OffsetX, e.OffsetY, e.SemiAxisX, e.SemiAxisY),
        _ => throw new NotSupportedException("面形统计当前支持圆、环形、偏心圆、矩形和椭圆孔径。")
    };
}
