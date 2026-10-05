using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Tolerancing;

namespace OptilandWorkbench.CoatingDesign.Engine;

public sealed record DesignProgress(string Stage, long Evaluations, double? BestMerit = null);
public sealed record ToleranceTrial(int Number, bool Passed, double? Merit, double[] ThicknessNm, double AngleDegrees, RequirementCheck[] Checks, string? Error = null);
public sealed record ToleranceResult(int Trials, int Passed, int Seed, double Percent, double WorstMerit, string Fingerprint,
    ToleranceOptions? Options = null, ToleranceTrial[]? Samples = null);

public sealed partial class DesignService
{
    public const string GeneratorVersion = "coating-templates/2";
    private readonly MaterialRegistry _registry = new();

    private Dictionary<string, IMaterial> Materials(Experiment experiment)
    {
        experiment.Validate();
        var used = experiment.Layers.Select(l => l.MaterialId)
            .Concat([experiment.IncidentId, experiment.SubstrateId, experiment.LowId, experiment.HighId]).ToHashSet();
        var selected = experiment.Materials.Where(m => used.Contains(m.Id)).ToArray();
        foreach (var m in selected)
            if (experiment.Target.MinimumNm < m.MinimumNm - 8e-15 * m.MinimumNm || experiment.Target.MaximumNm > m.MaximumNm + 8e-15 * m.MaximumNm)
                throw new ArgumentException($"材料 {m.Name} 支持 {m.MinimumNm:G7}–{m.MaximumNm:G7} nm，请缩小计算波段。");
        return selected.ToDictionary(m => m.Id, m => m.Resolve(_registry));
    }

    private static CoherentThinFilmSolver Solver(Experiment d, IReadOnlyDictionary<string, IMaterial> materials, FilmLayer[]? layers = null) =>
        new(materials[d.IncidentId], materials[d.SubstrateId], (layers ?? d.Layers)
            .Select(l => new CoherentFilm(materials[l.MaterialId], l.ThicknessNm)));

    public Experiment Generate(Experiment input, IProgress<DesignProgress>? progress = null, CancellationToken token = default)
    {
        using var scope = ComputationCancellation.Push(token);
        var d = input.Snapshot();
        var materials = Materials(d);
        var t = d.Target;
        var seeds = new List<FilmLayer[]>();
        if (t.Kind != DesignKind.Antireflection && materials[d.HighId].RefractiveIndex(t.CenterNm) <= materials[d.LowId].RefractiveIndex(t.CenterNm))
            throw new ArgumentException("高反与窄带结构要求高折射率材料的 n 大于低折射率材料，请检查材料选择。");
        FilmLayer Layer(string id, double waves = 1) => new(id, waves * CoherentThinFilmSolver.QuarterWaveThickness(
            materials[id], materials[d.IncidentId], t.CenterNm, t.AngleDegrees));
        if (t.Kind == DesignKind.Antireflection)
        {
            // Template roles informed by TFStudio AR_TEMPLATES (MIT, see third-party notice).
            seeds.Add([Layer(d.LowId)]);
            seeds.Add([Layer(d.LowId), Layer(d.HighId)]);
            seeds.Add([Layer(d.LowId), Layer(d.HighId, 2), Layer(d.LowId)]);
            for (var count = 4; count <= Math.Min(12, t.MaximumLayers); count += 2)
            {
                seeds.Add(Enumerable.Range(0, count).Select(i => Layer(i % 2 == 0 ? d.LowId : d.HighId)).ToArray());
                seeds.Add(Enumerable.Range(0, count).Select(i => Layer(i % 2 == 0 ? d.LowId : d.HighId, i % 2 == 0 ? 0.8 : 0.35)).ToArray());
            }
        }
        else if (t.Kind == DesignKind.HighReflector)
        {
            for (var count = 2; count <= t.MaximumLayers; count += 2)
                seeds.Add(Enumerable.Range(0, count).Select(i => Layer(i % 2 == 0 ? d.HighId : d.LowId)).ToArray());
            if (t.MaximumLayers == 1) seeds.Add([Layer(d.HighId)]);
        }
        else
        {
            var cavities = t.Structure?.Cavities ?? 1;
            for (var pairs = 1; (cavities + 1) * (pairs * 2 + 1) + cavities <= t.MaximumLayers; pairs++)
            {
                var mirror = Enumerable.Range(0, pairs * 2 + 1).Select(i => Layer(i % 2 == 0 ? d.HighId : d.LowId)).ToArray();
                var stack = new List<FilmLayer>(mirror);
                for (var cavity = 0; cavity < cavities; cavity++)
                {
                    stack.Add(Layer(d.LowId, 2));
                    stack.AddRange(mirror);
                }
                seeds.Add(stack.ToArray());
            }
            if (seeds.Count == 0) throw new ArgumentException($"{cavities} 腔窄带初始结构至少需要 {4 * cavities + 3} 层，请提高层数上限。");
        }
        var grid = ObjectiveGrid(d);
        var ranked = new List<Candidate>();
        foreach (var layers in seeds.Where(x => x.Length <= t.MaximumLayers))
        {
            token.ThrowIfCancellationRequested();
            if (layers.Any(l => l.ThicknessNm < d.Settings.MinimumThicknessNm || l.ThicknessNm > d.Settings.MaximumThicknessNm)) continue;
            var merit = Merit(Objective(d, Solver(d, materials, layers), grid));
            ranked.Add(new Candidate($"初始结构 {layers.Length} 层", layers, merit, false));
            progress?.Report(new("生成初始结构", ranked.Count, merit));
        }
        if (ranked.Count == 0) throw new ArgumentException("厚度或层数限制排除了全部初始结构，请调整约束。");
        ranked = ranked.OrderBy(x => x.Merit).ThenBy(x => x.Layers.Length).ToList();
        var selected = d with { Layers = ranked[0].Layers, Result = null, Tolerance = null, Candidates = ranked.Take(32).ToArray() };
        return Evaluate(selected, new("模板结构枚举", GeneratorVersion, "模板枚举完成", ranked.Count, 0, null), progress, token);
    }

