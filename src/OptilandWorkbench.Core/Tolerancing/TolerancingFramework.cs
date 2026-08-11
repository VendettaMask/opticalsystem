using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

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

public interface IRangePerturbation : ISampledPerturbation
{
    double Minimum { get; }

    double Maximum { get; }

    void ApplyMinimum(Optic optic);

    void ApplyMaximum(Optic optic);
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

public sealed class VariableRangePerturbation : IRangePerturbation
{
    private readonly IOptimizationVariable _variable;
    private readonly bool _normalDistribution;
    private double _previousValue;

    public VariableRangePerturbation(
        string name,
        IOptimizationVariable variable,
        double minimum,
        double maximum,
        bool normalDistribution)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum), "Tolerance limits must be finite and ordered.");
        }

        Name = name;
        _variable = variable;
        Minimum = minimum;
        Maximum = maximum;
        _normalDistribution = normalDistribution;
    }

    public string Name { get; }

    public double Minimum { get; }

    public double Maximum { get; }

    public void Apply(Optic optic) => Apply(optic, new Random(1234));

    public void Apply(Optic optic, Random random)
    {
        var delta = _normalDistribution
            ? SampleTruncatedNormal(random)
            : Minimum + ((Maximum - Minimum) * random.NextDouble());
        ApplyDelta(delta);
    }

    public void ApplyMinimum(Optic optic) => ApplyDelta(Minimum);

    public void ApplyMaximum(Optic optic) => ApplyDelta(Maximum);

    public void Revert(Optic optic) => _variable.Value = _previousValue;

    private void ApplyDelta(double delta)
    {
        _previousValue = _variable.Value;
        _variable.Value = _previousValue + delta;
    }

    private double SampleTruncatedNormal(Random random)
    {
        const double sigmaSpan = 2.0;
        var midpoint = (Minimum + Maximum) / 2.0;
        var sigma = (Maximum - Minimum) / (2.0 * sigmaSpan);
        if (sigma <= 1e-15)
        {
            return midpoint;
        }

        // Zemax-style modified Gaussian: the tolerance interval is centered on
        // the midpoint and samples outside the configured sigma span are rejected.
        // Clamping would create artificial point masses at the tolerance limits.
        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            var u1 = Math.Max(1e-12, random.NextDouble());
            var u2 = random.NextDouble();
            var value = midpoint
                + (sigma * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
            if (value >= Minimum && value <= Maximum)
            {
                return value;
            }
        }

        return midpoint;
    }
}

public sealed class Tolerancing
{
    private readonly List<IPerturbation> _perturbations = new();
    private readonly List<Operand> _operands = new();
    private readonly List<IOptimizationVariable> _compensators = new();
    private Func<double>? _criterionEvaluator;

    public IReadOnlyList<IPerturbation> Perturbations => _perturbations;

    public IReadOnlyList<Operand> Operands => _operands;

    public IReadOnlyList<IOptimizationVariable> Compensators => _compensators;

    public void AddPerturbation(IPerturbation perturbation) => _perturbations.Add(perturbation);

    public void AddOperand(Operand operand) => _operands.Add(operand);

    public void AddCompensator(IOptimizationVariable variable) => _compensators.Add(variable);

    public void SetCriterionEvaluator(Func<double> evaluator)
    {
        _criterionEvaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
    }

    public double Merit() => _operands.Sum(operand => operand.Squared());

