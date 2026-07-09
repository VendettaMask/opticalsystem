using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Optimization;

public sealed record OptimizationResult(
    bool Success,
    string Message,
    double InitialMetric,
    double FinalMetric,
    double FinalValue,
    int Iterations);

public sealed class SimpleOptimizer
{
    private readonly Optic _optic;

    public SimpleOptimizer(Optic optic)
    {
        _optic = optic;
    }

    public OptimizationResult OptimizeRadius(OpticalSurface surface, int iterations = 24)
    {
        if (surface.IsPlane)
        {
            surface.Radius = 40;
        }

        var analysis = new AnalysisRunner(_optic);
        var initialRadius = surface.Radius;
        var bestRadius = initialRadius;
        var bestMetric = analysis.EvaluateSpotDiagram().RmsSpotRadius;
        var initialMetric = bestMetric;
        var step = Math.Max(1.0, Math.Abs(initialRadius) * 0.18);

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var improved = false;
            foreach (var candidate in new[] { bestRadius - step, bestRadius + step })
            {
                surface.Radius = Math.Abs(candidate) < 1e-6 ? Math.Sign(candidate) * 1e-6 : candidate;
                var metric = analysis.EvaluateSpotDiagram().RmsSpotRadius;
                if (metric < bestMetric)
                {
                    bestMetric = metric;
                    bestRadius = surface.Radius;
                    improved = true;
                }
            }

            surface.Radius = bestRadius;
            if (!improved)
            {
                step *= 0.5;
            }
        }

        return new OptimizationResult(
            Success: bestMetric <= initialMetric,
            Message: $"Optimized surface {surface.Number} radius from {initialRadius:0.###} to {bestRadius:0.###}.",
            InitialMetric: initialMetric,
            FinalMetric: bestMetric,
            FinalValue: bestRadius,
            Iterations: iterations);
    }
}
