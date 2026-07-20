using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public interface IOptimizationVariable
{
    string Name { get; }

    double Value { get; set; }

    double LowerBound { get; }

    double UpperBound { get; }

    double StepHint { get; }

    IVariableScaler Scaler { get; }

    double ScaledValue { get; set; }
}

public sealed class DelegateVariable : IOptimizationVariable
{
    private readonly Func<double> _getter;
    private readonly Action<double> _setter;

    public DelegateVariable(
        string name,
        Func<double> getter,
        Action<double> setter,
        double lowerBound,
        double upperBound,
        double? stepHint = null,
        IVariableScaler? scaler = null)
    {
        Name = name;
        _getter = getter;
        _setter = setter;
        LowerBound = lowerBound;
        UpperBound = upperBound;
        Scaler = scaler ?? new LinearScaler();
        StepHint = stepHint is > 0
            ? stepHint.Value
            : Math.Max(1e-6, Math.Abs(upperBound - lowerBound) * 0.05);
    }

    public string Name { get; }

    public double Value
    {
        get => _getter();
        set => _setter(Math.Clamp(value, LowerBound, UpperBound));
    }

    public double LowerBound { get; }

    public double UpperBound { get; }

    public double StepHint { get; }

    public IVariableScaler Scaler { get; }

    public double ScaledValue
    {
        get => Scaler.ToScaled(Value);
        set => Value = Scaler.FromScaled(value);
    }
}

public sealed record Operand(string Name, double Target, double Weight, Func<double> Evaluate)
{
    public double Error() => Evaluate() - Target;

    public double Residual() => Math.Sqrt(Math.Abs(Weight)) * Error();

    public double Squared()
    {
        var residual = Residual();
        return residual * residual;
    }
}

public sealed record OptimizationEvaluation(
    double[] ObjectiveResiduals,
    double[] ConstraintResiduals)
{
    public double Merit => SumSquares(ObjectiveResiduals);

    public double ConstraintError => SumSquares(ConstraintResiduals);

    private static double SumSquares(IEnumerable<double> values)
    {
        var total = 0.0;
        foreach (var value in values)
        {
            total += value * value;
        }

        return total;
    }
}

public interface IVariableScaler
{
    string Name { get; }

    double ToScaled(double value);

    double FromScaled(double value);
}

public sealed class LinearScaler : IVariableScaler
{
    public string Name => "linear";

    public double ToScaled(double value) => value;

    public double FromScaled(double value) => value;
}

public sealed class UnitRangeScaler : IVariableScaler
{
    private readonly double _lowerBound;
    private readonly double _upperBound;

    public UnitRangeScaler(double lowerBound, double upperBound)
    {
        _lowerBound = lowerBound;
        _upperBound = upperBound;
    }

    public string Name => "unit-range";

    public double ToScaled(double value)
    {
        var span = _upperBound - _lowerBound;
        return Math.Abs(span) < 1e-12 ? 0 : (value - _lowerBound) / span;
    }

    public double FromScaled(double value)
    {
        return _lowerBound + (value * (_upperBound - _lowerBound));
    }
}

public sealed class OptimizationProblem
{
    private readonly List<IOptimizationVariable> _variables = new();
    private readonly List<Operand> _operands = new();
    private Func<IReadOnlyList<double>, double[]>? _independentValueEvaluator;

    public IReadOnlyList<IOptimizationVariable> Variables => _variables;

    public IReadOnlyList<Operand> Operands => _operands;

    public bool BatchingEnabled { get; private set; } = true;

    public bool SupportsParallelResidualEvaluation => _independentValueEvaluator is not null;

    public void AddVariable(IOptimizationVariable variable) => _variables.Add(variable);

    public void AddOperand(Operand operand) => _operands.Add(operand);

    public void SetIndependentValueEvaluator(Func<IReadOnlyList<double>, double[]> evaluator)
    {
        _independentValueEvaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
    }

    public void DisableBatching() => BatchingEnabled = false;

    public void EnableBatching() => BatchingEnabled = true;

    public double[] ResidualVector()
    {
        var evaluation = EvaluateCurrent();
        return evaluation.ObjectiveResiduals.Concat(evaluation.ConstraintResiduals).ToArray();
    }

    public double SumSquared()
    {
        var evaluation = EvaluateCurrent();
        return evaluation.Merit + evaluation.ConstraintError;
    }

    public double[] VariableVector() => _variables.Select(variable => variable.Value).ToArray();

    public double[] ScaledVariableVector() => _variables.Select(variable => variable.ScaledValue).ToArray();

    public double[] VariableVectorFromScaled(IReadOnlyList<double> scaledValues)
    {
        var values = new double[Math.Min(scaledValues.Count, _variables.Count)];
        for (var index = 0; index < values.Length; index++)
        {
            var variable = _variables[index];
            values[index] = Math.Clamp(
                variable.Scaler.FromScaled(scaledValues[index]),
                variable.LowerBound,
                variable.UpperBound);
        }

        return values;
    }

    public OptimizationEvaluation EvaluateAtScaled(IReadOnlyList<double> scaledValues)
    {
        if (_independentValueEvaluator is not null)
        {
            return BuildEvaluation(_independentValueEvaluator(VariableVectorFromScaled(scaledValues)));
        }

        var original = ScaledVariableVector();
        try
        {
            SetScaledVariableVector(scaledValues);
            return EvaluateCurrent();
        }
        finally
        {
            SetScaledVariableVector(original);
        }
    }

    private OptimizationEvaluation EvaluateCurrent()
    {
        using var batch = BatchingEnabled ? MeritFunctionCatalog.BeginEvaluationBatch() : null;
        return BuildEvaluation(_operands.Select(operand => operand.Evaluate()).ToArray());
    }

