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
    public double Residual() => (Evaluate() - Target) * Weight;

    public double Squared() => Residual() * Residual();
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

    public IReadOnlyList<IOptimizationVariable> Variables => _variables;

    public IReadOnlyList<Operand> Operands => _operands;

    public bool BatchingEnabled { get; private set; } = true;

    public void AddVariable(IOptimizationVariable variable) => _variables.Add(variable);

    public void AddOperand(Operand operand) => _operands.Add(operand);

    public void DisableBatching() => BatchingEnabled = false;

    public void EnableBatching() => BatchingEnabled = true;

    public double[] ResidualVector() => _operands.Select(operand => operand.Residual()).ToArray();

    public double SumSquared() => _operands.Sum(operand => operand.Squared());

    public double[] VariableVector() => _variables.Select(variable => variable.Value).ToArray();

    public double[] ScaledVariableVector() => _variables.Select(variable => variable.ScaledValue).ToArray();

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

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
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

            if (!improved)
            {
                foreach (var variable in problem.Variables)
                {
                    stepByVariable[variable] *= 0.5;
                }
            }

            history.Add(best);
        }

        problem.SetVariableVector(bestVector);
        return new OptimizerResult
        {
            Success = best <= initial,
            InitialMerit = initial,
            FinalMerit = best,
            Iterations = maxIterations,
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
        "Least Squares",
        "Nelder-Mead",
        "Powell",
        "BFGS",
        "L-BFGS-B",
        "COBYLA",
        "Orthogonal Descent",
        "Differential Evolution",
        "Dual Annealing",
        "Basin Hopping",
        "Glass Expert"
    };

    public static IOptimizer Create(string name)
    {
        return name switch
        {
            "Least Squares" => new LeastSquaresOptimizer(),
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
