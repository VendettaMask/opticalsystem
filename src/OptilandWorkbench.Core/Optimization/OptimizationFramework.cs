using System.Runtime.ExceptionServices;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static class OptimizationLimits
{
    public const int MinimumIterations = 1;
    public const int MaximumIterations = 1_000;

    public static int RequireIterationCount(int value, string parameterName = "maxIterations")
    {
        if (value is < MinimumIterations or > MaximumIterations)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"Optimization iteration count must be between {MinimumIterations} and {MaximumIterations}.");
        }

        return value;
    }
}

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

internal static class OptimizationGuards
{
    public static string RequireName(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Optimization names cannot be empty.", parameterName);
        }

        return value.Trim();
    }

    public static double RequireFiniteArgument(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Optimization numeric inputs must be finite.");
        }

        return value;
    }

    public static double RequireFiniteState(double value, string description)
    {
        if (!double.IsFinite(value))
        {
            throw new InvalidOperationException($"{description} must be finite.");
        }

        return value;
    }
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
        Name = OptimizationGuards.RequireName(name, nameof(name));
        _getter = getter ?? throw new ArgumentNullException(nameof(getter));
        _setter = setter ?? throw new ArgumentNullException(nameof(setter));
        LowerBound = OptimizationGuards.RequireFiniteArgument(lowerBound, nameof(lowerBound));
        UpperBound = OptimizationGuards.RequireFiniteArgument(upperBound, nameof(upperBound));
        if (LowerBound > UpperBound)
        {
            throw new ArgumentOutOfRangeException(nameof(lowerBound), "Variable lower bound must not exceed upper bound.");
        }

        Scaler = scaler ?? new LinearScaler();
        if (stepHint is { } explicitStep
            && (!double.IsFinite(explicitStep) || explicitStep <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(stepHint), "Variable step hint must be finite and positive.");
        }

        StepHint = stepHint is > 0
            ? stepHint.Value
            : Math.Max(1e-6, Math.Abs(upperBound - lowerBound) * 0.05);
        if (!double.IsFinite(StepHint) || StepHint <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepHint), "Variable step hint must be finite and positive.");
        }

        var current = OptimizationGuards.RequireFiniteState(
            _getter(),
            $"Optimization variable '{Name}' current value");
        var scaled = Scaler.ToScaled(Math.Clamp(current, LowerBound, UpperBound));
        OptimizationGuards.RequireFiniteState(
            scaled,
            $"Optimization variable '{Name}' scaled value");
        OptimizationGuards.RequireFiniteState(
            Scaler.FromScaled(scaled),
            $"Optimization variable '{Name}' inverse scaled value");
    }

    public string Name { get; }

    public double Value
    {
        get => OptimizationGuards.RequireFiniteState(
            _getter(),
            $"Optimization variable '{Name}' current value");
        set
        {
            OptimizationGuards.RequireFiniteArgument(value, nameof(value));
            _setter(Math.Clamp(value, LowerBound, UpperBound));
            OptimizationGuards.RequireFiniteState(
                _getter(),
                $"Optimization variable '{Name}' current value");
        }
    }

    public double LowerBound { get; }

    public double UpperBound { get; }

    public double StepHint { get; }

    public IVariableScaler Scaler { get; }

    public double ScaledValue
    {
        get => OptimizationGuards.RequireFiniteState(
            Scaler.ToScaled(Value),
            $"Optimization variable '{Name}' scaled value");
        set
        {
            OptimizationGuards.RequireFiniteArgument(value, nameof(value));
            Value = OptimizationGuards.RequireFiniteState(
                Scaler.FromScaled(value),
                $"Optimization variable '{Name}' inverse scaled value");
        }
    }
}

public sealed record Operand(string Name, double Target, double Weight, Func<double> Evaluate)
{
    public string Name { get; init; } = OptimizationGuards.RequireName(Name, nameof(Name));

    public double Target { get; init; } = OptimizationGuards.RequireFiniteArgument(Target, nameof(Target));

    public double Weight { get; init; } = OptimizationGuards.RequireFiniteArgument(Weight, nameof(Weight));

    public Func<double> Evaluate { get; init; } =
        Evaluate ?? throw new ArgumentNullException(nameof(Evaluate));

