using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Tolerancing;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed partial class DesignService
{
    private sealed record GeneralSeed(string Name, FilmLayer[] Layers, NarrowBandPrototype? Origin, CoatingConstraintAssessment Assessment, double[] Response);

    private static IEnumerable<GeneralSeed> GeneralPool(IEnumerable<GeneralSeed> source, int limit)
    {
        var candidates = source.DistinctBy(s => LayerIdentity(s.Layers)).ToList(); var selected = new List<GeneralSeed>();
        while (selected.Count < limit && candidates.Count > 0)
        {
            static bool Dominates(GeneralSeed a, GeneralSeed b)
            {
                var strict = false; for (var i = 0; i < a.Assessment.Violations.Length; i++)
                { if (a.Assessment.Violations[i] > b.Assessment.Violations[i]) return false; strict |= a.Assessment.Violations[i] < b.Assessment.Violations[i]; }
                return strict;
            }
            var front = candidates.Where(a => !candidates.Any(b => !ReferenceEquals(a, b) && Dominates(b, a))).ToList();
            while (front.Count > 0 && selected.Count < limit)
            {
                var pick = selected.Count == 0 ? front.OrderBy(s => s.Assessment.MaximumViolation).ThenBy(s => s.Assessment.Merit).ThenByDescending(s => s.Assessment.PeakTransmittance).First()
                    : front.OrderByDescending(s => selected.Min(p => s.Response.Zip(p.Response).Sum(v => (v.First - v.Second) * (v.First - v.Second))))
                        .ThenBy(s => s.Assessment.MaximumViolation).ThenBy(s => s.Assessment.Merit).First();
                selected.Add(pick); front.Remove(pick); candidates.Remove(pick);
            }
        }
        return selected;
    }

    private Experiment GenerateGeneralNarrowBand(Experiment d, IReadOnlyDictionary<string, IMaterial> materials, bool fullSearch,
        IProgress<DesignProgress>? progress, CancellationToken token)
    {
        var diagnostic = AssessMaterials(d, materials, token);
        progress?.Report(new("材料适用性诊断：真实材料通用约束竞争", 0));
        var t = d.Target;
        var nh = materials[d.HighId].RefractiveIndex(t.CenterNm); var nl = materials[d.LowId].RefractiveIndex(t.CenterNm);
        if (nh <= nl) throw new ArgumentException("高折射率材料的 n 必须大于低折射率材料。");
        var work = new GeneralCounter(); var grid = ObjectiveGrid(d); var evaluated = new Dictionary<string, GeneralSeed>();
        var q = t.Structure?.Cavities ?? 1; var discrete = 0; var rounds = 0;
        var delta = (int)Math.Clamp(Math.Floor(Math.Log(materials[d.SubstrateId].RefractiveIndex(t.CenterNm) / nh) / Math.Log(nl / nh) + .5), 0, 41);
        var minQuarter = new[] { d.LowId, d.HighId }.Min(id => CoherentThinFilmSolver.QuarterWaveThickness(materials[id], materials[d.IncidentId], t.CenterNm, t.AngleDegrees));
        var maxOrder = (int)Math.Min(400, Math.Floor(d.Settings.MaximumThicknessNm / (2 * minQuarter)));
        var maxMirror = Math.Min(41, (t.MaximumLayers - q) / (q + 1));
        GeneralSeed Describe(string name, FilmLayer[] layers, NarrowBandPrototype? origin)
        {
            var system = GeneralConstraintSystem(d with { Layers = layers }, Solver(d, materials, layers), grid, false, work);
            // Fixed normalized wavelengths give a physical diversity descriptor, independent of material names/labels.
            var response = Enumerable.Range(0, 17).Select(i =>
            {
                var wavelength = Math.Clamp(t.CenterNm + (i - 8) * t.FwhmNm / 4, t.MinimumNm, t.MaximumNm);
                return system.Samples.MinBy(s => Math.Abs(s.WavelengthNanometers - wavelength))!.Power.Transmittance;
            }).ToArray();
            return new(name, layers, origin, AssessGeneral(d, system.Samples), response);
        }
        bool Add(NarrowBandPrototype p)
        {
            token.ThrowIfCancellationRequested();
            if (discrete == DiscreteEvaluationLimit) return false;
            var layers = PrototypeLayers(d, materials, p); if (layers is null) return false;
            var key = LayerIdentity(layers); if (evaluated.ContainsKey(key)) return false;
            evaluated.Add(key, Describe($"镜段 [{string.Join(',', p.MirrorLayers)}] / 腔阶 [{string.Join(',', p.CavityOrders)}]", layers, p)); discrete++;
            if (discrete % 16 == 0) progress?.Report(new("通用结构约束竞争（未认证）", discrete));
            return true;
        }
        if (d.Layers.Length > 0)
        {
            if (!WithinBounds(d, d.Layers)) throw new ArgumentException("当前膜系不满足原层数/膜厚界。");
            evaluated.Add(LayerIdentity(d.Layers), Describe("保留当前结构", d.Layers, null)); discrete++;
        }
        var structural = d.Layers.Length == 0 || ((d.Settings.Search?.VaryStructure ?? true) && d.Layers.All(l => l.Variable));
        var total = Math.Max(0, maxMirror * maxOrder * 4); var cursor = 0;
        static int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return a; }
        var stride = Math.Max(1, (int)(total * (Math.Sqrt(5) - 1) / 2)); while (total > 0 && Gcd(stride, total) != 1) stride++;
        bool NextStratified()
        {
            while (cursor < total)
            {
                var index = (int)((long)cursor++ * stride % total);
                var m = index / 4 % maxMirror + 1; var order = index / (4 * maxMirror) + 1;
                var coupled = index % 4 >= 2; var firstHigh = index % 2 == 0;
                var mirrors = Enumerable.Range(0, q + 1).Select(i => !coupled || i == 0 || i == q ? m : Math.Max(1, 2 * m + 2 * delta - 1)).ToArray();
                if (Add(new(mirrors, Enumerable.Repeat(order, q).ToArray(), firstHigh))) return true;
            }
            return false;
        }
        if (structural) while (discrete < InitialStructureLimit && NextStratified()) { }
        var initial = discrete;
        if (structural)
            while (discrete < DiscreteEvaluationLimit)
            {
                var before = discrete; rounds++;
                foreach (var seed in GeneralPool(evaluated.Values, 8).Where(s => s.Origin is not null))
                    foreach (var neighbor in GeneralNeighbors(seed.Origin!)) Add(neighbor);
                if (discrete == before && !NextStratified()) break;
                if (discrete >= DiscreteEvaluationLimit) break;
            }
        if (evaluated.Count == 0) throw new ArgumentException("原层数和膜厚约束排除了全部起点。");
        var pool = evaluated.Values.ToList(); var matches = new List<IncidentMatchingTrial>();
        if (structural)
            foreach (var seed in GeneralPool(pool, MatchingCandidateLimit).ToArray())
            {
                progress?.Report(new("实际透射目标多根匹配", matches.Count));
                foreach (var trial in MatchGeneralIncidentMedium(d with { Layers = seed.Layers }, materials, token))
                {
                    matches.Add(trial); work.Add(trial.Run.GeneralWork);
                    pool.Add(Describe(seed.Name + " · " + trial.Pattern, trial.Layers, seed.Origin));
                }
            }
        var screens = new List<ScreeningRun>(); var refinedSeeds = new List<GeneralSeed>();
        foreach (var seed in GeneralPool(pool, ShortCandidateLimit))
        {
            progress?.Report(new("通用候选约束精化 " + (screens.Count + 1), screens.Sum(s => s.Run.Evaluations)));
            var refined = RefineGeneralThickness(d with { Layers = seed.Layers }, materials, grid, Math.Min(4, d.Settings.MaximumIterations), progress, token);
            work.Add(refined.Run.GeneralWork);
            screens.Add(new(screens.Count + 1, seed.Layers, refined.Layers, refined.Merit, refined.Run) { OriginPrototype = seed.Origin });
            refinedSeeds.Add(Describe(seed.Name, refined.Layers, seed.Origin));
        }
        var finalists = GeneralPool(refinedSeeds.Concat(pool), ShortCandidateLimit).ToList();
        if (d.Layers.Length > 0 && finalists.All(s => LayerIdentity(s.Layers) != LayerIdentity(d.Layers))) finalists.Add(evaluated[LayerIdentity(d.Layers)]);
        long evaluations = matches.Sum(m => m.Run.Evaluations) + screens.Sum(s => s.Run.Evaluations);
        var iterations = matches.Sum(m => m.Run.Iterations) + screens.Sum(s => s.Run.Iterations);
        var original = d.Layers.Length > 0 ? Evaluate(d with { Result = null, Candidates = null, Tolerance = null }, progress: progress, token: token) : null;
        var originalAssessment = original is null ? null : AssessGeneral(original, original.Result!.Spectrum);
        if (original is not null) work.Dense++;
        Experiment? selected = original; CoatingConstraintAssessment? selectedAssessment = originalAssessment; var candidates = new List<Candidate>();
        if (fullSearch)
        {
            var random = new Random(d.Settings.RandomSeed); var options = d.Settings.Search ?? new();
            var jitter = new UniformSampler(-options.JitterPercent / 100, options.JitterPercent / 100);
            for (var i = 0; i < options.Starts; i++)
            {
                var seed = finalists[i % finalists.Count]; var layers = seed.Layers;
                if (i >= finalists.Count) layers = layers.Select(l => l.Variable ? l with { ThicknessNm = Math.Clamp(l.ThicknessNm * (1 + jitter.Sample(random)), d.Settings.MinimumThicknessNm, d.Settings.MaximumThicknessNm) } : l).ToArray();
                progress?.Report(new($"通用候选完整优化 {i + 1}/{options.Starts}", evaluations));
                var candidate = Optimize(d with { Layers = layers, Result = null, Candidates = null, Tolerance = null }, progress, token);
                var r = candidate.Result!; var a = AssessGeneral(candidate, r.Spectrum); work.Add(r.Run.GeneralWork);
                evaluations += r.Run.Evaluations; iterations += r.Run.Iterations;
                candidates.Add(new(seed.Name, candidate.Layers, r.Merit, r.Passed, r.Run) { OriginPrototype = seed.Origin });
                if (originalAssessment is not null && !WithinGeneralTargetEnvelope(a, originalAssessment)) { work.DenseRejections++; continue; }
                if (selected is null || r.Passed && !selected.Result!.Passed || r.Passed == selected.Result!.Passed && PreferGeneral(a, selectedAssessment!))
                { selected = candidate; selectedAssessment = a; }
            }
            if (original is not null)
            {
                candidates.Add(new("原结构完整复验", original.Layers, original.Result!.Merit, original.Result.Passed));
            }
        }
        else
        {
            // Dense ranking is reserved for this small shortlist, never the hundreds of coarse seeds.
            foreach (var seed in finalists.Take(Math.Min(finalists.Count, (d.Settings.Search ?? new()).Starts)))
            {
                var verified = Evaluate(d with { Layers = seed.Layers, Result = null, Candidates = null, Tolerance = null }, progress: progress, token: token);
                work.Dense++; var assessment = AssessGeneral(verified, verified.Result!.Spectrum);
                if (originalAssessment is not null && !WithinGeneralTargetEnvelope(assessment, originalAssessment)) { work.DenseRejections++; continue; }
                if (selected is null || verified.Result.Passed && !selected.Result!.Passed
                    || verified.Result.Passed == selected.Result!.Passed && PreferGeneral(assessment, selectedAssessment!))
                { selected = verified; selectedAssessment = assessment; }
            }
            candidates.AddRange(finalists.Select(s => new Candidate(s.Name, s.Layers, s.Assessment.Merit, false) { OriginPrototype = s.Origin }));
        }
        var run = new RunRecord("通用约束/Pareto结构竞争 + 多根透射匹配 + 光学相位优化", GeneralCoatingProtocol,
            "有界预算完成；仅原密集复验决定达标", evaluations, iterations, d.Settings.RandomSeed)
        {
            DesignProtocol = GeneralCoatingProtocol,
            GeneralWork = work.Snapshot(),
            Screening = screens.ToArray(),
            TemplateEvaluations = discrete + matches.Count,
            NarrowBandCompetition = new(initial, discrete, DiscreteEvaluationLimit, rounds, ShortCandidateLimit, matches.ToArray()),
            TargetViolationCeilings = originalAssessment?.Violations,
            TargetEnvelopeSampling = originalAssessment is null ? null : "dense-start",
            TargetProtectionPhase = "candidate-selection"
        };
        return selected! with { Candidates = candidates.ToArray(), Result = selected.Result! with { Run = run } };
    }

    private static IEnumerable<NarrowBandPrototype> GeneralNeighbors(NarrowBandPrototype p)
    {
        foreach (var step in new[] { -1, 1, -2, 2 })
        {
            for (var i = 0; i < p.MirrorLayers.Length; i++)
            { var m = p.MirrorLayers.ToArray(); m[i] += step; yield return p with { MirrorLayers = m }; }
            for (var i = 0; i < p.CavityOrders.Length; i++)
            {
                var orders = p.CavityOrders.ToArray(); orders[i] += step; yield return p with { CavityOrders = orders };
                var m = p.MirrorLayers.ToArray(); m[i] -= step; m[i + 1] -= step;
                yield return p with { MirrorLayers = m, CavityOrders = orders };
            }
        }
    }
}
