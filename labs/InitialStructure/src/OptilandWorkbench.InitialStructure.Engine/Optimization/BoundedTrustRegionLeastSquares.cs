namespace OptilandWorkbench.InitialStructure.Engine.Optimization;

/// <summary>
/// Bounded trust-region least squares: rectangular dogleg or projected regularized
/// QR steps with optional column scaling and guarded Broyden updates. Both require measured decrease.
/// The evaluator is fixed for one Solve call. Changed residual definitions, materials, sampling or
/// variable sets require a new call. This is generic numerical algebra, not an optical model or PSD.
/// </summary>
internal static class BoundedTrustRegionLeastSquares
{
    public static LeastSquaresResult Solve(IReadOnlyList<double> initial,
        IReadOnlyList<double> lowerBounds, IReadOnlyList<double> upperBounds,
        Func<IReadOnlyList<double>, CancellationToken, LeastSquaresEvaluation> evaluate,
        LeastSquaresOptions? options = null, CancellationToken cancellationToken = default,
        Func<bool>? stopRequested = null,
        Action<IReadOnlyList<double>, LeastSquaresEvaluation>? acceptedStep = null)
    {
        options ??= new();
        Validate(initial, lowerBounds, upperBounds, options);
        ArgumentNullException.ThrowIfNull(evaluate);
        var vector = initial.ToArray();
        var lower = lowerBounds.ToArray();
        var upper = upperBounds.ToArray();
        var count = 0;
        var differences = 0;
        var builds = 0;
        var models = 0;
        var updates = 0;
        var age = 0;
        double[,]? reusedJacobian = null;
        var factorizations = 0;
        var workUnits = 0L;
        var trials = new List<LeastSquaresTrial>();
        var radius = options.InitialRadius;
        var maximumRadius = options.MaximumRadius;
        var scales = Enumerable.Repeat(1.0, vector.Length).ToArray();
        LeastSquaresTermination? interrupted = null;
        var evaluation = Evaluate(vector, difference: false, expectedRows: null);
        if (evaluation is null) return Finish(interrupted!.Value);
        if (!evaluation.IsValid) return Finish(LeastSquaresTermination.InvalidInitialEvaluation);

        while (true)
        {
            if (!CanEvaluate()) return Finish(interrupted!.Value);
            var reused = reusedJacobian is not null;
            var rawJacobian = reusedJacobian ?? BuildJacobian();
            reusedJacobian = null;
            var jacobian = rawJacobian is null ? null : (double[,])rawJacobian.Clone();
            if (jacobian is null) return Finish(interrupted ?? LeastSquaresTermination.DerivativeUnavailable);
            if (!reused) { builds++; age = 0; }
            models++;
            if (options.ScaleByJacobian)
            {
                for (var column = 0; column < vector.Length; column++)
                {
                    var norm = 0.0;
                    for (var row = 0; row < evaluation.Residuals.Count; row++) norm = double.Hypot(norm, jacobian[row, column]);
                    if (!double.IsFinite(norm)) return Finish(LeastSquaresTermination.NonFiniteModel);
                    scales[column] = models == 1 ? (norm == 0 ? 1 : norm) : Math.Max(scales[column], norm);
                    for (var row = 0; row < evaluation.Residuals.Count; row++) jacobian[row, column] /= scales[column];
                }
                if (models == 1)
                {
                    var size = Math.Max(1, vector.Select((value, column) => Math.Abs(value * scales[column])).Max());
                    radius *= size;
                    maximumRadius *= size;
                    if (!double.IsFinite(maximumRadius)) return Finish(LeastSquaresTermination.NonFiniteModel);
                }
            }
            var gradient = new double[vector.Length];
            for (var column = 0; column < vector.Length; column++)
                for (var row = 0; row < evaluation.Residuals.Count; row++)
                    gradient[column] += jacobian[row, column] * evaluation.Residuals[row];
            if (gradient.Any(value => !double.IsFinite(value))) return Finish(LeastSquaresTermination.NonFiniteModel);
            var free = Enumerable.Range(0, vector.Length).Where(column => lower[column] != upper[column]
                && !(vector[column] - lower[column] <= ActivityTolerance(column) && gradient[column] > 0)
                && !(upper[column] - vector[column] <= ActivityTolerance(column) && gradient[column] < 0)).ToArray();
            if (free.Select(column => Math.Abs(gradient[column])).DefaultIfEmpty().Max() <= options.GradientTolerance)
            {
                if (reused) continue; // Confirm stationarity with fresh derivatives.
                return Finish(LeastSquaresTermination.Stationary);
            }

            var reduced = new double[evaluation.Residuals.Count, free.Length];
            for (var column = 0; column < free.Length; column++)
                for (var row = 0; row < evaluation.Residuals.Count; row++) reduced[row, column] = jacobian[row, free[column]];
            var gaussNewton = new double[vector.Length];
            RegularizedLeastSquaresModel? regularized = null;
            if (options.StepMethod == LeastSquaresStepMethod.Regularized)
                regularized = new(reduced, evaluation.Residuals);
            else
            {
                var solved = PivotedLeastSquares.Solve(reduced, evaluation.Residuals.Select(value => -value).ToArray());
                for (var column = 0; column < free.Length; column++) gaussNewton[free[column]] = solved[column];
            }
            factorizations++;
            if (gaussNewton.Any(value => !double.IsFinite(value))) return Finish(LeastSquaresTermination.NonFiniteModel);

            // Rejections of a freshly differentiated model reuse its full-row QR;
            // an approximate model instead triggers fresh derivatives. Regularization
            // refactors only the small triangular model and charges no function calls.
            while (true)
            {
                if (!CanEvaluate()) return Finish(interrupted!.Value);
                var scaledLower = lower.Select((value, index) => (value - vector[index]) * scales[index]).ToArray();
                var scaledUpper = upper.Select((value, index) => (value - vector[index]) * scales[index]).ToArray();
                var step = regularized is null
                    ? Dogleg(jacobian, gradient, gaussNewton, new double[vector.Length], scaledLower, scaledUpper, free, radius)
                    : RegularizedStep(regularized, jacobian, gradient, scaledLower, scaledUpper, free, radius, ref factorizations);
                var proposed = vector.Select((value, index) => Math.Clamp(value + step[index] / scales[index], lower[index], upper[index])).ToArray();
                if (proposed.Any(value => !double.IsFinite(value))) return Finish(LeastSquaresTermination.NonFiniteModel);
                for (var index = 0; index < step.Length; index++) step[index] = (proposed[index] - vector[index]) * scales[index];
                var stepNorm = regularized is null ? MaxAbs(step) : step.Aggregate(0.0, double.Hypot);
                if (stepNorm <= options.StepTolerance * Math.Max(1, vector.Select((value, index) => Math.Abs(value * scales[index])).Max()))
                {
                    if (reused) break;
                    return Finish(LeastSquaresTermination.StepTooSmall);
                }
                var predicted = PredictedReduction(jacobian, gradient, step);
                if (!double.IsFinite(predicted)) return Finish(LeastSquaresTermination.NonFiniteModel);
                if (predicted <= 0)
                {
                    if (reused) break;
                    return Finish(LeastSquaresTermination.StepTooSmall);
                }
                var trial = Evaluate(proposed, difference: false, evaluation.Residuals.Count);
                if (trial is null) return Finish(interrupted!.Value);
                double? actual = trial.IsValid ? ActualReduction(evaluation.Residuals, trial.Residuals) : null;
                double? ratio = actual / predicted;
                var accepted = ratio is > 0.1 && actual is > 0;
                var nextRadius = radius;
                if (ratio is null or < 0.25) nextRadius = 0.25 * stepNorm;
                else if (ratio > 0.75 && stepNorm >= radius * (1 - 16 * PivotedLeastSquares.MachineEpsilon))
                    nextRadius = Math.Min(maximumRadius, 2 * radius);
                trials.Add(new(models, count, workUnits, Array.AsReadOnly((double[])vector.Clone()),
                    Array.AsReadOnly(proposed), radius, nextRadius, predicted, actual, ratio, accepted));
                radius = nextRadius;
                if (!accepted)
                {
                    // A rejected approximate model is discarded at the same accepted
                    // point. Invalid rays never enter a secant update.
                    if (reused) break;
                    continue;
                }
                if (age < options.MaximumSecantUpdates && ratio is >= .25 and <= 2)
                {
                    reusedJacobian = SecantUpdate(jacobian, scales, step, evaluation.Residuals, trial.Residuals);
                    if (reusedJacobian is not null) { updates++; age++; }
                }
                vector = proposed;
                evaluation = trial;
                acceptedStep?.Invoke(Array.AsReadOnly((double[])vector.Clone()), evaluation);
                break;
            }
        }

        bool CanEvaluate()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stopRequested?.Invoke() == true) interrupted = LeastSquaresTermination.StopRequested;
            else if (count >= options.MaximumEvaluations) interrupted = LeastSquaresTermination.EvaluationLimit;
            return interrupted is null;
        }

        LeastSquaresEvaluation? Evaluate(double[] point, bool difference, int? expectedRows)
        {
            if (!CanEvaluate()) return null;
            count++;
            if (difference) differences++;
            var value = evaluate(Array.AsReadOnly((double[])point.Clone()), cancellationToken);
            if (value.WorkUnits < 0) throw new InvalidOperationException("Evaluation work units cannot be negative.");
            workUnits = checked(workUnits + value.WorkUnits);
            if (!value.IsValid) return new(Array.Empty<double>(), false, value.WorkUnits);
            if (value.Residuals.Count == 0 || expectedRows is { } rows && value.Residuals.Count != rows)
                throw new InvalidOperationException("Valid evaluations must have a nonempty, fixed residual shape throughout one solve.");
            var residuals = value.Residuals.ToArray();
            var cost = residuals.Sum(residual => 0.5 * residual * residual);
            return new(Array.AsReadOnly(residuals), double.IsFinite(cost), value.WorkUnits);
        }

        double[,]? BuildJacobian()
        {
            var jacobian = new double[evaluation.Residuals.Count, vector.Length];
            for (var column = 0; column < vector.Length; column++)
            {
                if (lower[column] == upper[column]) continue;
                var delta = options.RelativeDifferenceStep * Math.Max(1, Math.Abs(vector[column]));
                var available = false;
                for (var attempt = 0; attempt < 4; attempt++, delta *= 0.25)
                {
                    var plus = (double[])vector.Clone();
                    var minus = (double[])vector.Clone();
                    plus[column] = Math.Min(upper[column], vector[column] + delta);
                    minus[column] = Math.Max(lower[column], vector[column] - delta);
                    if (!double.IsFinite(plus[column])) plus[column] = vector[column];
                    if (!double.IsFinite(minus[column])) minus[column] = vector[column];
                    var hp = plus[column] - vector[column];
                    var hm = vector[column] - minus[column];
                    var up = hp > 0 ? Evaluate(plus, difference: true, evaluation.Residuals.Count) : null;
                    if (interrupted is not null) return null;
                    var down = hm > 0 ? Evaluate(minus, difference: true, evaluation.Residuals.Count) : null;
                    if (interrupted is not null) return null;
                    var useUp = up?.IsValid == true;
                    var useDown = down?.IsValid == true;
                    if (!useUp && !useDown) continue;
                    var upperWeight = hp <= hm ? 1 / (1 + hp / hm) : (hm / hp) / (1 + hm / hp);
                    // Unequal distances near a bound require weighted one-sided slopes.
                    for (var row = 0; row < evaluation.Residuals.Count; row++)
                    {
                        var upperSlope = useUp ? (up!.Residuals[row] - evaluation.Residuals[row]) / hp : 0;
                        var lowerSlope = useDown ? (evaluation.Residuals[row] - down!.Residuals[row]) / hm : 0;
                        jacobian[row, column] = useUp && useDown
                            ? upperSlope * upperWeight + lowerSlope * (1 - upperWeight)
                            : useUp ? upperSlope : lowerSlope;
                        if (!double.IsFinite(jacobian[row, column])) { interrupted = LeastSquaresTermination.NonFiniteModel; return null; }
                    }
                    available = true;
                    break;
                }
                if (!available) return null;
            }
            return jacobian;
        }

        LeastSquaresResult Finish(LeastSquaresTermination termination) => new(Array.AsReadOnly((double[])vector.Clone()),
            evaluation, termination, count, differences, builds, factorizations, workUnits, trials.AsReadOnly())
        { SecantUpdateCount = updates };

        // A numerically active coordinate must not truncate every other coordinate
        // to a sub-tolerance step. Use the declared step resolution, capped by the
        // interval width; inward gradients can still release either active bound.
        double ActivityTolerance(int column) => Math.Min(
            options.StepTolerance * Math.Max(1, Math.Abs(vector[column])),
            .25 * (upper[column] - lower[column]));
    }

    // Broyden rank-one correction in the current scaled coordinates. Return an
    // unscaled Jacobian so subsequent scale changes do not change its meaning.
    private static double[,]? SecantUpdate(double[,] scaledJacobian, double[] scales, double[] step,
        IReadOnlyList<double> before, IReadOnlyList<double> after)
    {
        var norm = step.Aggregate(0.0, double.Hypot);
        if (!(norm > 0) || !double.IsFinite(norm)) return null;
        var unit = step.Select(value => value / norm).ToArray();
        var updated = new double[before.Count, step.Length];
        for (var row = 0; row < before.Count; row++)
        {
            var defect = (after[row] - before[row]) / norm;
            for (var column = 0; column < step.Length; column++) defect -= scaledJacobian[row, column] * unit[column];
            for (var column = 0; column < step.Length; column++)
            {
                updated[row, column] = (scaledJacobian[row, column] + defect * unit[column]) * scales[column];
                if (!double.IsFinite(updated[row, column])) return null;
            }
        }
        return updated;
    }

    private static double[] RegularizedStep(RegularizedLeastSquaresModel model, double[,] jacobian,
        double[] gradient, double[] lower, double[] upper, int[] free, double radius, ref int factorizations)
    {
        var solved = model.Step(radius, out var solves);
        factorizations += solves;
        var candidate = new double[gradient.Length];
        for (var column = 0; column < free.Length; column++)
            candidate[free[column]] = Math.Clamp(solved[column], lower[free[column]], upper[free[column]]);
        var cauchy = Dogleg(jacobian, gradient, new double[gradient.Length], new double[gradient.Length], lower, upper, free, radius);
        var norm = cauchy.Aggregate(0.0, double.Hypot);
        if (norm > radius)
            for (var column = 0; column < cauchy.Length; column++) cauchy[column] *= radius / norm;
        return PredictedReduction(jacobian, gradient, candidate) >= PredictedReduction(jacobian, gradient, cauchy) ? candidate : cauchy;
    }

    private static double[] Dogleg(double[,] jacobian, double[] gradient, double[] gaussNewton,
        double[] vector, double[] lower, double[] upper, int[] free, double radius)
    {
        var lo = vector.Select((value, index) => Math.Max(lower[index] - value, -radius)).ToArray();
        var hi = vector.Select((value, index) => Math.Min(upper[index] - value, radius)).ToArray();
        var steepest = new double[vector.Length];
        var gradientNorm = free.Max(column => Math.Abs(gradient[column]));
        foreach (var column in free) steepest[column] = -gradient[column] / gradientNorm;
        var mapped = Multiply(jacobian, steepest);
        var curvature = Dot(mapped, mapped);
        var slope = Dot(gradient, steepest);
        var length = MaximumLength(new double[vector.Length], steepest, lo, hi, double.PositiveInfinity);
        if (curvature > 0) length = Math.Min(length, -slope / curvature);
        var cauchy = steepest.Select(value => value * length).ToArray();
        var towardNewton = gaussNewton.Select((value, index) => value - cauchy[index]).ToArray();
        var fraction = MaximumLength(cauchy, towardNewton, lo, hi, 1);
        var dogleg = cauchy.Select((value, index) => Math.Clamp(value + fraction * towardNewton[index], lo[index], hi[index])).ToArray();
        // QR rank truncation or roundoff must never produce less model decrease than the Cauchy point.
        return PredictedReduction(jacobian, gradient, dogleg) >= PredictedReduction(jacobian, gradient, cauchy) ? dogleg : cauchy;
    }

    private static double MaximumLength(double[] start, double[] direction, double[] lower, double[] upper, double maximum)
    {
        for (var index = 0; index < direction.Length; index++)
        {
            if (direction[index] > 0) maximum = Math.Min(maximum, (upper[index] - start[index]) / direction[index]);
            else if (direction[index] < 0) maximum = Math.Min(maximum, (lower[index] - start[index]) / direction[index]);
        }
        return Math.Max(0, maximum);
    }

    private static double PredictedReduction(double[,] jacobian, double[] gradient, double[] step)
    {
        var mapped = Multiply(jacobian, step);
        return -Dot(gradient, step) - 0.5 * Dot(mapped, mapped);
    }

    private static double ActualReduction(IReadOnlyList<double> current, IReadOnlyList<double> trial)
    {
        var result = 0.0;
        for (var row = 0; row < current.Count; row++) result += 0.5 * (current[row] - trial[row]) * (current[row] + trial[row]);
        return result;
    }

    private static double[] Multiply(double[,] matrix, double[] vector)
    {
        var result = new double[matrix.GetLength(0)];
        for (var row = 0; row < result.Length; row++)
            for (var column = 0; column < vector.Length; column++) result[row] += matrix[row, column] * vector[column];
        return result;
    }

    private static double Dot(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        var result = 0.0;
        for (var index = 0; index < left.Count; index++) result += left[index] * right[index];
        return result;
    }

    private static double MaxAbs(double[] values) => values.Select(Math.Abs).Max();

    private static void Validate(IReadOnlyList<double> initial, IReadOnlyList<double> lower,
        IReadOnlyList<double> upper, LeastSquaresOptions options)
    {
        if (initial.Count == 0 || initial.Count != lower.Count || initial.Count != upper.Count)
            throw new ArgumentException("The variable and bound vectors must have the same nonzero length.");
        for (var index = 0; index < initial.Count; index++)
            if (!double.IsFinite(initial[index]) || double.IsNaN(lower[index]) || double.IsNaN(upper[index])
                || lower[index] > upper[index] || initial[index] < lower[index] || initial[index] > upper[index])
                throw new ArgumentException("Initial variables must be finite and within consistent bounds.");
        if (options.MaximumEvaluations < 1 || !PositiveFinite(options.InitialRadius)
            || !PositiveFinite(options.MaximumRadius) || options.InitialRadius > options.MaximumRadius
            || !double.IsFinite(options.GradientTolerance) || options.GradientTolerance < 0
            || !PositiveFinite(options.StepTolerance) || !PositiveFinite(options.RelativeDifferenceStep)
            || options.RelativeDifferenceStep >= 1 || !Enum.IsDefined(options.StepMethod)
            || options.MaximumSecantUpdates is < 0 or > 32)
            throw new ArgumentException("Invalid least-squares solver options.", nameof(options));
    }

    private static bool PositiveFinite(double value) => double.IsFinite(value) && value > 0;
}
