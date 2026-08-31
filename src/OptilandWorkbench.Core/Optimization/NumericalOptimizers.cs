using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public sealed class DampedLeastSquaresOptimizer : IOptimizer
{
    public string Name => "Damped Least Squares";

    public OptimizerResult Optimize(OptimizationProblem problem, int maxIterations = 100)
    {
        return DampedLeastSquaresSearch.Run(problem, Name, maxIterations);
    }
}

[Obsolete("Use DampedLeastSquaresOptimizer. This type is retained only for source compatibility.")]
public sealed class LeastSquaresOptimizer : IOptimizer
{
    private readonly DampedLeastSquaresOptimizer _inner = new();

    public string Name => _inner.Name;

    public OptimizerResult Optimize(OptimizationProblem problem, int maxIterations = 100) =>
        OptimizationResults.WithWarning(
            _inner.Optimize(problem, maxIterations),
            OptimizerCatalog.CompatibilityWarning("Least Squares", Name));
}

internal static class DampedLeastSquaresSearch
{
    public static OptimizerResult Run(OptimizationProblem problem, string name, int maxIterations)
    {
        ArgumentNullException.ThrowIfNull(problem);
        OptimizationLimits.RequireIterationCount(maxIterations);
        var evaluationStart = problem.FunctionEvaluationCount;
        var current = problem.ScaledVariableVector();
        var evaluation = problem.EvaluateAtScaled(current);
        var initial = ReportedMerit(evaluation);
        var best = initial;
        var bestScaled = current.ToArray();
        var history = new List<double> { initial };
        var lambda = 1e-3;
        var iterations = 0;
        var stagnantIterations = 0;
        var stopReason = "MaximumIterations";
        double? finalGradientNorm = null;

        for (; iterations < maxIterations; iterations++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var jacobian = EstimateJacobian(problem, current, evaluation);
            var gradientNorm = GradientNorm(jacobian.Objective, evaluation.ObjectiveResiduals);
            finalGradientNorm = gradientNorm;
            if ((!double.IsFinite(gradientNorm) || gradientNorm <= 1e-10)
                && evaluation.ConstraintError <= 1e-16)
            {
                stopReason = "GradientTolerance";
                break;
            }

            var accepted = false;
            var iterationStart = best;
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var step = SolveConstrainedStep(jacobian, evaluation, lambda);

                var stepNorm = Math.Sqrt(step.Sum(value => value * value));
                if (stepNorm <= 1e-9 * (1 + Math.Sqrt(current.Sum(value => value * value))))
                {
                    stagnantIterations = 4;
                    break;
                }

                var candidate = current
                    .Select((value, index) => value + step[index])
                    .ToArray();
                var actualCandidate = ToActualScaledVector(problem, candidate);
                var candidateEvaluation = problem.EvaluateAtScaled(actualCandidate);
                var candidateMerit = ReportedMerit(candidateEvaluation);
                if (double.IsFinite(candidateMerit)
                    && Accept(evaluation, candidateEvaluation))
                {
                    current = actualCandidate;
                    evaluation = candidateEvaluation;
                    best = candidateMerit;
                    bestScaled = current.ToArray();
                    lambda = Math.Max(1e-12, lambda / 3);
                    accepted = true;
                    break;
                }

                lambda = Math.Min(1e12, lambda * 10);
            }

            history.Add(best);
            var meaningful = iterationStart - best > 1e-10 * Math.Max(1, Math.Abs(iterationStart));
            stagnantIterations = accepted && meaningful ? 0 : stagnantIterations + 1;
            if (stagnantIterations >= 4)
            {
                stopReason = "StepOrMeritStagnation";
                iterations++;
                break;
            }
        }

