using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Tolerancing;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed record DuplexOptimizationRecord(DuplexCoatingOptions Options, int IndependentVariables,
    FilmLayer[] InitialFront, FilmLayer[] InitialBack, long PowerEvaluations)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DuplexAnalyticWork? Analytic { get; init; }
}
public sealed record DuplexAnalyticWork(long SensitivityEvaluations, long AnalyticJacobians, int RestorationIterations,
    int QualityIterations, int DenseValidations, long DenseSpectrumPoints, long DensePowerEvaluations, int RejectedProtectedCandidates, int IterationBudget);

public sealed partial class DesignService
{
    public const string DuplexDesignProtocol = "duplex-target-constraints/1";
    public static IEnumerable<string> EvaluationMaterialIds(Experiment d) => d.Duplex?.Evaluate switch
    {
        CoatingEvaluationSurface.Back => d.EffectiveBackLayers.Select(l => l.MaterialId).Concat([d.SubstrateId, d.StackGeometry!.ExitMediumId]),
        CoatingEvaluationSurface.WholePlate => d.Layers.Concat(d.EffectiveBackLayers).Select(l => l.MaterialId)
            .Concat([d.IncidentId, d.SubstrateId, d.StackGeometry!.ExitMediumId]),
        _ => d.Layers.Select(l => l.MaterialId).Concat([d.IncidentId, d.SubstrateId])
    };
    private static Func<double, double, ThinFilmPolarization, ThinFilmPower> PowerEvaluator(Experiment d,
        IReadOnlyDictionary<string, IMaterial> materials, bool display = false)
    {
        CoherentThinFilmSolver Rear() => new(materials[d.StackGeometry!.ExitMediumId], materials[d.SubstrateId],
            d.EffectiveBackLayers.Select(l => new CoherentFilm(materials[l.MaterialId], l.ThicknessNm)));
        if (d.Duplex?.Evaluate == CoatingEvaluationSurface.Back) return Rear().Evaluate;
        var front = Solver(d, materials);
        if (d.Duplex?.Evaluate == CoatingEvaluationSurface.WholePlate)
        {
            var rear = Rear();
            return (w, a, p) => front.EvaluateIncoherentSubstrate(rear, d.StackGeometry!.SubstrateThicknessMm * 1e6, w, a, p);
        }
        if (d.Duplex is null && display && d.StackGeometry is { IncludeBackSurface: true } geometry)
            return (w, a, p) => front.EvaluateIncoherentSubstrate(materials[geometry.ExitMediumId], geometry.SubstrateThicknessMm * 1e6, w, a, p);
        return front.Evaluate;
    }
    public static string EvaluationLabel(Experiment d) => d.Duplex?.Evaluate switch
    { CoatingEvaluationSurface.Front => "正面", CoatingEvaluationSurface.Back => "背面", CoatingEvaluationSurface.WholePlate => "整片", _ => "正面（旧单面验收）" };
    public static string OptimizationLabel(Experiment d) => d.Duplex?.Optimize switch
    { CoatingDesignSurface.Back => "背面", CoatingDesignSurface.Both => d.Duplex.Symmetric ? "两面（对称）" : "两面", _ => "正面" };

