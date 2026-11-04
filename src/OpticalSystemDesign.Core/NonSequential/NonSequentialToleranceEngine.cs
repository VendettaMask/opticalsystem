using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Tolerancing;

namespace OpticalSystemDesign.Core.NonSequential;

public static class NonSequentialToleranceEngine
{
    public const string Algorithm = "NscTolerance/1; shared samplers/transport; unbounded manufacturing draws; fixed common ray seed; bounded compensation";

    public static NonSequentialToleranceResult Run(NonSequentialDocument document, MaterialRegistry materials,
        IReadOnlyList<NonSequentialToleranceVariable> tolerances, IReadOnlyList<NonSequentialDesignTarget> targets,
        int samples = 100, int manufacturingSeed = 1, NonSequentialDesignSettings? settings = null,
        IReadOnlyList<NonSequentialDesignVariable>? compensators = null, int compensationTraceEvaluations = 8,
        IProgress<NonSequentialDesignProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        using var scope = ComputationCancellation.Push(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
        settings ??= new(); var source = document.Clone(); NonSequentialDesignEngine.Validate(source, targets, settings);
        if (tolerances is null || tolerances.Count is < 1 or > 64 || tolerances.Any(t => t is null || !double.IsFinite(t.Scale) || t.Scale <= 0 || !Enum.IsDefined(t.Distribution))
            || samples is < 0 or > 10_000 || compensationTraceEvaluations is < 2 or > 256)
            throw new ArgumentException("Invalid manufacturing distributions, sample count or compensation allocation.");
        var variables = tolerances.Select(t => t.Variable).ToArray(); NonSequentialDesignParameters.Validate(source, variables);
        compensators = compensators?.ToArray() ?? []; if (compensators.Count > 0) NonSequentialDesignParameters.Validate(source, compensators);
        var baseline = variables.Select(v => NonSequentialDesignParameters.Read(source, v)).ToArray();
        var budget = new NonSequentialDesignEngine.EvaluationBudget(source, settings, progress);
        var worstTraces = checked(1L + 2 * tolerances.Count + samples * (1L + (compensators.Count > 0 ? compensationTraceEvaluations : 0)));
        if (worstTraces > settings.MaximumTraceEvaluations || worstTraces * budget.RaysPerTrace > settings.MaximumLaunchedRays)
            throw new InvalidOperationException($"公差、灵敏度和全部补偿合计最多 {worstTraces:N0} 次追迹、{worstTraces * budget.RaysPerTrace:N0} 条源光线，超过声明的统一预算；未减少样本。");
        var nominal = NonSequentialDesignEngine.Evaluate(source, materials, targets, settings, settings.TrainingSeed, budget, "nominal");
        var sensitivities = new List<NonSequentialSensitivity>(); var rows = new List<NonSequentialToleranceSample>();
        for (var i = 0; i < variables.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested(); var low = baseline.ToArray(); var high = baseline.ToArray();
            low[i] -= tolerances[i].Scale; high[i] += tolerances[i].Scale;
            try
            {
                var minus = NonSequentialDesignEngine.Evaluate(NonSequentialDesignParameters.Apply(source, variables, low, false), materials,
                    targets, settings, settings.TrainingSeed, budget, "sensitivity minus");
                var plus = NonSequentialDesignEngine.Evaluate(NonSequentialDesignParameters.Apply(source, variables, high, false), materials,
                    targets, settings, settings.TrainingSeed, budget, "sensitivity plus");
                sensitivities.Add(new(i, low[i], high[i], Array.AsReadOnly(minus.Zip(plus).Select(p => (p.Second.Value - p.First.Value) / (2 * tolerances[i].Scale)).ToArray()), null));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArithmeticException or InvalidDataException)
            { sensitivities.Add(new(i, low[i], high[i], null, ex.Message)); }
        }
        var random = new Random(manufacturingSeed);
        var samplers = tolerances.Select(t => t.Distribution == NonSequentialToleranceDistribution.Gaussian
            ? (ISampler)new NormalSampler(0, t.Scale) : new UniformSampler(-t.Scale, t.Scale)).ToArray();
        for (var sample = 0; sample < samples; sample++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = baseline.Select((v, i) => v + samplers[i].Sample(random)).ToArray();
            try
            {
                // Manufacturing draws are retained exactly; optimization bounds are not distribution clipping.
                var perturbed = NonSequentialDesignParameters.Apply(source, variables, values, false);
                var metrics = NonSequentialDesignEngine.Evaluate(perturbed, materials, targets, settings, settings.TrainingSeed, budget, $"sample {sample + 1}");
                IReadOnlyList<double>? adjusted = null;
                if (compensators.Count > 0)
                {
                    var childSettings = settings with { MaximumTraceEvaluations = compensationTraceEvaluations,
                        MaximumLaunchedRays = (long)budget.RaysPerTrace * compensationTraceEvaluations };
                    // This entire allocation was charged in the whole-job preflight; no per-sample budget reset loophole.
                    var result = NonSequentialDesignEngine.Optimize(perturbed, materials, compensators, targets, childSettings,
                        cancellationToken: cancellationToken, costObserver: cost =>
                        {
                            budget.Evaluations += cost.TraceEvaluations; budget.Rays += cost.LaunchedSourceRays;
                            budget.ReservedRays += cost.ReservedSourceRays; budget.Branches += cost.Branches;
                            budget.Segments += cost.Segments; budget.CacheHits += cost.CacheHits;
                        });
                    metrics = result.ValidationMetrics; adjusted = result.BestValues;
                }
                rows.Add(new(sample + 1, Array.AsReadOnly(values), metrics, metrics.All(v => v.Passed), null, adjusted));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArithmeticException or InvalidDataException)
            { rows.Add(new(sample + 1, Array.AsReadOnly(values), null, false, ex.Message)); }
            progress?.Report(new($"sample {sample + 1}/{samples}", budget.Evaluations, budget.Rays));
        }
        return new(Algorithm, NonSequentialSceneHasher.Compute(source, materials), nominal, sensitivities.AsReadOnly(), rows.AsReadOnly(),
            samples > 0 ? rows.Count(r => r.Passed) / (double)samples : null, manufacturingSeed, budget.Snapshot());
    }
}
