using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.NonSequential;

public static class NonSequentialDesignEngine
{
    public const string Algorithm = "NscDesign/1; shared optimizer/transport; fixed training seed; exact intercept moments; independent validation seed";

    public static NonSequentialDesignResult Optimize(NonSequentialDocument document, MaterialRegistry materials,
        IReadOnlyList<NonSequentialDesignVariable> variables, IReadOnlyList<NonSequentialDesignTarget> targets,
        NonSequentialDesignSettings? settings = null, IProgress<NonSequentialDesignProgress>? progress = null,
        CancellationToken cancellationToken = default, Action<NonSequentialDesignCost>? costObserver = null)
    {
        using var scope = ComputationCancellation.Push(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
        settings ??= new(); var source = document.Clone(); NonSequentialDesignParameters.Validate(source, variables);
        Validate(source, targets, settings);
        variables = Array.AsReadOnly(variables.ToArray()); targets = Array.AsReadOnly(targets.ToArray());
        var initial = variables.Select(v => NonSequentialDesignParameters.Read(source, v)).ToArray(); var current = initial.ToArray();
        var cost = new EvaluationBudget(source, settings, progress);
        using var receipt = new CostReceipt(cost, costObserver);
        var initialMetrics = Evaluate(source, materials, targets, settings, settings.TrainingSeed, cost, "initial");
        var best = initial.ToArray(); var training = initialMetrics; var bestMerit = Merit(initialMetrics);
        double[]? lastValues = initial.ToArray(); IReadOnlyList<NonSequentialMetricValue> lastMetrics = initialMetrics;
        var problem = new OptimizationProblem();
        for (var i = 0; i < variables.Count; i++)
        {
            var index = i; var variable = variables[i];
            problem.AddVariable(new DelegateVariable($"{variable.ObjectId:N}/{variable.Domain}/{variable.Parameter}",
                () => current[index], value => current[index] = value, variable.LowerBound, variable.UpperBound, variable.Step));
        }
        IReadOnlyList<NonSequentialMetricValue> Current()
        {
            if (lastValues is not null && current.SequenceEqual(lastValues)) { cost.CacheHits++; return lastMetrics; }
            if (cost.Evaluations >= settings.MaximumTraceEvaluations - 1 || cost.ReservedRays + cost.RaysPerTrace * 2 > settings.MaximumLaunchedRays)
                throw new DesignBudgetException();
            try
            {
                var candidate = NonSequentialDesignParameters.Apply(source, variables, current);
                var metrics = Evaluate(candidate, materials, targets, settings, settings.TrainingSeed, cost, "optimization");
                var merit = Merit(metrics); lastValues = current.ToArray(); lastMetrics = metrics;
                if (merit < bestMerit) { bestMerit = merit; best = current.ToArray(); training = metrics; }
                progress?.Report(new("optimization", cost.Evaluations, cost.Rays, merit)); return metrics;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException && ex is not OptimizationEvaluationException)
            { throw new OptimizationEvaluationException("non-sequential detector objectives", ex.Message); }
        }
        for (var i = 0; i < targets.Count; i++)
        {
            var index = i; var t = targets[i];
            problem.AddOperand(new($"{t.DetectorId:N}/{t.Metric}/{i}", 0, t.Weight,
                () => (Current()[index].Value - t.Target) / t.Tolerance));
        }
        OptimizerResult result;
        try { result = OptimizerCatalog.Create(settings.Optimizer).Optimize(problem, settings.MaximumIterations); }
        catch (DesignBudgetException)
        {
            result = new() { Success = false, Algorithm = settings.Optimizer, StopReason = "LaunchedRayOrTraceBudget",
                Message = "保留已实际评估的最好候选；剩余额度用于独立种子验证。", InitialMerit = Merit(initialMetrics), FinalMerit = bestMerit,
                BestVariables = Array.AsReadOnly(best.ToArray()), FunctionEvaluations = problem.FunctionEvaluationCount };
        }
        var final = NonSequentialDesignParameters.Apply(source, variables, best);
        var validation = Evaluate(final, materials, targets, settings, settings.ValidationSeed, cost, "independent validation");
        progress?.Report(new("completed", cost.Evaluations, cost.Rays, bestMerit));
        return new(Algorithm, NonSequentialSceneHasher.Compute(source, materials), final, variables, Array.AsReadOnly(initial), Array.AsReadOnly(best),
            initialMetrics, training, validation, training.All(v => v.Passed) && validation.All(v => v.Passed), result, cost.Snapshot(), settings);
    }

    internal static void Validate(NonSequentialDocument doc, IReadOnlyList<NonSequentialDesignTarget> targets, NonSequentialDesignSettings settings)
    {
        ArgumentNullException.ThrowIfNull(targets); doc.Validate();
        OptimizationLimits.RequireIterationCount(settings.MaximumIterations); _ = OptimizerCatalog.Create(settings.Optimizer);
        if (settings.MaximumTraceEvaluations is < 2 or > 10_000 || settings.MaximumLaunchedRays is < 2
            || settings.MaximumLaunchedRays > doc.TraceSettings.MaximumTotalSourceRays
            || settings.TrainingSeed == settings.ValidationSeed || settings.RayCountOverride is < 1 or > 1_000_000
            || !double.IsFinite(settings.MaximumTruncatedFraction) || settings.MaximumTruncatedFraction is < 0 or > 1
            || targets.Count is < 1 or > 64 || targets.Any(t => t is null || !Enum.IsDefined(t.Metric) || !double.IsFinite(t.Target)
                || !double.IsFinite(t.Tolerance) || t.Tolerance <= 0 || !double.IsFinite(t.Weight) || t.Weight <= 0
                || t.WavelengthNumber < 0 || t.WavelengthNumber > doc.Wavelengths.Count))
            throw new ArgumentException("Invalid design budget, independent seeds, or 1–64 typed detector targets.");
        var cells = NonSequentialFieldBudget.NumericSlots(doc);
        foreach (var target in targets)
        {
            if (!doc.TryFindObject(target.DetectorId, out var d) || !d.Enabled || d.Parameters is not DetectorRectangleParameters p)
                throw new ArgumentException("Every target needs an enabled rectangular detector.");
            if (target.SourceObjectId is { } source && (!doc.TryFindObject(source, out var s) || s.Parameters is not SourceParameters || !s.Enabled))
                throw new ArgumentException("Target source filter is absent or disabled.");
            _ = NonSequentialPathFilter.Parse(target.PathFilter).Bind(doc);
            cells = checked(cells + NonSequentialFieldBudget.NumericSlots(doc, target.DetectorId) + doc.Wavelengths.Count * 6L);
        }
        if (cells > NonSequentialDocument.MaximumDetectorPixelWavelengths)
            throw new InvalidOperationException("设计目标的探测器累加/快照超过既有数值预算；未缩图。");
    }

    internal static IReadOnlyList<NonSequentialMetricValue> Evaluate(NonSequentialDocument doc, MaterialRegistry materials,
        IReadOnlyList<NonSequentialDesignTarget> targets, NonSequentialDesignSettings settings, int seed, EvaluationBudget budget, string phase)
    {
        ComputationCancellation.ThrowIfCancellationRequested(); budget.Begin(phase);
        var collectors = targets.Select(t => new NonSequentialDetectorCollector(doc, t.SourceObjectId, t.DetectorId, t.PathFilter)).ToArray();
        var sink = new TargetSink(collectors);
        var work = new NonSequentialTraceWork(); NonSequentialDocumentTraceResult trace;
        try { trace = new NonSequentialDocumentTracer().Trace(doc, materials,
            new(RandomSeed: seed, RayCountOverride: settings.RayCountOverride, OutputMode: NonSequentialTraceOutputMode.SummaryOnly) { Work = work }, sink); }
        finally { budget.Rays += work.LaunchedRays; budget.Branches += work.Branches; budget.Segments += work.Segments; }
        if (sink.InvalidBranches > 0 || trace.EnergyBalance.SourcePowerWatts <= 0
            || trace.EnergyBalance.TruncatedPowerWatts / trace.EnergyBalance.SourcePowerWatts > settings.MaximumTruncatedFraction)
            throw new InvalidOperationException("追迹无效或被截断功率超出声明上限，不能作为合格设计评价。");
        return Array.AsReadOnly(targets.Select((t, i) => NonSequentialDesignMetrics.Evaluate(doc, collectors[i].Snapshot().Single(), t)).ToArray());
    }
    internal static double Merit(IReadOnlyList<NonSequentialMetricValue> values) => values.Sum(v => v.Definition.Weight * Math.Pow((v.Value - v.Definition.Target) / v.Definition.Tolerance, 2))
        / values.Sum(v => v.Definition.Weight);

    internal sealed class EvaluationBudget
    {
        internal int Evaluations, CacheHits; internal long Rays, ReservedRays, Branches, Segments;
        internal readonly int RaysPerTrace; private readonly NonSequentialDesignSettings _settings; private readonly IProgress<NonSequentialDesignProgress>? _progress;
        internal EvaluationBudget(NonSequentialDocument doc, NonSequentialDesignSettings settings, IProgress<NonSequentialDesignProgress>? progress)
        {
            _settings = settings; _progress = progress;
            RaysPerTrace = NonSequentialDocumentTracer.SourceRayCount(doc, new(RayCountOverride: settings.RayCountOverride));
            if (RaysPerTrace <= 0 || 2L * RaysPerTrace > settings.MaximumLaunchedRays) throw new InvalidOperationException("需要有效光源，并为评价和独立种子验证预留全部光线预算。");
        }
        internal void Begin(string phase)
        {
            if (Evaluations >= _settings.MaximumTraceEvaluations || ReservedRays + RaysPerTrace > _settings.MaximumLaunchedRays) throw new DesignBudgetException();
            Evaluations++; ReservedRays += RaysPerTrace; _progress?.Report(new(phase, Evaluations, Rays));
        }
        internal NonSequentialDesignCost Snapshot() => new(Evaluations, Rays, Branches, Segments, CacheHits, ReservedRays);
    }
    private sealed class CostReceipt(EvaluationBudget budget, Action<NonSequentialDesignCost>? observer) : IDisposable
    { public void Dispose() => observer?.Invoke(budget.Snapshot()); }
    private sealed class TargetSink(NonSequentialDetectorCollector[] collectors) : INonSequentialTraceSink
    {
        internal long InvalidBranches;
        public void OnBranch(NonSequentialRayBranch branch)
        {
            if (branch.TerminationReason == Raytrace.NonSequentialTerminationReason.InvalidRay) InvalidBranches++;
            foreach (var collector in collectors) collector.OnBranch(branch);
        }
    }
    internal sealed class DesignBudgetException : Exception;
}
