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
    private long _functionEvaluationCount;

    public IReadOnlyList<IOptimizationVariable> Variables => _variables;

    public IReadOnlyList<Operand> Operands => _operands;

    public bool BatchingEnabled { get; private set; } = true;

    public bool SupportsParallelResidualEvaluation => _independentValueEvaluator is not null;

    public long FunctionEvaluationCount => Interlocked.Read(ref _functionEvaluationCount);

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
            Interlocked.Increment(ref _functionEvaluationCount);
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
        Interlocked.Increment(ref _functionEvaluationCount);
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

    public string Algorithm { get; init; } = string.Empty;

    public string AlgorithmVersion { get; init; } = string.Empty;

    public string StopReason { get; init; } = string.Empty;

    public double? GradientNorm { get; init; }

    public long FunctionEvaluations { get; init; }

    public int? RandomSeed { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public interface IOptimizer
{
    string Name { get; }

    OptimizerResult Optimize(OptimizationProblem problem, int maxIterations = 100);
}

[Obsolete("Use CoordinatePatternSearchOptimizer. This type is retained only for source compatibility.")]
public sealed class OrthogonalDescentOptimizer : IOptimizer
{
    private readonly CoordinatePatternSearchOptimizer _inner = new();

    public string Name => _inner.Name;

    public OptimizerResult Optimize(OptimizationProblem problem, int maxIterations = 100)
    {
        return OptimizationResults.WithWarning(
            _inner.Optimize(problem, maxIterations),
            OptimizerCatalog.CompatibilityWarning("Orthogonal Descent", Name));
    }
}

public static class OptimizerCatalog
{
    public static IReadOnlyList<string> Names { get; } = new[]
    {
        "Damped Least Squares",
        "Nelder-Mead",
        "Coordinate Pattern Search",
        "Momentum Gradient Descent",
        "Greedy Random Perturbation"
    };

    public static IOptimizer Create(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return name switch
        {
            "Damped Least Squares" => new DampedLeastSquaresOptimizer(),
            "Nelder-Mead" => new NelderMeadOptimizer(),
            "Coordinate Pattern Search" => new CoordinatePatternSearchOptimizer(),
            "Momentum Gradient Descent" => new MomentumGradientDescentOptimizer(),
            "Greedy Random Perturbation" => new GreedyRandomPerturbationOptimizer(),
            "LM / DLS" or "Least Squares" => Alias(name, new DampedLeastSquaresOptimizer()),
            "Orthogonal Descent" => Alias(name, new CoordinatePatternSearchOptimizer()),
            "Powell" or "COBYLA" or "BFGS" or "L-BFGS-B"
                or "Differential Evolution" or "Dual Annealing" or "Basin Hopping" =>
                throw new NotSupportedException(
                    $"Optimizer '{name}' is not implemented. Choose one of the canonical optimizer names."),
            "Glass Expert" => throw new NotSupportedException("Glass Expert is not implemented."),
            _ => throw new ArgumentException($"Unknown optimizer '{name}'.", nameof(name))
        };
    }

    internal static string CompatibilityWarning(string alias, string canonicalName) =>
        $"兼容名称“{alias}”不是当前实现的真实算法名称；本次实际执行“{canonicalName}”。请更新调用。";

    private static IOptimizer Alias(string alias, IOptimizer optimizer) =>
        new CompatibilityAliasOptimizer(alias, optimizer);

    private sealed class CompatibilityAliasOptimizer : IOptimizer
    {
        private readonly string _alias;
        private readonly IOptimizer _optimizer;

        public CompatibilityAliasOptimizer(string alias, IOptimizer optimizer)
        {
            _alias = alias;
            _optimizer = optimizer;
        }

        public string Name => _optimizer.Name;

        public OptimizerResult Optimize(OptimizationProblem problem, int maxIterations = 100) =>
            OptimizationResults.WithWarning(
                _optimizer.Optimize(problem, maxIterations),
                CompatibilityWarning(_alias, _optimizer.Name));
    }
}
