namespace OptilandWorkbench.Core.Optimization;

public interface IOptimizationVariable
{
    string Name { get; }

    double Value { get; set; }

    double LowerBound { get; }

    double UpperBound { get; }
}

public sealed class DelegateVariable : IOptimizationVariable
{
    private readonly Func<double> _getter;
    private readonly Action<double> _setter;

    public DelegateVariable(string name, Func<double> getter, Action<double> setter, double lowerBound, double upperBound)
    {
        Name = name;
        _getter = getter;
        _setter = setter;
        LowerBound = lowerBound;
        UpperBound = upperBound;
    }

    public string Name { get; }

    public double Value
    {
        get => _getter();
        set => _setter(Math.Clamp(value, LowerBound, UpperBound));
    }

    public double LowerBound { get; }

    public double UpperBound { get; }
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
}

public sealed class OptimizerResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public double InitialMerit { get; init; }

    public double FinalMerit { get; init; }

    public int Iterations { get; init; }
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
        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            foreach (var variable in problem.Variables)
            {
                var original = variable.Value;
                var step = Math.Max(1e-6, Math.Abs(original) * 0.05);
                foreach (var candidate in new[] { original - step, original + step })
                {
                    variable.Value = candidate;
                    var merit = problem.SumSquared();
                    if (merit < best)
                    {
                        best = merit;
                        original = variable.Value;
                    }
                }

                variable.Value = original;
            }
        }

        return new OptimizerResult
        {
            Success = best <= initial,
            InitialMerit = initial,
            FinalMerit = best,
            Iterations = maxIterations,
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
        var result = _fallback.Optimize(problem, maxIterations);
        return new OptimizerResult
        {
            Success = result.Success,
            InitialMerit = result.InitialMerit,
            FinalMerit = result.FinalMerit,
            Iterations = result.Iterations,
            Message = $"Optimized with {Name} using orthogonal descent fallback"
        };
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
        return name == "Orthogonal Descent" ? new OrthogonalDescentOptimizer() : new NamedOptimizer(name);
    }
}

public sealed class GlassExpert
{
    public OptimizerResult Run(OptimizationProblem problem, IReadOnlyList<string> candidateGlasses, int maxIterations = 25)
    {
        return OptimizerCatalog.Create("Orthogonal Descent").Optimize(problem, maxIterations);
    }
}
