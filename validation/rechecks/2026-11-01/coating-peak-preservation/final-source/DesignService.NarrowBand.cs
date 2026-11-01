using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Tolerancing;

namespace OpticalSystemDesign.CoatingDesign.Engine;

// Mirror/cavity vectors are indexed from the substrate, as in the pinned reference.
// This describes the seed before continuous refinement, never the optimized thicknesses.
public sealed record NarrowBandPrototype(int[] MirrorLayers, int[] CavityOrders,
    bool SubstrateStartsHigh = true, string Family = "manual");
public sealed record IncidentMatchingTrial(string Pattern, FilmLayer[] Layers, ThinFilmPower Before,
    ThinFilmPower After, RunRecord Run)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public FilmLayer[]? InitialMatchingLayers { get; init; }
}
public sealed record NarrowBandCompetitionRecord(int InitialStructures, int DiscreteEvaluations, int DiscreteEvaluationLimit,
    int IntegerRounds, int ShortCandidateLimit, IncidentMatchingTrial[] MatchingTrials);

public sealed partial class DesignService
{
    public const string NarrowBandCompetitionProtocol = "coupled-narrowband-competition/3";
    private const int InitialStructureLimit = 512;
    private const int DiscreteEvaluationLimit = 1024;
    private const int ShortCandidateLimit = 12;
    private const int MatchingCandidateLimit = 8;

    private static string? Protocol(Experiment d) => d.Settings.DesignProtocolVersion switch
    { 2 => TargetBalancedProtocol, 3 => NarrowBandCompetitionProtocol, 4 => GeneralCoatingProtocol, _ => null };

    public FilmLayer[] BuildNarrowBandPrototype(Experiment input, NarrowBandPrototype prototype)
    {
        ArgumentNullException.ThrowIfNull(prototype);
        prototype = prototype with { MirrorLayers = prototype.MirrorLayers?.ToArray()!, CavityOrders = prototype.CavityOrders?.ToArray()! };
        var d = input.Snapshot(); var materials = Materials(d);
        if (d.Target.Kind != DesignKind.NarrowBand) throw new ArgumentException("镜段/腔阶原型仅用于窄带设计。");
        return PrototypeLayers(d, materials, prototype)
            ?? throw new ArgumentException("镜段/腔阶原型不满足腔数、层数或膜厚约束。");
    }

    // All optical thicknesses come from the shared Core, including oblique/absorbing materials.
    private static FilmLayer[]? PrototypeLayers(Experiment d, IReadOnlyDictionary<string, IMaterial> materials, NarrowBandPrototype p)
    {
        var q = d.Target.Structure?.Cavities ?? 1;
        if (p.MirrorLayers is null || p.CavityOrders is null || p.MirrorLayers.Length != q + 1 || p.CavityOrders.Length != q
            || p.MirrorLayers.Any(m => m is < 1 or > 41) || p.CavityOrders.Any(k => k is < 1 or > 400)
            || p.MirrorLayers.Sum() + q > d.Target.MaximumLayers) return null;
        var layers = new List<FilmLayer>(); var position = 0;
        void Add(double quarters)
        {
            var id = (++position % 2 == 1) == p.SubstrateStartsHigh ? d.HighId : d.LowId;
            layers.Add(new(id, quarters * CoherentThinFilmSolver.QuarterWaveThickness(materials[id], materials[d.IncidentId],
                d.Target.CenterNm, d.Target.AngleDegrees)));
        }
        for (var i = 0; i <= q; i++)
        {
            for (var j = 0; j < p.MirrorLayers[i]; j++) Add(1);
            if (i < q) Add(2 * p.CavityOrders[i]);
        }
        layers.Reverse();
        return WithinBounds(d, layers) ? layers.ToArray() : null;
    }

