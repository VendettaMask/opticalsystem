using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.Core.Tolerancing;

public sealed partial class Tolerancing
{
    private Func<IReadOnlyList<ToleranceFieldValue>>? _fieldEvaluator;
    private readonly Dictionary<string, double> _compensatorNominals = new();
    private readonly Dictionary<int, Tolerancing> _stageObjectives = new();
    public bool HigherIsBetter { get; set; }
    public string CompensationOptimizer { get; set; } = "Damped Least Squares";
    private double InvalidCriterion => HigherIsBetter ? double.NegativeInfinity : double.PositiveInfinity;

    public void SetFieldEvaluator(Func<IReadOnlyList<ToleranceFieldValue>> evaluator) =>
        _fieldEvaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));

    public void SetStageObjective(int stage, Tolerancing objective)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stage);
        ArgumentNullException.ThrowIfNull(objective);
        _stageObjectives.Add(stage, objective);
    }

    public ToleranceEvaluation Evaluate()
    {
        var fields = _fieldEvaluator?.Invoke();
        var criterion = Criterion();
        return new ToleranceEvaluation(Merit(), criterion, fields, ReadCompensators());
    }

    private ToleranceCompensatorValue[] ReadCompensators() =>
        _compensators.Select(variable => new ToleranceCompensatorValue(
            variable.Name,
            _compensatorNominals.TryGetValue(variable.Name, out var nominal) ? nominal : variable.Value,
            variable.Value, variable.LowerBound, variable.UpperBound, _compensatorStages[variable])).ToArray();

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

    public ToleranceEvaluation EvaluateCompensated(int iterations, CancellationToken cancellationToken, Action<int>? afterStage = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var variable in _compensators)
            _compensatorNominals.TryAdd(variable.Name, variable.Value);
        if (iterations <= 0 || _compensators.Count == 0) return Evaluate();
        if (CompensationOptimizer is not ("Damped Least Squares" or "Coordinate Pattern Search"))
            throw new NotSupportedException("Unsupported tolerance compensation optimizer.");

        var stages = _compensators.GroupBy(variable => _compensatorStages[variable]).OrderBy(group => group.Key).ToArray();
        if (iterations > MonteCarlo.MaximumCompensationIterations / stages.Length)
            throw new ArgumentOutOfRangeException(nameof(iterations), "The total compensation iteration budget was exceeded.");
        if (stages.Length == 1 && stages[0].Key == 1 && afterStage is null && _stageObjectives.Count == 0 && !StrictMerit)
        {
            OptimizerCatalog.Create(CompensationOptimizer).Optimize(CreateCompensationProblem(), iterations);
            cancellationToken.ThrowIfCancellationRequested();
            return Evaluate();
        }

        var history = new List<ToleranceStageValue>(stages.Length);
        var evaluation = default(ToleranceEvaluation);
        foreach (var stage in stages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var objective = _stageObjectives.GetValueOrDefault(stage.Key);
            var measure = objective ?? this;
            try
            {
                OptimizerCatalog.Create(CompensationOptimizer).Optimize(CreateCompensationProblem(stage, objective?.Operands), iterations);
            }
            catch (OptimizationEvaluationException error) when (measure.StrictMerit)
            {
                var failed = ReadCompensators();
                afterStage?.Invoke(stage.Key);
                history.Add(new(stage.Key, measure.InvalidCriterion,
                    failed.Where(variable => variable.Stage == stage.Key).ToArray(), Error: error.Message));
                return new(double.PositiveInfinity, InvalidCriterion, null, failed, history);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (objective is null) evaluation = Evaluate();
            var criterion = objective?.Criterion() ?? evaluation.Criterion;
            var fields = objective?._fieldEvaluator?.Invoke();
            var adjustments = objective is null ? evaluation.Compensators! : ReadCompensators();
            afterStage?.Invoke(stage.Key);
            history.Add(new(stage.Key, criterion, adjustments.Where(variable => variable.Stage == stage.Key).ToArray(), fields));
            if ((objective is not null || measure.StrictMerit) && !double.IsFinite(criterion))
                return new(double.PositiveInfinity, InvalidCriterion, null, adjustments, history);
        }
        if (_stageObjectives.ContainsKey(stages[^1].Key)) evaluation = Evaluate();
        return evaluation with { Stages = history };
    }
}
