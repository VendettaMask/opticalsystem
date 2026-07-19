using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Core.Tolerancing;

public interface IPerturbation
{
    string Name { get; }

    void Apply(Optic optic);

    void Revert(Optic optic);
}

public interface ISampledPerturbation : IPerturbation
{
    void Apply(Optic optic, Random random);
}

public sealed class DelegatePerturbation : IPerturbation
{
    private readonly Action<Optic> _apply;
    private readonly Action<Optic> _revert;

    public DelegatePerturbation(string name, Action<Optic> apply, Action<Optic> revert)
    {
        Name = name;
        _apply = apply;
        _revert = revert;
    }

    public string Name { get; }

    public void Apply(Optic optic) => _apply(optic);

    public void Revert(Optic optic) => _revert(optic);
}

public interface ISampler
{
    double Sample(Random random);
}

public sealed class NormalSampler : ISampler
{
    public NormalSampler(double mean, double sigma)
    {
        Mean = mean;
        Sigma = sigma;
    }

    public double Mean { get; }

    public double Sigma { get; }

    public double Sample(Random random)
    {
        var u1 = Math.Max(1e-12, random.NextDouble());
        var u2 = random.NextDouble();
        return Mean + (Sigma * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
    }
}

public sealed class UniformSampler : ISampler
{
    public UniformSampler(double minimum, double maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }

    public double Minimum { get; }

    public double Maximum { get; }

    public double Sample(Random random)
    {
        return Minimum + ((Maximum - Minimum) * random.NextDouble());
    }
}

public sealed class ConstantSampler : ISampler
{
    public ConstantSampler(double value)
    {
        Value = value;
    }

    public double Value { get; }

    public double Sample(Random random) => Value;
}

public sealed class VariablePerturbation : ISampledPerturbation
{
    private readonly IOptimizationVariable _variable;
    private readonly ISampler _sampler;
    private double _previousValue;

    public VariablePerturbation(string name, IOptimizationVariable variable, ISampler sampler)
    {
        Name = name;
        _variable = variable;
        _sampler = sampler;
    }

    public string Name { get; }

    public void Apply(Optic optic)
    {
        Apply(optic, new Random(1234));
    }

    public void Apply(Optic optic, Random random)
    {
        _previousValue = _variable.Value;
        _variable.Value = _previousValue + _sampler.Sample(random);
    }

    public void Revert(Optic optic)
    {
        _variable.Value = _previousValue;
    }
}

public sealed class Tolerancing
{
    private readonly List<IPerturbation> _perturbations = new();
    private readonly List<Operand> _operands = new();
    private readonly List<IOptimizationVariable> _compensators = new();

    public IReadOnlyList<IPerturbation> Perturbations => _perturbations;

    public IReadOnlyList<Operand> Operands => _operands;

    public IReadOnlyList<IOptimizationVariable> Compensators => _compensators;

    public void AddPerturbation(IPerturbation perturbation) => _perturbations.Add(perturbation);

    public void AddOperand(Operand operand) => _operands.Add(operand);

    public void AddCompensator(IOptimizationVariable variable) => _compensators.Add(variable);

    public double Merit() => _operands.Sum(operand => operand.Squared());

    public OptimizationProblem CreateCompensationProblem()
    {
        var problem = new OptimizationProblem();
        foreach (var compensator in _compensators)
        {
            problem.AddVariable(compensator);
        }

        foreach (var operand in _operands)
        {
            problem.AddOperand(operand);
        }

        return problem;
    }
}

public sealed record SensitivityResult(string Perturbation, double DeltaMerit);

public sealed class SensitivityAnalysis
{
    private readonly Optic _optic;
    private readonly Tolerancing _tolerancing;

    public SensitivityAnalysis(Optic optic, Tolerancing tolerancing)
    {
        _optic = optic;
        _tolerancing = tolerancing;
    }

    public IReadOnlyList<SensitivityResult> Run(int compensationIterations = 0)
    {
        return Run(compensationIterations, CancellationToken.None);
    }