    private Experiment OptimizeDuplex(Experiment input, IProgress<DesignProgress>? progress, CancellationToken token,
        int? iterations = null, bool certify = true)
    {
        if (input.Duplex?.OptimizationVersion == 2) return OptimizeDuplexAnalytic(input, progress, token, iterations, certify);
        using var scope = ComputationCancellation.Push(token);
        var d = input.Snapshot(); var materials = Materials(d); var options = d.Duplex!;
        var variables = new List<(bool Back, int Index)>();
        if (options.Optimize != CoatingDesignSurface.Back)
            variables.AddRange(d.Layers.Select((l, i) => (l, i)).Where(x => x.l.Variable).Select(x => (false, x.i)));
        if (options.Optimize != CoatingDesignSurface.Front && !options.Symmetric)
            variables.AddRange(d.EffectiveBackLayers.Select((l, i) => (l, i)).Where(x => x.l.Variable).Select(x => (true, x.i)));
        var all = d.Layers.Concat(d.EffectiveBackLayers).ToArray();
        if (all.Length == 0) throw new ArgumentException("请先添加正面或背面膜层。");
        if (d.Layers.Length > d.Target.MaximumLayers || d.EffectiveBackLayers.Length > d.Target.MaximumLayers
            || all.Any(l => l.ThicknessNm < d.Settings.MinimumThicknessNm || l.ThicknessNm > d.Settings.MaximumThicknessNm))
            throw new ArgumentException("双面膜层超出原层数或厚度约束，请先修正。");
        var grid = ObjectiveGrid(d); long evaluations = 0;
        var values = variables.Select(v => (v.Back ? d.EffectiveBackLayers : d.Layers)[v.Index].ThicknessNm).ToArray();
        var best = d; var bestScore = double.PositiveInfinity; double[]? cacheVector = null, cacheResiduals = null;
        Experiment WithValues()
        {
            var front = d.Layers.ToArray(); var back = d.EffectiveBackLayers.ToArray();
            for (var j = 0; j < variables.Count; j++)
            { var v = variables[j]; var layers = v.Back ? back : front; layers[v.Index] = layers[v.Index] with { ThicknessNm = values[j] }; }
            return d with { Layers = front, BackLayers = options.Symmetric ? null : back, Result = null, Tolerance = null };
        }
        double[] Residuals()
        {
            token.ThrowIfCancellationRequested();
            if (cacheVector is not null && values.SequenceEqual(cacheVector)) return cacheResiduals!;
            var current = WithValues(); var evaluate = PowerEvaluator(current, materials);
            var samples = ThinFilmSpectrum.Sample(w => evaluate(w, d.Target.AngleDegrees, d.Target.Polarization), grid); evaluations += samples.Length;
            var residuals = DuplexResiduals(current, samples); var score = Merit(residuals);
            if (score < bestScore) { bestScore = score; best = current; }
            cacheVector = values.ToArray(); cacheResiduals = residuals;
            progress?.Report(new("双面厚度优化", evaluations, bestScore)); return residuals;
        }
        var problem = new OptimizationProblem(); problem.DisableBatching();
        for (var j = 0; j < variables.Count; j++)
        {
            var index = j; var v = variables[j];
            problem.AddVariable(new DelegateVariable($"{(v.Back ? "背面" : "正面")} {v.Index + 1}", () => values[index], x => values[index] = x,
                d.Settings.MinimumThicknessNm, d.Settings.MaximumThicknessNm, stepHint: .01));
        }
        var size = Residuals().Length;
        for (var j = 0; j < size; j++) { var index = j; problem.AddOperand(new Operand($"双面光谱目标 {j + 1}", 0, 1, () => Residuals()[index])); }
        RunRecord run;
        if (variables.Count == 0) run = new("无变量", "none", "全部可优化膜层固定", 1, 0, null);
        else
        {
            var report = OptimizerCatalog.Create(d.Settings.Optimizer).Optimize(problem, iterations ?? d.Settings.MaximumIterations);
            run = new(report.Algorithm, report.AlgorithmVersion, report.StopReason, report.FunctionEvaluations, report.Iterations, report.RandomSeed);
        }
        run = run with
        {
            DesignProtocol = DuplexDesignProtocol,
            Duplex = new(options, variables.Count, d.Layers, d.EffectiveBackLayers, evaluations)
        };
        token.ThrowIfCancellationRequested();
        if (!certify) return best with { Candidates = [new("双面短程候选", best.Layers, bestScore, false, run) { BackLayers = best.BackLayers }] };
        var baseline = Evaluate(d, progress: progress, token: token);
        var result = Evaluate(best, run, progress, token);
        var a = AssessGeneral(baseline, baseline.Result!.Spectrum); var b = AssessGeneral(result, result.Result!.Spectrum);
        // The same per-target envelope protects already met gates and unfinished targets at dense certification.
        if (!WithinGeneralTargetEnvelope(b, a) || baseline.Result.Passed && !result.Result.Passed)
            result = baseline with { Result = baseline.Result with { Run = run with { StopReason = "DenseValidationRetainedInput" } } };
        return result with
        {
            Candidates = (d.Candidates ?? []).Append(new Candidate("双面优化结果", result.Layers,
            result.Result!.Merit, result.Result.Passed, result.Result.Run)
            { BackLayers = result.BackLayers }).TakeLast(12).ToArray()
        };
    }
    private static double[] DuplexResiduals(Experiment d, ThinFilmSample[] samples)
    {
        var target = AssessGeneral(d, samples).Violations;
        if (d.Target.Kind == DesignKind.NarrowBand) return target;
        var ar = d.Target.Kind == DesignKind.Antireflection;
        var scale = Math.Max(.001, ar ? d.Target.Reflectance : 1 - d.Target.Reflectance);
        return samples.Select(s => (Math.Max(0, ar ? s.Power.Reflectance - d.Target.Reflectance : d.Target.Reflectance - s.Power.Reflectance)
            + .02 * (ar ? s.Power.Reflectance : 1 - s.Power.Reflectance)) / (scale * Math.Sqrt(samples.Length))).ToArray();
    }

