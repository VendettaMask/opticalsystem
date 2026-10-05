using OptilandWorkbench.Core.Optimization;

namespace OptilandWorkbench.Core.Tolerancing;

public sealed partial class Tolerancing
{
    private Func<IReadOnlyList<ToleranceFieldValue>>? _fieldEvaluator;
    private readonly Dictionary<string, double> _compensatorNominals = new();
    public bool HigherIsBetter { get; set; }
    public string CompensationOptimizer { get; set; } = "Damped Least Squares";
    private double InvalidCriterion => HigherIsBetter ? double.NegativeInfinity : double.PositiveInfinity;

    public void SetFieldEvaluator(Func<IReadOnlyList<ToleranceFieldValue>> evaluator) =>
        _fieldEvaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));

    public ToleranceEvaluation Evaluate()
    {
        var fields = _fieldEvaluator?.Invoke();
        var criterion = Criterion();
        var compensators = _compensators.Select(variable => new ToleranceCompensatorValue(
            variable.Name,
            _compensatorNominals.TryGetValue(variable.Name, out var nominal) ? nominal : variable.Value,
            variable.Value, variable.LowerBound, variable.UpperBound)).ToArray();
        return new ToleranceEvaluation(Merit(), criterion, fields, compensators);
    }

    public bool MeetsTargets(ToleranceEvaluation evaluation, double target,
        IReadOnlyDictionary<int, double>? fieldTargets = null)
    {
        if (!double.IsFinite(evaluation.Criterion)) return false;
        if (fieldTargets is null)
            return HigherIsBetter ? evaluation.Criterion >= target : evaluation.Criterion <= target;
        return evaluation.Fields is { Count: > 0 } fields
            && fieldTargets.All(pair => fields.Any(field => field.FieldNumber == pair.Key
                && double.IsFinite(field.Value) && string.IsNullOrEmpty(field.Error)
                && (HigherIsBetter ? field.Value >= pair.Value : field.Value <= pair.Value)));
    }

    public ToleranceEvaluation EvaluateCompensated(int iterations, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var variable in _compensators)
            _compensatorNominals.TryAdd(variable.Name, variable.Value);
        if (iterations > 0 && _compensators.Count > 0)
        {
            if (CompensationOptimizer is not ("Damped Least Squares" or "Coordinate Pattern Search"))
                throw new NotSupportedException("Unsupported tolerance compensation optimizer.");
            OptimizerCatalog.Create(CompensationOptimizer).Optimize(CreateCompensationProblem(), iterations);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Evaluate();
    }
}