    public double Criterion()
    {
        try
        {
            var value = _criterionEvaluator?.Invoke() ?? Math.Sqrt(Math.Max(0, Merit()));
            return double.IsFinite(value) ? value : double.PositiveInfinity;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return double.PositiveInfinity;
        }
    }

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

public sealed record SensitivityResult(
    string Perturbation,
    double DeltaMerit,
    double NegativeMerit = double.NaN,
    double PositiveMerit = double.NaN,
    double WorstMerit = double.NaN,
    double DeltaCriterion = double.NaN,
    double NegativeCriterion = double.NaN,
    double PositiveCriterion = double.NaN,
    double WorstCriterion = double.NaN);

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
        var baseline = EvaluateNominal(compensationIterations, cancellationToken);
        var results = new List<SensitivityResult>();
        foreach (var perturbation in _tolerancing.Perturbations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (perturbation is IRangePerturbation range)
            {
                var negative = EvaluateEndpoint(range, useMaximum: false, compensationIterations, cancellationToken);
                var positive = EvaluateEndpoint(range, useMaximum: true, compensationIterations, cancellationToken);
                var worstMerit = Math.Max(negative.Merit, positive.Merit);
                var worstCriterion = Math.Max(negative.Criterion, positive.Criterion);
                results.Add(new SensitivityResult(
                    perturbation.Name,
                    worstMerit - baseline.Merit,
                    negative.Merit,
                    positive.Merit,
                    worstMerit,
                    worstCriterion - baseline.Criterion,
                    negative.Criterion,
                    positive.Criterion,
                    worstCriterion));
                continue;
            }

            var snapshot = _optic.ToSnapshot();
            try
            {
                perturbation.Apply(_optic);
                cancellationToken.ThrowIfCancellationRequested();
                var perturbed = CompensatedEvaluation(compensationIterations, cancellationToken);
                results.Add(new SensitivityResult(
                    perturbation.Name,
                    perturbed.Merit - baseline.Merit,
                    DeltaCriterion: perturbed.Criterion - baseline.Criterion,
                    WorstCriterion: perturbed.Criterion));
            }
            finally
            {
                try
                {
                    perturbation.Revert(_optic);
                }
                finally
                {
                    _optic.RestoreTrustedSnapshot(snapshot);
                }
            }
        }

        return results
            .OrderByDescending(result => double.IsFinite(result.DeltaCriterion)
                ? Math.Abs(result.DeltaCriterion)
                : double.PositiveInfinity)
            .ToArray();
    }

    public ToleranceEvaluation EvaluateNominal(
        int compensationIterations,
        CancellationToken cancellationToken)
    {
        var snapshot = _optic.ToSnapshot();
        try
        {
            return CompensatedEvaluation(compensationIterations, cancellationToken);
        }
        finally
        {
            _optic.RestoreTrustedSnapshot(snapshot);
        }
    }

    private ToleranceEvaluation EvaluateEndpoint(
        IRangePerturbation perturbation,
        bool useMaximum,
        int compensationIterations,
        CancellationToken cancellationToken)
    {
        var snapshot = _optic.ToSnapshot();
        try
        {
            if (useMaximum)
            {
                perturbation.ApplyMaximum(_optic);
            }
            else
            {
                perturbation.ApplyMinimum(_optic);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return CompensatedEvaluation(compensationIterations, cancellationToken);
        }
        finally
        {
            try
            {
                perturbation.Revert(_optic);
            }
            finally
            {
                _optic.RestoreTrustedSnapshot(snapshot);
            }
        }
    }

    private ToleranceEvaluation CompensatedEvaluation(
        int compensationIterations,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (compensationIterations <= 0 || _tolerancing.Compensators.Count == 0)
        {
            return new ToleranceEvaluation(_tolerancing.Merit(), _tolerancing.Criterion());
        }

        var problem = _tolerancing.CreateCompensationProblem();
        OptimizerCatalog.Create("LM / DLS").Optimize(problem, compensationIterations);
        cancellationToken.ThrowIfCancellationRequested();
        return new ToleranceEvaluation(problem.SumSquared(), _tolerancing.Criterion());
    }
}

public readonly record struct ToleranceEvaluation(double Merit, double Criterion);

