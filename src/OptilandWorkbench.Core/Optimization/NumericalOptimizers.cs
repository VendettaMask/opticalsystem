using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public sealed class LeastSquaresOptimizer : IOptimizer
{
    public string Name => "Least Squares";

    public OptimizerResult Optimize(OptimizationProblem problem, int maxIterations = 100)
    {
        return GradientSearch.Run(problem, Name, maxIterations, useMomentum: false);
    }
}

public sealed class GradientOptimizer : IOptimizer
{
    private readonly bool _useMomentum;

    public GradientOptimizer(string name, bool useMomentum)
    {
        Name = name;
        _useMomentum = useMomentum;
    }

    public string Name { get; }

    public OptimizerResult Optimize(OptimizationProblem problem, int maxIterations = 100)
    {
        return GradientSearch.Run(problem, Name, maxIterations, _useMomentum);
    }
}

public sealed class PowellOptimizer : IOptimizer
{
    public PowellOptimizer(string name = "Powell")
    {
        Name = name;
    }

    public string Name { get; }

    public OptimizerResult Optimize(OptimizationProblem problem, int maxIterations = 100)
    {
        var initial = problem.SumSquared();
        var best = initial;
        var bestVector = problem.VariableVector();
        var stepByVariable = problem.Variables.Select(variable => Math.Max(1e-9, variable.StepHint)).ToArray();
        var history = new List<double> { initial };

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
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

            if (!improved)
            {
                for (var index = 0; index < stepByVariable.Length; index++)
                {
                    stepByVariable[index] *= 0.5;
                }
            }

            history.Add(best);
        }

        problem.SetVariableVector(bestVector);
        return OptimizationResults.Create(Name, initial, best, maxIterations, bestVector, history);
    }
}

public sealed class NelderMeadOptimizer : IOptimizer
{
    public string Name => "Nelder-Mead";

    public OptimizerResult Optimize(OptimizationProblem problem, int maxIterations = 100)
    {
        var dimension = problem.Variables.Count;
        var initial = problem.SumSquared();
        if (dimension == 0)
        {
            return OptimizationResults.Create(Name, initial, initial, 0, Array.Empty<double>(), new[] { initial });
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
        }

        var bestIndex = values.IndexOf(values.Min());
        var bestVector = simplex[bestIndex];
        var final = Evaluate(problem, bestVector);
        problem.SetVariableVector(bestVector);
        return OptimizationResults.Create(Name, initial, final, iterations, problem.VariableVector(), history);
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

public sealed class PopulationSearchOptimizer : IOptimizer
{
    public PopulationSearchOptimizer(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public OptimizerResult Optimize(OptimizationProblem problem, int maxIterations = 100)
    {
        var random = new Random(12345);
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
        return OptimizationResults.Create(Name, initial, best, maxIterations, bestVector, history);
    }
}

internal static class GradientSearch
{
    public static OptimizerResult Run(OptimizationProblem problem, string name, int maxIterations, bool useMomentum)
    {
        var initial = problem.SumSquared();
        var best = initial;
        var bestVector = problem.VariableVector();
        var history = new List<double> { initial };
        var velocity = new double[problem.Variables.Count];
        var learningRate = 0.2;

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var current = problem.VariableVector();
            var gradient = EstimateGradient(problem);
            var accepted = false;
            var localRate = learningRate;

            for (var attempt = 0; attempt < 12; attempt++)
            {
                var candidate = new double[current.Length];
                for (var index = 0; index < current.Length; index++)
                {
                    var direction = -gradient[index];
                    velocity[index] = useMomentum ? (0.8 * velocity[index]) + (0.2 * direction) : direction;
                    candidate[index] = current[index] + (localRate * velocity[index]);
                }

                problem.SetVariableVector(candidate);
                var merit = problem.SumSquared();
                if (merit < best)
                {
                    best = merit;
                    bestVector = problem.VariableVector();
                    accepted = true;
                    break;
                }

                localRate *= 0.5;
            }

            if (!accepted)
            {
                problem.SetVariableVector(bestVector);
                learningRate *= 0.5;
            }

            history.Add(best);
        }

        problem.SetVariableVector(bestVector);
        return OptimizationResults.Create(name, initial, best, maxIterations, bestVector, history);
    }

    private static double[] EstimateGradient(OptimizationProblem problem)
    {
        var origin = problem.VariableVector();
        var gradient = new double[origin.Length];
        for (var index = 0; index < origin.Length; index++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var step = Math.Max(1e-6, problem.Variables[index].StepHint * 1e-3);
            var plus = origin.ToArray();
            plus[index] += step;
            problem.SetVariableVector(plus);
            var plusMerit = problem.SumSquared();

            var minus = origin.ToArray();
            minus[index] -= step;
            problem.SetVariableVector(minus);
            var minusMerit = problem.SumSquared();

            gradient[index] = (plusMerit - minusMerit) / (2 * step);
        }

        problem.SetVariableVector(origin);
        return gradient;
    }
}

internal static class OptimizationResults
{
    public static OptimizerResult Create(
        string name,
        double initial,
        double final,
        int iterations,
        IReadOnlyList<double> bestVector,
        IReadOnlyList<double> history)
    {
        return new OptimizerResult
        {
            Success = final <= initial,
            InitialMerit = initial,
            FinalMerit = final,
            Iterations = iterations,
            BestVariables = bestVector.ToArray(),
            MeritHistory = history.ToArray(),
            Message = $"Optimized with {name}"
        };
    }
}
