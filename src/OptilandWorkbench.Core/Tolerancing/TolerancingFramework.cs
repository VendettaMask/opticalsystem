using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Core.Tolerancing;

public interface IPerturbation
{
    string Name { get; }

    void Apply(Optic optic);

    void Revert(Optic optic);
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

    public IReadOnlyList<SensitivityResult> Run()
    {
        var baseline = _tolerancing.Operands.Sum(operand => operand.Squared());
        var results = new List<SensitivityResult>();
        foreach (var perturbation in _tolerancing.Perturbations)
        {
            perturbation.Apply(_optic);
            var perturbed = _tolerancing.Operands.Sum(operand => operand.Squared());
            perturbation.Revert(_optic);
            results.Add(new SensitivityResult(perturbation.Name, perturbed - baseline));
        }

        return results;
    }
}

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
        var results = new List<double>();
        for (var trial = 0; trial < Math.Max(1, trials); trial++)
        {
            var snapshot = _optic.ToSnapshot();
            foreach (var perturbation in _tolerancing.Perturbations)
            {
                perturbation.Apply(_optic);
            }

            results.Add(_tolerancing.Operands.Sum(operand => operand.Squared()));
            _optic.ApplySnapshot(snapshot);
        }

        return results;
    }
}
