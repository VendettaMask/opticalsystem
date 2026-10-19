using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine.Optimization;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>
/// CPU experiment inspired by AutoLens e58db32f: half-to-full cosine aperture growth.
/// Adaptation: fixed catalog glasses, shared Core finite differences, bounded Adam,
/// and the existing full-target trust-region polish/independent physical gates.
/// This is not a port of DeepLens autograd, material relaxation or CUDA.
/// See third_party/autolens for upstream attribution and Apache-2.0 terms.
/// </summary>
internal static class AutoLensCpuCurriculum
{
    public static IReadOnlyList<DesignStage> Stages { get; } = Enumerable.Range(0, 5)
        .Select(index => new DesignStage(ApertureFraction(index / 4.0), 1, true)).ToArray();

    internal static double ApertureFraction(double progress)
    {
        if (!double.IsFinite(progress) || progress is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(progress));
        return .5 + .25 * (1 - Math.Cos(Math.PI * progress));
    }

    public static OpticalLocalResult Solve(FlatStartSolverCoordinates coordinates, double[] initial,
        DesignEvaluation initialEvaluation, int maximumEvaluations, Func<double[], DesignEvaluation> evaluate,
        Func<bool> stopRequested, CancellationToken cancellationToken,
        Action<double[], DesignEvaluation> accepted, Action<AdamResult> statistics, Func<long> tracedRayCount)
    {
        var current = initialEvaluation;
        var latest = initialEvaluation;
        var result = BoundedAdam.Solve(coordinates.Encode(initial), coordinates.Lower, coordinates.Upper,
            point =>
            {
                var before = tracedRayCount();
                latest = evaluate(coordinates.Decode(point));
                return new(latest.Residuals, latest.HasContinuousSearchResiduals, tracedRayCount() - before);
            }, maximumEvaluations, cancellationToken, stopRequested,
            point =>
            {
                current = latest;
                accepted(coordinates.Decode(point), current);
            });
        statistics(result);
        return new(coordinates.Decode(result.Variables), current, result.EvaluationCount, result.Termination);
    }
}