        problem.SetScaledVariableVector(bestScaled);
        return OptimizationResults.Create(
            name,
            "damped-least-squares/1",
            stopReason,
            initial,
            best,
            iterations,
            problem.VariableVector(),
            history,
            problem.FunctionEvaluationCount - evaluationStart,
            finalGradientNorm);
    }

    private static Jacobian EstimateJacobian(
        OptimizationProblem problem,
        IReadOnlyList<double> origin,
        OptimizationEvaluation baseline)
    {
        var objectiveColumns = new double[origin.Count][];
        var constraintColumns = new double[origin.Count][];
        void EvaluateColumn(int column)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var scaledStep = AdaptiveScaledStep(problem, origin, column);
            OptimizationEvaluation? perturbedEvaluation = null;
            var actualStep = 0.0;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var perturbed = origin.ToArray();
                perturbed[column] += scaledStep;
                var actualScaled = ToActualScaledVector(problem, perturbed);
                actualStep = actualScaled[column] - origin[column];
                perturbedEvaluation = problem.EvaluateAtScaled(actualScaled);
                ValidateLengths(baseline, perturbedEvaluation);
                if (HasDerivativeSignal(baseline, perturbedEvaluation) || attempt == 2)
                {
                    break;
                }

                scaledStep *= 10;
            }

            objectiveColumns[column] = Derivative(
                baseline.ObjectiveResiduals,
                perturbedEvaluation!.ObjectiveResiduals,
                actualStep);
            constraintColumns[column] = Derivative(
                baseline.ConstraintResiduals,
                perturbedEvaluation.ConstraintResiduals,
                actualStep);
        }

        if (problem.SupportsParallelResidualEvaluation && origin.Count > 1)
        {
            Parallel.For(
                0,
                origin.Count,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Min(origin.Count, Environment.ProcessorCount) },
                column =>
                {
                    using var parallelism = ComputationParallelism.SuppressNestedParallelism();
                    EvaluateColumn(column);
                });
        }
        else
        {
            for (var column = 0; column < origin.Count; column++)
            {
                EvaluateColumn(column);
            }
        }

        var objective = new double[baseline.ObjectiveResiduals.Length, origin.Count];
        var constraints = new double[baseline.ConstraintResiduals.Length, origin.Count];
        for (var column = 0; column < origin.Count; column++)
        {
            for (var row = 0; row < baseline.ObjectiveResiduals.Length; row++)
            {
                objective[row, column] = objectiveColumns[column][row];
            }

            for (var row = 0; row < baseline.ConstraintResiduals.Length; row++)
            {
                constraints[row, column] = constraintColumns[column][row];
            }
        }

        return new Jacobian(objective, constraints);
    }

    private static double[] ToActualScaledVector(OptimizationProblem problem, IReadOnlyList<double> scaled)
    {
        var values = problem.VariableVectorFromScaled(scaled);
        var result = new double[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            result[index] = problem.Variables[index].Scaler.ToScaled(values[index]);
        }

        return result;
    }

    private static double AdaptiveScaledStep(
        OptimizationProblem problem,
        IReadOnlyList<double> origin,
        int column)
    {
        var variable = problem.Variables[column];
        var values = problem.VariableVectorFromScaled(origin);
        var current = values[column];
        var span = Math.Max(1e-12, variable.UpperBound - variable.LowerBound);
        var physicalStep = Math.Clamp(
            Math.Max(
                Math.Sqrt(2.2204460492503131e-16) * (1 + Math.Abs(current)),
                variable.StepHint * 1e-3),
            span * 1e-7,
            span * 1e-2);
        var target = current + physicalStep <= variable.UpperBound
            ? current + physicalStep
            : current - physicalStep;
        var scaledTarget = variable.Scaler.ToScaled(Math.Clamp(
            target,
            variable.LowerBound,
            variable.UpperBound));
        var step = scaledTarget - origin[column];
        return Math.Abs(step) > 1e-12 ? step : -1e-6;
    }

    private static double[] SolveConstrainedStep(
        Jacobian jacobian,
        OptimizationEvaluation evaluation,
        double lambda)
    {
        var variableCount = jacobian.Objective.GetLength(1);
        var objectiveRows = jacobian.Objective.GetLength(0);
        var augmented = new double[objectiveRows + variableCount, variableCount];
        var rightHandSide = new double[objectiveRows + variableCount];
        for (var row = 0; row < objectiveRows; row++)
        {
            rightHandSide[row] = -evaluation.ObjectiveResiduals[row];
            for (var column = 0; column < variableCount; column++)
            {
                augmented[row, column] = jacobian.Objective[row, column];
            }
        }

        for (var column = 0; column < variableCount; column++)
        {
            var normSquared = 0.0;
            for (var row = 0; row < objectiveRows; row++)
            {
                normSquared += jacobian.Objective[row, column] * jacobian.Objective[row, column];
            }

            augmented[objectiveRows + column, column] =
                Math.Sqrt(lambda) * Math.Max(1e-6, Math.Sqrt(normSquared));
        }

        if (evaluation.ConstraintResiduals.Length == 0)
        {
            return SvdLeastSquares(augmented, rightHandSide);
        }

        return ConstrainedLeastSquares(
            augmented,
            rightHandSide,
            jacobian.Constraints,
            evaluation.ConstraintResiduals.Select(value => -value).ToArray());
    }

    private static double[] ConstrainedLeastSquares(
        double[,] objective,
        double[] objectiveTarget,
        double[,] constraints,
        double[] constraintTarget)
    {
        var constraintSvd = FactorSvd(constraints);
        var particular = Solve(constraintSvd, constraintTarget);
        var nullColumns = Enumerable.Range(0, constraintSvd.SingularSquared.Length)
            .Where(index => constraintSvd.SingularSquared[index] <= constraintSvd.Threshold)
            .ToArray();
        if (nullColumns.Length == 0)
        {
            return particular;
        }

        var nullSpace = new double[constraints.GetLength(1), nullColumns.Length];
        for (var column = 0; column < nullColumns.Length; column++)
        {
            for (var row = 0; row < constraints.GetLength(1); row++)
            {
                nullSpace[row, column] = constraintSvd.RightVectors[row, nullColumns[column]];
            }
        }

        var reduced = Multiply(objective, nullSpace);
        var reducedTarget = objectiveTarget.ToArray();
        var objectiveAtParticular = Multiply(objective, particular);
        for (var row = 0; row < reducedTarget.Length; row++)
        {
            reducedTarget[row] -= objectiveAtParticular[row];
        }

        var reducedStep = SvdLeastSquares(reduced, reducedTarget);
        var correction = Multiply(nullSpace, reducedStep);
        return particular.Select((value, index) => value + correction[index]).ToArray();
    }

    private static double[] SvdLeastSquares(double[,] matrix, double[] rightHandSide)
    {
        return Solve(FactorSvd(matrix), rightHandSide);
    }

    private static SvdFactor FactorSvd(double[,] matrix)
    {
        var rows = matrix.GetLength(0);
        var columns = matrix.GetLength(1);
        var rotated = (double[,])matrix.Clone();
        var rightVectors = new double[columns, columns];
        for (var index = 0; index < columns; index++)
        {
            rightVectors[index, index] = 1;
        }

        var maximumSweeps = Math.Max(12, Math.Min(64, columns * 4));
        for (var sweep = 0; sweep < maximumSweeps; sweep++)
        {
            var changed = false;
            for (var left = 0; left < columns - 1; left++)
            {
                for (var right = left + 1; right < columns; right++)
                {
                    var alpha = ColumnDot(rotated, left, left);
                    var beta = ColumnDot(rotated, right, right);
                    var gamma = ColumnDot(rotated, left, right);
                    if (Math.Abs(gamma) <= 1e-14 * Math.Sqrt(Math.Max(0, alpha * beta)))
                    {
                        continue;
                    }

                    var zeta = (beta - alpha) / (2 * gamma);
                    var tangent = Math.CopySign(1, zeta)
                        / (Math.Abs(zeta) + Math.Sqrt(1 + (zeta * zeta)));
                    var cosine = 1 / Math.Sqrt(1 + (tangent * tangent));
                    var sine = cosine * tangent;
                    RotateColumns(rotated, left, right, cosine, sine);
                    RotateColumns(rightVectors, left, right, cosine, sine);
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        var singularSquared = Enumerable.Range(0, columns)
            .Select(column => ColumnDot(rotated, column, column))
            .ToArray();
        var maximum = singularSquared.DefaultIfEmpty(0).Max();
        var relativeTolerance = Math.Max(rows, columns) * 1e-12;
        var threshold = maximum * relativeTolerance * relativeTolerance;
        return new SvdFactor(rotated, rightVectors, singularSquared, threshold);
    }

    private static double[] Solve(SvdFactor factor, IReadOnlyList<double> rightHandSide)
    {
        var columns = factor.RightVectors.GetLength(0);
        var result = new double[columns];
        for (var component = 0; component < columns; component++)
        {
            var sigmaSquared = factor.SingularSquared[component];
            if (sigmaSquared <= factor.Threshold)
            {
                continue;
            }

            var projection = 0.0;
            for (var row = 0; row < rightHandSide.Count; row++)
            {
                projection += factor.Rotated[row, component] * rightHandSide[row];
            }

            var coefficient = projection / sigmaSquared;
            for (var row = 0; row < columns; row++)
            {
                result[row] += factor.RightVectors[row, component] * coefficient;
            }
        }

        return result;
    }

    private static double ColumnDot(double[,] matrix, int left, int right)
    {
        var total = 0.0;
        for (var row = 0; row < matrix.GetLength(0); row++)
        {
            total += matrix[row, left] * matrix[row, right];
        }

        return total;
    }

    private static void RotateColumns(
        double[,] matrix,
        int left,
        int right,
        double cosine,
        double sine)
    {
        for (var row = 0; row < matrix.GetLength(0); row++)
        {
            var leftValue = matrix[row, left];
            var rightValue = matrix[row, right];
            matrix[row, left] = (cosine * leftValue) - (sine * rightValue);
            matrix[row, right] = (sine * leftValue) + (cosine * rightValue);
        }
    }

    private static double[,] Multiply(double[,] left, double[,] right)
    {
        var result = new double[left.GetLength(0), right.GetLength(1)];
        for (var row = 0; row < result.GetLength(0); row++)
        {
            for (var column = 0; column < result.GetLength(1); column++)
            {
                for (var inner = 0; inner < left.GetLength(1); inner++)
                {
                    result[row, column] += left[row, inner] * right[inner, column];
                }
            }
        }

        return result;
    }

    private static double[] Multiply(double[,] matrix, IReadOnlyList<double> vector)
    {
        var result = new double[matrix.GetLength(0)];
        for (var row = 0; row < result.Length; row++)
        {
            for (var column = 0; column < matrix.GetLength(1); column++)
            {
                result[row] += matrix[row, column] * vector[column];
            }
        }

        return result;
    }

    private static double GradientNorm(double[,] jacobian, IReadOnlyList<double> residuals)
    {
        var maximum = 0.0;
        for (var column = 0; column < jacobian.GetLength(1); column++)
        {
            var value = 0.0;
            for (var row = 0; row < jacobian.GetLength(0); row++)
            {
                value += jacobian[row, column] * residuals[row];
            }

            maximum = Math.Max(maximum, Math.Abs(value));
        }

        return maximum;
    }

    private static bool Accept(OptimizationEvaluation current, OptimizationEvaluation candidate)
    {
        if (current.ConstraintResiduals.Length == 0)
        {
            return candidate.Merit < current.Merit;
        }

        var constraintTolerance = 1e-16;
        return candidate.ConstraintError < current.ConstraintError * (1 - 1e-6)
            || (candidate.ConstraintError <= Math.Max(constraintTolerance, current.ConstraintError * 1.001)
                && candidate.Merit < current.Merit);
    }

    private static double ReportedMerit(OptimizationEvaluation evaluation)
    {
        return evaluation.Merit + evaluation.ConstraintError;
    }

    private static double[] Derivative(
        IReadOnlyList<double> baseline,
        IReadOnlyList<double> perturbed,
        double step)
    {
        var result = new double[baseline.Count];
        if (Math.Abs(step) <= 1e-15)
        {
            return result;
        }

        for (var index = 0; index < result.Length; index++)
        {
            result[index] = (perturbed[index] - baseline[index]) / step;
        }

        return result;
    }

    private static bool HasDerivativeSignal(
        OptimizationEvaluation baseline,
        OptimizationEvaluation perturbed)
    {
        return baseline.ObjectiveResiduals.Where((value, index) => value != perturbed.ObjectiveResiduals[index]).Any()
            || baseline.ConstraintResiduals.Where((value, index) => value != perturbed.ConstraintResiduals[index]).Any();
    }

    private static void ValidateLengths(
        OptimizationEvaluation baseline,
        OptimizationEvaluation perturbed)
    {
        if (baseline.ObjectiveResiduals.Length != perturbed.ObjectiveResiduals.Length
            || baseline.ConstraintResiduals.Length != perturbed.ConstraintResiduals.Length)
        {
            throw new InvalidOperationException("并行评价返回了不同长度的目标或约束向量。");
        }
    }

    private sealed record Jacobian(double[,] Objective, double[,] Constraints);

    private sealed record SvdFactor(
        double[,] Rotated,
        double[,] RightVectors,
        double[] SingularSquared,
        double Threshold);
}

public sealed class MomentumGradientDescentOptimizer : IOptimizer
{
    public string Name => "Momentum Gradient Descent";

    public OptimizerResult Optimize(OptimizationProblem problem, int maxIterations = 100)
    {
        return GradientSearch.Run(problem, Name, maxIterations, useMomentum: true);
    }
}

[Obsolete("Use MomentumGradientDescentOptimizer. This type is retained only for source compatibility.")]
public sealed class GradientOptimizer : IOptimizer
{
    private readonly string _legacyName;
    private readonly bool _useMomentum;

    public GradientOptimizer(string name, bool useMomentum)
    {
        _legacyName = name;
        _useMomentum = useMomentum;
    }

    public string Name => _useMomentum ? "Momentum Gradient Descent" : "Gradient Descent";

    public OptimizerResult Optimize(OptimizationProblem problem, int maxIterations = 100) =>
        OptimizationResults.WithWarning(
            GradientSearch.Run(problem, Name, maxIterations, _useMomentum),
            OptimizerCatalog.CompatibilityWarning(_legacyName, Name));
}

public sealed class CoordinatePatternSearchOptimizer : IOptimizer
{
    public string Name => "Coordinate Pattern Search";

    public OptimizerResult Optimize(OptimizationProblem problem, int maxIterations = 100)
    {
        ArgumentNullException.ThrowIfNull(problem);
        OptimizationLimits.RequireIterationCount(maxIterations);
        var evaluationStart = problem.FunctionEvaluationCount;
        var initial = problem.SumSquared();
        var best = initial;
        var bestVector = problem.VariableVector();
        var stepByVariable = problem.Variables.Select(variable => Math.Max(1e-9, variable.StepHint)).ToArray();
        var history = new List<double> { initial };

        var iterations = 0;
        var stagnantIterations = 0;
        var stopReason = "MaximumIterations";
        for (; iterations < maxIterations; iterations++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var iterationStart = best;
            var improved = false;
            for (var variableIndex = 0; variableIndex < problem.Variables.Count; variableIndex++)
            {
                var variable = problem.Variables[variableIndex];
                var original = variable.Value;
                var step = stepByVariable[variableIndex];
                var candidates = new[] { original - step, original + step, original - (2 * step), original + (2 * step) };
                foreach (var candidate in candidates)
                {
                    variable.Value = candidate;
                    var merit = problem.SumSquared();
                    if (merit < best)
                    {
                        best = merit;
                        bestVector = problem.VariableVector();
                        original = variable.Value;
                        improved = true;
                    }
                }

                variable.Value = original;
            }

            var meaningfulImprovement = iterationStart - best
                > 1e-9 * Math.Max(1, Math.Abs(iterationStart));
            if (!improved || !meaningfulImprovement)
            {
                if (!improved)
                {
                    for (var index = 0; index < stepByVariable.Length; index++)
                    {
                        stepByVariable[index] *= 0.5;
                    }
                }

                stagnantIterations++;
                if (stagnantIterations >= 6)
                {
                    stopReason = "StepOrMeritStagnation";
                    iterations++;
                    break;
                }
            }
            else
            {
                stagnantIterations = 0;
            }

            history.Add(best);
        }

        problem.SetVariableVector(bestVector);
        return OptimizationResults.Create(
            Name,
            "coordinate-pattern-search/1",
            stopReason,
            initial,
            best,
            iterations,
            bestVector,
            history,
            problem.FunctionEvaluationCount - evaluationStart);
    }
}

[Obsolete("Use CoordinatePatternSearchOptimizer. This type is retained only for source compatibility.")]
public sealed class PowellOptimizer : IOptimizer
{
    private readonly string _legacyName;
    private readonly CoordinatePatternSearchOptimizer _inner = new();

    public PowellOptimizer(string name = "Powell")
    {
        _legacyName = name;
    }

    public string Name => _inner.Name;

    public OptimizerResult Optimize(OptimizationProblem problem, int maxIterations = 100) =>
        OptimizationResults.WithWarning(
            _inner.Optimize(problem, maxIterations),
            OptimizerCatalog.CompatibilityWarning(_legacyName, Name));
}

public sealed class NelderMeadOptimizer : IOptimizer
{
    public string Name => "Nelder-Mead";

    public OptimizerResult Optimize(OptimizationProblem problem, int maxIterations = 100)
    {
        ArgumentNullException.ThrowIfNull(problem);
        OptimizationLimits.RequireIterationCount(maxIterations);
        var evaluationStart = problem.FunctionEvaluationCount;
        var dimension = problem.Variables.Count;
        var initial = problem.SumSquared();
        if (dimension == 0)
        {
            return OptimizationResults.Create(
                Name,
                "nelder-mead/1",
                "NoVariables",
                initial,
                initial,
                0,
                Array.Empty<double>(),
                new[] { initial },
                problem.FunctionEvaluationCount - evaluationStart);
        }

        var simplex = new List<double[]> { problem.VariableVector() };
        for (var axis = 0; axis < dimension; axis++)
        {
            var vertex = problem.VariableVector();
            vertex[axis] += Math.Max(1e-9, problem.Variables[axis].StepHint);
            simplex.Add(vertex);
        }

        var values = simplex.Select(vertex => Evaluate(problem, vertex)).ToList();
        var history = new List<double> { values.Min() };
        var iterations = 0;
        var stagnantIterations = 0;
        var previousBest = values.Min();
        var stopReason = "MaximumIterations";

        for (; iterations < maxIterations; iterations++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var order = values
                .Select((value, index) => (value, index))
                .OrderBy(item => item.value)
                .Select(item => item.index)
                .ToArray();
            simplex = order.Select(index => simplex[index]).ToList();
            values = order.Select(index => values[index]).ToList();

            var best = simplex[0];
            var worst = simplex[^1];
            var centroid = new double[dimension];
            for (var vertexIndex = 0; vertexIndex < dimension; vertexIndex++)
            {
                for (var axis = 0; axis < dimension; axis++)
                {
                    centroid[axis] += simplex[vertexIndex][axis] / dimension;
                }
            }

            var reflected = Combine(centroid, worst, 1.0);
            var reflectedValue = Evaluate(problem, reflected);
            if (reflectedValue < values[0])
            {
                var expanded = Combine(centroid, worst, 2.0);
                var expandedValue = Evaluate(problem, expanded);
                simplex[^1] = expandedValue < reflectedValue ? expanded : reflected;
                values[^1] = Math.Min(expandedValue, reflectedValue);
            }
            else if (reflectedValue < values[^2])
            {
                simplex[^1] = reflected;
                values[^1] = reflectedValue;
            }
            else
            {
                var contracted = Combine(centroid, worst, 0.5);
                var contractedValue = Evaluate(problem, contracted);
                if (contractedValue < values[^1])
                {
                    simplex[^1] = contracted;
                    values[^1] = contractedValue;
                }
                else
                {
                    for (var vertexIndex = 1; vertexIndex < simplex.Count; vertexIndex++)
                    {
                        simplex[vertexIndex] = Shrink(best, simplex[vertexIndex]);
                        values[vertexIndex] = Evaluate(problem, simplex[vertexIndex]);
                    }
                }
            }

            history.Add(values.Min());
            var currentBest = values.Min();
            var tolerance = 1e-10 * Math.Max(1, Math.Abs(previousBest));
            if (previousBest - currentBest <= tolerance)
            {
                stagnantIterations++;
                if (stagnantIterations >= 8)
                {
                    stopReason = "MeritStagnation";
                    iterations++;
                    break;
                }
            }
            else
            {
                stagnantIterations = 0;
            }

            previousBest = currentBest;
        }

        var bestIndex = values.IndexOf(values.Min());
        var bestVector = simplex[bestIndex];
        var final = Evaluate(problem, bestVector);
        problem.SetVariableVector(bestVector);
        return OptimizationResults.Create(
            Name,
            "nelder-mead/1",
            stopReason,
            initial,
            final,
            iterations,
            problem.VariableVector(),
            history,
            problem.FunctionEvaluationCount - evaluationStart);
    }

    private static double Evaluate(OptimizationProblem problem, IReadOnlyList<double> vector)
    {
        problem.SetVariableVector(vector);
        return problem.SumSquared();
    }

    private static double[] Combine(IReadOnlyList<double> centroid, IReadOnlyList<double> worst, double factor)
    {
        var result = new double[centroid.Count];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = centroid[index] + (factor * (centroid[index] - worst[index]));
        }

        return result;
    }

    private static double[] Shrink(IReadOnlyList<double> best, IReadOnlyList<double> vertex)
    {
        var result = new double[best.Count];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = best[index] + (0.5 * (vertex[index] - best[index]));
        }

        return result;
    }
}