    public IReadOnlyList<SensitivityResult> Run(
        int compensationIterations,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var baseline = _tolerancing.Merit();
        var results = new List<SensitivityResult>();
        foreach (var perturbation in _tolerancing.Perturbations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = _optic.ToSnapshot();
            try
            {
                perturbation.Apply(_optic);
                cancellationToken.ThrowIfCancellationRequested();
                var perturbed = CompensatedMerit(compensationIterations, cancellationToken);
                results.Add(new SensitivityResult(perturbation.Name, perturbed - baseline));
            }
            finally
            {
                try
                {
                    perturbation.Revert(_optic);
                }
                finally
                {
                    _optic.ApplySnapshot(snapshot);
                }
            }
        }

        return results
            .OrderByDescending(result => Math.Abs(result.DeltaMerit))
            .ToArray();
    }

    private double CompensatedMerit(
        int compensationIterations,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (compensationIterations <= 0 || _tolerancing.Compensators.Count == 0)
        {
            return _tolerancing.Merit();
        }

        var problem = _tolerancing.CreateCompensationProblem();
        new OrthogonalDescentOptimizer().Optimize(problem, compensationIterations);
        cancellationToken.ThrowIfCancellationRequested();
        return problem.SumSquared();
    }
}

public sealed record TolerancingTrialResult(int Trial, double Merit, double CompensatedMerit);

public sealed class MonteCarlo
{
    private readonly Optic _optic;
    private readonly Tolerancing _tolerancing;

    public MonteCarlo(Optic optic, Tolerancing tolerancing)
    {
        _optic = optic;
        _tolerancing = tolerancing;
    }

    public IReadOnlyList<double> Run(int trials, int seed = 1234)
    {
        return RunDetailed(trials, seed, 0, CancellationToken.None)
            .Select(result => result.CompensatedMerit)
            .ToArray();
    }

    public IReadOnlyList<double> Run(
        int trials,
        int seed,
        CancellationToken cancellationToken)
    {
        return RunDetailed(trials, seed, 0, cancellationToken)
            .Select(result => result.CompensatedMerit)
            .ToArray();
    }

    public IReadOnlyList<TolerancingTrialResult> RunDetailed(
        int trials,
        int seed = 1234,
        int compensationIterations = 0)
    {
        return RunDetailed(trials, seed, compensationIterations, CancellationToken.None);
    }

    public IReadOnlyList<TolerancingTrialResult> RunDetailed(
        int trials,
        int seed,
        int compensationIterations,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var random = new Random(seed);
        var results = new List<TolerancingTrialResult>();
        for (var trial = 0; trial < Math.Max(1, trials); trial++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = _optic.ToSnapshot();
            try
            {
                foreach (var perturbation in _tolerancing.Perturbations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ApplyPerturbation(perturbation, random);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var merit = _tolerancing.Merit();
                var compensated = CompensatedMerit(compensationIterations, cancellationToken);
                results.Add(new TolerancingTrialResult(trial, merit, compensated));
            }
            finally
            {
                try
                {
                    foreach (var perturbation in _tolerancing.Perturbations.Reverse())
                    {
                        perturbation.Revert(_optic);
                    }
                }
                finally
                {
                    _optic.ApplySnapshot(snapshot);
                }
            }
        }

        return results;
    }

    private void ApplyPerturbation(IPerturbation perturbation, Random random)
    {
        if (perturbation is ISampledPerturbation sampled)
        {
            sampled.Apply(_optic, random);
            return;
        }

        perturbation.Apply(_optic);
    }

    private double CompensatedMerit(
        int compensationIterations,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (compensationIterations <= 0 || _tolerancing.Compensators.Count == 0)
        {
            return _tolerancing.Merit();
        }

        var problem = _tolerancing.CreateCompensationProblem();
        new OrthogonalDescentOptimizer().Optimize(problem, compensationIterations);
        cancellationToken.ThrowIfCancellationRequested();
        return problem.SumSquared();
    }
}