    public double Error()
    {
        var value = OptimizationGuards.RequireFiniteState(
            Evaluate(),
            $"Optimization operand '{Name}' value");
        return value - Target;
    }

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
            OptimizationGuards.RequireFiniteState(value, "Optimization residual");
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

    public double ToScaled(double value) =>
        OptimizationGuards.RequireFiniteArgument(value, nameof(value));

    public double FromScaled(double value) =>
        OptimizationGuards.RequireFiniteArgument(value, nameof(value));
}

public sealed class UnitRangeScaler : IVariableScaler
{
    private readonly double _lowerBound;
    private readonly double _upperBound;

    public UnitRangeScaler(double lowerBound, double upperBound)
    {
        _lowerBound = OptimizationGuards.RequireFiniteArgument(lowerBound, nameof(lowerBound));
        _upperBound = OptimizationGuards.RequireFiniteArgument(upperBound, nameof(upperBound));
        if (_lowerBound > _upperBound)
        {
            throw new ArgumentOutOfRangeException(nameof(lowerBound), "Scaler lower bound must not exceed upper bound.");
        }
    }

    public string Name => "unit-range";

    public double ToScaled(double value)
    {
        OptimizationGuards.RequireFiniteArgument(value, nameof(value));
        var span = _upperBound - _lowerBound;
        return OptimizationGuards.RequireFiniteState(
            Math.Abs(span) < 1e-12 ? 0 : (value - _lowerBound) / span,
            "Scaled optimization value");
    }