    private OptimizationEvaluation BuildEvaluation(IReadOnlyList<double> values)
    {
        if (values.Count != _operands.Count)
        {
            throw new InvalidOperationException("独立评价器返回的值数量与操作数数量不一致。");
        }

        var weightSum = _operands.Sum(operand => Math.Abs(operand.Weight));
        var objective = new List<double>();
        var constraints = new List<double>();
        for (var index = 0; index < _operands.Count; index++)
        {
            var operand = _operands[index];
            var error = values[index] - operand.Target;
            if (operand.Weight > 0 && weightSum > 0)
            {
                objective.Add(Math.Sqrt(operand.Weight / weightSum) * error);
            }
            else if (operand.Weight < 0 && weightSum > 0)
            {
                constraints.Add(Math.Sqrt(Math.Abs(operand.Weight) / weightSum) * error);
            }
        }

        return new OptimizationEvaluation(objective.ToArray(), constraints.ToArray());
    }

    public void SetVariableVector(IReadOnlyList<double> values)
    {
        for (var index = 0; index < Math.Min(values.Count, _variables.Count); index++)
        {
            _variables[index].Value = values[index];
        }
    }

    public void SetScaledVariableVector(IReadOnlyList<double> values)
    {
        for (var index = 0; index < Math.Min(values.Count, _variables.Count); index++)
        {
            _variables[index].ScaledValue = values[index];
        }
    }
}

public sealed class OptimizerResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public double InitialMerit { get; init; }

    public double FinalMerit { get; init; }

    public int Iterations { get; init; }

    public IReadOnlyList<double> BestVariables { get; init; } = Array.Empty<double>();

    public IReadOnlyList<double> MeritHistory { get; init; } = Array.Empty<double>();
}

public interface IOptimizer
{
    string Name { get; }

    OptimizerResult Optimize(OptimizationProblem problem, int maxIterations = 100);
}

public sealed class OrthogonalDescentOptimizer : IOptimizer
{
    public string Name => "Orthogonal Descent";

    public OptimizerResult Optimize(OptimizationProblem problem, int maxIterations = 100)
    {
        var initial = problem.SumSquared();
        var best = initial;
        var bestVector = problem.VariableVector();
        var stepByVariable = problem.Variables.ToDictionary(
            variable => variable,
            variable => Math.Max(1e-9, variable.StepHint));
        var history = new List<double> { initial };

        var iterations = 0;
        var stagnantIterations = 0;
        for (; iterations < maxIterations; iterations++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var iterationStart = best;
            var improved = false;
            foreach (var variable in problem.Variables)
            {
                var original = variable.Value;
                var step = stepByVariable[variable];
                foreach (var candidate in new[] { original - step, original + step })
                {
                    variable.Value = candidate;
                    var merit = problem.SumSquared();
                    if (merit < best)
                    {
                        best = merit;
                        original = variable.Value;
                        bestVector = problem.VariableVector();
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
                    foreach (var variable in problem.Variables)
                    {
                        stepByVariable[variable] *= 0.5;
                    }
                }

                stagnantIterations++;
                if (stagnantIterations >= 6)
                {
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
        return new OptimizerResult
        {
            Success = best <= initial,
            InitialMerit = initial,
            FinalMerit = best,
            Iterations = iterations,
            BestVariables = bestVector,
            MeritHistory = history,
            Message = $"Optimized with {Name}"
        };
    }
}

public sealed class NamedOptimizer : IOptimizer
{
    private readonly OrthogonalDescentOptimizer _fallback = new();

    public NamedOptimizer(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public OptimizerResult Optimize(OptimizationProblem problem, int maxIterations = 100)
    {
        return _fallback.Optimize(problem, maxIterations).WithMessage($"Optimized with {Name} using orthogonal descent fallback");
    }
}

public static class OptimizerCatalog
{
    public static IReadOnlyList<string> Names { get; } = new[]
    {
        "LM / DLS",
        "Nelder-Mead",
        "Powell",
        "Orthogonal Descent"
    };

    public static IOptimizer Create(string name)
    {
        return name switch
        {
            "LM / DLS" or "Least Squares" => new LeastSquaresOptimizer(),
            "Nelder-Mead" => new NelderMeadOptimizer(),
            "Powell" => new PowellOptimizer(),
            "BFGS" => new GradientOptimizer("BFGS", useMomentum: true),
            "L-BFGS-B" => new GradientOptimizer("L-BFGS-B", useMomentum: true),
            "COBYLA" => new PowellOptimizer("COBYLA"),
            "Orthogonal Descent" => new OrthogonalDescentOptimizer(),
            "Differential Evolution" => new PopulationSearchOptimizer("Differential Evolution"),
            "Dual Annealing" => new PopulationSearchOptimizer("Dual Annealing"),
            "Basin Hopping" => new PopulationSearchOptimizer("Basin Hopping"),
            "Glass Expert" => new NamedOptimizer("Glass Expert"),
            _ => new NamedOptimizer(name)
        };
    }
}

public sealed class GlassExpert
{
    public OptimizerResult Run(OptimizationProblem problem, IReadOnlyList<string> candidateGlasses, int maxIterations = 25)
    {
        return OptimizerCatalog.Create("Orthogonal Descent").Optimize(problem, maxIterations);
    }
}

internal static class OptimizerResultExtensions
{
    public static OptimizerResult WithMessage(this OptimizerResult result, string message)
    {
        return new OptimizerResult
        {
            Success = result.Success,
            InitialMerit = result.InitialMerit,
            FinalMerit = result.FinalMerit,
            Iterations = result.Iterations,
            BestVariables = result.BestVariables,
            MeritHistory = result.MeritHistory,
            Message = message
        };
    }
}