public sealed class GreedyRandomPerturbationOptimizer : IOptimizer
{
    private readonly int _randomSeed;

    public GreedyRandomPerturbationOptimizer(int randomSeed = 12345)
    {
        _randomSeed = randomSeed;
    }

    public string Name => "Greedy Random Perturbation";

    public OptimizerResult Optimize(OptimizationProblem problem, int maxIterations = 100)
    {
        ArgumentNullException.ThrowIfNull(problem);
        OptimizationLimits.RequireIterationCount(maxIterations);
        var evaluationStart = problem.FunctionEvaluationCount;
        var random = new Random(_randomSeed);
        var initial = problem.SumSquared();
        var best = initial;
        var bestVector = problem.VariableVector();
        var history = new List<double> { initial };

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var temperature = 1.0 - ((double)iteration / Math.Max(1, maxIterations));
            for (var index = 0; index < problem.Variables.Count; index++)
            {
                var variable = problem.Variables[index];
                var span = Math.Max(1e-9, variable.UpperBound - variable.LowerBound);
                var candidate = bestVector.ToArray();
                candidate[index] += (random.NextDouble() - 0.5) * span * Math.Max(0.02, temperature);
                problem.SetVariableVector(candidate);
                var merit = problem.SumSquared();
                if (merit < best)
                {
                    best = merit;
                    bestVector = problem.VariableVector();
                }
            }

            history.Add(best);
        }