    public Experiment Evaluate(Experiment input, RunRecord? run = null, IProgress<DesignProgress>? progress = null, CancellationToken token = default)
    {
        using var scope = ComputationCancellation.Push(token);
        var d = input.Snapshot();
        var materials = Materials(d);
        var solver = Solver(d, materials);
        var t = d.Target;
        var intervals = d.Settings.PreviewIntervals;
        if (t.Kind == DesignKind.NarrowBand)
        {
            var required = Math.Ceiling((t.MaximumNm - t.MinimumNm) / t.FwhmNm * 64);
            if (required > (ThinFilmSpectrum.MaximumSamples - 1) / 2)
                throw new ArgumentException("带宽相对计算范围过窄，无法在采样上限内复验，请缩小计算范围。");
            intervals = Math.Max(intervals, (int)required);
        }
        if (intervals > (ThinFilmSpectrum.MaximumSamples - 1) / 2)
            throw new ArgumentException("带宽相对计算范围过窄，无法在采样上限内复验，请缩小计算范围。");
        var previous = Scan(intervals);
        var converged = false;
        var samples = previous;
        for (var attempt = 0; attempt < 4 && intervals * 2 < ThinFilmSpectrum.MaximumSamples - 512; attempt++)
        {
            token.ThrowIfCancellationRequested();
            intervals *= 2;
            progress?.Report(new("独立密集采样复验", intervals + 1));
            samples = Scan(intervals);
            converged = Converged(d, previous, samples);
            if (converged) break;
            previous = samples;
        }
        var metrics = Metrics(d, samples);
        var checks = Checks(d, samples, metrics);
        var warnings = new List<string>();
        if (!converged) warnings.Add("采样尚未收敛，不能确认设计达标。请增加采样或缩小范围。");
        if (t.Kind == DesignKind.NarrowBand && metrics.FwhmNanometers is null) warnings.Add("未找到完整的半高通带边缘，FWHM 未定义。");
        var result = new DesignResult(d.Fingerprint(), samples, metrics, checks, converged, samples.Length,
            run ?? new("光谱复验", CoherentThinFilmSolver.Version, "计算完成", samples.Length, 0, null),
            Merit(Objective(d, solver, ObjectiveGrid(d))), warnings.ToArray());
        return d with { Result = result, Tolerance = d.Tolerance?.Fingerprint == d.Fingerprint() ? d.Tolerance : null };

        ThinFilmSample[] Scan(int count)
        {
            var baseSamples = ThinFilmSpectrum.Sample(solver, GridWithAnchors(d, count), t.AngleDegrees, t.Polarization);
            if (t.Kind != DesignKind.NarrowBand) return baseSamples;
            var m = Metrics(d, baseSamples);
            var step = (t.MaximumNm - t.MinimumNm) / count;
            var points = new List<double>();
            foreach (var center in new double?[] { m.PeakWavelengthNanometers, m.LeftHalfMaximumNanometers, m.RightHalfMaximumNanometers })
                if (center.HasValue)
                {
                    var lower = Math.Max(t.MinimumNm, center.Value - 2 * step);
                    var upper = Math.Min(t.MaximumNm, center.Value + 2 * step);
                    points.AddRange(ThinFilmSpectrum.Grid(lower, upper, 32));
                }
            return baseSamples.Concat(ThinFilmSpectrum.Sample(solver, points, t.AngleDegrees, t.Polarization))
                .GroupBy(x => x.WavelengthNanometers).Select(g => g.First()).OrderBy(x => x.WavelengthNanometers).ToArray();
        }
    }

