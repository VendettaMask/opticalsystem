using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Optimization;

/// <summary>Curvature coordinates (inverse millimeters), including the plane at zero.</summary>
public static class SurfaceCurvatureParameter
{
    public static double Read(OpticalSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return surface.IsPlane ? 0 : 1 / surface.Radius;
    }

    public static void Write(OpticalSurface surface, double curvature)
    {
        ArgumentNullException.ThrowIfNull(surface);
        OptimizationGuards.RequireFiniteArgument(curvature, nameof(curvature));
        // Preserve the exact prescription when applying an unchanged coordinate.
        if (curvature == Read(surface)) return;
        var radius = curvature == 0 ? 0 : 1 / curvature;
        if (!double.IsFinite(radius))
            throw new OptimizationEvaluationException("Surface curvature", "The radius is not representable.");
        surface.Radius = radius;
    }

    public static IOptimizationVariable Create(OpticalSurface surface, Action? changed = null)
    {
        var initial = Read(surface);
        // A local search range, not a physical-feasibility or manufacturing limit.
        var extent = Math.Max(0.025, 2 * Math.Abs(initial));
        return new DelegateVariable(
            $"Surface {surface.Number} curvature (1/mm)",
            () => Read(surface),
            value =>
            {
                Write(surface, value);
                changed?.Invoke();
            },
            -extent,
            extent,
            extent * 0.05,
            new UnitRangeScaler(-extent, extent));
    }
}