        problem.SetVariableVector(bestVector);
        return OptimizationResults.Create(
            Name,
            "greedy-random-perturbation/1",
            "MaximumIterations",
            initial,
            best,
            maxIterations,
            bestVector,
            history,
            problem.FunctionEvaluationCount - evaluationStart,
            randomSeed: _randomSeed);
    }
}

[Obsolete("Use GreedyRandomPerturbationOptimizer. This type is retained only for source compatibility.")]
public sealed class PopulationSearchOptimizer : IOptimizer
{
    private readonly string _legacyName;
    private readonly GreedyRandomPerturbationOptimizer _inner = new();

    public PopulationSearchOptimizer(string name)
    {
        _legacyName = name;
    }

    public string Name => _inner.Name;

    public OptimizerResult Optimize(OptimizationProblem problem, int maxIterations = 100) =>
        OptimizationResults.WithWarning(
            _inner.Optimize(problem, maxIterations),
            OptimizerCatalog.CompatibilityWarning(_legacyName, Name));
}

internal static class GradientSearch
{
    public static OptimizerResult Run(OptimizationProblem problem, string name, int maxIterations, bool useMomentum)
    {
        ArgumentNullException.ThrowIfNull(problem);
        OptimizationLimits.RequireIterationCount(maxIterations);
        var evaluationStart = problem.FunctionEvaluationCount;
        var initial = problem.SumSquared();
        var best = initial;
        var bestVector = problem.ScaledVariableVector();
        var history = new List<double> { initial };
        var velocity = new double[problem.Variables.Count];
        var learningRate = 0.2;
        var iterations = 0;
        var stagnantIterations = 0;
        var stopReason = "MaximumIterations";
        double? finalGradientNorm = null;

        for (; iterations < maxIterations; iterations++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            problem.SetScaledVariableVector(bestVector);
            var current = problem.ScaledVariableVector();
            var gradient = EstimateGradient(problem, current, best);
            var gradientNorm = Math.Sqrt(gradient.Sum(component => component * component));
            finalGradientNorm = gradientNorm;
            if (!double.IsFinite(gradientNorm) || gradientNorm <= 1e-10)
            {
                stopReason = "GradientTolerance";
                break;
            }

            var direction = new double[current.Length];
            for (var index = 0; index < current.Length; index++)
            {
                var descent = -gradient[index] / gradientNorm;
                velocity[index] = useMomentum ? (0.8 * velocity[index]) + (0.2 * descent) : descent;
                direction[index] = velocity[index];
            }

            var accepted = false;
            var localRate = learningRate;

            for (var attempt = 0; attempt < 6; attempt++)
            {
                var candidate = new double[current.Length];
                for (var index = 0; index < current.Length; index++)
                {
                    candidate[index] = current[index] + (localRate * direction[index]);
                }

                problem.SetScaledVariableVector(candidate);
                var merit = problem.SumSquared();
                if (merit < best)
                {
                    var improvement = best - merit;
                    best = merit;
                    bestVector = problem.ScaledVariableVector();
                    accepted = true;
                    stagnantIterations = improvement <= 1e-10 * Math.Max(1, Math.Abs(best))
                        ? stagnantIterations + 1
                        : 0;
                    break;
                }

                localRate *= 0.5;
            }

            if (!accepted)
            {
                problem.SetScaledVariableVector(bestVector);
                learningRate *= 0.5;
                stagnantIterations++;
            }

            history.Add(best);
            if (stagnantIterations >= 4 || learningRate <= 1e-6)
            {
                stopReason = "StepOrMeritStagnation";
                iterations++;
                break;
            }
        }

        problem.SetScaledVariableVector(bestVector);
        return OptimizationResults.Create(
            name,
            useMomentum ? "momentum-gradient-descent/1" : "gradient-descent/1",
            stopReason,
            initial,
            best,
            iterations,
            problem.VariableVector(),
            history,
            problem.FunctionEvaluationCount - evaluationStart,
            finalGradientNorm);
    }