    public Experiment Optimize(Experiment input, IProgress<DesignProgress>? progress = null, CancellationToken token = default)
    {
        using var scope = ComputationCancellation.Push(token);
        var d = input.Snapshot();
        var materials = Materials(d);
        if (d.Layers.Length == 0) throw new ArgumentException("请先生成或添加膜层，再优化。");
        var indices = d.Layers.Select((l, i) => (l, i)).Where(x => x.l.Variable).Select(x => x.i).ToArray();
        if (indices.Length == 0) return Evaluate(d, new("无变量", "none", "全部膜层固定", 0, 0, null), progress, token);
        if (d.Layers.Length > d.Target.MaximumLayers) throw new ArgumentException("当前层数超过上限，请先删除膜层或提高上限。");
        if (d.Layers.Any(l => l.ThicknessNm < d.Settings.MinimumThicknessNm || l.ThicknessNm > d.Settings.MaximumThicknessNm))
            throw new ArgumentException("当前膜层厚度超出高级设置中的约束，请先修正。");
        var values = indices.Select(i => d.Layers[i].ThicknessNm).ToArray();
        var grid = ObjectiveGrid(d);
        var problem = new OptimizationProblem();
        problem.DisableBatching();
        for (var j = 0; j < values.Length; j++)
        {
            var index = j;
            problem.AddVariable(new DelegateVariable($"膜层 {indices[j] + 1} 厚度", () => values[index], v => values[index] = v,
                d.Settings.MinimumThicknessNm, d.Settings.MaximumThicknessNm, stepHint: 0.01));
        }
        double[]? cacheVector = null, cacheResiduals = null;
        var best = double.PositiveInfinity;
        FilmLayer[] bestLayers = d.Layers;
        long evaluations = 0;
        double[] Residuals()
        {
            token.ThrowIfCancellationRequested();
            if (cacheVector is not null && values.SequenceEqual(cacheVector)) return cacheResiduals!;
            var layers = d.Layers.ToArray();
            for (var j = 0; j < indices.Length; j++) layers[indices[j]] = layers[indices[j]] with { ThicknessNm = values[j] };
            var residuals = Objective(d, Solver(d, materials, layers), grid);
            var merit = Merit(residuals);
            if (merit < best) { best = merit; bestLayers = layers; }
            cacheVector = values.ToArray(); cacheResiduals = residuals;
            evaluations++;
            if (evaluations % 10 == 0) progress?.Report(new("优化厚度", evaluations, best));
            return residuals;
        }
        var size = Residuals().Length;
        for (var i = 0; i < size; i++)
        {
            var index = i;
            problem.AddOperand(new Operand($"光谱目标 {i + 1}", 0, 1, () => Residuals()[index]));
        }
        var optimizer = OptimizerCatalog.Create(d.Settings.Optimizer);
        var report = optimizer.Optimize(problem, d.Settings.MaximumIterations);
        token.ThrowIfCancellationRequested();
        var record = new RunRecord(report.Algorithm, report.AlgorithmVersion, report.StopReason,
            report.FunctionEvaluations, report.Iterations, report.RandomSeed);
        var optimized = Evaluate(d with { Layers = bestLayers, Result = null }, record, progress, token);
        var candidates = (d.Candidates ?? []).Append(new Candidate("优化结果", bestLayers, optimized.Result!.Merit, optimized.Result.Passed, record))
            .TakeLast(12).ToArray();
        return optimized with { Candidates = candidates };
    }

