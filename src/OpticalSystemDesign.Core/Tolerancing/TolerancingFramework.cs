using OpticalSystemDesign.Core.Capabilities;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Tolerancing;

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

public interface IScaledRangePerturbation : IRangePerturbation
{
    void ApplyScaledMinimum(Optic optic, double scale);

    void ApplyScaledMaximum(Optic optic, double scale);
}

public sealed class DelegatePerturbation : IPerturbation
{
    private readonly Action<Optic> _apply;
    private readonly Action<Optic> _revert;

    public DelegatePerturbation(string name, Action<Optic> apply, Action<Optic> revert)
    {
        Name = OptimizationGuards.RequireName(name, nameof(name));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _revert = revert ?? throw new ArgumentNullException(nameof(revert));
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
        Mean = OptimizationGuards.RequireFiniteArgument(mean, nameof(mean));
        Sigma = OptimizationGuards.RequireFiniteArgument(sigma, nameof(sigma));
        if (Sigma < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sigma), "Normal sampler sigma must be non-negative.");
        }
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
        Minimum = OptimizationGuards.RequireFiniteArgument(minimum, nameof(minimum));
        Maximum = OptimizationGuards.RequireFiniteArgument(maximum, nameof(maximum));
        if (Minimum > Maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum), "Uniform sampler minimum must not exceed maximum.");
        }
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
        Value = OptimizationGuards.RequireFiniteArgument(value, nameof(value));
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
        Name = OptimizationGuards.RequireName(name, nameof(name));
        _variable = variable ?? throw new ArgumentNullException(nameof(variable));
        _sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
    }

    public string Name { get; }

    public void Apply(Optic optic)
    {
        Apply(optic, new Random(1234));
    }

    public void Apply(Optic optic, Random random)
    {
        _previousValue = _variable.Value;
        var sample = OptimizationGuards.RequireFiniteState(
            _sampler.Sample(random),
            $"Tolerance perturbation '{Name}' sampled delta");
        _variable.Value = OptimizationGuards.RequireFiniteState(
            _previousValue + sample,
            $"Tolerance perturbation '{Name}' applied value");
    }

    public void Revert(Optic optic)
    {
        _variable.Value = _previousValue;
    }
}

public sealed class VariableRangePerturbation : IScaledRangePerturbation
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

        Name = OptimizationGuards.RequireName(name, nameof(name));
        _variable = variable ?? throw new ArgumentNullException(nameof(variable));
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
        ApplyDelta(DrawDelta(random));
    }

    public void ApplyMinimum(Optic optic) => ApplyDelta(Minimum);

    public void ApplyMaximum(Optic optic) => ApplyDelta(Maximum);

    public void ApplyScaledMinimum(Optic optic, double scale) =>
        ApplyDelta(Minimum * ValidateScale(scale));

    public void ApplyScaledMaximum(Optic optic, double scale) =>
        ApplyDelta(Maximum * ValidateScale(scale));

    public void Revert(Optic optic) => _variable.Value = _previousValue;

    internal double DrawDelta(Random random) => SampleDelta(random, Minimum, Maximum, _normalDistribution);

    internal void ApplyDelta(double delta)
    {
        _previousValue = _variable.Value;
        _variable.Value = _previousValue + delta;
    }

    internal static double SampleDelta(Random random, double minimum, double maximum, bool normalDistribution,
        double sigmaSpan = 2)
    {
        if (!normalDistribution) return minimum + (maximum - minimum) * random.NextDouble();
        var midpoint = (minimum + maximum) / 2.0;
        var sigma = (maximum - minimum) / (2.0 * sigmaSpan);
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
            if (value >= minimum && value <= maximum)
            {
                return value;
            }
        }

        return midpoint;
    }

    internal static double ValidateScale(double scale)
    {
        if (!double.IsFinite(scale) || scale < 0 || scale > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        return scale;
    }
}

