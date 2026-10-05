using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Phase;

namespace OptilandWorkbench.Core.Services;

public enum SurfacePhaseQuantity { Phase, Slope }

public readonly record struct SurfacePhaseStatistics(SurfaceProfileStatistics Samples, double RemovedConstant)
{
    public double Value(int data) => data switch
    {
        >= 1 and <= 8 => Samples.Value(data),
        9 => RemovedConstant,
        >= 10 and <= 12 => 0, // Supported Remove=0/1 modes remove no tilt or power.
        _ => throw new ArgumentOutOfRangeException(nameof(data), "相位统计 Data 为 1..12。")
    };
}

/// <summary>Local phase in waves and its analytic gradient in waves/mm, evaluated at the supplied wavelength.</summary>
public static class SurfacePhaseMetrics
{
    public static double AtPoint(OpticalSurface surface, SurfacePhaseQuantity quantity, double wavelengthNanometers,
        double x, double y, int mode = 1, int remove = 0, int orientation = 0)
    {
        var profile = Validate(surface, quantity, wavelengthNanometers, remove, orientation);
        if (mode is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(mode), "Mode 为 0/1（局部 mm）或 2（按净半口径归一）。");
        if (mode == 2) { x *= surface.SemiDiameter; y *= surface.SemiDiameter; }
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(x));
        EnsureWork(profile, quantity, 1);
        var constant = remove == 1 ? Finite(profile.Phase(0, 0, wavelengthNanometers)) : 0;
        return At(profile, quantity, wavelengthNanometers, x, y, constant, orientation);
    }

    public static SurfacePhaseStatistics Evaluate(OpticalSurface surface, SurfacePhaseQuantity quantity,
        double wavelengthNanometers, int sampling = 1, int remove = 0, int orientation = 0)
    {
        var profile = Validate(surface, quantity, wavelengthNanometers, remove, orientation);
        var count = SurfaceMetricSampling.GridSize(sampling);
        EnsureWork(profile, quantity, (long)count * count);
        var constant = remove == 1 ? Finite(profile.Phase(0, 0, wavelengthNanometers)) : 0;
        return new(SurfaceMetricSampling.Evaluate(surface, sampling,
            (x, y) => At(profile, quantity, wavelengthNanometers, x, y, constant, orientation)), constant / (2 * Math.PI));
    }

    private static IPhaseProfile Validate(OpticalSurface surface, SurfacePhaseQuantity quantity,
        double wavelength, int remove, int orientation)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        OpticCapabilityPreflight.EnsureSurfaceSupported(surface, OpticCapabilityOperation.Optimization);
        if (!Enum.IsDefined(quantity)) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (!double.IsFinite(wavelength) || wavelength <= 0) throw new ArgumentOutOfRangeException(nameof(wavelength));
        if (remove is not (0 or 1)) throw new NotSupportedException("相位当前支持 Remove=0/1；Zernike 倾斜/光焦度项移除尚未接通。");
        if (orientation is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(orientation), "Orientation 为 0..4。");
        return surface.InteractionModel is PhaseInteractionModel interaction ? interaction.Profile
            : throw new NotSupportedException("此面不是相位面，不能计算相位操作数。");
    }

    private static void EnsureWork(IPhaseProfile profile, SurfacePhaseQuantity quantity, long samples)
    {
        // GridPhaseProfile currently solves a not-a-knot spline for each row and
        // query. Budget its actual dense-solver work, rather than just the output grid.
        var perPoint = profile switch
        {
            GridPhaseProfile grid => Math.Pow(grid.XCoordinates.Count, 3) * grid.YCoordinates.Count
                + Math.Pow(grid.YCoordinates.Count, 3),
            PolynomialPhaseProfile polynomial => Math.Max(1, polynomial.Coefficients.Count),
            RadialPhaseProfile radial => Math.Max(1, radial.Coefficients.Count),
            _ => 1.0
        };
        if (samples * perPoint * (quantity == SurfacePhaseQuantity.Slope ? 2 : 1) > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(samples), "相位采样超过 1 亿估算计算预算，请降低采样或相位模型复杂度。");
    }

    private static double At(IPhaseProfile profile, SurfacePhaseQuantity quantity, double wavelength,
        double x, double y, double constant, int orientation)
    {
        if (quantity == SurfacePhaseQuantity.Phase) return Finite((Finite(profile.Phase(x, y, wavelength)) - constant) / (2 * Math.PI));
        var (gx, gy) = profile.Gradient(x, y, wavelength);
        var dx = Finite(gx) / (2 * Math.PI); var dy = Finite(gy) / (2 * Math.PI);
        var scale = Math.Max(Math.Abs(x), Math.Abs(y));
        var radius = scale == 0 ? 1 : double.Hypot(x / scale, y / scale);
        var ux = scale == 0 ? 0 : (x / scale) / radius; var uy = scale == 0 ? 1 : (y / scale) / radius;
        return Finite(orientation switch
        {
            0 => dx * ux + dy * uy,
            1 => -dx * uy + dy * ux,
            2 => dx,
            3 => dy,
            _ => double.Hypot(dx, dy)
        });
    }

    private static double Finite(double value) => double.IsFinite(value) ? value
        : throw new InvalidOperationException("相位模型返回了非有限相位或梯度。");
}