    private Experiment GenerateDuplex(Experiment input, bool search, IProgress<DesignProgress>? progress, CancellationToken token)
    {
        using var scope = ComputationCancellation.Push(token);
        var d = input.Snapshot(); d.Validate(); var options = d.Duplex!;
        var random = new Random(d.Settings.RandomSeed); var searchOptions = d.Settings.Search ?? new();
        FilmLayer[][] Seeds(bool back)
        {
            var original = back ? d.EffectiveBackLayers : d.Layers;
            var movable = back ? options.Optimize != CoatingDesignSurface.Front && !options.Symmetric : options.Optimize != CoatingDesignSurface.Back;
            if (!movable || original.Any(l => !l.Variable) || search && !searchOptions.VaryStructure && original.Length > 0) return [original];
            var single = d with
            {
                SchemaVersion = Experiment.CurrentSchema,
                Duplex = null,
                BackLayers = null,
                StackGeometry = null,
                IncidentId = back ? d.StackGeometry!.ExitMediumId : d.IncidentId,
                Layers = original,
                Result = null,
                Candidates = null,
                Tolerance = null
            };
            var generated = Generate(single, progress, token);
            return new[] { original }.Concat((generated.Candidates ?? []).Select(c => c.Layers)).Append(generated.Layers)
                .Where(l => l.Length > 0).DistinctBy(LayerIdentity).Take(8).ToArray();
        }
        var fronts = Seeds(false); var backs = options.Symmetric ? new FilmLayer[][] { [] } : Seeds(true);
        var pool = new List<Experiment>();
        foreach (var front in fronts) foreach (var back in backs)
            pool.Add(d with { Layers = front, BackLayers = options.Symmetric ? null : back, Result = null, Candidates = null, Tolerance = null });
        if (search)
        {
            var source = pool.ToArray();
            for (var i = 0; i < searchOptions.Starts; i++)
            {
                var seed = source[i % source.Length];
                FilmLayer[] Jitter(FilmLayer[] layers, bool movable) => layers.Select(l => !movable || !l.Variable ? l : l with
                { ThicknessNm = Math.Clamp(l.ThicknessNm * (1 + (2 * random.NextDouble() - 1) * searchOptions.JitterPercent / 100), d.Settings.MinimumThicknessNm, d.Settings.MaximumThicknessNm) }).ToArray();
                pool.Add(seed with
                {
                    Layers = Jitter(seed.Layers, options.Optimize != CoatingDesignSurface.Back),
                    BackLayers = options.Symmetric ? null : Jitter(seed.EffectiveBackLayers, options.Optimize != CoatingDesignSurface.Front)
                });
            }
        }
        var materials = Materials(d); var grid = ObjectiveGrid(d); var ranked = new List<(Experiment Design, double Merit)>();
        foreach (var seed in pool)
        {
            token.ThrowIfCancellationRequested(); var evaluate = PowerEvaluator(seed, materials);
            var score = Merit(DuplexResiduals(seed, ThinFilmSpectrum.Sample(w => evaluate(w, d.Target.AngleDegrees, d.Target.Polarization), grid)));
            ranked.Add((seed, score)); progress?.Report(new("双面结构光谱筛选", ranked.Count, score));
        }
        var screens = new List<(Experiment Design, double Merit)>();
        foreach (var seed in ranked.OrderBy(c => c.Merit).Take(Math.Min(8, searchOptions.Starts)))
        {
            var refined = OptimizeDuplex(seed.Design, progress, token, Math.Min(2, d.Settings.MaximumIterations), certify: false);
            screens.Add((refined, refined.Candidates![0].Merit));
        }
        Experiment? best = null;
        foreach (var seed in screens.OrderBy(c => c.Merit).Take(search ? Math.Min(3, searchOptions.Starts) : 1))
        {
            var trial = search ? OptimizeDuplex(seed.Design, progress, token) : Evaluate(seed.Design, progress: progress, token: token);
            if (best is null || trial.Result!.Passed && !best.Result!.Passed || trial.Result!.Passed == best.Result!.Passed
                && PreferGeneral(AssessGeneral(trial, trial.Result.Spectrum), AssessGeneral(best, best.Result.Spectrum))) best = trial;
        }
        if (best is null) throw new ArgumentException("没有满足双面结构限制的候选。");
        // Preserve the existing input as a certified finalist, rather than overwriting it with a worse new topology.
        if (d.Layers.Length + d.EffectiveBackLayers.Length > 0)
        {
            var baseline = Evaluate(d, progress: progress, token: token);
            if (!WithinGeneralTargetEnvelope(AssessGeneral(best, best.Result!.Spectrum), AssessGeneral(baseline, baseline.Result!.Spectrum))
                || baseline.Result.Passed && !best.Result.Passed) best = baseline;
        }
        return best with
        {
            Candidates = screens.OrderBy(c => c.Merit).Select((s, i) => new Candidate($"双面候选 {i + 1}",
            s.Design.Layers, s.Merit, false, s.Design.Candidates?.FirstOrDefault()?.Run)
            { BackLayers = s.Design.BackLayers })
            .Append(new Candidate("双面正式保留", best.Layers, best.Result!.Merit, best.Result.Passed, best.Result.Run) { BackLayers = best.BackLayers }).ToArray()
        };
    }