public sealed partial class Tolerancing
{
    private readonly List<IPerturbation> _perturbations = new();
    private readonly List<Operand> _operands = new();
    private readonly List<IOptimizationVariable> _compensators = new();
    private readonly Dictionary<IOptimizationVariable, int> _compensatorStages = new();
    private Func<double>? _criterionEvaluator;

    public IReadOnlyList<IPerturbation> Perturbations => _perturbations;
    // Install transient sag overlays after changes to the prescription's underlying geometry.
    internal IEnumerable<IPerturbation> TrialPerturbations()
    {
        var isoRows = _perturbations.OfType<SurfaceFormPerturbation>()
            .Where(form => form.Mode is SurfaceFormMode.IsoIrregularityPv or SurfaceFormMode.IsoRotationalIrregularityPv)
            .ToLookup(form => (form.SurfaceNumber, form.Mode));
        foreach (var perturbation in _perturbations.OrderBy(perturbation => perturbation is SurfaceFormPerturbation))
        {
            if (perturbation is SurfaceFormPerturbation form)
            {
                if (form.Mode == SurfaceFormMode.IsoIrregularityPv &&
                    isoRows[(form.SurfaceNumber, SurfaceFormMode.IsoRotationalIrregularityPv)].SingleOrDefault() is { } rotational)
                {
                    yield return isoRows[(form.SurfaceNumber, SurfaceFormMode.IsoIrregularityPv)].Single()
                        .WithRotationalIrregularity(rotational);
                    continue;
                }
                if (form.Mode == SurfaceFormMode.IsoRotationalIrregularityPv &&
                    isoRows.Contains((form.SurfaceNumber, SurfaceFormMode.IsoIrregularityPv)))
                    continue;
            }
            yield return perturbation;
        }
    }

    public IReadOnlyList<Operand> Operands => _operands;

    public IReadOnlyList<IOptimizationVariable> Compensators => _compensators;

    public void AddPerturbation(IPerturbation perturbation)
    {
        ArgumentNullException.ThrowIfNull(perturbation);
        _perturbations.Add(perturbation);
    }

    public void AddOperand(Operand operand)
    {
        ArgumentNullException.ThrowIfNull(operand);
        _operands.Add(operand);
    }

    public void AddCompensator(IOptimizationVariable variable, int stage = 1)
    {
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stage);
        _compensatorStages[variable] = stage;
        _compensators.Add(variable);
        _compensatorNominals[variable.Name] = variable.Value;
    }

    public void SetCriterionEvaluator(Func<double> evaluator)
    {
        _criterionEvaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
    }

    public bool StrictMerit { get; set; }

    public double Merit()
    {
        if (!StrictMerit) return _operands.Sum(operand => operand.Squared());
        using var batch = MeritFunctionCatalog.BeginEvaluationBatch();
        try { return _operands.Sum(operand => operand.Squared()); }
        catch (OptimizationEvaluationException) { return double.PositiveInfinity; }
    }

    public double Criterion()
    {
        try
        {
            var value = _criterionEvaluator?.Invoke() ?? Math.Sqrt(Math.Max(0, Merit()));
            return double.IsFinite(value) ? value : InvalidCriterion;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or ArgumentException
            or ArithmeticException
            or KeyNotFoundException
            or NotSupportedException)
        {
            return InvalidCriterion;
        }
    }

    public OptimizationProblem CreateCompensationProblem() => CreateCompensationProblem(_compensators);

    private OptimizationProblem CreateCompensationProblem(IEnumerable<IOptimizationVariable> compensators,
        IReadOnlyList<Operand>? objectiveOperands = null)
    {
        var problem = new OptimizationProblem();
        foreach (var compensator in compensators)
        {
            problem.AddVariable(compensator);
        }

        foreach (var operand in objectiveOperands ?? _operands)
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
    double WorstCriterion = double.NaN,
    ToleranceEvaluation? NegativeEvaluation = null,
    ToleranceEvaluation? PositiveEvaluation = null);

public enum InverseToleranceEndpointStatus
{
    UnchangedWithinTarget,
    Tightened,
    ZeroRange,
    UnsupportedPerturbation
}