    private static bool WithinBounds(Experiment d, IEnumerable<FilmLayer> layers)
    {
        var array = layers as FilmLayer[] ?? layers.ToArray();
        return array.Length is > 0 && array.Length <= d.Target.MaximumLayers && array.All(l => double.IsFinite(l.ThicknessNm)
            && l.ThicknessNm >= d.Settings.MinimumThicknessNm && l.ThicknessNm <= d.Settings.MaximumThicknessNm);
    }

    private sealed record NarrowSeed(string Name, FilmLayer[] Layers, double Merit, NarrowBandPrototype? Origin = null);

    private Experiment GenerateNarrowBand(Experiment d, IReadOnlyDictionary<string, IMaterial> materials, bool fullSearch,
        IProgress<DesignProgress>? progress, CancellationToken token)
    {
        var suitability = AssessMaterials(d, materials, token);
        progress?.Report(new("材料适用性诊断：" + (suitability.ReferenceLosslessPrototypeApplicable ? "满足参考无吸收前提" : "真实吸收材料，数值匹配"), 0));
        if (materials[d.HighId].RefractiveIndex(d.Target.CenterNm) <= materials[d.LowId].RefractiveIndex(d.Target.CenterNm))
            throw new ArgumentException("高折射率材料的 n 必须大于低折射率材料。");
        var grid = ObjectiveGrid(d); var evaluated = new Dictionary<string, NarrowSeed>();
        long discrete = 0; var rounds = 0; var initialCount = 0;
        var structural = d.Layers.Length == 0 || ((d.Settings.Search?.VaryStructure ?? true) && d.Layers.All(l => l.Variable));
        double Score(FilmLayer[] layers) => ObjectiveMerit(d, Objective(d, Solver(d, materials, layers), grid));
        void Add(NarrowBandPrototype p)
        {
            token.ThrowIfCancellationRequested();
            if (discrete >= DiscreteEvaluationLimit) return;
            var layers = PrototypeLayers(d, materials, p);
            if (layers is null) return;
            var key = LayerIdentity(layers);
            if (evaluated.ContainsKey(key)) return;
            var seed = new NarrowSeed($"{p.Family} [{string.Join(',', p.MirrorLayers)}] / [{string.Join(',', p.CavityOrders)}]", layers, Score(layers), p);
            evaluated.Add(key, seed); discrete++;
            if (discrete % 16 == 0) progress?.Report(new("镜段/腔阶竞争（粗采样，不判达标）", discrete, seed.Merit));
        }
        if (d.Layers.Length > 0)
        {
            if (!WithinBounds(d, d.Layers)) throw new ArgumentException("当前结构超出层数或膜厚约束，请先修正。");
            evaluated[LayerIdentity(d.Layers)] = new("保留当前结构", d.Layers, Score(d.Layers)); discrete++;
        }
        if (structural)
        {
            var q = d.Target.Structure?.Cavities ?? 1;
            var nH = materials[d.HighId].RefractiveIndex(d.Target.CenterNm); var nL = materials[d.LowId].RefractiveIndex(d.Target.CenterNm);
            var nS = materials[d.SubstrateId].RefractiveIndex(d.Target.CenterNm);
            // TFStudio coupledPrototype: integer matching proposal only; actual Core spectra decide its value.
            var delta = (int)Math.Clamp(Math.Floor(Math.Log(nS / nH) / Math.Log(nL / nH) + .5), 0, 41);
            var minimumQuarter = new[] { d.LowId, d.HighId }.Min(id => CoherentThinFilmSolver.QuarterWaveThickness(
                materials[id], materials[d.IncidentId], d.Target.CenterNm, d.Target.AngleDegrees));
            var maximumOrder = Math.Min(400, (int)Math.Min(400, Math.Floor(d.Settings.MaximumThicknessNm / (2 * minimumQuarter))));
            for (var order = 1; order <= maximumOrder && discrete < InitialStructureLimit; order++)
                for (var m = 1; m <= Math.Min(41, (d.Target.MaximumLayers - q) / (q + 1)) && discrete < InitialStructureLimit; m++)
                    foreach (var highFirst in new[] { true, false })
                    {
                        if (discrete >= InitialStructureLimit) break;
                        Add(new(Enumerable.Repeat(m, q + 1).ToArray(), Enumerable.Repeat(order, q).ToArray(), highFirst, "均匀镜段"));
                        if (discrete < InitialStructureLimit)
                            Add(new(Enumerable.Range(0, q + 1).Select(i => i == 0 || i == q ? m : Math.Max(1, 2 * m + 2 * delta - 1)).ToArray(),
                                Enumerable.Repeat(order, q).ToArray(), highFirst, "耦合镜段"));
                    }
            initialCount = (int)discrete;
            // Beam coordinate competition changes each mirror/cavity independently, plus symmetric compound moves.
            // The complete starting set remains available: integer descent never deletes its seed.
            for (var round = 0; round < 2 && discrete < DiscreteEvaluationLimit; round++)
            {
                rounds++;
                foreach (var seed in DiverseNarrowSeeds(evaluated.Values, 8).Where(s => s.Origin is not null).ToArray())
                    foreach (var p in Neighbors(seed.Origin!)) Add(p);
            }
        }
        else initialCount = (int)discrete;
        if (evaluated.Count == 0) throw new ArgumentException("层数和膜厚约束排除了所有镜段/腔阶起点。");
        var pool = evaluated.Values.ToList(); var matches = new List<IncidentMatchingTrial>();
        if (structural)
            foreach (var seed in DiverseNarrowSeeds(pool, MatchingCandidateLimit).ToArray())
            {
                progress?.Report(new("空气侧匹配（实际材料与角度）", matches.Sum(m => m.Run.Evaluations)));
                foreach (var trial in MatchIncidentMedium(d with { Layers = seed.Layers }, token))
                {
                    matches.Add(trial);
                    pool.Add(new(seed.Name + " · " + trial.Pattern, trial.Layers, Score(trial.Layers), seed.Origin));
                }
            }
        var screens = new List<ScreeningRun>(); var refinedSeeds = new List<NarrowSeed>();
        foreach (var seed in DiverseNarrowSeeds(pool, ShortCandidateLimit))
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(new($"窄带候选短程优化 {screens.Count + 1}", screens.Sum(s => s.Run.Evaluations)));
            var refined = RefineThickness(d with { Layers = seed.Layers }, materials, grid, Math.Min(4, d.Settings.MaximumIterations), progress, token);
            screens.Add(new(screens.Count + 1, seed.Layers, refined.Layers, refined.Merit, refined.Run) { OriginPrototype = seed.Origin });
            refinedSeeds.Add(seed with { Layers = refined.Layers, Merit = refined.Merit });
        }
        // A worse continuous/matching trial can never displace its better unmodified seed.
        var finalists = refinedSeeds.Concat(DiverseNarrowSeeds(pool, ShortCandidateLimit)).DistinctBy(s => LayerIdentity(s.Layers))
            .OrderBy(s => s.Merit).Take(ShortCandidateLimit).ToList();
        if (d.Layers.Length > 0 && finalists.All(s => LayerIdentity(s.Layers) != LayerIdentity(d.Layers)))
            finalists.Add(evaluated[LayerIdentity(d.Layers)]);
        long evaluations = screens.Sum(s => s.Run.Evaluations) + matches.Sum(m => m.Run.Evaluations);
        var iterations = screens.Sum(s => s.Run.Iterations) + matches.Sum(m => m.Run.Iterations);
        var candidates = new List<Candidate>(); Experiment? selected = null;
        if (fullSearch)
        {
            var starts = d.Settings.Search?.Starts ?? 6;
            var random = new Random(d.Settings.RandomSeed);
            var jitter = new UniformSampler(-(d.Settings.Search?.JitterPercent ?? 12) / 100, (d.Settings.Search?.JitterPercent ?? 12) / 100);
            for (var i = 0; i < starts; i++)
            {
                token.ThrowIfCancellationRequested(); var seed = finalists[i % finalists.Count];
                if (i >= finalists.Count)
                    seed = seed with
                    {
                        Layers = seed.Layers.Select(l => l.Variable ? l with
                        { ThicknessNm = Math.Clamp(l.ThicknessNm * (1 + jitter.Sample(random)), d.Settings.MinimumThicknessNm, d.Settings.MaximumThicknessNm) } : l).ToArray()
                    };
                progress?.Report(new($"窄带完整优化 {i + 1}/{starts}", evaluations));
                var candidate = Optimize(d with { Layers = seed.Layers, Result = null, Candidates = null, Tolerance = null }, progress, token);
                var result = candidate.Result!; evaluations += result.Run.Evaluations; iterations += result.Run.Iterations;
                candidates.Add(new(seed.Name, candidate.Layers, result.Merit, result.Passed, result.Run) { OriginPrototype = seed.Origin });
                if (selected is null || result.Passed && !selected.Result!.Passed
                    || result.Passed == selected.Result!.Passed && result.Merit < selected.Result.Merit) selected = candidate;
            }
            // Original user geometry is a real fallback, not just an unverified coarse ranking row.
            if (d.Layers.Length > 0)
            {
                var original = Evaluate(d with { Result = null, Candidates = null, Tolerance = null }, progress: progress, token: token);
                if (selected is null || original.Result!.Passed && !selected.Result!.Passed
                    || original.Result!.Passed == selected.Result!.Passed && original.Result.Merit < selected.Result.Merit) selected = original;
                candidates.Add(new("原结构完整复验", original.Layers, original.Result!.Merit, original.Result.Passed));
            }
        }
        else
        {
            selected = Evaluate(d with { Layers = finalists[0].Layers, Result = null, Candidates = null, Tolerance = null }, progress: progress, token: token);
            candidates.AddRange(finalists.Select(s => new Candidate(s.Name, s.Layers, s.Merit, false) { OriginPrototype = s.Origin }));
        }
        var record = new RunRecord(fullSearch ? "镜段/腔阶竞争 + 空气侧匹配 + 多起点优化" : "镜段/腔阶竞争 + 空气侧匹配 + 短程优化",
            NarrowBandCompetitionProtocol, "有界候选预算完成；达标由原独立密集复验判定", evaluations, iterations, d.Settings.RandomSeed)
        {
            DesignProtocol = NarrowBandCompetitionProtocol,
            Screening = screens.ToArray(),
            TemplateEvaluations = discrete + matches.Count,
            NarrowBandCompetition = new(initialCount, (int)discrete, DiscreteEvaluationLimit, rounds, ShortCandidateLimit, matches.ToArray())
        };
        return selected! with { Candidates = candidates.ToArray(), Result = selected.Result! with { Run = record } };
    }

    private static IEnumerable<NarrowSeed> DiverseNarrowSeeds(IEnumerable<NarrowSeed> seeds, int limit)
    {
        var ranked = seeds.DistinctBy(s => LayerIdentity(s.Layers)).OrderBy(s => s.Merit).ToArray();
        return ranked.Take(Math.Max(1, limit / 2)).Concat(ranked.GroupBy(s => (s.Layers.Length, s.Origin?.Family)).Select(g => g.First()))
            .Concat(ranked).DistinctBy(s => LayerIdentity(s.Layers)).Take(limit);
    }

    private static IEnumerable<NarrowBandPrototype> Neighbors(NarrowBandPrototype p)
    {
        foreach (var step in new[] { -1, 1 })
        {
            for (var i = 0; i < p.MirrorLayers.Length; i++)
            { var mirror = p.MirrorLayers.ToArray(); mirror[i] += step; yield return p with { MirrorLayers = mirror, Family = "独立镜段竞争" }; }
            for (var i = 0; i < p.CavityOrders.Length; i++)
            { var orders = p.CavityOrders.ToArray(); orders[i] += step; yield return p with { CavityOrders = orders, Family = "独立腔阶竞争" }; }
            yield return p with { MirrorLayers = p.MirrorLayers.Select(m => m + step).ToArray(), Family = "联合镜段竞争" };
            yield return p with { CavityOrders = p.CavityOrders.Select(k => k + step).ToArray(), Family = "联合腔阶竞争" };
        }
    }

    public IncidentMatchingTrial[] MatchIncidentMedium(Experiment input, CancellationToken token = default)
    {
        using var scope = ComputationCancellation.Push(token);
        token.ThrowIfCancellationRequested();
        var d = input.Snapshot(); var materials = Materials(d);
        if (d.Settings.DesignProtocolVersion == 4) return MatchGeneralIncidentMedium(d, materials, token);
        if (d.Layers.Length == 0 || d.Layers.Any(l => !l.Variable)) return [];
        if (!WithinBounds(d, d.Layers)) throw new ArgumentException("匹配前的结构不满足膜厚或层数约束。");
        var before = Solver(d, materials).Evaluate(d.Target.CenterNm, d.Target.AngleDegrees, d.Target.Polarization);
        var trials = new List<IncidentMatchingTrial>();
        foreach (var ids in new[] { new[] { d.LowId }, new[] { d.HighId }, new[] { d.LowId, d.HighId }, new[] { d.HighId, d.LowId } })
        {
            if (d.Layers.Length + ids.Length > d.Target.MaximumLayers) continue;
            FilmLayer[]? best = null; var bestR = double.PositiveInfinity; long evaluations = 0; var iterations = 0;
            var starts = ids.Length == 1 ? new[] { new[] { .3 }, new[] { 1.0 }, new[] { 1.7 } }
                : (from first in new[] { .3, 1, 1.7 } from second in new[] { .3, 1, 1.7 } select new[] { first, second }).ToArray();
            foreach (var fractions in starts)
            {
                token.ThrowIfCancellationRequested();
                var quarters = ids.Select(id => CoherentThinFilmSolver.QuarterWaveThickness(materials[id], materials[d.IncidentId], d.Target.CenterNm, d.Target.AngleDegrees)).ToArray();
                var upper = quarters.Select(q => Math.Min(d.Settings.MaximumThicknessNm, 2 * q)).ToArray();
                if (upper.Any(u => u < d.Settings.MinimumThicknessNm)) continue;
                var values = quarters.Select((q, i) => Math.Clamp(fractions[i] * q, d.Settings.MinimumThicknessNm, upper[i])).ToArray();
                var problem = new OptimizationProblem(); problem.DisableBatching();
                for (var i = 0; i < values.Length; i++)
                {
                    var index = i;
                    problem.AddVariable(new DelegateVariable("匹配层 " + (i + 1), () => values[index], v => values[index] = v,
                        d.Settings.MinimumThicknessNm, upper[i], stepHint: quarters[i] / 5));
                }
                var polarizations = d.Target.Polarization == ThinFilmPolarization.Unpolarized
                    ? new[] { ThinFilmPolarization.S, ThinFilmPolarization.P } : new[] { d.Target.Polarization };
                double[]? cachedVector = null, cachedResiduals = null;
                double[] ReflectionResiduals()
                {
                    token.ThrowIfCancellationRequested();
                    if (cachedVector is not null && cachedVector.SequenceEqual(values)) return cachedResiduals!;
                    var layers = ids.Select((id, i) => new FilmLayer(id, values[i])).Concat(d.Layers).ToArray();
                    var solver = Solver(d, materials, layers);
                    var amplitudes = polarizations.Select(pol => solver.EvaluateAmplitude(d.Target.CenterNm, d.Target.AngleDegrees, pol)).ToArray();
                    var r = amplitudes.Average(a => a.Power.Reflectance);
                    if (r < bestR) { bestR = r; best = layers; }
                    cachedResiduals = amplitudes.SelectMany(a => new[] { a.Reflection.Real / Math.Sqrt(polarizations.Length),
                        a.Reflection.Imaginary / Math.Sqrt(polarizations.Length) }).ToArray();
                    cachedVector = values.ToArray(); return cachedResiduals;
                }
                for (var i = 0; i < 2 * polarizations.Length; i++)
                {
                    var index = i;
                    problem.AddOperand(new Operand("实际入射介质中心反射振幅 " + i, 0, 1, () => ReflectionResiduals()[index]));
                }
                var report = OptimizerCatalog.Create(d.Settings.Optimizer).Optimize(problem, Math.Min(32, d.Settings.MaximumIterations));
                evaluations += report.FunctionEvaluations; iterations += report.Iterations;
            }
            if (best is null) continue;
            // Collapse a variable cap into the adjacent identical film only when the resulting layer obeys its bound.
            var merged = new List<FilmLayer>();
            foreach (var layer in best)
            {
                if (merged.Count > 0 && merged[^1].MaterialId == layer.MaterialId && merged[^1].Variable && layer.Variable
                    && merged[^1].ThicknessNm + layer.ThicknessNm <= d.Settings.MaximumThicknessNm)
                    merged[^1] = layer with { ThicknessNm = merged[^1].ThicknessNm + layer.ThicknessNm };
                else merged.Add(layer);
            }
            var output = merged.ToArray(); var after = Solver(d, materials, output).Evaluate(d.Target.CenterNm, d.Target.AngleDegrees, d.Target.Polarization);
            trials.Add(new(string.Join('/', ids.Select(id => id == d.LowId ? "L" : "H")) + " 入射侧匹配", output, before, after,
                new(d.Settings.Optimizer, "actual-medium-matching/1", "有界相位组合起点完成；以实际反射振幅最小化匹配层中心 R，整谱另行竞争",
                    evaluations, iterations, null)
                { DesignProtocol = Protocol(d) }));
        }
        return trials.ToArray();
    }

    private static double[] CompetitiveNarrowBandObjective(Experiment d, CoherentThinFilmSolver solver, double[] grid)
    {
        var t = d.Target; var samples = ThinFilmSpectrum.Sample(solver, grid, t.AngleDegrees, t.Polarization); var m = Metrics(d, samples);
        var center = solver.Evaluate(t.CenterNm, t.AngleDegrees, t.Polarization).Transmittance;
        var widthScale = Math.Max(.02, center * t.FwhmToleranceNm / t.FwhmNm);
        var residuals = new List<double>
        {
            Math.Max(0, t.PeakTransmittance - m.PeakTransmittance) / .05,
            Math.Max(0, t.PeakTransmittance - center) / .1,
            Math.Max(0, Math.Abs(m.PeakWavelengthNanometers - t.CenterNm) - t.CenterToleranceNm) / t.CenterToleranceNm,
            m.FwhmNanometers is { } width ? Math.Max(0, Math.Abs(width - t.FwhmNm) - t.FwhmToleranceNm) / t.FwhmToleranceNm : 20,
            (solver.Evaluate(t.CenterNm - t.FwhmNm / 2, t.AngleDegrees, t.Polarization).Transmittance - center / 2) / widthScale,
            (solver.Evaluate(t.CenterNm + t.FwhmNm / 2, t.AngleDegrees, t.Polarization).Transmittance - center / 2) / widthScale
        };
        foreach (var band in t.StopBands!)
        {
            var rows = samples.Where(s => s.WavelengthNanometers >= band.MinimumNm && s.WavelengthNanometers <= band.MaximumNm)
                .Select(s => Math.Max(0, t.BlockingOd - s.Power.OpticalDensity) / Math.Max(.1, t.BlockingOd * .1)).ToArray();
            residuals.AddRange(rows.Select(r => r / Math.Sqrt(2 * rows.Length)));
            residuals.Add(rows.Max() / Math.Sqrt(2));
        }
        return residuals.ToArray();
    }
}
