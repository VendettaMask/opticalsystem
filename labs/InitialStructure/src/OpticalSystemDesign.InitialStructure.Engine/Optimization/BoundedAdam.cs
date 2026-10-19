namespace OpticalSystemDesign.InitialStructure.Engine.Optimization;

internal sealed record AdamResult(double[] Variables, LeastSquaresTermination Termination,
    int EvaluationCount, int DifferenceEvaluationCount, int AcceptedSteps, long WorkUnits);

/// <summary>
/// Managed CPU Adam experiment. Gradients are finite differences of caller-supplied residuals,
/// not automatic differentiation. Each probe/trial is charged. Bounds, measured decrease and
/// complete finite residuals are mandatory; no missing derivative is replaced by zero.
/// Moments are solve-local and never reused across curriculum objectives or checkpoints.
/// </summary>
internal static class BoundedAdam
{
    public static AdamResult Solve(IReadOnlyList<double> initial, IReadOnlyList<double> lower,
        IReadOnlyList<double> upper, Func<IReadOnlyList<double>, LeastSquaresEvaluation> evaluate,
        int maximumEvaluations, CancellationToken cancellationToken = default,
        Func<bool>? stopRequested = null, Action<IReadOnlyList<double>>? acceptedStep = null)
    {
        ArgumentNullException.ThrowIfNull(evaluate);
        if (initial.Count == 0 || lower.Count != initial.Count || upper.Count != initial.Count
            || maximumEvaluations < 1 || Enumerable.Range(0, initial.Count).Any(i =>
                !double.IsFinite(initial[i]) || !double.IsFinite(lower[i]) || !double.IsFinite(upper[i])
                || lower[i] > initial[i] || initial[i] > upper[i]))
            throw new ArgumentException("Invalid bounded Adam input or evaluation budget.");
        var vector = initial.ToArray();
        var first = new double[vector.Length];
        var second = new double[vector.Length];
        var count = 0;
        var differences = 0;
        var accepted = 0;
        var work = 0L;
        LeastSquaresTermination? interrupted = null;
        var current = Evaluate(vector, false, null);
        if (current is null) return Finish(interrupted!.Value);
        if (!Valid(current)) return Finish(LeastSquaresTermination.InvalidInitialEvaluation);
        var cost = Cost(current);
        for (var iteration = 1; ; iteration++)
        {
            var gradient = new double[vector.Length];
            for (var column = 0; column < vector.Length; column++)
            {
                if (lower[column] == upper[column]) continue;
                var step = 6.055454452393343e-6 * Math.Max(1, Math.Abs(vector[column]));
                var up = vector.ToArray();
                var down = vector.ToArray();
                up[column] = Math.Min(upper[column], vector[column] + step);
                down[column] = Math.Max(lower[column], vector[column] - step);
                LeastSquaresEvaluation? positive = current, negative = current;
                if (up[column] != vector[column]) positive = Evaluate(up, true, current.Residuals.Count);
                if (positive is null) return Finish(interrupted!.Value);
                if (down[column] != vector[column]) negative = Evaluate(down, true, current.Residuals.Count);
                if (negative is null) return Finish(interrupted!.Value);
                // Invalid propagation is an unavailable derivative, not an optical penalty.
                if (!Valid(positive) || !Valid(negative)) return Finish(LeastSquaresTermination.DerivativeUnavailable);
                var span = up[column] - down[column];
                if (span <= 0) return Finish(LeastSquaresTermination.DerivativeUnavailable);
                for (var row = 0; row < current.Residuals.Count; row++)
                    gradient[column] += 2 * current.Residuals[row]
                        * (positive.Residuals[row] - negative.Residuals[row]) / span;
                if (!double.IsFinite(gradient[column])) return Finish(LeastSquaresTermination.NonFiniteModel);
                if (vector[column] == lower[column] && gradient[column] > 0
                    || vector[column] == upper[column] && gradient[column] < 0) gradient[column] = 0;
            }
            if (gradient.Max(Math.Abs) <= 1e-10) return Finish(LeastSquaresTermination.Stationary);
            var direction = new double[vector.Length];
            for (var column = 0; column < vector.Length; column++)
            {
                first[column] = .9 * first[column] + .1 * gradient[column];
                second[column] = .999 * second[column] + .001 * gradient[column] * gradient[column];
                direction[column] = (first[column] / (1 - Math.Pow(.9, iteration)))
                    / (Math.Sqrt(second[column] / (1 - Math.Pow(.999, iteration))) + 1e-8);
                if (!double.IsFinite(direction[column])) return Finish(LeastSquaresTermination.NonFiniteModel);
            }
            // Stale momentum may point uphill. Use a fresh scaled gradient in that case.
            if (gradient.Select((value, i) => value * direction[i]).Sum() <= 0)
            {
                Array.Clear(first);
                Array.Clear(second);
                var scale = gradient.Max(Math.Abs);
                for (var i = 0; i < direction.Length; i++) direction[i] = gradient[i] / scale;
            }
            var learningRate = .02 * (.1 + .9 * .5 * (1 + Math.Cos(Math.PI * ((iteration - 1) % 8) / 8)));
            var improved = false;
            for (var backoff = 0; backoff < 8; backoff++)
            {
                var trial = vector.Select((value, i) => Math.Clamp(value - learningRate * direction[i], lower[i], upper[i])).ToArray();
                if (trial.SequenceEqual(vector)) return Finish(LeastSquaresTermination.StepTooSmall);
                var evaluation = Evaluate(trial, false, current.Residuals.Count);
                if (evaluation is null) return Finish(interrupted!.Value);
                var trialCost = Valid(evaluation) ? Cost(evaluation) : double.PositiveInfinity;
                if (trialCost < cost)
                {
                    vector = trial;
                    current = evaluation;
                    cost = trialCost;
                    accepted++;
                    acceptedStep?.Invoke(vector.ToArray());
                    improved = true;
                    break;
                }
                learningRate *= .5;
            }
            if (!improved) return Finish(LeastSquaresTermination.StepTooSmall);
        }

        LeastSquaresEvaluation? Evaluate(double[] point, bool difference, int? rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stopRequested?.Invoke() == true) { interrupted = LeastSquaresTermination.StopRequested; return null; }
            if (count >= maximumEvaluations) { interrupted = LeastSquaresTermination.EvaluationLimit; return null; }
            count++;
            if (difference) differences++;
            var result = evaluate(point);
            work = checked(work + result.WorkUnits);
            if (result.WorkUnits < 0 || rows.HasValue && result.Residuals.Count != rows.Value)
                throw new InvalidOperationException("Evaluation work or residual dimension changed during a fixed Adam objective.");
            return result;
        }

        AdamResult Finish(LeastSquaresTermination termination) => new(vector.ToArray(), termination, count, differences, accepted, work);
    }

    private static bool Valid(LeastSquaresEvaluation value) => value.IsValid && value.Residuals.Count > 0
        && value.Residuals.All(double.IsFinite) && double.IsFinite(Cost(value));
    private static double Cost(LeastSquaresEvaluation value) => value.Residuals.Sum(residual => residual * residual);
}