public sealed record InverseToleranceEndpointResult(
    double OriginalTolerance,
    double AdjustedTolerance,
    double Criterion,
    InverseToleranceEndpointStatus Status,
    int Iterations);

public sealed record InverseSensitivityResult(
    string Perturbation,
    InverseToleranceEndpointResult Minimum,
    InverseToleranceEndpointResult Maximum);

public sealed class SensitivityAnalysis
{
    public const int MaximumInverseIterations = 48;
    private readonly Optic _optic;
    private readonly Tolerancing _tolerancing;

    public SensitivityAnalysis(Optic optic, Tolerancing tolerancing)
    {
        OpticCapabilityPreflight.EnsureSupported(optic, OpticCapabilityOperation.Tolerancing);
        _optic = optic;
        _tolerancing = tolerancing;
    }

    private bool HigherIsBetter => _tolerancing.HigherIsBetter;

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
                var worstCriterion = HigherIsBetter
                    ? Math.Min(negative.Criterion, positive.Criterion)
                    : Math.Max(negative.Criterion, positive.Criterion);
                results.Add(new SensitivityResult(
                    perturbation.Name,
                    worstMerit - baseline.Merit,
                    negative.Merit,
                    positive.Merit,
                    worstMerit,
                    worstCriterion - baseline.Criterion,
                    negative.Criterion,
                    positive.Criterion,
                    worstCriterion, negative, positive));
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