public sealed record TolerancingTrialResult(
    int Trial,
    double Merit,
    double CompensatedMerit,
    double Criterion = double.NaN,
    double CompensatedCriterion = double.NaN)
{
    public bool IsValid =>
        double.IsFinite(Criterion) && double.IsFinite(CompensatedCriterion);
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
        for (var trial = 0; trial < Math.Max(0, trials); trial++)
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
                var criterion = _tolerancing.Criterion();
                var compensated = CompensatedEvaluation(
                    compensationIterations,
                    cancellationToken,
                    merit,
                    criterion);
                results.Add(new TolerancingTrialResult(
                    trial,
                    merit,
                    compensated.Merit,
                    criterion,
                    compensated.Criterion));
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
                    _optic.RestoreTrustedSnapshot(snapshot);
                }
            }
        }

        return results;
    }

    public IReadOnlyList<TolerancingTrialResult> RunDetailed(
        int trials,
        int seed,
        int compensationIterations,
        CancellationToken cancellationToken,
        Func<Optic, Tolerancing> workerFactory,
        int maxDegreeOfParallelism = -1)
    {
        ArgumentNullException.ThrowIfNull(workerFactory);
        cancellationToken.ThrowIfCancellationRequested();
        if (maxDegreeOfParallelism is 0 or < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism));
        }


        var trialCount = Math.Max(0, trials);
        if (trialCount == 0)
        {
            return Array.Empty<TolerancingTrialResult>();
        }
        var nominalSnapshot = _optic.ToSnapshot();
        var results = new TolerancingTrialResult[trialCount];
        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = maxDegreeOfParallelism
        };

        Parallel.For(0, trialCount, options, trial =>
        {
            using var parallelism = ComputationParallelism.SuppressNestedParallelism();
            var workerOptic = Optic.FromSnapshot(nominalSnapshot);
            var workerTolerancing = workerFactory(workerOptic)
                ?? throw new InvalidOperationException("The tolerancing worker factory returned null.");
            var random = new Random(DeriveTrialSeed(seed, trial));
            foreach (var perturbation in workerTolerancing.Perturbations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (perturbation is ISampledPerturbation sampled)
                {
                    sampled.Apply(workerOptic, random);
                }
                else
                {
                    perturbation.Apply(workerOptic);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var merit = workerTolerancing.Merit();
            var criterion = workerTolerancing.Criterion();
            var compensated = EvaluateCompensatedWorker(
                workerTolerancing,
                compensationIterations,
                cancellationToken,
                merit,
                criterion);
            results[trial] = new TolerancingTrialResult(
                trial,
                merit,
                compensated.Merit,
                criterion,
                compensated.Criterion);
        });

        return results;
    }

    private static int DeriveTrialSeed(int seed, int trial)
    {
        unchecked
        {
            var value = (uint)seed + (0x9E3779B9u * ((uint)trial + 1u));
            value ^= value >> 16;
            value *= 0x85EBCA6Bu;
            value ^= value >> 13;
            value *= 0xC2B2AE35u;
            value ^= value >> 16;
            return (int)value;
        }
    }

    private static ToleranceEvaluation EvaluateCompensatedWorker(
        Tolerancing tolerancing,
        int compensationIterations,
        CancellationToken cancellationToken,
        double uncompensatedMerit,
        double uncompensatedCriterion)
    {
        if (compensationIterations <= 0 || tolerancing.Compensators.Count == 0)
        {
            return new ToleranceEvaluation(uncompensatedMerit, uncompensatedCriterion);
        }

        var problem = tolerancing.CreateCompensationProblem();
        OptimizerCatalog.Create("LM / DLS").Optimize(problem, compensationIterations);
        cancellationToken.ThrowIfCancellationRequested();
        return new ToleranceEvaluation(problem.SumSquared(), tolerancing.Criterion());
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

    private ToleranceEvaluation CompensatedEvaluation(
        int compensationIterations,
        CancellationToken cancellationToken,
        double uncompensatedMerit,
        double uncompensatedCriterion)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (compensationIterations <= 0 || _tolerancing.Compensators.Count == 0)
        {
            return new ToleranceEvaluation(uncompensatedMerit, uncompensatedCriterion);
        }

        var problem = _tolerancing.CreateCompensationProblem();
        OptimizerCatalog.Create("LM / DLS").Optimize(problem, compensationIterations);
        cancellationToken.ThrowIfCancellationRequested();
        return new ToleranceEvaluation(problem.SumSquared(), _tolerancing.Criterion());
    }
}