    public ToleranceResult Tolerance(Experiment input, IProgress<DesignProgress>? progress = null, CancellationToken token = default)
    {
        var d = input.Snapshot(); d.Validate();
        var random = new Random(d.Settings.RandomSeed);
        var options = d.Settings.Tolerancing ?? new();
        ISampler Sampler(double amount) => options.Distribution == ToleranceDistribution.Normal
            ? new NormalSampler(0, amount) : new UniformSampler(-amount, amount);
        var thicknessSampler = Sampler(d.Settings.TolerancePercent / 100);
        var angleSampler = Sampler(options.AngleDegrees);
        var passed = 0; var worst = 0.0;
        var samples = new List<ToleranceTrial>();
        for (var i = 0; i < d.Settings.ToleranceTrials; i++)
        {
            token.ThrowIfCancellationRequested();
            var offsets = new Dictionary<string, double>();
            var layers = d.Layers.Select((l, index) =>
            {
                var key = options.Correlation switch { ThicknessCorrelation.Common => "all", ThicknessCorrelation.SameMaterial => l.MaterialId, _ => index.ToString(System.Globalization.CultureInfo.InvariantCulture) };
                if (!offsets.TryGetValue(key, out var offset)) offsets[key] = offset = thicknessSampler.Sample(random);
                return l with { ThicknessNm = l.ThicknessNm * (1 + offset) };
            }).ToArray();
            var angle = d.Target.AngleDegrees + angleSampler.Sample(random);
            // Incidence sign is symmetric for the supported isotropic stack; retain the sampled signed value in the record.
            try
            {
                var trial = Evaluate(d with { Layers = layers, Target = d.Target with { AngleDegrees = Math.Abs(angle) }, Result = null, Tolerance = null }, token: token);
                if (trial.Result!.Passed) passed++;
                worst = Math.Max(worst, trial.Result.Merit);
                samples.Add(new(i + 1, trial.Result.Passed, trial.Result.Merit, layers.Select(l => l.ThicknessNm).ToArray(), angle, trial.Result.Checks));
            }
            catch (ArgumentException error)
            {
                samples.Add(new(i + 1, false, null, layers.Select(l => l.ThicknessNm).ToArray(), angle, [], error.Message));
                worst = double.PositiveInfinity;
            }
            progress?.Report(new("公差密集复验", i + 1, worst));
        }
        return new(d.Settings.ToleranceTrials, passed, d.Settings.RandomSeed, d.Settings.TolerancePercent, worst, d.Fingerprint(), options, samples.ToArray());
    }

    private static double[] GridWithAnchors(Experiment d, int intervals) => ThinFilmSpectrum.Grid(d.Target.MinimumNm, d.Target.MaximumNm, intervals)
        .Concat(new[] { d.Target.CenterNm }).Concat((d.Target.StopBands ?? []).SelectMany(b => new[] { b.MinimumNm, b.MaximumNm }))
        .Distinct().Order().ToArray();

    private static double[] ObjectiveGrid(Experiment d)
    {
        var grid = GridWithAnchors(d, d.Settings.OptimizationIntervals).AsEnumerable();
        if (d.Target.Kind == DesignKind.NarrowBand)
        {
            var t = d.Target;
            grid = grid.Concat(ThinFilmSpectrum.Grid(Math.Max(t.MinimumNm, t.CenterNm - 2 * t.FwhmNm),
                Math.Min(t.MaximumNm, t.CenterNm + 2 * t.FwhmNm), 64));
        }
        return grid.Distinct().Order().ToArray();
    }

    private static double[] Objective(Experiment d, CoherentThinFilmSolver solver, double[] grid)
    {
        var t = d.Target;
        var samples = ThinFilmSpectrum.Sample(solver, grid, t.AngleDegrees, t.Polarization);
        if (t.Kind == DesignKind.Antireflection)
            return samples.Select(x => Math.Max(0, x.Power.Reflectance - t.Reflectance) + x.Power.Reflectance * 0.02).ToArray();
        if (t.Kind == DesignKind.HighReflector)
            return samples.Select(x => Math.Max(0, t.Reflectance - x.Power.Reflectance) + (1 - x.Power.Reflectance) * 0.02).ToArray();
        var center = solver.Evaluate(t.CenterNm, t.AngleDegrees, t.Polarization).Transmittance;
        var halfLeft = solver.Evaluate(t.CenterNm - t.FwhmNm / 2, t.AngleDegrees, t.Polarization).Transmittance;
        var halfRight = solver.Evaluate(t.CenterNm + t.FwhmNm / 2, t.AngleDegrees, t.Polarization).Transmittance;
        var residuals = new List<double>
        {
            8 * Math.Max(0, t.PeakTransmittance - center),
            4 * (halfLeft - center / 2), 4 * (halfRight - center / 2)
        };
        foreach (var sample in samples)
        {
            if ((t.StopBands ?? []).Any(b => sample.WavelengthNanometers >= b.MinimumNm && sample.WavelengthNanometers <= b.MaximumNm))
                residuals.Add(Math.Max(0, t.BlockingOd - sample.Power.OpticalDensity));
            else if (Math.Abs(sample.WavelengthNanometers - t.CenterNm) < t.FwhmNm / 4)
                residuals.Add(Math.Max(0, sample.Power.Transmittance - center) * 4);
        }
        return residuals.ToArray();
    }