    private static double[] EstimateGradient(
        OptimizationProblem problem,
        IReadOnlyList<double> origin,
        double originMerit)
    {
        var gradient = new double[origin.Count];
        for (var index = 0; index < origin.Count; index++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            const double step = 1e-4;
            var candidate = origin.ToArray();
            var direction = origin[index] >= 1 ? -1 : 1;
            candidate[index] += direction * step;
            problem.SetScaledVariableVector(candidate);
            var actual = problem.ScaledVariableVector()[index] - origin[index];
            gradient[index] = Math.Abs(actual) <= 1e-12
                ? 0
                : (problem.SumSquared() - originMerit) / actual;
        }

        problem.SetScaledVariableVector(origin);
        return gradient;
    }
}

internal static class OptimizationResults
{
    public static OptimizerResult Create(
        string name,
        string algorithmVersion,
        string stopReason,
        double initial,
        double final,
        int iterations,
        IReadOnlyList<double> bestVector,
        IReadOnlyList<double> history,
        long functionEvaluations,
        double? gradientNorm = null,
        int? randomSeed = null,
        IReadOnlyList<string>? warnings = null)
    {
        return new OptimizerResult
        {
            Success = final <= initial,
            InitialMerit = initial,
            FinalMerit = final,
            Iterations = iterations,
            BestVariables = bestVector.ToArray(),
            MeritHistory = history.ToArray(),
            Message = $"Optimized with {name}",
            Algorithm = name,
            AlgorithmVersion = algorithmVersion,
            StopReason = stopReason,
            GradientNorm = gradientNorm,
            FunctionEvaluations = functionEvaluations,
            RandomSeed = randomSeed,
            Warnings = warnings?.ToArray() ?? Array.Empty<string>()
        };
    }

    public static OptimizerResult WithWarning(OptimizerResult source, string warning)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(warning);
        return new OptimizerResult
        {
            Success = source.Success,
            Message = source.Message,
            InitialMerit = source.InitialMerit,
            FinalMerit = source.FinalMerit,
            Iterations = source.Iterations,
            BestVariables = source.BestVariables,
            MeritHistory = source.MeritHistory,
            Algorithm = source.Algorithm,
            AlgorithmVersion = source.AlgorithmVersion,
            StopReason = source.StopReason,
            GradientNorm = source.GradientNorm,
            FunctionEvaluations = source.FunctionEvaluations,
            RandomSeed = source.RandomSeed,
            Warnings = source.Warnings.Append(warning).ToArray()
        };
    }
}