    private ToleranceResult ToleranceDuplex(Experiment input, IProgress<DesignProgress>? progress, CancellationToken token)
    {
        using var scope = ComputationCancellation.Push(token);
        var d = input.Snapshot(); d.Validate(); var random = new Random(d.Settings.RandomSeed);
        var options = d.Settings.Tolerancing ?? new();
        ISampler Sampler(double amount) => options.Distribution == ToleranceDistribution.Normal ? new NormalSampler(0, amount) : new UniformSampler(-amount, amount);
        var thicknessSampler = Sampler(d.Settings.TolerancePercent / 100); var angleSampler = Sampler(options.AngleDegrees);
        var samples = new List<ToleranceTrial>(); var passed = 0; var worst = 0.0;
        for (var i = 0; i < d.Settings.ToleranceTrials; i++)
        {
            token.ThrowIfCancellationRequested(); var offsets = new Dictionary<string, double>();
            FilmLayer[] Perturb(FilmLayer[] layers, string side) => layers.Select((l, index) =>
            {
                var key = options.Correlation switch { ThicknessCorrelation.Common => "all", ThicknessCorrelation.SameMaterial => l.MaterialId, _ => $"{side}:{index}" };
                if (!offsets.TryGetValue(key, out var offset)) offsets[key] = offset = thicknessSampler.Sample(random);
                return l with { ThicknessNm = l.ThicknessNm * (1 + offset) };
            }).ToArray();
            var front = Perturb(d.Layers, "front"); var back = Perturb(d.EffectiveBackLayers, "back");
            var angle = d.Target.AngleDegrees + angleSampler.Sample(random);
            try
            {
                // Symmetric design variables do not imply correlated manufacturing errors; the selected sampler controls that.
                var trial = Evaluate(d with
                {
                    Layers = front,
                    BackLayers = back,
                    Duplex = d.Duplex! with { Symmetric = false },
                    Target = d.Target with { AngleDegrees = Math.Abs(angle) },
                    Result = null,
                    Candidates = null,
                    Tolerance = null
                }, token: token);
                if (trial.Result!.Passed) passed++; worst = Math.Max(worst, trial.Result.Merit);
                samples.Add(new(i + 1, trial.Result.Passed, trial.Result.Merit, front.Select(l => l.ThicknessNm).ToArray(), angle, trial.Result.Checks)
                { BackThicknessNm = back.Select(l => l.ThicknessNm).ToArray() });
            }
            catch (Exception error) when (error is ArgumentException or ArithmeticException or NotSupportedException)
            {
                worst = double.PositiveInfinity;
                samples.Add(new(i + 1, false, null, front.Select(l => l.ThicknessNm).ToArray(), angle, [], error.Message)
                { BackThicknessNm = back.Select(l => l.ThicknessNm).ToArray() });
            }
            progress?.Report(new("双面公差密集复验", i + 1, worst));
        }
        return new(d.Settings.ToleranceTrials, passed, d.Settings.RandomSeed, d.Settings.TolerancePercent, worst, d.Fingerprint(), options, samples.ToArray());
    }
}
