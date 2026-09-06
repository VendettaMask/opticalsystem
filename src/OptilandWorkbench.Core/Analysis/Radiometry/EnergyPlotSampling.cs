namespace OptilandWorkbench.Core.Analysis;

internal static class EnergyPlotSampling
{
    // Native geometric-energy plots use 100 cumulative knots, then four samples
    // per interval, omitting the final endpoint. Their cubic overshoot is clamped
    // to [0,1] and the running maximum. This is an output convention, not a fit.
    internal static IReadOnlyList<AnalysisPoint> Geometric(
        IReadOnlyList<AnalysisPoint> cumulativeKnots)
    {
        var x = cumulativeKnots.Select(p => p.X).ToArray();
        var y = cumulativeKnots.Select(p => p.Y).ToArray();
        var count = 4 * (x.Length - 1);
        var target = Enumerable.Range(0, count)
            .Select(i => x[0] + (x[^1] - x[0]) * i / count).ToArray();
        var values = MtfThroughFocusAnalysis.CubicSplineInterpolate(x, y, target);
        var previous = 0d;
        return target.Select((coordinate, i) =>
            new AnalysisPoint(coordinate, previous = Math.Clamp(Math.Max(previous, values[i]), 0, 1))).ToArray();
    }
}