    private static double Merit(double[] residuals) => residuals.Sum(x => x * x) / residuals.Length;
    private static ThinFilmBandMetrics Metrics(Experiment d, ThinFilmSample[] samples) => ThinFilmSpectrum.Measure(
        d.Target.Kind == DesignKind.NarrowBand ? samples.Where(x => Math.Abs(x.WavelengthNanometers - d.Target.CenterNm) <= 2 * d.Target.FwhmNm).ToArray() : samples);
    private static double MinimumOd(ThinFilmSample[] samples, SpectralBand band) => samples
        .Where(x => x.WavelengthNanometers >= band.MinimumNm && x.WavelengthNanometers <= band.MaximumNm)
        .Min(x => x.Power.OpticalDensity);

    private static bool Converged(Experiment d, ThinFilmSample[] a, ThinFilmSample[] b)
    {
        var x = Metrics(d, a); var y = Metrics(d, b);
        if (Math.Abs(x.MinimumReflectance - y.MinimumReflectance) > 1e-5 || Math.Abs(x.MaximumReflectance - y.MaximumReflectance) > 1e-5) return false;
        if (d.Target.Kind != DesignKind.NarrowBand) return true;
        return x.FwhmNanometers.HasValue && y.FwhmNanometers.HasValue
            && Math.Abs(x.PeakTransmittance - y.PeakTransmittance) <= 1e-5
            && Math.Abs(x.PeakWavelengthNanometers - y.PeakWavelengthNanometers) <= Math.Min(0.02, d.Target.CenterToleranceNm / 10)
            && Math.Abs(x.FwhmNanometers.Value - y.FwhmNanometers.Value) <= Math.Min(0.02, d.Target.FwhmToleranceNm / 10)
            && (d.Target.StopBands ?? []).All(band => Math.Abs(MinimumOd(a, band) - MinimumOd(b, band)) <= 0.005);
    }

    private static RequirementCheck[] Checks(Experiment d, ThinFilmSample[] samples, ThinFilmBandMetrics m)
    {
        var t = d.Target; var s = d.Settings;
        var checks = new List<RequirementCheck>
        {
            new("层数", d.Layers.Length, $"≤ {t.MaximumLayers}", d.Layers.Length <= t.MaximumLayers),
            new("物理厚度约束 (nm)", null, $"{s.MinimumThicknessNm:G8}–{s.MaximumThicknessNm:G8}",
                d.Layers.All(l => l.ThicknessNm >= s.MinimumThicknessNm && l.ThicknessNm <= s.MaximumThicknessNm))
        };
        if (t.Kind == DesignKind.Antireflection)
            checks.Add(new("波段最大 R (%)", m.MaximumReflectance * 100, $"≤ {t.Reflectance * 100:G8}", m.MaximumReflectance <= t.Reflectance));
        else if (t.Kind == DesignKind.HighReflector)
            checks.Add(new("波段最低 R (%)", m.MinimumReflectance * 100, $"≥ {t.Reflectance * 100:G8}", m.MinimumReflectance >= t.Reflectance));
        else
        {
            checks.Add(new("峰值位置 (nm)", m.PeakWavelengthNanometers, $"{t.CenterNm:G8} ± {t.CenterToleranceNm:G8}", Math.Abs(m.PeakWavelengthNanometers - t.CenterNm) <= t.CenterToleranceNm));
            checks.Add(new("FWHM (nm)", m.FwhmNanometers, $"{t.FwhmNm:G8} ± {t.FwhmToleranceNm:G8}", m.FwhmNanometers.HasValue && Math.Abs(m.FwhmNanometers.Value - t.FwhmNm) <= t.FwhmToleranceNm));
            checks.Add(new("峰值 T (%)", m.PeakTransmittance * 100, $"≥ {t.PeakTransmittance * 100:G8}", m.PeakTransmittance >= t.PeakTransmittance));
            foreach (var band in t.StopBands ?? [])
            {
                var od = MinimumOd(samples, band);
                checks.Add(new($"截止 OD：{band.MinimumNm:G8}–{band.MaximumNm:G8} nm", od, $"≥ {t.BlockingOd:G8}", od >= t.BlockingOd));
            }
        }
        return checks.ToArray();
    }
}