    public IReadOnlyList<InverseSensitivityResult> RunInverse(
        double targetCriterion,
        int compensationIterations = 0,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<int, double>? fieldTargets = null)
    {
        if (!double.IsFinite(targetCriterion) || targetCriterion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetCriterion));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var baseline = EvaluateNominal(compensationIterations, cancellationToken);
        if (!double.IsFinite(baseline.Criterion) || (HigherIsBetter ? targetCriterion >= baseline.Criterion : targetCriterion <= baseline.Criterion))
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetCriterion),
                "The inverse tolerance target must represent worse performance than the nominal criterion.");
        }

        if (fieldTargets is not null && (fieldTargets.Count == 0
            || fieldTargets.Any(target => !double.IsFinite(target.Value) || target.Value <= 0)
            || !MeetsTargets(baseline, targetCriterion, fieldTargets)))
            throw new ArgumentOutOfRangeException(nameof(fieldTargets), "Nominal fields must meet every inverse target.");

        var results = new List<InverseSensitivityResult>();
        foreach (var perturbation in _tolerancing.Perturbations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (perturbation is not IScaledRangePerturbation range)
            {
                var unsupported = new InverseToleranceEndpointResult(
                    double.NaN,
                    double.NaN,
                    baseline.Criterion,
                    InverseToleranceEndpointStatus.UnsupportedPerturbation,
                    0);
                results.Add(new InverseSensitivityResult(
                    perturbation.Name,
                    unsupported,
                    unsupported));
                continue;
            }

            results.Add(new InverseSensitivityResult(
                perturbation.Name,
                SolveInverseEndpoint(
                    range,
                    useMaximum: false,
                    baseline.Criterion,
                    targetCriterion,
                    compensationIterations,
                    cancellationToken, fieldTargets),
                SolveInverseEndpoint(
                    range,
                    useMaximum: true,
                    baseline.Criterion,
                    targetCriterion,
                    compensationIterations,
                    cancellationToken, fieldTargets)));
        }

        return results;
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

    private InverseToleranceEndpointResult SolveInverseEndpoint(
        IScaledRangePerturbation perturbation,
        bool useMaximum,
        double nominalCriterion,
        double targetCriterion,
        int compensationIterations,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<int, double>? fieldTargets)
    {
        var originalTolerance = useMaximum ? perturbation.Maximum : perturbation.Minimum;
        if (Math.Abs(originalTolerance) <= 1e-15)
        {
            return new InverseToleranceEndpointResult(
                originalTolerance,
                originalTolerance,
                nominalCriterion,
                InverseToleranceEndpointStatus.ZeroRange,
                0);
        }

        var endpoint = EvaluateScaledEndpoint(
            perturbation,
            useMaximum,
            1,
            compensationIterations,
            cancellationToken);
        if (MeetsTargets(endpoint, targetCriterion, fieldTargets))
        {
            return new InverseToleranceEndpointResult(
                originalTolerance,
                originalTolerance,
                endpoint.Criterion,
                InverseToleranceEndpointStatus.UnchangedWithinTarget,
                0);
        }

        var lowerScale = 0.0;
        var upperScale = 1.0;
        var lowerCriterion = nominalCriterion;
        var iterations = 0;
        var scaleTolerance = Math.Max(1e-12, 1e-7 / Math.Max(1, Math.Abs(originalTolerance)));
        for (; iterations < MaximumInverseIterations; iterations++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var middleScale = (lowerScale + upperScale) / 2.0;
            var middle = EvaluateScaledEndpoint(
                perturbation,
                useMaximum,
                middleScale,
                compensationIterations,
                cancellationToken);
            if (MeetsTargets(middle, targetCriterion, fieldTargets))
            {
                lowerScale = middleScale;
                lowerCriterion = middle.Criterion;
            }
            else
            {
                upperScale = middleScale;
            }

            if (upperScale - lowerScale <= scaleTolerance)
            {
                iterations++;
                break;
            }
        }

        return new InverseToleranceEndpointResult(
            originalTolerance,
            originalTolerance * lowerScale,
            lowerCriterion,
            InverseToleranceEndpointStatus.Tightened,
            iterations);
    }

    private ToleranceEvaluation EvaluateScaledEndpoint(
        IScaledRangePerturbation perturbation,
        bool useMaximum,
        double scale,
        int compensationIterations,
        CancellationToken cancellationToken)
    {
        var snapshot = _optic.ToSnapshot();
        try
        {
            if (useMaximum)
            {
                perturbation.ApplyScaledMaximum(_optic, scale);
            }
            else
            {
                perturbation.ApplyScaledMinimum(_optic, scale);
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

    private bool MeetsTargets(ToleranceEvaluation evaluation, double target,
        IReadOnlyDictionary<int, double>? fieldTargets) =>
        _tolerancing.MeetsTargets(evaluation, target, fieldTargets);

    private ToleranceEvaluation CompensatedEvaluation(int compensationIterations,
        CancellationToken cancellationToken) =>
        _tolerancing.EvaluateCompensated(compensationIterations, cancellationToken);

}

public readonly record struct ToleranceFieldValue(int FieldNumber, double Value, string Error = "");
public readonly record struct ToleranceCompensatorValue(string Name, double Nominal, double Value,
    double Minimum, double Maximum, int Stage = 1);
public sealed record ToleranceStageValue(int Stage, double Criterion, IReadOnlyList<ToleranceCompensatorValue> Compensators,
    IReadOnlyList<ToleranceFieldValue>? Fields = null, string Error = "");
public readonly record struct ToleranceEvaluation(double Merit, double Criterion,
    IReadOnlyList<ToleranceFieldValue>? Fields = null,
    IReadOnlyList<ToleranceCompensatorValue>? Compensators = null,
    IReadOnlyList<ToleranceStageValue>? Stages = null);

public sealed record TolerancingTrialResult(
    int Trial,
    double Merit,
    double CompensatedMerit,
    double Criterion = double.NaN,
    double CompensatedCriterion = double.NaN,
    IReadOnlyList<ToleranceFieldValue>? Fields = null,
    IReadOnlyList<ToleranceFieldValue>? CompensatedFields = null,
    IReadOnlyList<ToleranceCompensatorValue>? Compensators = null,
    IReadOnlyList<ToleranceStageValue>? Stages = null)
{
    public bool IsValid =>
        double.IsFinite(Criterion) && double.IsFinite(CompensatedCriterion);
}

public sealed class MonteCarlo
{
    public const int MaximumTrialCount = 10_000;
    public const int MaximumCompensationIterations = 500;
    private readonly Optic _optic;
    private readonly Tolerancing _tolerancing;

    public MonteCarlo(Optic optic, Tolerancing tolerancing)
    {
        OpticCapabilityPreflight.EnsureSupported(optic, OpticCapabilityOperation.Tolerancing);
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
        ValidateBudget(trials, compensationIterations);
        var results = new List<TolerancingTrialResult>();
        var perturbations = _tolerancing.TrialPerturbations().ToArray();
        for (var trial = 0; trial < Math.Max(0, trials); trial++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var random = new Random(DeriveTrialSeed(seed, trial));
            var snapshot = _optic.ToSnapshot();
            try
            {
                foreach (var perturbation in perturbations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ApplyPerturbation(_optic, perturbation, random);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var merit = _tolerancing.Merit();
                var uncompensated = _tolerancing.Evaluate();
                var criterion = uncompensated.Criterion;
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
                    compensated.Criterion, uncompensated.Fields, compensated.Fields, compensated.Compensators, compensated.Stages));
            }
            finally
            {
                try
                {
                    foreach (var perturbation in perturbations.Reverse())
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
        ValidateBudget(trials, compensationIterations);

        var trialCount = trials;
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
            results[trial] = EvaluateWorkerTrial(nominalSnapshot, workerFactory, trial, seed, compensationIterations, cancellationToken));
        return results;
    }

    public TolerancingTrialResult ReplayDetailed(int trial, int seed, int compensationIterations,
        CancellationToken cancellationToken, Func<Optic, Tolerancing> workerFactory)
    {
        ArgumentNullException.ThrowIfNull(workerFactory);
        cancellationToken.ThrowIfCancellationRequested();
        if (trial is < 0 or >= MaximumTrialCount) throw new ArgumentOutOfRangeException(nameof(trial));
        ValidateBudget(1, compensationIterations);
        return EvaluateWorkerTrial(_optic.ToSnapshot(), workerFactory, trial, seed, compensationIterations, cancellationToken);
    }

    private static TolerancingTrialResult EvaluateWorkerTrial(OpticSnapshot nominalSnapshot,
        Func<Optic, Tolerancing> workerFactory, int trial, int seed, int iterations, CancellationToken cancellationToken)
    {
        using var parallelism = ComputationParallelism.SuppressNestedParallelism();
        var optic = Optic.FromSnapshot(nominalSnapshot);
        var tolerancing = workerFactory(optic)
            ?? throw new InvalidOperationException("The tolerancing worker factory returned null.");
        var random = new Random(DeriveTrialSeed(seed, trial));
        foreach (var perturbation in tolerancing.TrialPerturbations())
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyPerturbation(optic, perturbation, random);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var merit = tolerancing.Merit();
        var uncompensated = tolerancing.Evaluate();
        var compensated = tolerancing.EvaluateCompensated(iterations, cancellationToken);
        return new TolerancingTrialResult(trial, merit, compensated.Merit, uncompensated.Criterion,
            compensated.Criterion, uncompensated.Fields, compensated.Fields, compensated.Compensators, compensated.Stages);
    }

    internal static int DeriveTrialSeed(int seed, int trial)
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

    private static void ValidateBudget(int trials, int compensationIterations)
    {
        if (trials < 0 || trials > MaximumTrialCount) throw new ArgumentOutOfRangeException(nameof(trials));
        if (compensationIterations < 0 || compensationIterations > MaximumCompensationIterations)
            throw new ArgumentOutOfRangeException(nameof(compensationIterations));
    }

    private static void ApplyPerturbation(Optic optic, IPerturbation perturbation, Random random)
    {
        if (perturbation is ISampledPerturbation sampled) sampled.Apply(optic, random);
        else perturbation.Apply(optic);
    }

    private ToleranceEvaluation CompensatedEvaluation(
        int compensationIterations,
        CancellationToken cancellationToken,
        double uncompensatedMerit,
        double uncompensatedCriterion)
    {
        return _tolerancing.EvaluateCompensated(compensationIterations, cancellationToken);
    }
}
