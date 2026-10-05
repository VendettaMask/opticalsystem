namespace OptilandWorkbench.Core.Services;

/// <summary>Convergence-checked derivative of a sampled ray intercept, per normalized pupil coordinate.</summary>
public static class RayFanDerivativeMetrics
{
    public static double Differentiate(Func<double, double> intercept, double coordinate, double otherCoordinate)
    {
        if (!double.IsFinite(coordinate) || !double.IsFinite(otherCoordinate)
            || coordinate * coordinate + otherCoordinate * otherCoordinate > 1)
            throw new ArgumentOutOfRangeException(nameof(coordinate), "导数采样点必须在单位圆瞳内。");
        var limit = Math.Sqrt(1 - otherCoordinate * otherCoordinate);
        if (limit == 0) throw new InvalidOperationException("此圆瞳切点沿指定方向没有可用邻域。");
        var left = coordinate + limit; var right = limit - coordinate;
        var central = Math.Min(left, right) >= 0.002;
        var sign = right >= left ? 1 : -1;
        var h = Math.Min(0.001, (central ? Math.Min(left, right) : Math.Max(left, right)) / 4);
        double Sample(double x)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var value = intercept(x);
            return double.IsFinite(value) ? value : throw new InvalidOperationException("光线扇导数采样不是有限数值。");
        }
        double Estimate(double step) => central
            ? (Sample(coordinate - 2 * step) - 8 * Sample(coordinate - step)
                + 8 * Sample(coordinate + step) - Sample(coordinate + 2 * step)) / (12 * step)
            : sign * (-25 * Sample(coordinate) + 48 * Sample(coordinate + sign * step)
                - 36 * Sample(coordinate + 2 * sign * step) + 16 * Sample(coordinate + 3 * sign * step)
                - 3 * Sample(coordinate + 4 * sign * step)) / (12 * step);
        var previous = Estimate(h);
        for (var iteration = 0; iteration < 8; iteration++)
        {
            h /= 2;
            var current = Estimate(h);
            if (double.IsFinite(current) && Math.Abs(current - previous) <= 1e-7 + 1e-5 * Math.Abs(current)) return current;
            previous = current;
        }
        throw new InvalidOperationException("光线扇导数未收敛，不能返回可靠值。");
    }
}