    public double FromScaled(double value)
    {
        OptimizationGuards.RequireFiniteArgument(value, nameof(value));
        return OptimizationGuards.RequireFiniteState(
            _lowerBound + (value * (_upperBound - _lowerBound)),
            "Unscaled optimization value");
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

    public void AddVariable(IOptimizationVariable variable)
    {
        ArgumentNullException.ThrowIfNull(variable);
        ValidateVariable(variable);
        _variables.Add(variable);
    }

    public void AddOperand(Operand operand)
    {
        ArgumentNullException.ThrowIfNull(operand);
        OptimizationGuards.RequireName(operand.Name, nameof(operand));
        OptimizationGuards.RequireFiniteArgument(operand.Target, nameof(operand));
        OptimizationGuards.RequireFiniteArgument(operand.Weight, nameof(operand));
        ArgumentNullException.ThrowIfNull(operand.Evaluate);
        _operands.Add(operand);
    }

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
        RequireVectorLength(scaledValues, _variables.Count, nameof(scaledValues));
        var values = new double[_variables.Count];
        for (var index = 0; index < values.Length; index++)
        {
            var variable = _variables[index];
            OptimizationGuards.RequireFiniteArgument(scaledValues[index], nameof(scaledValues));
            values[index] = Math.Clamp(
                OptimizationGuards.RequireFiniteState(
                    variable.Scaler.FromScaled(scaledValues[index]),
                    $"Optimization variable '{variable.Name}' inverse scaled value"),
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
        Exception? evaluationFailure = null;
        try
        {
            SetScaledVariableVector(scaledValues);
            return EvaluateCurrent();
        }
        catch (Exception exception)
        {
            evaluationFailure = exception;
            throw;
        }
        finally
        {
            try
            {
                SetScaledVariableVector(original);
            }
            catch (Exception restorationFailure) when (evaluationFailure is not null)
            {
                throw new AggregateException(
                    "Optimization evaluation failed and the original variable vector could not be fully restored.",
                    evaluationFailure,
                    restorationFailure);
            }
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
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count != _operands.Count)
        {
            throw new InvalidOperationException("独立评价器返回的值数量与操作数数量不一致。");
        }

        var weightSum = 0.0;
        foreach (var operand in _operands)
        {
            OptimizationGuards.RequireFiniteState(
                operand.Target,
                $"Optimization operand '{operand.Name}' target");
            OptimizationGuards.RequireFiniteState(
                operand.Weight,
                $"Optimization operand '{operand.Name}' weight");
            weightSum += Math.Abs(operand.Weight);
        }
        OptimizationGuards.RequireFiniteState(weightSum, "Optimization operand weight sum");
        var objective = new List<double>();
        var constraints = new List<double>();
        for (var index = 0; index < _operands.Count; index++)
        {
            var operand = _operands[index];
            var value = OptimizationGuards.RequireFiniteState(
                values[index],
                $"Optimization operand '{operand.Name}' value");
            var error = value - operand.Target;
            OptimizationGuards.RequireFiniteState(
                error,
                $"Optimization operand '{operand.Name}' error");
            if (operand.Weight > 0 && weightSum > 0)
            {
                objective.Add(OptimizationGuards.RequireFiniteState(
                    Math.Sqrt(operand.Weight / weightSum) * error,
                    $"Optimization operand '{operand.Name}' objective residual"));
            }
            else if (operand.Weight < 0 && weightSum > 0)
            {
                constraints.Add(OptimizationGuards.RequireFiniteState(
                    Math.Sqrt(Math.Abs(operand.Weight) / weightSum) * error,
                    $"Optimization operand '{operand.Name}' constraint residual"));
            }
        }

        return new OptimizationEvaluation(objective.ToArray(), constraints.ToArray());
    }

    public void SetVariableVector(IReadOnlyList<double> values)
    {
        RequireVectorLength(values, _variables.Count, nameof(values));
        var committedValues = new double[values.Count];
        for (var index = 0; index < committedValues.Length; index++)
        {
            var variable = _variables[index];
            committedValues[index] = Math.Clamp(
                OptimizationGuards.RequireFiniteArgument(values[index], nameof(values)),
                variable.LowerBound,
                variable.UpperBound);
        }

        var originalValues = VariableVector();
        var attemptedIndex = -1;
        try
        {
            for (var index = 0; index < committedValues.Length; index++)
            {
                attemptedIndex = index;
                _variables[index].Value = committedValues[index];
            }
        }
        catch (Exception commitFailure)
        {
            List<Exception>? rollbackFailures = null;
            for (var index = attemptedIndex; index >= 0; index--)
            {
                try
                {
                    _variables[index].Value = originalValues[index];
                }
                catch (Exception rollbackFailure)
                {
                    (rollbackFailures ??= new List<Exception>()).Add(rollbackFailure);
                }
            }

            if (rollbackFailures is null)
            {
                ExceptionDispatchInfo.Capture(commitFailure).Throw();
            }

            throw new AggregateException(
                "Optimization variable update failed and its previous vector could not be fully restored.",
                new[] { commitFailure }.Concat(rollbackFailures!));
        }
    }

    public void SetScaledVariableVector(IReadOnlyList<double> values)
    {
        RequireVectorLength(values, _variables.Count, nameof(values));
        SetVariableVector(VariableVectorFromScaled(values));
    }

    private static void RequireVectorLength<T>(
        IReadOnlyList<T>? values,
        int expectedCount,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (values.Count != expectedCount)
        {
            throw new ArgumentException(
                $"Optimization vector length must be exactly {expectedCount}, but received {values.Count}.",
                parameterName);
        }
    }

    private static void ValidateVariable(IOptimizationVariable variable)
    {
        OptimizationGuards.RequireName(variable.Name, nameof(variable));
        OptimizationGuards.RequireFiniteState(variable.LowerBound, $"Optimization variable '{variable.Name}' lower bound");
        OptimizationGuards.RequireFiniteState(variable.UpperBound, $"Optimization variable '{variable.Name}' upper bound");
        if (variable.LowerBound > variable.UpperBound)
        {
            throw new ArgumentOutOfRangeException(nameof(variable), "Variable lower bound must not exceed upper bound.");
        }

        OptimizationGuards.RequireFiniteState(variable.StepHint, $"Optimization variable '{variable.Name}' step hint");
        if (variable.StepHint <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(variable), "Variable step hint must be positive.");
        }

        ArgumentNullException.ThrowIfNull(variable.Scaler);
        var current = OptimizationGuards.RequireFiniteState(
            variable.Value,
            $"Optimization variable '{variable.Name}' current value");
        var scaled = OptimizationGuards.RequireFiniteState(
            variable.Scaler.ToScaled(Math.Clamp(current, variable.LowerBound, variable.UpperBound)),
            $"Optimization variable '{variable.Name}' scaled value");
        OptimizationGuards.RequireFiniteState(
            variable.Scaler.FromScaled(scaled),
            $"Optimization variable '{variable.Name}' inverse scaled value");
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
